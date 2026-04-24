using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Render;

public class FireGlowRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;
    private readonly List<GlowPoint> points = new();
    private readonly FireLight light = new();
    private IShaderProgram? shader;
    private MeshRef? meshRef;
    private bool lightRegistered;

    private class FireLight : IPointLight
    {
        public Vec3f Color { get; set; } = new Vec3f(0.04f, 0.35f, 1f);
        public Vec3d Pos { get; set; } = new();
    }

    private struct GlowPoint
    {
        public Vec3d Pos;
        public Vec3d Vel;
        public float Radius;
        public float Life;
        public float MaxLife;
    }

    public double RenderOrder => 0.91;
    public int RenderRange => 128;

    public FireGlowRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        InitShader();
        capi.Event.ReloadShader += OnReloadShader;
    }

    public void AddFireGlow(Vec3d origin, Vec3d direction, float range, int count = 20)
    {
        if (direction.LengthSq() < 0.0001) direction = new Vec3d(0, 1, 0);
        direction = direction.Normalize();

        Vec3d refUp = Math.Abs(direction.Y) > 0.95 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
        Vec3d right = direction.Cross(refUp).Normalize();
        Vec3d up = right.Cross(direction).Normalize();
        var rng = capi.World.Rand;

        for (int i = 0; i < count; i++)
        {
            double dist = rng.NextDouble() * range;
            double spread = 0.12 + dist * 0.16;
            double a = rng.NextDouble() * Math.PI * 2;
            double r = Math.Sqrt(rng.NextDouble()) * spread;
            float life = 0.18f + (float)rng.NextDouble() * 0.22f;

            points.Add(new GlowPoint
            {
                Pos = origin + direction * dist + right * (Math.Cos(a) * r) + up * (Math.Sin(a) * r),
                Vel = direction * (0.6 + rng.NextDouble() * 1.2) + new Vec3d(0, 0.7 + rng.NextDouble() * 0.8, 0),
                Radius = 0.07f + (float)rng.NextDouble() * 0.08f,
                Life = life,
                MaxLife = life,
            });
        }
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        const float gravity = 3.2f;
        for (int i = points.Count - 1; i >= 0; i--)
        {
            var p = points[i];
            p.Life -= dt;
            if (p.Life <= 0f) { points.RemoveAt(i); continue; }
            p.Vel.Y -= gravity * dt;
            p.Pos.X += p.Vel.X * dt;
            p.Pos.Y += p.Vel.Y * dt;
            p.Pos.Z += p.Vel.Z * dt;
            points[i] = p;
        }

        if (points.Count == 0)
        {
            if (lightRegistered) { capi.Render.RemovePointLight(light); lightRegistered = false; }
            return;
        }

        double cx = 0, cy = 0, cz = 0;
        foreach (var p in points) { cx += p.Pos.X; cy += p.Pos.Y; cz += p.Pos.Z; }
        light.Pos = new Vec3d(cx / points.Count, cy / points.Count, cz / points.Count);
        if (!lightRegistered) { capi.Render.AddPointLight(light); lightRegistered = true; }

        if (shader == null) return;

        var rapi = capi.Render;
        var mesh = new MeshData(points.Count * 4, points.Count * 6, false, true, true, false)
        {
            mode = EnumDrawMode.Triangles
        };

        var camPos = capi.World.Player.Entity.CameraPos;
        float[] view = rapi.CameraMatrixOriginf;
        float rx = view[0], ry = view[4], rz = view[8];
        float ux = view[1], uy = view[5], uz = view[9];
        int vi = 0, ii = 0;

        foreach (var p in points)
        {
            float frac = p.Life / p.MaxLife;
            float alpha = frac * frac;
            float px = (float)(p.Pos.X - camPos.X);
            float py = (float)(p.Pos.Y - camPos.Y);
            float pz = (float)(p.Pos.Z - camPos.Z);
            byte a = (byte)(alpha * 210);
            int baseVi = vi;
            float[] ox = { -1, 1, 1, -1 };
            float[] oy = { -1, -1, 1, 1 };

            for (int c = 0; c < 4; c++)
            {
                mesh.xyz[vi * 3] = px + (rx * ox[c] + ux * oy[c]) * p.Radius;
                mesh.xyz[vi * 3 + 1] = py + (ry * ox[c] + uy * oy[c]) * p.Radius;
                mesh.xyz[vi * 3 + 2] = pz + (rz * ox[c] + uz * oy[c]) * p.Radius;
                mesh.Rgba[vi * 4] = 255;
                mesh.Rgba[vi * 4 + 1] = 118;
                mesh.Rgba[vi * 4 + 2] = 8;
                mesh.Rgba[vi * 4 + 3] = a;
                mesh.Uv[vi * 2] = ox[c] * 0.5f + 0.5f;
                mesh.Uv[vi * 2 + 1] = oy[c] * 0.5f + 0.5f;
                vi++;
            }

            mesh.Indices[ii++] = baseVi;
            mesh.Indices[ii++] = baseVi + 1;
            mesh.Indices[ii++] = baseVi + 2;
            mesh.Indices[ii++] = baseVi;
            mesh.Indices[ii++] = baseVi + 2;
            mesh.Indices[ii++] = baseVi + 3;
        }

        mesh.VerticesCount = vi;
        mesh.IndicesCount = ii;
        meshRef?.Dispose();
        meshRef = rapi.UploadMesh(mesh);

        shader.Use();
        shader.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
        shader.UniformMatrix("modelViewMatrix", rapi.CameraMatrixOriginf);
        rapi.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
        rapi.GLDepthMask(false);
        rapi.GlDisableCullFace();
        rapi.RenderMesh(meshRef);
        rapi.GLDepthMask(true);
        rapi.GlToggleBlend(true);
        rapi.GlEnableCullFace();
        shader.Stop();
    }

    public void Dispose()
    {
        capi.Event.ReloadShader -= OnReloadShader;
        if (lightRegistered) capi.Render.RemovePointLight(light);
        shader?.Dispose();
        meshRef?.Dispose();
    }

    private bool OnReloadShader()
    {
        InitShader();
        return true;
    }

    private void InitShader()
    {
        var prog = capi.Shader.NewShaderProgram();
        prog.AssetDomain = "spellsandrunes";
        prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);
        prog.VertexShader.Code = @"
            #version 330 core
            layout(location = 0) in vec3 position;
            layout(location = 1) in vec2 uv;
            layout(location = 2) in vec4 color;
            uniform mat4 projectionMatrix;
            uniform mat4 modelViewMatrix;
            out vec4 vColor;
            void main() {
                vColor = color;
                gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
            }";
        prog.FragmentShader.Code = @"
            #version 330 core
            in vec4 vColor;
            out vec4 outColor;
            void main() {
                float a = vColor.a;
                outColor = vec4(vColor.rgb * a, a);
            }";

        if (!prog.Compile())
        {
            capi.Logger.Error($"[SnR] FireGlowRenderer shader compile error: {prog.LoadError}");
            return;
        }

        shader = prog;
    }
}
