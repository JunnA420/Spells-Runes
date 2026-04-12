using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

/// <summary>
/// Tier I Offense — Blasts the caster upward then launches them forward
/// at high speed in the direction they're looking.
/// Requires Feather Fall + Air Push.
/// </summary>
public class AirKick : Spell
{
    public override string Id          => "air_air_kick";
    public override string Name        => "Air Kick";
    public override string Description => "Blast yourself into the air and rocket forward, crushing anything in your path.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 30f;
    public override float CastTime => 0.7f;

    public override string? AnimationCode        => "air_wind_kick";
    public override bool    AnimationUpperBodyOnly => false;

    public override (int col, int row) TreePosition => (1, 1);

    public override IReadOnlyList<string> Prerequisites => new[] { "air_feather_fall", "air_air_push" };

    public const float UpForce      = 0.37f;
    public const float ForwardForce = 1.10f;

    public const float ProjectileSpeed       = 28f;
    public const float ProjectileRadius      = 0.5f;
    public const float ImpactDamage          = 12f;
    public const float MaxRange              = 20f;
    public const float LaunchKnockbackRadius = 3f;
    public const float LaunchKnockbackForce  = 0.6f;

    private static readonly Vec3d Up = new Vec3d(0, 1, 0);

    [ThreadStatic] private static SimpleParticleProperties? _pool;
    private static SimpleParticleProperties Pool => _pool ??= new SimpleParticleProperties();

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        float dmgMul = GetDamageMultiplier(spellLevel);
        var   origin = caster.SidedPos.XYZ.Add(0, 0.5, 0);

        world.GetEntitiesAround(origin, LaunchKnockbackRadius, LaunchKnockbackRadius, e =>
        {
            if (e.EntityId == caster.EntityId) return false;
            if (e is not EntityAgent) return false;
            Vec3d dir  = e.SidedPos.XYZ - origin;
            double dist = dir.Length();
            if (dist > LaunchKnockbackRadius) return false;
            dir = dir.Normalize();
            float falloff = 1f - (float)(dist / LaunchKnockbackRadius) * 0.5f;
            e.SidedPos.Motion.Add(dir.X * LaunchKnockbackForce * dmgMul * falloff, 0.2 * falloff, dir.Z * LaunchKnockbackForce * dmgMul * falloff);
            return false;
        });
    }

    /// <summary>Dense compressed air ball — called every 50ms as projectile moves.</summary>
    public static void SpawnTrailFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1)
    {
        int   mult  = 1 + (spellLevel - 1) / 4;
        var   rng   = world.Rand;
        Vec3d right  = lookDir.Cross(Up).Normalize();
        Vec3d upPerp = lookDir.Cross(right).Normalize();
        var   p     = Pool;

        p.ParticleModel     = EnumParticleModel.Quad;
        p.ShouldDieInLiquid = false;
        p.MinQuantity       = 1;
        p.AddQuantity       = 0;
        p.WithTerrainCollision = false;

        // ── Dense core ────────────────────────────────────────────────────────────
        p.GravityEffect = 0f;
        p.AddVelocity   = new Vec3f(0.3f, 0.3f, 0.3f);
        p.AddPos        = new Vec3d(0.04, 0.04, 0.04);
        p.MinSize       = 0.18f;
        p.MaxSize       = 0.55f;

        for (int i = 0; i < 22 * mult; i++)
        {
            double a    = rng.NextDouble() * 2 * Math.PI;
            double b    = rng.NextDouble() * 2 * Math.PI;
            double cosA = Math.Cos(a);
            double sinA = Math.Sin(a);
            double sinB = Math.Sin(b);
            double r    = rng.NextDouble() * 0.55;

            Vec3d pos = origin
                + right   * (cosA * r)
                + upPerp  * (sinA * r)
                + lookDir * ((rng.NextDouble() - 0.5) * 0.4);

            p.MinPos = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 4.0 + cosA * right.X * 1.5 + sinB * upPerp.X * 1.5),
                (float)(lookDir.Y * 4.0 + sinB * 1.5),
                (float)(lookDir.Z * 4.0 + cosA * right.Z * 1.5 + sinB * upPerp.Z * 1.5));
            p.LifeLength = 0.08f + (float)(rng.NextDouble() * 0.06f);
            p.Color      = ColorUtil.ColorFromRgba(230, 248, 255, 170 + (int)(rng.NextDouble() * 85));

            world.SpawnParticles(p);
        }

        // ── Bright core center ────────────────────────────────────────────────────
        p.AddVelocity = new Vec3f(0.1f, 0.1f, 0.1f);
        p.AddPos      = new Vec3d(0.02, 0.02, 0.02);
        p.MinSize     = 0.06f;
        p.MaxSize     = 0.16f;

        Vec3f coreVel = new Vec3f((float)(lookDir.X * 6), (float)(lookDir.Y * 6), (float)(lookDir.Z * 6));
        for (int i = 0; i < 8; i++)
        {
            Vec3d pos = origin
                + lookDir * ((rng.NextDouble() - 0.5) * 0.2)
                + right   * ((rng.NextDouble() - 0.5) * 0.15)
                + upPerp  * ((rng.NextDouble() - 0.5) * 0.15);

            p.MinPos      = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = coreVel;
            p.LifeLength  = 0.05f + (float)(rng.NextDouble() * 0.04f);
            p.Color       = ColorUtil.ColorFromRgba(250, 253, 255, 230 + (int)(rng.NextDouble() * 25));

            world.SpawnParticles(p);
        }

        // ── Spinning turbulence ring ──────────────────────────────────────────────
        p.AddVelocity = new Vec3f(0.2f, 0.2f, 0.2f);
        p.AddPos      = new Vec3d(0.05, 0.05, 0.05);
        p.MinSize     = 0.10f;
        p.MaxSize     = 0.30f;

        const double swirl = 4.0;
        for (int i = 0; i < 16; i++)
        {
            double a    = i * 2.0 * Math.PI / 16 + rng.NextDouble() * 0.3;
            double cosA = Math.Cos(a);
            double sinA = Math.Sin(a);
            double radius = 0.6 + rng.NextDouble() * 0.25;

            Vec3d pos = origin + right * (cosA * radius) + upPerp * (sinA * radius);

            p.MinPos = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 3.0 - sinA * right.X * swirl + cosA * upPerp.X * swirl),
                (float)(lookDir.Y * 3.0 - sinA * right.Y * swirl + cosA * upPerp.Y * swirl),
                (float)(lookDir.Z * 3.0 - sinA * right.Z * swirl + cosA * upPerp.Z * swirl));
            p.LifeLength = 0.07f + (float)(rng.NextDouble() * 0.05f);
            p.Color      = ColorUtil.ColorFromRgba(210, 240, 255, 120 + (int)(rng.NextDouble() * 80));

            world.SpawnParticles(p);
        }
    }

    /// <summary>Impact puff on direct hit.</summary>
    public static void SpawnImpactFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1)
    {
        int mult = 1 + (spellLevel - 1) / 4;
        var rng  = world.Rand;
        var p    = Pool;

        p.ParticleModel        = EnumParticleModel.Quad;
        p.WithTerrainCollision = false;
        p.MinQuantity          = 1;
        p.AddQuantity          = 0;
        p.GravityEffect        = 0f;
        p.AddVelocity          = new Vec3f(0.15f, 0.15f, 0.15f);
        p.AddPos               = new Vec3d(0.03, 0.03, 0.03);
        p.MinSize              = 0.06f;
        p.MaxSize              = 0.18f;

        for (int i = 0; i < 12 * mult; i++)
        {
            double a     = rng.NextDouble() * 2 * Math.PI;
            double b     = rng.NextDouble() * 2 * Math.PI;
            double cosB  = Math.Cos(b);
            double speed = 2.0 + rng.NextDouble() * 2.5;
            Vec3d  dir   = new Vec3d(Math.Cos(a) * cosB, Math.Sin(b), Math.Sin(a) * cosB);
            Vec3d  pos   = origin + dir * (rng.NextDouble() * 0.15);

            p.MinPos      = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f((float)(dir.X * speed), (float)(dir.Y * speed), (float)(dir.Z * speed));
            p.LifeLength  = 0.08f + (float)(rng.NextDouble() * 0.06f);
            p.Color       = ColorUtil.ColorFromRgba(240, 250, 255, 180 + (int)(rng.NextDouble() * 60));

            world.SpawnParticles(p);
        }
    }

    /// <summary>Launch FX at caster feet when Air Kick is cast.</summary>
    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir)
    {
        Vec3d right  = lookDir.Cross(Up).Normalize();
        Vec3d upPerp = lookDir.Cross(right).Normalize();
        var   rng    = world.Rand;
        var   p      = Pool;

        p.ParticleModel        = EnumParticleModel.Quad;
        p.ShouldDieInLiquid    = false;
        p.MinQuantity          = 1;
        p.AddQuantity          = 0;
        p.WithTerrainCollision = false;

        // ── 1. Downward ground blast ──────────────────────────────────────────────
        p.GravityEffect = 0.4f;
        p.AddVelocity   = new Vec3f(0.5f, 1.0f, 0.5f);
        p.AddPos        = new Vec3d(0.08, 0.04, 0.08);
        p.MinSize       = 0.10f;
        p.MaxSize       = 0.40f;

        for (int i = 0; i < 60; i++)
        {
            double angle  = rng.NextDouble() * 2 * Math.PI;
            double cosA   = Math.Cos(angle);
            double sinA   = Math.Sin(angle);
            double radius = Math.Sqrt(rng.NextDouble()) * 0.6;
            double speed  = 7.0 + rng.NextDouble() * 6.0;

            p.MinPos = new Vec3d(
                origin.X + cosA * radius * 0.4,
                origin.Y - 0.05,
                origin.Z + sinA * radius * 0.4);
            p.MinVelocity = new Vec3f(
                (float)(cosA * radius * 4.0),
                (float)(-speed),
                (float)(sinA * radius * 4.0));
            p.LifeLength = 0.14f + (float)(rng.NextDouble() * 0.10f);
            p.Color      = ColorUtil.ColorFromRgba(225, 245, 255, 150 + (int)(rng.NextDouble() * 90));

            world.SpawnParticles(p);
        }

        // ── 2. Radial shockwave ring ──────────────────────────────────────────────
        p.GravityEffect = -0.02f;
        p.AddVelocity   = new Vec3f(0.3f, 0.2f, 0.3f);
        p.AddPos        = new Vec3d(0.05, 0.05, 0.05);
        p.MinSize       = 0.12f;
        p.MaxSize       = 0.45f;

        for (int i = 0; i < 48; i++)
        {
            double angle    = i * 2.0 * Math.PI / 48 + rng.NextDouble() * 0.1;
            double cosA     = Math.Cos(angle);
            double sinA     = Math.Sin(angle);
            double outSpeed = 6.0 + rng.NextDouble() * 4.0;

            p.MinPos = new Vec3d(
                origin.X + cosA * 0.15,
                origin.Y - 0.1,
                origin.Z + sinA * 0.15);
            p.MinVelocity = new Vec3f(
                (float)(cosA * outSpeed),
                (float)(0.1 + rng.NextDouble() * 0.3),
                (float)(sinA * outSpeed));
            p.LifeLength = 0.20f + (float)(rng.NextDouble() * 0.10f);
            p.Color      = ColorUtil.ColorFromRgba(210, 238, 255, 130 + (int)(rng.NextDouble() * 80));

            world.SpawnParticles(p);
        }

        // ── 3. Forward launch trail ───────────────────────────────────────────────
        p.GravityEffect = 0f;
        p.AddVelocity   = new Vec3f(0.3f, 0.2f, 0.3f);
        p.AddPos        = new Vec3d(0.05, 0.05, 0.05);
        p.MinSize       = 0.06f;
        p.MaxSize       = 0.22f;

        for (int i = 0; i < 50; i++)
        {
            double t   = rng.NextDouble();
            double dist = t * 4.0;
            Vec3d  pos  = origin
                        + lookDir * dist
                        + right   * ((rng.NextDouble() - 0.5) * 0.3)
                        + upPerp  * ((rng.NextDouble() - 0.5) * 0.3 + t * 0.5);

            p.MinPos = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 12.0 + (rng.NextDouble() - 0.5) * 1.5),
                (float)(1.5 + rng.NextDouble() * 1.5),
                (float)(lookDir.Z * 12.0 + (rng.NextDouble() - 0.5) * 1.5));
            p.LifeLength = 0.10f + (float)(rng.NextDouble() * 0.08f);
            p.Color      = ColorUtil.ColorFromRgba(240, 250, 255, 190 + (int)(rng.NextDouble() * 65));

            world.SpawnParticles(p);
        }

        // ── 4. Bright upward core burst ───────────────────────────────────────────
        p.GravityEffect = -0.1f;
        p.AddVelocity   = new Vec3f(0.2f, 0.5f, 0.2f);
        p.AddPos        = new Vec3d(0.04, 0.04, 0.04);
        p.MinSize       = 0.05f;
        p.MaxSize       = 0.18f;

        for (int i = 0; i < 25; i++)
        {
            p.MinPos = new Vec3d(
                origin.X + (rng.NextDouble() - 0.5) * 0.2,
                origin.Y + rng.NextDouble() * 0.5,
                origin.Z + (rng.NextDouble() - 0.5) * 0.2);
            p.MinVelocity = new Vec3f(
                (float)((rng.NextDouble() - 0.5) * 2.0),
                (float)(8.0 + rng.NextDouble() * 5.0),
                (float)((rng.NextDouble() - 0.5) * 2.0));
            p.LifeLength = 0.12f + (float)(rng.NextDouble() * 0.08f);
            p.Color      = ColorUtil.ColorFromRgba(245, 252, 255, 210 + (int)(rng.NextDouble() * 45));

            world.SpawnParticles(p);
        }
    }
}
