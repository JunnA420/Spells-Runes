using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Render;

/// <summary>
/// Central renderer for Sylphweed glow cuboids.
/// BlockEntitySylphweed registers/unregisters itself — no scanning.
/// </summary>
public class SylphweedGlowRenderer : IRenderer
{
    public double RenderOrder => 0.91;
    public int    RenderRange => 256;

    private readonly ICoreClientAPI capi;
    private IShaderProgram?         shader;
    private MeshRef?                meshRef;

    private readonly List<SylphLight> lights = new();
    private readonly List<bool>       lightsRegistered = new();
    private float                   time;
    private float                   particleTimer;

    // Registered plants: pos → their cubes
    private readonly Dictionary<BlockPos, PlantEntry> entries = new();

    // Preallocated mesh — reused every frame, grown on demand
    private MeshData mesh = new MeshData(12, 18, false, true, true, false);
    private int      meshCapacityPlants; // how many plants the current arrays fit

    // Static corner offsets — no heap alloc per frame
    private static readonly float[] Oxf = { -1f,  1f, 1f, -1f };
    private static readonly float[] Oyf = { -1f, -1f, 1f,  1f };

    private static readonly int WhiteBgra = ColorUtil.ColorFromRgba(255, 255, 255, 200);

    private class SylphLight : IPointLight
    {
        public Vec3f Color { get; set; } = new Vec3f(0.5f, 0.5f, 0.5f);
        public Vec3d Pos   { get; set; } = new Vec3d();
    }

    private struct FloatCube
    {
        public float Angle;
        public float Y;
        public float OrbitR;
        public float RiseSpeed;
        public float AngSpeed;
        public float Size;
    }

    private class PlantEntry
    {
        public Vec3d       BasePos = new Vec3d();
        public FloatCube[] Cubes = new FloatCube[3];
        public int         LightIndex;
    }

    public SylphweedGlowRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        InitShader();
        capi.Event.ReloadShader += OnReloadShader;
    }

    public void Register(BlockPos pos)
    {
        if (entries.ContainsKey(pos)) return;
        var rng   = capi.World.Rand;
        var entry = new PlantEntry { BasePos = new Vec3d(pos.X + 0.5, pos.Y, pos.Z + 0.5), LightIndex = lights.Count };
        lights.Add(new SylphLight());
        lightsRegistered.Add(false);
        for (int i = 0; i < 3; i++)
        {
            float speed = 1.2f + (float)(rng.NextDouble() * 0.8f);
            entry.Cubes[i] = new FloatCube
            {
                Angle     = (float)(i * 2.094 + rng.NextDouble() * 0.5),
                OrbitR    = 0.65f + (float)(rng.NextDouble() * 0.20f),
                Y         = i * 0.45f,
                RiseSpeed = 0.55f + (float)(rng.NextDouble() * 0.25f),
                AngSpeed  = speed,
                Size      = 0.055f + (float)(rng.NextDouble() * 0.02f),
            };
        }
        entries[pos] = entry;
    }

    public void Unregister(BlockPos pos)
    {
        if (!entries.TryGetValue(pos, out var entry)) return;
        int li = entry.LightIndex;
        if (li < lights.Count)
        {
            if (lightsRegistered[li]) capi.Render.RemovePointLight(lights[li]);
            lights.RemoveAt(li);
            lightsRegistered.RemoveAt(li);
            foreach (var e in entries.Values)
                if (e.LightIndex > li) e.LightIndex--;
        }
        entries.Remove(pos);
    }

    private bool OnReloadShader() { InitShader(); return true; }

    private bool InitShader()
    {
        var prog = capi.Shader.NewShaderProgram();
        prog.AssetDomain    = "spellsandrunes";
        prog.VertexShader   = capi.Shader.NewShader(EnumShaderType.VertexShader);
        prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

        prog.VertexShader.Code = @"
            #version 330 core
            layout(location = 0) in vec3 position;
            layout(location = 1) in vec2 uv;
            layout(location = 2) in vec4 color;
            uniform mat4 projectionMatrix;
            uniform mat4 modelViewMatrix;
            out vec4 vColor;
            out vec2 vUV;
            void main() {
                vec4 c = color / 255.0;
                vColor = vec4(c.b, c.g, c.r, c.a);
                vUV    = uv;
                gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
            }";

        prog.FragmentShader.Code = @"
            #version 330 core
            in vec4 vColor;
            in vec2 vUV;
            out vec4 outColor;
            void main() {
                vec2  uv    = vUV - 0.5;
                vec2  ab    = abs(uv) * 2.0;
                float box   = max(ab.x, ab.y);
                float solid = 1.0 - smoothstep(0.5, 0.65, box);
                float glow  = (1.0 - smoothstep(0.65, 1.0, box)) * 0.5;
                float alpha = max(solid, glow);
                outColor = vec4(vColor.rgb, alpha * vColor.a);
            }";

        if (!prog.Compile())
        {
            capi.Logger.Error("[SnR] sylphweedglow shader failed: " + prog.LoadError);
            return false;
        }
        shader?.Dispose();
        shader = prog;
        return true;
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (shader == null || entries.Count == 0) return;

        time          += dt;
        particleTimer += dt;

        if (particleTimer >= 0.15f)
        {
            particleTimer = 0f;
            SpawnParticles();
        }

        // Animate all cubes
        foreach (var entry in entries.Values)
        {
            for (int i = 0; i < entry.Cubes.Length; i++)
            {
                ref var c = ref entry.Cubes[i];
                c.Y     += c.RiseSpeed * dt;
                c.Angle += c.AngSpeed  * dt;
                if (c.Y > 1.4f) c.Y = 0f;
            }
        }

        int plantCount  = entries.Count;
        int totalCubes  = plantCount * 3;
        int neededVerts = totalCubes * 4;
        int neededIdx   = totalCubes * 6;

        // Grow preallocated arrays only when needed (never shrink)
        if (plantCount > meshCapacityPlants)
        {
            mesh = new MeshData(neededVerts, neededIdx, false, true, true, false);
            mesh.mode = EnumDrawMode.Triangles;
            // UV is constant — write once
            for (int q = 0; q < totalCubes; q++)
            {
                int vb = q * 4;
                for (int c = 0; c < 4; c++)
                {
                    mesh.Uv[(vb + c) * 2 + 0] = Oxf[c] * 0.5f + 0.5f;
                    mesh.Uv[(vb + c) * 2 + 1] = Oyf[c] * 0.5f + 0.5f;
                }
                int ib = q * 6;
                mesh.Indices[ib + 0] = vb;
                mesh.Indices[ib + 1] = vb + 1;
                mesh.Indices[ib + 2] = vb + 2;
                mesh.Indices[ib + 3] = vb;
                mesh.Indices[ib + 4] = vb + 2;
                mesh.Indices[ib + 5] = vb + 3;
            }
            meshCapacityPlants = plantCount;
            // Force re-upload on first use after resize
            meshRef?.Dispose();
            meshRef = null;
        }

        var   camPos = capi.World.Player.Entity.CameraPos;
        var   rapi   = capi.Render;
        float[] view = rapi.CameraMatrixOriginf;
        float rx = view[0], ry = view[4], rz = view[8];
        float ux = view[1], uy = view[5], uz = view[9];

        int vi = 0;

        foreach (var entry in entries.Values)
        {
            bool lightSet = false;
            int  li       = entry.LightIndex;

            foreach (var cube in entry.Cubes)
            {
                float cosA = (float)Math.Cos(cube.Angle);
                float sinA = (float)Math.Sin(cube.Angle);

                float fadeIn  = Math.Min(1f, cube.Y / 0.15f);
                float fadeOut = Math.Max(0f, 1f - Math.Max(0f, cube.Y - 1.1f) / 0.3f);
                byte  a       = (byte)(fadeIn * fadeOut * 240);

                float wx = (float)(entry.BasePos.X + cosA * cube.OrbitR - camPos.X);
                float wy = (float)(entry.BasePos.Y + cube.Y + 0.1       - camPos.Y);
                float wz = (float)(entry.BasePos.Z + sinA * cube.OrbitR - camPos.Z);
                float rad = cube.Size;

                for (int c = 0; c < 4; c++)
                {
                    int v3 = vi * 3, v4 = vi * 4;
                    mesh.xyz[v3 + 0] = wx + (rx * Oxf[c] + ux * Oyf[c]) * rad;
                    mesh.xyz[v3 + 1] = wy + (ry * Oxf[c] + uy * Oyf[c]) * rad;
                    mesh.xyz[v3 + 2] = wz + (rz * Oxf[c] + uz * Oyf[c]) * rad;
                    mesh.Rgba[v4 + 0] = 255;
                    mesh.Rgba[v4 + 1] = 255;
                    mesh.Rgba[v4 + 2] = 255;
                    mesh.Rgba[v4 + 3] = a;
                    vi++;
                }

                // Each plant updates its own light from its first cube
                if (!lightSet && li < lights.Count)
                {
                    var l = lights[li];
                    l.Pos.X = entry.BasePos.X + cosA * cube.OrbitR;
                    l.Pos.Y = entry.BasePos.Y + cube.Y + 0.1;
                    l.Pos.Z = entry.BasePos.Z + sinA * cube.OrbitR;
                    if (!lightsRegistered[li]) { capi.Render.AddPointLight(l); lightsRegistered[li] = true; }
                    lightSet = true;
                }
            }
        }

        mesh.VerticesCount = vi;
        mesh.IndicesCount  = neededIdx;

        // First frame or after resize: upload; subsequent frames: update in-place (no alloc)
        if (meshRef == null)
            meshRef = rapi.UploadMesh(mesh);
        else
            rapi.UpdateMesh(meshRef, mesh);

        shader.Use();
        shader.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
        shader.UniformMatrix("modelViewMatrix",  rapi.CameraMatrixOriginf);

        rapi.GlToggleBlend(true);
        rapi.GLDepthMask(false);
        rapi.GlDisableCullFace();
        rapi.RenderMesh(meshRef);
        rapi.GlEnableCullFace();
        rapi.GLDepthMask(true);
        rapi.GlToggleBlend(false);

        shader.Stop();
    }

    private void SpawnParticles()
    {
        var rng = capi.World.Rand;
        foreach (var entry in entries.Values)
        {
            if (rng.NextDouble() > 0.5) continue;
            var bp = entry.BasePos;
            capi.World.SpawnParticles(new SimpleParticleProperties
            {
                MinPos             = new Vec3d(bp.X - 0.35, bp.Y + 0.05, bp.Z - 0.35),
                AddPos             = new Vec3d(0.7, 0.4, 0.7),
                MinQuantity        = 1f,
                AddQuantity        = 1f,
                Color              = WhiteBgra,
                GravityEffect      = -0.015f,
                LifeLength         = 2.5f,
                addLifeLength      = 1.0f,
                MinSize            = 0.04f,
                MaxSize            = 0.09f,
                SizeEvolve         = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.03f),
                OpacityEvolve      = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -0.6f),
                MinVelocity        = new Vec3f(-0.02f, 0.03f, -0.02f),
                AddVelocity        = new Vec3f( 0.04f, 0.05f,  0.04f),
                ParticleModel      = EnumParticleModel.Cube,
                SelfPropelled      = false,
                DieOnRainHeightmap = false,
                WindAffectednes    = 0.05f,
            });
        }
    }

    public void Dispose()
    {
        capi.Event.ReloadShader -= OnReloadShader;
        for (int i = 0; i < lights.Count; i++)
            if (lightsRegistered[i]) capi.Render.RemovePointLight(lights[i]);
        shader?.Dispose();
        meshRef?.Dispose();
    }
}
