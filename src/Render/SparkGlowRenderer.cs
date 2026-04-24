using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Render;

/// <summary>
/// Renders additive-blended billboard quads at spark positions to simulate glow.
/// Each glow point lives for a short duration then fades out.
/// </summary>
public class SparkGlowRenderer : IRenderer
{
    private readonly ICoreClientAPI capi;

    private IShaderProgram? shader;
    private MeshRef?        meshRef;

    // Active glow points: world pos, radius, color (r,g,b), remaining lifetime
    private readonly List<GlowPoint> points = new();

    private readonly SparkLight light;
    private bool lightRegistered = false;

    private class SparkLight : IPointLight
    {
        public Vec3f Color { get; set; } = new Vec3f(0.05f, 0.4f, 1f); // BGR: b=0.05,g=0.4,r=1 → orange
        public Vec3d Pos   { get; set; } = new Vec3d();
    }

    private struct GlowPoint
    {
        public Vec3d   Pos;
        public Vec3d   Vel;        // blocks/sec
        public float   Radius;
        public float   R, G, B;
        public float   Life;
        public float   MaxLife;
    }

    public double RenderOrder => 0.9;
    public int    RenderRange => 128;

    public SparkGlowRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;
        light = new SparkLight();
        InitShader();
        capi.Event.ReloadShader += OnReloadShader;
    }
    private bool OnReloadShader()
{
    InitShader();
    return true;
}

private bool InitShader()
{
    var prog = capi.Shader.NewShaderProgram();
    prog.AssetDomain = "spellsandrunes";

    prog.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
    prog.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

    // Dočasně: shader přímo v kódu
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
            vColor = color;
            vUV = uv;
            gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        }";

    prog.FragmentShader.Code = @"
        #version 330 core
        in vec4 vColor;
        in vec2 vUV;
        out vec4 outColor;
        void main() {
            // Hard square — no radial falloff, solid ember cube look
            float a = vColor.a;
            outColor = vec4(vColor.rgb * a, a);
        }";

    if (!prog.Compile())
    {
        capi.Logger.Error($"Shader compile error: {prog.LoadError}");
        return false;
    }

    shader = prog;
    return true;
}
    /// <summary>Add a burst of moving glow embers matching the spark particles.</summary>
    public void AddSparkBurst(Vec3d origin, Vec3d lookDir, float range, float coneAngleDeg, int count = 60)
    {
        Vec3d right  = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d upPerp = lookDir.Cross(right).Normalize();
        double tanA  = Math.Tan(coneAngleDeg * Math.PI / 180.0);
        var rng = capi.World.Rand;

        for (int i = 0; i < count; i++)
        {
            double t      = rng.NextDouble();
            double dist   = range * t;
            double spread = tanA * dist;
            double a      = rng.NextDouble() * 2 * Math.PI;
            double r      = Math.Sqrt(rng.NextDouble()) * spread;

            Vec3d pos = origin
                      + lookDir * dist
                      + right   * (Math.Cos(a) * r)
                      + upPerp  * (Math.Sin(a) * r);

            // Velocity matches the scatter sparks in Spark.cs section 1
            double scatterA = rng.NextDouble() * 2 * Math.PI;
            double scatterS = 2.0 + rng.NextDouble() * 4.0;
            var vel = new Vec3d(
                lookDir.X * 5.0 + Math.Cos(scatterA) * right.X * scatterS,
                0.5 + rng.NextDouble() * 1.5,
                lookDir.Z * 5.0 + Math.Cos(scatterA) * right.Z * scatterS);

            float life = 0.4f + (float)(rng.NextDouble() * 0.5f);

            // Fire color palette — stored as (R,G,B) intuitive, swapped to BGRA on mesh write
            // white-hot, yellow, orange-yellow, orange, deep-orange, red
            (float R, float G, float B)[] palette = {
                (1.00f, 0.95f, 0.70f), // white-hot
                (1.00f, 0.82f, 0.24f), // bright yellow
                (1.00f, 0.55f, 0.08f), // orange-yellow
                (1.00f, 0.31f, 0.02f), // orange
                (0.86f, 0.12f, 0.00f), // deep orange-red
                (0.70f, 0.04f, 0.00f), // red
            };
            var col = palette[rng.Next(palette.Length)];

            points.Add(new GlowPoint
            {
                Pos     = pos,
                Vel     = vel,
                Radius  = 0.03f + (float)(rng.NextDouble() * 0.04f),
                R       = col.R,
                G       = col.G,
                B       = col.B,
                Life    = life,
                MaxLife = life,
            });
        }
    }

    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        // Move and age all points
        const float gravity = 9.8f;
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
        // Update point light
        if (points.Count == 0)
        {
            if (lightRegistered) { capi.Render.RemovePointLight(light); lightRegistered = false; }
            if (shader == null) return;
        }
        else
        {
            // Centroid of all points
            double cx = 0, cy = 0, cz = 0;
            foreach (var p in points) { cx += p.Pos.X; cy += p.Pos.Y; cz += p.Pos.Z; }
            light.Pos = new Vec3d(cx / points.Count, cy / points.Count, cz / points.Count);
            if (!lightRegistered) { capi.Render.AddPointLight(light); lightRegistered = true; }
        }

        if (points.Count == 0 || shader == null) return;

        var rapi = capi.Render;

        var mesh = new MeshData(points.Count * 4, points.Count * 6, false, true, true, false);
        mesh.mode = EnumDrawMode.Triangles;

        var camPos = capi.World.Player.Entity.CameraPos;
        // Camera right/up extracted from view matrix (for billboarding)
        float[] view = rapi.CameraMatrixOriginf;
        // Column-major: right = (m[0], m[4], m[8]), up = (m[1], m[5], m[9])
        float rx = view[0], ry = view[4], rz = view[8];
        float ux = view[1], uy = view[5], uz = view[9];

        int vi = 0, ii = 0;
        foreach (var p in points)
        {
            float frac  = p.Life / p.MaxLife;
            float alpha = frac * frac; // quadratic fade

            float px = (float)(p.Pos.X - camPos.X);
            float py = (float)(p.Pos.Y - camPos.Y);
            float pz = (float)(p.Pos.Z - camPos.Z);

            float rad = p.Radius;
            byte r = (byte)(p.R * 255);
            byte g = (byte)(p.G * 255);
            byte b = (byte)(p.B * 255);
            byte a = (byte)(alpha * 200);

            // 4 corners: (-1,-1), (1,-1), (1,1), (-1,1)
            float[] ox = { -1, 1, 1, -1 };
            float[] oy = { -1, -1, 1, 1 };

            int baseVi = vi;
            for (int c = 0; c < 4; c++)
            {
                float cx = ox[c], cy = oy[c];
                mesh.xyz[vi * 3 + 0] = px + (rx * cx + ux * cy) * rad;
                mesh.xyz[vi * 3 + 1] = py + (ry * cx + uy * cy) * rad;
                mesh.xyz[vi * 3 + 2] = pz + (rz * cx + uz * cy) * rad;
                mesh.Rgba[vi * 4 + 0] = b;  // BGRA: slot0=B
                mesh.Rgba[vi * 4 + 1] = g;
                mesh.Rgba[vi * 4 + 2] = r;  // slot2=R
                mesh.Rgba[vi * 4 + 3] = a;
                // UV for soft circle in frag shader
                mesh.Uv[vi * 2 + 0] = cx * 0.5f + 0.5f;
                mesh.Uv[vi * 2 + 1] = cy * 0.5f + 0.5f;
                vi++;
            }

            // Two triangles: 0,1,2 and 0,2,3
            mesh.Indices[ii++] = baseVi;
            mesh.Indices[ii++] = baseVi + 1;
            mesh.Indices[ii++] = baseVi + 2;
            mesh.Indices[ii++] = baseVi;
            mesh.Indices[ii++] = baseVi + 2;
            mesh.Indices[ii++] = baseVi + 3;
        }

        mesh.VerticesCount = vi;
        mesh.IndicesCount  = ii;

        // Upload (re-upload each frame — small mesh, fine for now)
        meshRef?.Dispose();
        meshRef = rapi.UploadMesh(mesh);

        // Render
        shader.Use();
        shader.UniformMatrix("projectionMatrix", rapi.CurrentProjectionMatrix);
        shader.UniformMatrix("modelViewMatrix",  rapi.CameraMatrixOriginf);

        rapi.GlToggleBlend(true, EnumBlendMode.PremultipliedAlpha);
        rapi.GLDepthMask(false);          // don't write to depth
        rapi.GlDisableCullFace();

        rapi.RenderMesh(meshRef);

        rapi.GLDepthMask(true);
        rapi.GlToggleBlend(true);         // restore normal blend
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

}
