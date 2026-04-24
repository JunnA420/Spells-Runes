using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Render;

/// <summary>
/// Shared renderer for many animated blocks of the same shape. One animator per
/// (shape, animCode) is ticked once per frame; each instance gets its own
/// model matrix + optional custom transform.
/// </summary>
public class IdleAnimatedBlockRenderer : IRenderer, IDisposable
{
    private const float MaxRenderDistSq = 64f * 64f;

    public double RenderOrder => 1.0;
    public int    RenderRange => 99;

    private readonly ICoreClientAPI capi;
    private readonly Dictionary<string, ShapeGroup> groups = new();

    private readonly float[] modelMat = Mat4f.Create();
    private readonly float[] tmpMat   = new float[16];

    private class ShapeGroup
    {
        public string                  Key          = "";
        public Shape                   Shape        = null!;
        public MultiTextureMeshRef?    MeshRef;
        public AnimatorBase            SharedAnimator = null!;
        public Dictionary<string, AnimationMetaData> SharedActiveAnims = new();
        public Dictionary<BlockPos, Instance>        Instances         = new();
    }

    private class Instance
    {
        public BlockPos  Pos             = null!;
        public Vec3d     PosVec          = null!;
        public float[]?  CustomTransform;
        public string    CurrentAnimCode = "idle";
        public AnimatorBase? OwnAnimator;
        public Dictionary<string, AnimationMetaData>? OwnActiveAnims;
    }

    public IdleAnimatedBlockRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque,     "snr-animblock-op");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar,  "snr-animblock-sf");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "snr-animblock-sn");
    }

    public void Register(Block block, BlockPos pos, string animCode = "idle", float[]? customTransform = null)
    {
        if (block?.Shape?.Base == null) return;
        var group = GetOrCreateGroup(block);
        if (group == null) return;

        var inst = new Instance
        {
            Pos             = pos.Copy(),
            PosVec          = new Vec3d(pos.X, pos.Y, pos.Z),
            CustomTransform = customTransform,
            CurrentAnimCode = animCode,
        };
        group.Instances[inst.Pos] = inst;

        if (!group.SharedActiveAnims.ContainsKey(animCode))
        {
            var meta = new AnimationMetaData
            {
                Code           = animCode,
                Animation      = animCode,
                AnimationSpeed = 1f,
                EaseInSpeed    = 10f,
                EaseOutSpeed   = 10f,
            }.Init();
            group.SharedActiveAnims[animCode] = meta;
        }
    }

    public void Unregister(BlockPos pos)
    {
        foreach (var group in groups.Values)
        {
            if (group.Instances.Remove(pos)) return;
        }
    }

    /// <summary>Switches an instance to its own animator for a one-shot anim (e.g. destruction).</summary>
    public void SwitchAnimation(BlockPos pos, string newAnimCode, float animSpeed = 1f)
    {
        foreach (var group in groups.Values)
        {
            if (!group.Instances.TryGetValue(pos, out var inst)) continue;
            inst.CurrentAnimCode = newAnimCode;
            inst.OwnAnimator     = AnimationUtil.GetAnimator(capi, $"snr-{group.Key}-{pos}", group.Shape);
            inst.OwnActiveAnims  = new Dictionary<string, AnimationMetaData>
            {
                [newAnimCode] = new AnimationMetaData
                {
                    Code           = newAnimCode,
                    Animation      = newAnimCode,
                    AnimationSpeed = animSpeed,
                    EaseInSpeed    = 10f,
                    EaseOutSpeed   = 10f,
                }.Init(),
            };
            return;
        }
    }

    private ShapeGroup? GetOrCreateGroup(Block block)
    {
        string key = block.Shape.Base.ToString();
        if (groups.TryGetValue(key, out var existing)) return existing;

        var loc = block.Shape.Base.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
        var shape = Shape.TryGet(capi, loc);
        if (shape == null)
        {
            capi.Logger.Error($"[SnR] IdleAnimatedBlockRenderer: shape not found for {block.Code} at {loc}");
            return null;
        }

        var elementsByName = shape.CollectAndResolveReferences(capi.World.Logger, key);
        shape.CacheInvTransforms();
        shape.ResolveAndFindJoints(capi.World.Logger, key, elementsByName);

        var texSource = capi.Tesselator.GetTextureSource(block);
        var meta = new TesselationMetaData
        {
            TexSource         = texSource,
            WithJointIds      = true,
            WithDamageEffect  = true,
            TypeForLogging    = key,
        };
        capi.Tesselator.TesselateShape(meta, shape, out var meshData);
        var meshRef = capi.Render.UploadMultiTextureMesh(meshData);

        var animator = AnimationUtil.GetAnimator(capi, $"snr-shared-{key}", shape);
        if (animator == null)
        {
            capi.Logger.Error($"[SnR] IdleAnimatedBlockRenderer: could not build animator for {block.Code}");
            return null;
        }

        var group = new ShapeGroup
        {
            Key            = key,
            Shape          = shape,
            MeshRef        = meshRef,
            SharedAnimator = animator,
        };
        groups[key] = group;
        return group;
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (groups.Count == 0) return;
        if (capi.IsGamePaused) return;

        bool isShadow = stage != EnumRenderStage.Opaque;
        var  camPos   = capi.World.Player.Entity.CameraPos;

        // Tick shared + per-instance animators once per frame (during Opaque stage).
        if (stage == EnumRenderStage.Opaque)
        {
            foreach (var group in groups.Values)
            {
                if (group.SharedActiveAnims.Count > 0 || group.SharedAnimator.ActiveAnimationCount > 0)
                {
                    group.SharedAnimator.OnFrame(group.SharedActiveAnims, dt);
                }
                foreach (var inst in group.Instances.Values)
                {
                    if (inst.OwnAnimator != null && inst.OwnActiveAnims != null)
                    {
                        inst.OwnAnimator.OnFrame(inst.OwnActiveAnims, dt);
                    }
                }
            }
        }

        var render        = capi.Render;
        var prevShader    = render.CurrentActiveShader;
        prevShader?.Stop();

        var engineShader = render.GetEngineShader(isShadow
            ? EnumShaderProgram.Shadowmapentityanimated
            : EnumShaderProgram.Entityanimated);
        engineShader.Use();

        render.GLDepthMask(true);
        render.GlToggleBlend(true);

        if (!isShadow)
        {
            engineShader.Uniform      ("extraGlow",          0);
            engineShader.Uniform      ("rgbaAmbientIn",      render.AmbientColor);
            engineShader.Uniform      ("rgbaFogIn",          render.FogColor);
            engineShader.Uniform      ("fogMinIn",           render.FogMin);
            engineShader.Uniform      ("fogDensityIn",       render.FogDensity);
            engineShader.Uniform      ("alphaTest",          0.1f);
            engineShader.Uniform      ("windWaveIntensity",  0f);
            engineShader.Uniform      ("glitchEffectStrength", 0f);
            engineShader.Uniform      ("frostAlpha",         0f);
            engineShader.Uniform      ("renderColor",        ColorUtil.WhiteArgbVec);
            engineShader.UniformMatrix("viewMatrix",         render.CameraMatrixOriginf);
        }
        engineShader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
        engineShader.Uniform      ("addRenderFlags",   0);

        foreach (var group in groups.Values)
        {
            if (group.MeshRef == null || group.MeshRef.Disposed || !group.MeshRef.Initialized) continue;
            if (group.Instances.Count == 0) continue;

            // Upload shared animator pose once per group.
            var sharedAnim = group.SharedAnimator;
            engineShader.UBOs["Animation"].Update(sharedAnim.Matrices, 0, sharedAnim.MaxJointId * 16 * 4);
            bool uboIsShared = true;

            foreach (var inst in group.Instances.Values)
            {
                double dx = inst.PosVec.X - camPos.X;
                double dy = inst.PosVec.Y - camPos.Y;
                double dz = inst.PosVec.Z - camPos.Z;
                if (dx * dx + dy * dy + dz * dz > MaxRenderDistSq) continue;

                // Swap to per-instance animator pose if this instance has one.
                if (inst.OwnAnimator != null)
                {
                    engineShader.UBOs["Animation"].Update(inst.OwnAnimator.Matrices, 0, inst.OwnAnimator.MaxJointId * 16 * 4);
                    uboIsShared = false;
                }
                else if (!uboIsShared)
                {
                    engineShader.UBOs["Animation"].Update(sharedAnim.Matrices, 0, sharedAnim.MaxJointId * 16 * 4);
                    uboIsShared = true;
                }

                Mat4f.Identity(modelMat);
                Mat4f.Translate(modelMat, modelMat, (float)dx, (float)dy, (float)dz);
                if (inst.CustomTransform != null)
                {
                    Mat4f.Multiply(modelMat, modelMat, inst.CustomTransform);
                }

                if (!isShadow)
                {
                    var light = capi.World.BlockAccessor.GetLightRGBs(
                        (int)inst.PosVec.X, (int)inst.PosVec.Y, (int)inst.PosVec.Z);
                    engineShader.Uniform      ("rgbaLightIn", light);
                    engineShader.UniformMatrix("modelMatrix", modelMat);
                }
                else
                {
                    engineShader.UniformMatrix("modelViewMatrix",
                        Mat4f.Mul(tmpMat, render.CurrentModelviewMatrix, modelMat));
                }

                render.RenderMultiTextureMesh(group.MeshRef, "entityTex");
            }
        }

        engineShader.Stop();
        prevShader?.Use();
    }

    public void Dispose()
    {
        foreach (var group in groups.Values) group.MeshRef?.Dispose();
        groups.Clear();
        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
    }
}
