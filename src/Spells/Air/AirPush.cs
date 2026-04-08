using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

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

    public override (int col, int row) TreePosition => (2, 0);

    public const float Range        = 7f;
    public const float ConeAngleDeg = 50f;
    // Base horizontal impulse applied to a 1kg entity at point-blank, full falloff
    // Level multiplier: lvl1=1.0, lvl2=1.5, lvl3=2.2
    private const float BaseForce = 1.2f;

    private static float LevelMultiplier(int level) => level switch
    {
        1 => 1.0f,
        2 => 1.5f,
        3 => 2.2f,
        _ => 1.0f + (level - 1) * 0.5f,
    };

    public override void Execute(EntityAgent caster, IWorldAccessor world)
    {
        int level    = PlayerSpellData.For(caster).GetSpellLevel(Id);
        float lvlMul = LevelMultiplier(level);

        var origin   = caster.SidedPos.XYZ.Add(0, 0.5, 0);
        var lookDir  = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        float cosAngle = (float)Math.Cos(ConeAngleDeg * Math.PI / 180.0);

        world.GetEntitiesAround(origin, Range + 1, Range + 1, e =>
        {
            if (e.EntityId == caster.EntityId) return false;
            if (e is not EntityAgent agent) return false;

            Vec3d toEntity = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0) - origin;
            double dist = toEntity.Length();
            if (dist > Range) return false;
            if (lookDir.Dot(toEntity.Clone().Normalize()) < cosAngle) return false;

            float weight  = Math.Max(agent.Properties?.Weight ?? 40f, 1f);
            float falloff = 1f - (float)(dist / Range) * 0.5f;
            float force   = BaseForce * lvlMul / weight * falloff;

            agent.SidedPos.Motion.Add(
                lookDir.X * force,
                0.15f * falloff,
                lookDir.Z * force);

            return false;
        });

        SpawnWindParticles(world, origin, lookDir);
    }

    public static void SpawnWindParticles(IWorldAccessor world, Vec3d origin, Vec3d lookDir)
    {
        Vec3d right  = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d upPerp = lookDir.Cross(right).Normalize();
        var   rng    = world.Rand;
        double tanA  = Math.Tan(ConeAngleDeg * Math.PI / 180.0);

        // ── 1. Spiral arms — 3 arms rotating around the look axis ──────────────
        // Each arm is a helix from origin to Range, twisted ~2.5 turns
        for (int arm = 0; arm < 3; arm++)
        {
            double armOffset = arm * (2.0 * Math.PI / 3.0);
            int    pts       = 48;
            for (int i = 0; i < pts; i++)
            {
                double t      = (double)i / pts;
                double dist   = Range * t;
                double twist  = armOffset + t * Math.PI * 5.0; // 2.5 full turns
                double spread = tanA * dist * 0.7;             // tighter than cone edge

                Vec3d pos = origin
                          + lookDir * dist
                          + right   * (Math.Cos(twist) * spread)
                          + upPerp  * (Math.Sin(twist) * spread);

                // velocity: forward + tangential swirl
                double swirl = 3.5;
                Vec3f vel = new Vec3f(
                    (float)(lookDir.X * 8.0 - Math.Sin(twist) * right.X * swirl + Math.Cos(twist) * upPerp.X * swirl),
                    (float)(lookDir.Y * 4.0 - Math.Sin(twist) * right.Y * swirl + Math.Cos(twist) * upPerp.Y * swirl),
                    (float)(lookDir.Z * 8.0 - Math.Sin(twist) * right.Z * swirl + Math.Cos(twist) * upPerp.Z * swirl));

                float alpha = (float)(180 - t * 120);
                world.SpawnParticles(new SimpleParticleProperties
                {
                    MinPos               = new Vec3d(pos.X - 0.05, pos.Y - 0.05, pos.Z - 0.05),
                    AddPos               = new Vec3d(0.10, 0.10, 0.10),
                    MinVelocity          = vel,
                    AddVelocity          = new Vec3f(0.3f, 0.1f, 0.3f),
                    MinQuantity          = 1,
                    LifeLength           = 0.18f + (float)(t * 0.15f),
                    MinSize              = 0.10f + (float)(t * 0.20f),
                    MaxSize              = 0.28f + (float)(t * 0.25f),
                    GravityEffect        = -0.04f,
                    Color                = ColorUtil.ColorFromRgba(220, 240, 255, (int)alpha),
                    ParticleModel        = EnumParticleModel.Quad,
                    WithTerrainCollision = false,
                    ShouldDieInLiquid    = false,
                });
            }
        }

        // ── 2. Burst ring at origin — expands outward radially ──────────────────
        {
            int pts = 32;
            for (int i = 0; i < pts; i++)
            {
                double a      = i * 2.0 * Math.PI / pts;
                double radius = 0.3 + rng.NextDouble() * 0.3;
                Vec3d  pos    = origin
                              + right   * (Math.Cos(a) * radius)
                              + upPerp  * (Math.Sin(a) * radius);

                // velocity fans outward from origin in the cast plane
                double outSpeed = 5.0 + rng.NextDouble() * 3.0;
                Vec3f vel = new Vec3f(
                    (float)(lookDir.X * 4.0 + Math.Cos(a) * right.X * outSpeed + Math.Sin(a) * upPerp.X * outSpeed),
                    (float)(lookDir.Y * 2.0 + Math.Cos(a) * right.Y * outSpeed + Math.Sin(a) * upPerp.Y * outSpeed),
                    (float)(lookDir.Z * 4.0 + Math.Cos(a) * right.Z * outSpeed + Math.Sin(a) * upPerp.Z * outSpeed));

                world.SpawnParticles(new SimpleParticleProperties
                {
                    MinPos               = new Vec3d(pos.X, pos.Y, pos.Z),
                    AddPos               = new Vec3d(0.06, 0.06, 0.06),
                    MinVelocity          = vel,
                    AddVelocity          = new Vec3f(0.5f, 0.3f, 0.5f),
                    MinQuantity          = 1,
                    LifeLength           = 0.22f + (float)(rng.NextDouble() * 0.12f),
                    MinSize              = 0.15f,
                    MaxSize              = 0.45f,
                    GravityEffect        = -0.06f,
                    Color                = ColorUtil.ColorFromRgba(200, 230, 255, 160 + (int)(rng.NextDouble() * 80)),
                    ParticleModel        = EnumParticleModel.Quad,
                    WithTerrainCollision = false,
                    ShouldDieInLiquid    = false,
                });
            }
        }

        // ── 3. Rim shockwave ring at max range ───────────────────────────────────
        {
            double rimSpread = tanA * Range;
            int    pts       = 48;
            for (int i = 0; i < pts; i++)
            {
                double a   = i * 2.0 * Math.PI / pts;
                double jit = 0.85 + rng.NextDouble() * 0.3;
                Vec3d  pos = origin
                           + lookDir * Range * jit
                           + right   * (Math.Cos(a) * rimSpread * jit)
                           + upPerp  * (Math.Sin(a) * rimSpread * jit);

                Vec3f vel = new Vec3f(
                    (float)(lookDir.X * 3.0 + Math.Cos(a) * right.X * 2.5 + Math.Sin(a) * upPerp.X * 2.5),
                    (float)(0.05f + rng.NextDouble() * 0.15f),
                    (float)(lookDir.Z * 3.0 + Math.Cos(a) * right.Z * 2.5 + Math.Sin(a) * upPerp.Z * 2.5));

                world.SpawnParticles(new SimpleParticleProperties
                {
                    MinPos               = new Vec3d(pos.X, pos.Y, pos.Z),
                    AddPos               = new Vec3d(0.08, 0.08, 0.08),
                    MinVelocity          = vel,
                    AddVelocity          = new Vec3f(0.4f, 0.1f, 0.4f),
                    MinQuantity          = 1,
                    LifeLength           = 0.14f + (float)(rng.NextDouble() * 0.10f),
                    MinSize              = 0.08f,
                    MaxSize              = 0.30f,
                    GravityEffect        = 0f,
                    Color                = ColorUtil.ColorFromRgba(235, 248, 255, 100 + (int)(rng.NextDouble() * 80)),
                    ParticleModel        = EnumParticleModel.Quad,
                    WithTerrainCollision = false,
                    ShouldDieInLiquid    = false,
                });
            }
        }

        // ── 4. Dense bright core streaks along center axis ───────────────────────
        for (int i = 0; i < 40; i++)
        {
            double t   = rng.NextDouble();
            Vec3d  pos = origin + lookDir * (Range * t)
                       + right   * ((rng.NextDouble() - 0.5) * 0.25)
                       + upPerp  * ((rng.NextDouble() - 0.5) * 0.25);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos               = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos               = new Vec3d(0.04, 0.04, 0.04),
                MinVelocity          = new Vec3f(
                    (float)(lookDir.X * 10.0 + (rng.NextDouble() - 0.5) * 1.0),
                    (float)(lookDir.Y *  4.0 + (rng.NextDouble() - 0.5) * 0.5),
                    (float)(lookDir.Z * 10.0 + (rng.NextDouble() - 0.5) * 1.0)),
                AddVelocity          = new Vec3f(0.2f, 0.1f, 0.2f),
                MinQuantity          = 1,
                LifeLength           = 0.08f + (float)(rng.NextDouble() * 0.08f),
                MinSize              = 0.04f,
                MaxSize              = 0.14f,
                GravityEffect        = 0f,
                Color                = ColorUtil.ColorFromRgba(240, 250, 255, 200 + (int)(rng.NextDouble() * 55)),
                ParticleModel        = EnumParticleModel.Quad,
                WithTerrainCollision = false,
            });
        }
    }
}
