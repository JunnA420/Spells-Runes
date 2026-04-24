using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class WindVortex : Spell
{
    [ThreadStatic] private static SimpleParticleProperties? _pool;
    private static SimpleParticleProperties Pool => _pool ??= new SimpleParticleProperties();

    public override string Id          => "air_wind_vortex";
    public override string Name        => "Wind Vortex";
    public override string Description => "Surrounds the caster with a sustained sphere of wind while the button is held. Pushes away nearby entities. Releasing the cast collapses the sphere outward.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Defense;

    public override float FluxCost => 60f;
    public override float CastTime => 0f;
    public override string? AnimationCode => "air_wind_vortex";
    public override bool AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_spear"];

    public override (int col, int row) TreePosition => (2, 6);

    public const float AuraRadius = 2.8f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        SpawnFx(world, caster.SidedPos.XYZ.Add(0, 0.8, 0), caster.SidedPos.GetViewVector().ToVec3d().Normalize(), spellLevel);

        if (world.Side != EnumAppSide.Server || world.Api == null) return;

        float duration = 3f + (spellLevel - 1) * 0.2f;
        float elapsed = 0f;
        long listenerId = 0;
        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            elapsed += dt;
            var center = caster.SidedPos.XYZ.Add(0, 0.8, 0);
            var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
            SpawnFx(world, center, lookDir, spellLevel);


            if (elapsed >= duration)
            {

                SpawnFx(world, center, lookDir, spellLevel);
                world.Api.Event.UnregisterGameTickListener(listenerId);
            }
        }, 100);
    }

// Wind Vortex
// Generated as a per-call SpawnFx() method. If your effect should persist, call this from a tick listener. Courtesy of Fx Visualizer
public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1)
{
    var rng = world.Rand;
    int mult = 1 + (spellLevel - 1) / 4;
    Vec3d forward = lookDir.Normalize();
    double rangeScale = 1.0;
    Vec3d refUp = Math.Abs(forward.Y) > 0.98 ? new Vec3d(1, 0, 0) : new Vec3d(0, 1, 0);
    Vec3d right = refUp.Cross(forward).Normalize();
    Vec3d up = forward.Cross(right).Normalize();

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static Vec3d RandomUnit(Random rng)
    {
        Vec3d v;
        do
        {
            v = new Vec3d(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);
        }
        while (v.LengthSq() < 0.0001);
        return v.Normalize();
    }

    static Vec3d RotateLocalEuler(Vec3d vector, double pitchDeg, double yawDeg, double rollDeg)
    {
        double pitchRad = pitchDeg * Math.PI / 180.0;
        double yawRad = yawDeg * Math.PI / 180.0;
        double rollRad = rollDeg * Math.PI / 180.0;
        double a = Math.Cos(pitchRad);
        double b = Math.Sin(pitchRad);
        double c = Math.Cos(yawRad);
        double d = Math.Sin(yawRad);
        double e = Math.Cos(rollRad);
        double f = Math.Sin(rollRad);

        double m11 = c * e + d * b * f;
        double m12 = d * b * e - c * f;
        double m13 = a * d;
        double m21 = a * f;
        double m22 = a * e;
        double m23 = -b;
        double m31 = c * b * f - d * e;
        double m32 = d * f + c * b * e;
        double m33 = a * c;

        return new Vec3d(
            m11 * vector.X + m12 * vector.Y + m13 * vector.Z,
            m21 * vector.X + m22 * vector.Y + m23 * vector.Z,
            m31 * vector.X + m32 * vector.Y + m33 * vector.Z);
    }

    static Vec3d TransformBasis(Vec3d local, Vec3d forward, Vec3d right, Vec3d up)
    {
        return right * local.X + up * local.Y + forward * local.Z;
    }

    static Vec3d TransformLocal(Vec3d local, Vec3d forward, Vec3d right, Vec3d up, double pitchDeg, double yawDeg, double rollDeg)
    {
        Vec3d rotated = RotateLocalEuler(local, pitchDeg, yawDeg, rollDeg);
        return TransformBasis(rotated, forward, right, up);
    }

    static int LerpColor(int startR, int startG, int startB, int startA, int endR, int endG, int endB, int endA, double t)
    {
        return ColorUtil.ColorFromRgba(
            (int)Math.Round(Lerp(startR, endR, t)),
            (int)Math.Round(Lerp(startG, endG, t)),
            (int)Math.Round(Lerp(startB, endB, t)),
            (int)Math.Round(Lerp(startA, endA, t)));
    }

    static Vec3d SampleLocalPosition(string shape, Random rng, double radius, double radiusTop, double height, double coneAngleDeg, double spreadX, double spreadY, double spreadZ, double chaos)
    {
        if (shape == "ring")
        {
            double angle = rng.NextDouble() * Math.PI * 2;
            double y = (rng.NextDouble() - 0.5) * height;
            return new Vec3d(Math.Cos(angle) * radius, y, Math.Sin(angle) * radius);
        }

        if (shape == "vortex")
        {
            double t = rng.NextDouble();
            double angle = rng.NextDouble() * Math.PI * 2;
            double vortexRadius = Lerp(radius, radiusTop, t) * (0.82 + rng.NextDouble() * 0.35);
            double y = t * height;
            return new Vec3d(
                Math.Cos(angle) * vortexRadius + Math.Sin(y * 1.6) * chaos,
                y,
                Math.Sin(angle) * vortexRadius + Math.Cos(y * 1.4) * chaos);
        }


        return RandomUnit(rng) * (rng.NextDouble() * radius);
    }

    static Vec3d ComputeVelocity(string velocityMode, Vec3d direction, Vec3d localPos, Vec3d forward, Vec3d right, Vec3d up, double pitchDeg, double yawDeg, double rollDeg, double speed, double tangentialStrength, double inwardStrength, double verticalStrength)
    {
        Vec3d directional = TransformBasis(direction * speed, forward, right, up);
        Vec3d planar = new Vec3d(localPos.X, 0, localPos.Z);
        if (planar.LengthSq() < 0.0001) planar = new Vec3d(0, 0, 1);
        Vec3d radial = planar.Normalize();
        Vec3d tangential = new Vec3d(-radial.Z, 0, radial.X).Normalize();
        Vec3d vertical = new Vec3d(0, verticalStrength, 0);

        if (velocityMode == "radial") return TransformLocal(radial * speed + vertical, forward, right, up, pitchDeg, yawDeg, rollDeg);
        if (velocityMode == "tangential") return TransformLocal(tangential * tangentialStrength + vertical, forward, right, up, pitchDeg, yawDeg, rollDeg);
        return directional + TransformBasis(vertical, forward, right, up);
    }

    // Orbit Ring Low
    // @fxviz eyJsYWJlbCI6Ik9yYml0IFJpbmcgTG93Iiwic2hhcGUiOiJ2b3J0ZXgiLCJ2ZWxvY2l0eU1vZGUiOiJyYWRpYWwiLCJidXJzdENvdW50IjozNTEsImJ1cnN0SW50ZXJ2YWwiOjAuMDgsInNwYXduUmF0ZSI6MCwiZHVyYXRpb24iOjMsImxvb3AiOnRydWUsIndvcmxkU3BhY2UiOnRydWUsInJhZGl1cyI6MC44OSwicmFkaXVzVG9wIjoxLjQ3LCJoZWlnaHQiOjIuOTEsImNoYW9zIjowLjIzLCJjb25lQW5nbGUiOjAsInNwcmVhZFgiOjAuNDYsInNwcmVhZFkiOjAuNDUsInNwcmVhZFoiOjAuMTUsIm1heFBhcnRpY2xlcyI6MTU1MiwiZGlyZWN0aW9uIjp7IngiOjAsInkiOjAsInoiOjF9LCJzcGVlZCI6NC4yNSwidGFuZ2VudGlhbCI6MS4xNSwiaW53YXJkIjotMS43NSwidmVydGljYWwiOjIuNywicmFuZG9tVmVsb2NpdHkiOjAuNCwiZHJhZyI6MCwiZ3Jhdml0eSI6LTEuMiwidXBkcmFmdCI6MS45LCJsaWZlTWluIjowLjEyLCJsaWZlTWF4IjowLjE0LCJzaXplTWluIjowLjA4LCJzaXplTWF4IjowLjE0LCJzdGFydENvbG9yIjoiI2UxZjVmZiIsImVuZENvbG9yIjoiI2MzZTVmZiIsImFscGhhU3RhcnQiOjAuNzgsImFscGhhRW5kIjowLjIsInN0YXJ0RGVsYXkiOjAsIndpdGhUZXJyYWluQ29sbGlzaW9uIjpmYWxzZSwib2Zmc2V0WCI6MCwib2Zmc2V0WSI6MCwib2Zmc2V0WiI6MCwieWF3IjowLCJwaXRjaCI6MCwicm9sbCI6MH0=
    {
        const double pitchDeg = 0.0;
        const double yawDeg = 0.0;
        const double rollDeg = 0.0;
        Vec3d emitterOrigin = origin + right * 0.0 * rangeScale + up * 0.0 * rangeScale + forward * 0.0 * rangeScale;
        int count = 351 * mult;
    
    
        for (int i = 0; i < count; i++)
        {
            Vec3d localPos = SampleLocalPosition("vortex", rng, 0.89 * rangeScale, 1.47 * rangeScale, 2.91 * rangeScale, 0.0, 0.46 * rangeScale, 0.45 * rangeScale, 0.15 * rangeScale, 0.23);
            Vec3d worldPos = emitterOrigin + TransformLocal(localPos, forward, right, up, pitchDeg, yawDeg, rollDeg);
            Vec3d velocity = ComputeVelocity(
                "radial",
                new Vec3d(0.0, 0.0, 1.0),
                localPos,
                forward, right, up, pitchDeg, yawDeg, rollDeg,
                4.25, 1.15, -1.75, 2.7);
    
            if (0.4 > 0)
            {
                velocity += RandomUnit(rng) * (rng.NextDouble() * 0.4);
            }
    
            int color = LerpColor(
                225, 245, 255, 199,
                195, 229, 255, 51,
                rng.NextDouble());
    
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = color,
                MinPos = worldPos,
                AddPos = new Vec3d(0, 0, 0),
                MinVelocity = new Vec3f((float)velocity.X, (float)velocity.Y, (float)velocity.Z),
                AddVelocity = new Vec3f(0, 0, 0),
                LifeLength = (float)Lerp(0.12, 0.14, rng.NextDouble()),
                MinSize = 0.08f,
                MaxSize = 0.14f,
                GravityEffect = -1.2f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
    
    // Orbit Ring High
    // @fxviz eyJsYWJlbCI6Ik9yYml0IFJpbmcgSGlnaCIsInNoYXBlIjoicmluZyIsInZlbG9jaXR5TW9kZSI6InRhbmdlbnRpYWwiLCJidXJzdENvdW50IjoxMTAsImJ1cnN0SW50ZXJ2YWwiOjAuMSwic3Bhd25SYXRlIjowLCJkdXJhdGlvbiI6MywibG9vcCI6dHJ1ZSwid29ybGRTcGFjZSI6dHJ1ZSwicmFkaXVzIjoxLjc0LCJyYWRpdXNUb3AiOjEuOCwiaGVpZ2h0IjowLjksImNoYW9zIjowLCJjb25lQW5nbGUiOjAsInNwcmVhZFgiOjAuMDEsInNwcmVhZFkiOjAuNDUsInNwcmVhZFoiOjAuMDEsIm1heFBhcnRpY2xlcyI6MTc2LCJkaXJlY3Rpb24iOnsieCI6LTEsInkiOjAsInoiOjB9LCJzcGVlZCI6MC4yNSwidGFuZ2VudGlhbCI6LTAuMjUsImlud2FyZCI6MCwidmVydGljYWwiOjAsInJhbmRvbVZlbG9jaXR5IjowLjA1LCJkcmFnIjowLjAyLCJncmF2aXR5IjowLCJ1cGRyYWZ0IjowLCJsaWZlTWluIjowLjEyLCJsaWZlTWF4IjowLjE0LCJzaXplTWluIjowLjA4LCJzaXplTWF4IjowLjE0LCJzdGFydENvbG9yIjoiI2UxZjVmZiIsImVuZENvbG9yIjoiI2MzZTVmZiIsImFscGhhU3RhcnQiOjAuNzgsImFscGhhRW5kIjowLjIsInN0YXJ0RGVsYXkiOjAsIndpdGhUZXJyYWluQ29sbGlzaW9uIjpmYWxzZSwib2Zmc2V0WCI6MCwib2Zmc2V0WSI6MCwib2Zmc2V0WiI6MCwieWF3IjotMSwicGl0Y2giOjIsInJvbGwiOi04ZS0xNn0=
    {
        const double pitchDeg = 2.0;
        const double yawDeg = -1.0;
        const double rollDeg = -0.0;
        Vec3d emitterOrigin = origin + right * 0.0 * rangeScale + up * 0.0 * rangeScale + forward * 0.0 * rangeScale;
        int count = 110 * mult;
    
    
        for (int i = 0; i < count; i++)
        {
            Vec3d localPos = SampleLocalPosition("ring", rng, 1.74 * rangeScale, 1.8 * rangeScale, 0.9 * rangeScale, 0.0, 0.01 * rangeScale, 0.45 * rangeScale, 0.01 * rangeScale, 0.0);
            Vec3d worldPos = emitterOrigin + TransformLocal(localPos, forward, right, up, pitchDeg, yawDeg, rollDeg);
            Vec3d velocity = ComputeVelocity(
                "tangential",
                new Vec3d(-1.0, 0.0, 0.0),
                localPos,
                forward, right, up, pitchDeg, yawDeg, rollDeg,
                0.25, -0.25, 0.0, 0.0);
    
            if (0.05 > 0)
            {
                velocity += RandomUnit(rng) * (rng.NextDouble() * 0.05);
            }
    
            int color = LerpColor(
                225, 245, 255, 199,
                195, 229, 255, 51,
                rng.NextDouble());
    
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = color,
                MinPos = worldPos,
                AddPos = new Vec3d(0, 0, 0),
                MinVelocity = new Vec3f((float)velocity.X, (float)velocity.Y, (float)velocity.Z),
                AddVelocity = new Vec3f(0, 0, 0),
                LifeLength = (float)Lerp(0.12, 0.14, rng.NextDouble()),
                MinSize = 0.08f,
                MaxSize = 0.14f,
                GravityEffect = 0.0f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
}

// Suggested host method name: WindVortex.SpawnFx(...)

// Suggested host method name: WindVortex.SpawnFx(...)
}
