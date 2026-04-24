using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using SpellsAndRunes.Network;

namespace SpellsAndRunes.Spells.Air;

public class StormsEye : Spell
{
    public override string Id          => "air_storms_eye";
    public override string Name        => "Storm's Eye";
    public override string Description => "Creates a localized eye of turbulence around a target. The winds within hold them in place.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 55f;
    public override float CastTime => 1.5f;
    public override string? AnimationCode => "air_storms_eye";
    public override bool AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_clone"];

    public override (int col, int row) TreePosition => (0, 5);

    public const float Range = 7f;
    public const float Radius = 2.4f;
    public const float PullStrength = 0.3f;
    public const float DurationSeconds = 5f;

    public static Vec3d GetCenter(EntityAgent caster)
    {
        var center = caster.SidedPos.XYZ.Add(caster.SidedPos.GetViewVector().ToVec3d().Normalize() * Range).Add(0, 0.5, 0);
        return ClampToSurface(caster.World, center);
    }

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var center = GetCenter(caster);
        SpawnFx(world, center, spellLevel, Radius);

        if (world.Side != EnumAppSide.Server || world.Api == null) return;

        float duration = DurationSeconds + (spellLevel - 1) * 0.25f;
        float elapsed = 0f;
        long listenerId = 0;
        listenerId = world.Api.Event.RegisterGameTickListener(dt =>
        {
            elapsed += dt;
            BroadcastFx(world, center, spellLevel);

            world.GetEntitiesAround(center, Radius + 0.5f, Radius + 0.5f, e =>
            {
                if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
                Vec3d toCenter = center - e.SidedPos.XYZ;
                double dist = toCenter.Length();
                if (dist <= 0.01 || dist > Radius + 0.4f) return false;

                var dir = toCenter.Normalize();
                float holdFactor = dist < 0.6 ? 0.025f : PullStrength;
                e.SidedPos.Motion.Set(
                    dir.X * holdFactor,
                    Math.Max(e.SidedPos.Motion.Y * 0.2, -0.02),
                    dir.Z * holdFactor);
                return false;
            });

            if (elapsed >= duration)
                world.Api.Event.UnregisterGameTickListener(listenerId);
        }, 100);
    }

    private static void BroadcastFx(IWorldAccessor world, Vec3d center, int spellLevel)
    {
        if (world.Api is not ICoreServerAPI sapi) return;

        var fx = new MsgSpellFx
        {
            SpellId = "air_storms_eye",
            OriginX = center.X,
            OriginY = center.Y,
            OriginZ = center.Z,
            LookDirX = 0,
            LookDirY = 1,
            LookDirZ = 0,
            SpellLevel = spellLevel
        };

        var channel = sapi.Network.GetChannel("spellsandrunes");
        foreach (var p in sapi.World.AllOnlinePlayers)
            channel.SendPacket(fx, p as IServerPlayer);
    }

    // Storm's Eye
// Generated as a per-call SpawnFx() method. If your effect should persist, call this from a tick listener. Courtesy of Fx Visualizer
public static void SpawnFx(IWorldAccessor world, Vec3d center, int spellLevel = 1, float radius = Radius)
{
    var rng = world.Rand;
    int mult = 1 + (spellLevel - 1) / 4;
    Vec3d forward = new Vec3d(0, 0, 1);
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

        if (velocityMode == "inward") return TransformLocal(radial * -Math.Abs(inwardStrength) + vertical, forward, right, up, pitchDeg, yawDeg, rollDeg);
        if (velocityMode == "vortex") return TransformLocal(tangential * tangentialStrength + radial * -inwardStrength + vertical, forward, right, up, pitchDeg, yawDeg, rollDeg);
        return directional + TransformBasis(vertical, forward, right, up);
    }

    // Funnel Swirl
    // @fxviz eyJsYWJlbCI6IkZ1bm5lbCBTd2lybCIsInNoYXBlIjoidm9ydGV4IiwidmVsb2NpdHlNb2RlIjoidm9ydGV4IiwiYnVyc3RDb3VudCI6NDAyLCJidXJzdEludGVydmFsIjowLjEsInNwYXduUmF0ZSI6MCwiZHVyYXRpb24iOjUsImxvb3AiOnRydWUsIndvcmxkU3BhY2UiOnRydWUsInJhZGl1cyI6MC43NywicmFkaXVzVG9wIjoxLjYzLCJoZWlnaHQiOjIuNTQsImNoYW9zIjowLjQ0LCJjb25lQW5nbGUiOjU1LCJzcHJlYWRYIjowLjgsInNwcmVhZFkiOjEuMiwic3ByZWFkWiI6MC44LCJtYXhQYXJ0aWNsZXMiOjI1NDQsImRpcmVjdGlvbiI6eyJ4IjowLCJ5IjowLjA4LCJ6IjoxfSwic3BlZWQiOjAsInRhbmdlbnRpYWwiOjUuNCwiaW53YXJkIjoxLjgsInZlcnRpY2FsIjowLjEsInJhbmRvbVZlbG9jaXR5IjowLjA1LCJkcmFnIjowLjAzLCJncmF2aXR5IjotMC4wNSwidXBkcmFmdCI6MC4yNSwibGlmZU1pbiI6MC4xOCwibGlmZU1heCI6MC4yNCwic2l6ZU1pbiI6MC4wOSwic2l6ZU1heCI6MC4yMiwic3RhcnRDb2xvciI6IiNkY2Y3ZmYiLCJlbmRDb2xvciI6IiNiOGU0ZmYiLCJhbHBoYVN0YXJ0IjowLjgsImFscGhhRW5kIjowLCJzdGFydERlbGF5IjowLCJ3aXRoVGVycmFpbkNvbGxpc2lvbiI6ZmFsc2UsIm9mZnNldFgiOjAsIm9mZnNldFkiOjAsIm9mZnNldFoiOjAsInlhdyI6MCwicGl0Y2giOjAsInJvbGwiOjB9
    {
        const double pitchDeg = 0.0;
        const double yawDeg = 0.0;
        const double rollDeg = 0.0;
        Vec3d emitterOrigin = center + right * 0.0 * rangeScale + up * 0.0 * rangeScale + forward * 0.0 * rangeScale;
        int count = 402 * mult;
    
    
        for (int i = 0; i < count; i++)
        {
            Vec3d localPos = SampleLocalPosition("vortex", rng, 0.77 * rangeScale, 1.63 * rangeScale, 2.54 * rangeScale, 55.0, 0.8 * rangeScale, 1.2 * rangeScale, 0.8 * rangeScale, 0.44);
            Vec3d worldPos = emitterOrigin + TransformLocal(localPos, forward, right, up, pitchDeg, yawDeg, rollDeg);
            Vec3d velocity = ComputeVelocity(
                "vortex",
                new Vec3d(0.0, 0.08, 1.0),
                localPos,
                forward, right, up, pitchDeg, yawDeg, rollDeg,
                0.0, 5.4, 1.8, 0.1);
    
            if (0.05 > 0)
            {
                velocity += RandomUnit(rng) * (rng.NextDouble() * 0.05);
            }
    
            int color = LerpColor(
                220, 247, 255, 204,
                184, 228, 255, 0,
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
                LifeLength = (float)Lerp(0.18, 0.24, rng.NextDouble()),
                MinSize = 0.09f,
                MaxSize = 0.22f,
                GravityEffect = -0.05f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
    
    // Outer Inward Ring
    // @fxviz eyJsYWJlbCI6Ik91dGVyIElud2FyZCBSaW5nIiwic2hhcGUiOiJyaW5nIiwidmVsb2NpdHlNb2RlIjoiaW53YXJkIiwiYnVyc3RDb3VudCI6MTgsImJ1cnN0SW50ZXJ2YWwiOjAuMSwic3Bhd25SYXRlIjowLCJkdXJhdGlvbiI6NSwibG9vcCI6dHJ1ZSwid29ybGRTcGFjZSI6dHJ1ZSwicmFkaXVzIjoyLjUsInJhZGl1c1RvcCI6Mi44MSwiaGVpZ2h0IjoyLjkxLCJjaGFvcyI6MCwiY29uZUFuZ2xlIjoxMiwic3ByZWFkWCI6MC4wMiwic3ByZWFkWSI6MC4yLCJzcHJlYWRaIjowLjAyLCJtYXhQYXJ0aWNsZXMiOjIyNCwiZGlyZWN0aW9uIjp7IngiOjAsInkiOjAsInoiOi0xfSwic3BlZWQiOjMuMiwidGFuZ2VudGlhbCI6MCwiaW53YXJkIjozLjIsInZlcnRpY2FsIjowLCJyYW5kb21WZWxvY2l0eSI6MC4wNSwiZHJhZyI6MC4wNCwiZ3Jhdml0eSI6MCwidXBkcmFmdCI6MCwibGlmZU1pbiI6MC4xMiwibGlmZU1heCI6MC4xNiwic2l6ZU1pbiI6MC4wOCwic2l6ZU1heCI6MC4xNiwic3RhcnRDb2xvciI6IiNlYmZjZmYiLCJlbmRDb2xvciI6IiNkMGVmZmYiLCJhbHBoYVN0YXJ0IjowLjcyLCJhbHBoYUVuZCI6MCwic3RhcnREZWxheSI6MCwid2l0aFRlcnJhaW5Db2xsaXNpb24iOmZhbHNlLCJvZmZzZXRYIjowLCJvZmZzZXRZIjowLCJvZmZzZXRaIjowLCJ5YXciOjAsInBpdGNoIjowLCJyb2xsIjowfQ==
    {
        const double pitchDeg = 0.0;
        const double yawDeg = 0.0;
        const double rollDeg = 0.0;
        Vec3d emitterOrigin = center + right * 0.0 * rangeScale + up * 0.0 * rangeScale + forward * 0.0 * rangeScale;
        int count = 18 * mult;
    
    
        for (int i = 0; i < count; i++)
        {
            Vec3d localPos = SampleLocalPosition("ring", rng, 2.5 * rangeScale, 2.81 * rangeScale, 2.91 * rangeScale, 12.0, 0.02 * rangeScale, 0.2 * rangeScale, 0.02 * rangeScale, 0.0);
            Vec3d worldPos = emitterOrigin + TransformLocal(localPos, forward, right, up, pitchDeg, yawDeg, rollDeg);
            Vec3d velocity = ComputeVelocity(
                "inward",
                new Vec3d(0.0, 0.0, -1.0),
                localPos,
                forward, right, up, pitchDeg, yawDeg, rollDeg,
                3.2, 0.0, 3.2, 0.0);
    
            if (0.05 > 0)
            {
                velocity += RandomUnit(rng) * (rng.NextDouble() * 0.05);
            }
    
            int color = LerpColor(
                235, 252, 255, 184,
                208, 239, 255, 0,
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
                LifeLength = (float)Lerp(0.12, 0.16, rng.NextDouble()),
                MinSize = 0.08f,
                MaxSize = 0.16f,
                GravityEffect = 0.0f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
}

// Suggested host method name: StormSEye.SpawnFx(...)
}
