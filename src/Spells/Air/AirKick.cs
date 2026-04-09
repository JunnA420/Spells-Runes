using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
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
    public override float CastTime => 1.5f;

    public override (int col, int row) TreePosition => (1, 1);

    public override IReadOnlyList<string> Prerequisites => new[] { "air_feather_fall", "air_air_push" };

    public const float UpForce      = 0.37f; // initial upward impulse (2/3 of original 0.55)
    public const float ForwardForce = 1.10f; // horizontal dash force

    public const float ProjectileSpeed  = 28f;
    public const float ProjectileRadius = 0.5f;
    public const float ImpactDamage    = 12f;
    public const float MaxRange        = 20f;
    public const float LaunchKnockbackRadius = 3f;
    public const float LaunchKnockbackForce  = 0.6f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        float dmgMul = GetDamageMultiplier(spellLevel);

        // Knockback nearby entities at launch
        var origin = caster.SidedPos.XYZ.Add(0, 0.5, 0);
        world.GetEntitiesAround(origin, LaunchKnockbackRadius, LaunchKnockbackRadius, e =>
        {
            if (e.EntityId == caster.EntityId) return false;
            if (e is not EntityAgent) return false;
            Vec3d dir = (e.SidedPos.XYZ - origin);
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
        int mult = 1 + (spellLevel - 1) / 4;  // 1x @ lvl1-4, 2x @ lvl5-8, 3x @ lvl9-10
        var rng = world.Rand;
        Vec3d right  = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d upPerp = lookDir.Cross(right).Normalize();

        // Dense core — tightly packed sphere of compressed air
        for (int i = 0; i < 22 * mult; i++)
        {
            double a = rng.NextDouble() * 2 * Math.PI;
            double b = rng.NextDouble() * 2 * Math.PI;
            double r = rng.NextDouble() * 0.55;
            Vec3d pos = origin
                + right   * (Math.Cos(a) * r)
                + upPerp  * (Math.Sin(a) * r)
                + lookDir * ((rng.NextDouble() - 0.5) * 0.4);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos        = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos        = new Vec3d(0.04, 0.04, 0.04),
                MinVelocity   = new Vec3f(
                    (float)(lookDir.X * 4.0 + Math.Cos(a) * right.X * 1.5 + Math.Sin(b) * upPerp.X * 1.5),
                    (float)(lookDir.Y * 4.0 + Math.Sin(b) * 1.5),
                    (float)(lookDir.Z * 4.0 + Math.Cos(a) * right.Z * 1.5 + Math.Sin(b) * upPerp.Z * 1.5)),
                AddVelocity   = new Vec3f(0.3f, 0.3f, 0.3f),
                MinQuantity   = 1,
                LifeLength    = 0.08f + (float)(rng.NextDouble() * 0.06f),
                MinSize       = 0.18f,
                MaxSize       = 0.55f,
                GravityEffect = 0f,
                Color         = ColorUtil.ColorFromRgba(230, 248, 255, 170 + (int)(rng.NextDouble() * 85)),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid    = false,
            });
        }

        // Bright compressed core center
        for (int i = 0; i < 8; i++)
        {
            Vec3d pos = origin + lookDir * ((rng.NextDouble() - 0.5) * 0.2)
                + right  * ((rng.NextDouble() - 0.5) * 0.15)
                + upPerp * ((rng.NextDouble() - 0.5) * 0.15);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos        = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos        = new Vec3d(0.02, 0.02, 0.02),
                MinVelocity   = new Vec3f((float)(lookDir.X * 6), (float)(lookDir.Y * 6), (float)(lookDir.Z * 6)),
                AddVelocity   = new Vec3f(0.1f, 0.1f, 0.1f),
                MinQuantity   = 1,
                LifeLength    = 0.05f + (float)(rng.NextDouble() * 0.04f),
                MinSize       = 0.06f,
                MaxSize       = 0.16f,
                GravityEffect = 0f,
                Color         = ColorUtil.ColorFromRgba(250, 253, 255, 230 + (int)(rng.NextDouble() * 25)),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
            });
        }

        // Spinning turbulence ring around the ball
        int pts = 16;
        for (int i = 0; i < pts; i++)
        {
            double a      = i * 2.0 * Math.PI / pts + rng.NextDouble() * 0.3;
            double radius = 0.6 + rng.NextDouble() * 0.25;
            Vec3d  pos    = origin + right * (Math.Cos(a) * radius) + upPerp * (Math.Sin(a) * radius);
            double swirl  = 4.0;

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos        = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos        = new Vec3d(0.05, 0.05, 0.05),
                MinVelocity   = new Vec3f(
                    (float)(lookDir.X * 3.0 - Math.Sin(a) * right.X * swirl + Math.Cos(a) * upPerp.X * swirl),
                    (float)(lookDir.Y * 3.0 - Math.Sin(a) * right.Y * swirl + Math.Cos(a) * upPerp.Y * swirl),
                    (float)(lookDir.Z * 3.0 - Math.Sin(a) * right.Z * swirl + Math.Cos(a) * upPerp.Z * swirl)),
                AddVelocity   = new Vec3f(0.2f, 0.2f, 0.2f),
                MinQuantity   = 1,
                LifeLength    = 0.07f + (float)(rng.NextDouble() * 0.05f),
                MinSize       = 0.10f,
                MaxSize       = 0.30f,
                GravityEffect = 0f,
                Color         = ColorUtil.ColorFromRgba(210, 240, 255, 120 + (int)(rng.NextDouble() * 80)),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
            });
        }
    }

    /// <summary>Tiny impact puff — only visible on direct hit.</summary>
    public static void SpawnImpactFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1)
    {
        int mult = 1 + (spellLevel - 1) / 4;
        var rng = world.Rand;
        for (int i = 0; i < 12 * mult; i++)
        {
            double a = rng.NextDouble() * 2 * Math.PI;
            double b = rng.NextDouble() * 2 * Math.PI;
            double speed = 2.0 + rng.NextDouble() * 2.5;
            Vec3d dir = new Vec3d(Math.Cos(a) * Math.Cos(b), Math.Sin(b), Math.Sin(a) * Math.Cos(b));
            Vec3d pos = origin + dir * (rng.NextDouble() * 0.15);
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos        = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos        = new Vec3d(0.03, 0.03, 0.03),
                MinVelocity   = new Vec3f((float)(dir.X * speed), (float)(dir.Y * speed), (float)(dir.Z * speed)),
                AddVelocity   = new Vec3f(0.15f, 0.15f, 0.15f),
                MinQuantity   = 1,
                LifeLength    = 0.08f + (float)(rng.NextDouble() * 0.06f),
                MinSize       = 0.06f,
                MaxSize       = 0.18f,
                GravityEffect = 0f,
                Color         = ColorUtil.ColorFromRgba(240, 250, 255, 180 + (int)(rng.NextDouble() * 60)),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
            });
        }
    }

    /// <summary>Launch FX at caster feet when Air Kick is cast.</summary>
    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir)
    {
        Vec3d right  = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d upPerp = lookDir.Cross(right).Normalize();
        var   rng    = world.Rand;

        // ── 1. Downward ground blast (compressed air pillar straight down) ───────
        for (int i = 0; i < 60; i++)
        {
            double angle  = rng.NextDouble() * 2 * Math.PI;
            double radius = Math.Sqrt(rng.NextDouble()) * 0.6;
            double speed  = 7.0 + rng.NextDouble() * 6.0;

            Vec3d pos = origin + new Vec3d(
                Math.Cos(angle) * radius * 0.4,
                -0.05,
                Math.Sin(angle) * radius * 0.4);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos            = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos            = new Vec3d(0.08, 0.04, 0.08),
                MinVelocity       = new Vec3f(
                    (float)(Math.Cos(angle) * radius * 4.0),
                    (float)(-speed),
                    (float)(Math.Sin(angle) * radius * 4.0)),
                AddVelocity       = new Vec3f(0.5f, 1.0f, 0.5f),
                MinQuantity       = 1,
                LifeLength        = 0.14f + (float)(rng.NextDouble() * 0.10f),
                MinSize           = 0.10f,
                MaxSize           = 0.40f,
                GravityEffect     = 0.4f,
                Color             = ColorUtil.ColorFromRgba(225, 245, 255, 150 + (int)(rng.NextDouble() * 90)),
                ParticleModel     = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false,
            });
        }

        // ── 2. Radial shockwave ring at feet expanding outward ───────────────────
        for (int i = 0; i < 48; i++)
        {
            double angle    = i * 2.0 * Math.PI / 48 + rng.NextDouble() * 0.1;
            double outSpeed = 6.0 + rng.NextDouble() * 4.0;
            Vec3d  pos      = origin + new Vec3d(Math.Cos(angle) * 0.15, -0.1, Math.Sin(angle) * 0.15);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos            = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos            = new Vec3d(0.05, 0.05, 0.05),
                MinVelocity       = new Vec3f(
                    (float)(Math.Cos(angle) * outSpeed),
                    (float)(0.1 + rng.NextDouble() * 0.3),
                    (float)(Math.Sin(angle) * outSpeed)),
                AddVelocity       = new Vec3f(0.3f, 0.2f, 0.3f),
                MinQuantity       = 1,
                LifeLength        = 0.20f + (float)(rng.NextDouble() * 0.10f),
                MinSize           = 0.12f,
                MaxSize           = 0.45f,
                GravityEffect     = -0.02f,
                Color             = ColorUtil.ColorFromRgba(210, 238, 255, 130 + (int)(rng.NextDouble() * 80)),
                ParticleModel     = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false,
            });
        }

        // ── 3. Forward launch trail — dense streak along look direction ──────────
        for (int i = 0; i < 50; i++)
        {
            double t    = rng.NextDouble();
            double dist = t * 4.0; // trail extends 4m forward
            Vec3d  pos  = origin
                        + lookDir * dist
                        + right   * ((rng.NextDouble() - 0.5) * 0.3)
                        + upPerp  * ((rng.NextDouble() - 0.5) * 0.3 + t * 0.5);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos            = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos            = new Vec3d(0.05, 0.05, 0.05),
                MinVelocity       = new Vec3f(
                    (float)(lookDir.X * 12.0 + (rng.NextDouble() - 0.5) * 1.5),
                    (float)(1.5 + rng.NextDouble() * 1.5),
                    (float)(lookDir.Z * 12.0 + (rng.NextDouble() - 0.5) * 1.5)),
                AddVelocity       = new Vec3f(0.3f, 0.2f, 0.3f),
                MinQuantity       = 1,
                LifeLength        = 0.10f + (float)(rng.NextDouble() * 0.08f),
                MinSize           = 0.06f,
                MaxSize           = 0.22f,
                GravityEffect     = 0f,
                Color             = ColorUtil.ColorFromRgba(240, 250, 255, 190 + (int)(rng.NextDouble() * 65)),
                ParticleModel     = EnumParticleModel.Quad,
                WithTerrainCollision = false,
            });
        }

        // ── 4. Bright upward core burst ──────────────────────────────────────────
        for (int i = 0; i < 25; i++)
        {
            Vec3d pos = origin + new Vec3d(
                (rng.NextDouble() - 0.5) * 0.2,
                rng.NextDouble() * 0.5,
                (rng.NextDouble() - 0.5) * 0.2);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos            = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos            = new Vec3d(0.04, 0.04, 0.04),
                MinVelocity       = new Vec3f(
                    (float)((rng.NextDouble() - 0.5) * 2.0),
                    (float)(8.0 + rng.NextDouble() * 5.0),
                    (float)((rng.NextDouble() - 0.5) * 2.0)),
                AddVelocity       = new Vec3f(0.2f, 0.5f, 0.2f),
                MinQuantity       = 1,
                LifeLength        = 0.12f + (float)(rng.NextDouble() * 0.08f),
                MinSize           = 0.05f,
                MaxSize           = 0.18f,
                GravityEffect     = -0.1f,
                Color             = ColorUtil.ColorFromRgba(245, 252, 255, 210 + (int)(rng.NextDouble() * 45)),
                ParticleModel     = EnumParticleModel.Quad,
                WithTerrainCollision = false,
            });
        }
    }
}
