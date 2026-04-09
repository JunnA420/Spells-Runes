using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class AirPush : Spell
{
    public override string Id          => "air_air_push";
    public override string Name        => "Air Push";
    public override string Description => "Fires a directional gust of air in front of you, pushing enemies back.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 25f;
    public override float CastTime => 1.5f;

    public override string? AnimationCode => "air_wind_push";

    public override (int col, int row) TreePosition => (2, 0);

    public const float Range        = 7f;
    public const float ConeAngleDeg = 50f;
    private const float BaseForce   = 1.2f;
    private static readonly Vec3d Up = new Vec3d(0, 1, 0);
    private static readonly float CosAngle = (float)Math.Cos(ConeAngleDeg * Math.PI / 180.0);
    private static readonly double TanAngle = Math.Tan(ConeAngleDeg * Math.PI / 180.0);

    // Pooled particle properties — mutated per spawn call, never escapes to another thread
    [ThreadStatic] private static SimpleParticleProperties? _pool;
    private static SimpleParticleProperties Pool => _pool ??= new SimpleParticleProperties();

    private static float LevelMultiplier(int level) => level switch
    {
        1 => 1.0f,
        2 => 1.5f,
        3 => 2.2f,
        _ => 1.0f + (level - 1) * 0.5f,
    };

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        float lvlMul   = LevelMultiplier(spellLevel);
        float range    = Range * GetRangeMultiplier(spellLevel);
        var   origin   = caster.SidedPos.XYZ.Add(0, 0.5, 0);
        var   lookDir  = caster.SidedPos.GetViewVector().ToVec3d().Normalize();

        world.GetEntitiesAround(origin, range + 1, range + 1, e =>
        {
            if (e.EntityId == caster.EntityId) return false;
            if (e is not EntityAgent agent)    return false;

            Vec3d targetPos = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0);
            Vec3d toEntity  = targetPos - origin;
            double dist     = toEntity.Length();
            if (dist > range) return false;
            if (lookDir.Dot(toEntity.Normalize()) < CosAngle) return false;

            // LOS check — skip if solid block in the way
            BlockSelection? bsel = null; EntitySelection? esel = null;
            world.RayTraceForSelection(origin, targetPos, ref bsel, ref esel);
            if (bsel != null) return false;

            float weight  = Math.Max(agent.Properties?.Weight ?? 40f, 1f);
            float falloff = 1f - (float)(dist / range) * 0.5f;
            float force   = BaseForce * lvlMul / weight * falloff;

            agent.SidedPos.Motion.Add(lookDir.X * force, 0.15f * falloff, lookDir.Z * force);
            return false;
        });

        SpawnWindParticles(world, origin, lookDir, spellLevel, range);
    }

    public static void SpawnWindParticles(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1, float? scaledRange = null)
    {
        origin = origin.AddCopy(lookDir.X * 0.6, 0.4, lookDir.Z * 0.6);
        float  range = scaledRange ?? Range;
        int    mult  = 1 + (spellLevel - 1) / 4;
        Vec3d  right  = lookDir.Cross(Up).Normalize();
        Vec3d  upPerp = lookDir.Cross(right).Normalize();
        var    rng    = world.Rand;
        var    p      = Pool;

        // Shared defaults
        p.ParticleModel        = EnumParticleModel.Quad;
        p.ShouldDieInLiquid    = false;
        p.MinQuantity          = 1;
        p.AddQuantity          = 0;

        // ── 1. Spiral arms ────────────────────────────────────────────────────────
        p.WithTerrainCollision = false; // fast-moving, short-lived — collision not worth it
        p.AddVelocity          = new Vec3f(0.3f, 0.1f, 0.3f);
        p.GravityEffect        = -0.04f;

        for (int arm = 0; arm < 3 * mult; arm++)
        {
            double armOffset = arm * (2.0 * Math.PI / 3.0);
            for (int i = 0; i < 48; i++)
            {
                double t      = (double)i / 48;
                double dist   = range * t;
                double twist  = armOffset + t * Math.PI * 5.0;
                double spread = TanAngle * dist * 0.7;
                double cosT   = Math.Cos(twist);
                double sinT   = Math.Sin(twist);

                Vec3d pos = origin
                          + lookDir * dist
                          + right   * (cosT * spread)
                          + upPerp  * (sinT * spread);

                const double swirl = 3.5;
                p.MinPos      = new Vec3d(pos.X - 0.05, pos.Y - 0.05, pos.Z - 0.05);
                p.AddPos      = new Vec3d(0.10, 0.10, 0.10);
                p.MinVelocity = new Vec3f(
                    (float)(lookDir.X * 8.0 - sinT * right.X * swirl + cosT * upPerp.X * swirl),
                    (float)(lookDir.Y * 4.0 - sinT * right.Y * swirl + cosT * upPerp.Y * swirl),
                    (float)(lookDir.Z * 8.0 - sinT * right.Z * swirl + cosT * upPerp.Z * swirl));
                p.LifeLength  = 0.18f + (float)(t * 0.15f);
                p.MinSize     = 0.10f + (float)(t * 0.20f);
                p.MaxSize     = 0.28f + (float)(t * 0.25f);
                p.Color       = ColorUtil.ColorFromRgba(220, 240, 255, (int)(180 - t * 120));

                world.SpawnParticles(p);
            }
        }

        // ── 2. Burst ring at origin ───────────────────────────────────────────────
        p.WithTerrainCollision = true;
        p.GravityEffect        = -0.06f;
        p.AddVelocity          = new Vec3f(0.5f, 0.3f, 0.5f);
        p.MinSize              = 0.15f;
        p.MaxSize              = 0.45f;
        p.AddPos               = new Vec3d(0.06, 0.06, 0.06);

        for (int i = 0; i < 32; i++)
        {
            double a        = i * 2.0 * Math.PI / 32;
            double cosA     = Math.Cos(a);
            double sinA     = Math.Sin(a);
            double radius   = 0.3 + rng.NextDouble() * 0.3;
            double outSpeed = 5.0 + rng.NextDouble() * 3.0;

            Vec3d pos = origin + right * (cosA * radius) + upPerp * (sinA * radius);
            p.MinPos      = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 4.0 + cosA * right.X * outSpeed + sinA * upPerp.X * outSpeed),
                (float)(lookDir.Y * 2.0 + cosA * right.Y * outSpeed + sinA * upPerp.Y * outSpeed),
                (float)(lookDir.Z * 4.0 + cosA * right.Z * outSpeed + sinA * upPerp.Z * outSpeed));
            p.LifeLength = 0.22f + (float)(rng.NextDouble() * 0.12f);
            p.Color      = ColorUtil.ColorFromRgba(200, 230, 255, 160 + (int)(rng.NextDouble() * 80));

            world.SpawnParticles(p);
        }

        // ── 3. Rim shockwave ring ─────────────────────────────────────────────────
        p.GravityEffect        = 0f;
        p.AddVelocity          = new Vec3f(0.4f, 0.1f, 0.4f);
        p.MinSize              = 0.08f;
        p.MaxSize              = 0.30f;
        p.AddPos               = new Vec3d(0.08, 0.08, 0.08);
        double rimSpread       = TanAngle * range;

        for (int i = 0; i < 48; i++)
        {
            double a    = i * 2.0 * Math.PI / 48;
            double cosA = Math.Cos(a);
            double sinA = Math.Sin(a);
            double jit  = 0.85 + rng.NextDouble() * 0.3;

            Vec3d pos = origin
                      + lookDir * (range * jit)
                      + right   * (cosA * rimSpread * jit)
                      + upPerp  * (sinA * rimSpread * jit);

            p.MinPos      = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 3.0 + cosA * right.X * 2.5 + sinA * upPerp.X * 2.5),
                (float)(0.05f + rng.NextDouble() * 0.15f),
                (float)(lookDir.Z * 3.0 + cosA * right.Z * 2.5 + sinA * upPerp.Z * 2.5));
            p.LifeLength = 0.14f + (float)(rng.NextDouble() * 0.10f);
            p.Color      = ColorUtil.ColorFromRgba(235, 248, 255, 100 + (int)(rng.NextDouble() * 80));

            world.SpawnParticles(p);
        }

        // ── 4. Dense wind gust core ───────────────────────────────────────────────
        p.WithTerrainCollision = false;
        p.GravityEffect        = -0.05f;
        p.AddVelocity          = new Vec3f(0.3f, 0.2f, 0.3f);
        p.MinSize              = 0.08f;
        p.MaxSize              = 0.25f;
        p.AddPos               = new Vec3d(0.08, 0.08, 0.08);

        for (int i = 0; i < 200 * mult; i++)
        {
            double t           = rng.NextDouble();
            double spreadWidth = 0.5 + rng.NextDouble() * 0.3;

            Vec3d pos = origin + lookDir * (range * t)
                       + right  * ((rng.NextDouble() - 0.5) * spreadWidth)
                       + upPerp * ((rng.NextDouble() - 0.5) * spreadWidth);

            p.MinPos      = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 12.0 + (rng.NextDouble() - 0.5) * 2.5),
                (float)(lookDir.Y *  3.0 + (rng.NextDouble() - 0.5) * 1.0),
                (float)(lookDir.Z * 12.0 + (rng.NextDouble() - 0.5) * 2.5));
            p.LifeLength = 0.12f + (float)(rng.NextDouble() * 0.10f);
            p.Color      = ColorUtil.ColorFromRgba(220, 245, 255, 120 + (int)(rng.NextDouble() * 80));

            world.SpawnParticles(p);
        }
    }
}
