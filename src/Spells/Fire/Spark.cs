using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Fire;

public class Spark : Spell
{
    public override string Id          => "fire_spark";
    public override string Name        => "Spark";
    public override string Description => "Strike the air with fire, sending a burst of damaging sparks forward.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 15f;
    public override float CastTime => 1.0f;

    public override (int col, int row) TreePosition => (2, 0);

    public const float Range        = 6f;
    public const float ConeAngleDeg = 40f;
    public const float Damage       = 4f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var origin   = caster.SidedPos.XYZ.Add(0, 0.9, 0);
        var lookDir  = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        float cosAngle = (float)Math.Cos(ConeAngleDeg * Math.PI / 180.0);

        world.GetEntitiesAround(origin, Range + 1, Range + 1, e =>
        {
            if (e.EntityId == caster.EntityId) return false;
            if (e is not EntityAgent) return false;
            Vec3d toEntity = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0) - origin;
            if (toEntity.Length() > Range) return false;
            if (lookDir.Dot(toEntity.Clone().Normalize()) < cosAngle) return false;

            e.ReceiveDamage(new DamageSource
            {
                Source       = EnumDamageSource.Entity,
                SourceEntity = caster,
                Type         = EnumDamageType.Fire,
            }, Damage);
            return false;
        });
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir)
    {
        Vec3d right  = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d upPerp = lookDir.Cross(right).Normalize();
        var   rng    = world.Rand;
        double tanA  = Math.Tan(ConeAngleDeg * Math.PI / 180.0);

        // ColorFromRgba returns RGBA but VS particle system expects BGRA — swap R and B
        int[] fireColors = {
            ColorUtil.ColorFromRgba(180, 240, 255, 255),  // white-hot
            ColorUtil.ColorFromRgba( 60, 210, 255, 255),  // bright yellow
            ColorUtil.ColorFromRgba( 20, 140, 255, 255),  // orange-yellow
            ColorUtil.ColorFromRgba(  5,  80, 255, 255),  // orange
            ColorUtil.ColorFromRgba(  0,  30, 220, 240),  // deep orange-red
            ColorUtil.ColorFromRgba(  0,  10, 180, 220),  // red
        };

        // ── 1. Dense spark shower — scatter sideways, land and linger ───────────
        for (int i = 0; i < 140; i++)
        {
            double t      = rng.NextDouble();
            double dist   = Range * t;
            double spread = tanA * dist;
            double a      = rng.NextDouble() * 2 * Math.PI;
            double r      = Math.Sqrt(rng.NextDouble()) * spread;

            Vec3d pos = origin
                      + lookDir * dist
                      + right   * (Math.Cos(a) * r)
                      + upPerp  * (Math.Sin(a) * r);

            double scatterA = rng.NextDouble() * 2 * Math.PI;
            double scatterS = 2.0 + rng.NextDouble() * 4.0;
            int    col      = fireColors[rng.Next(fireColors.Length)];

            // Sparks that land: slow ones with high gravity linger on ground
            bool linger = rng.NextDouble() < 0.4;

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos        = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos        = new Vec3d(0.05, 0.05, 0.05),
                MinVelocity   = new Vec3f(
                    (float)(lookDir.X * 5.0 + Math.Cos(scatterA) * right.X * scatterS + Math.Sin(scatterA) * upPerp.X * scatterS),
                    (float)(lookDir.Y * 2.0 + Math.Sin(scatterA) * scatterS * 0.5 + (linger ? 1.0 : 0.2)),
                    (float)(lookDir.Z * 5.0 + Math.Cos(scatterA) * right.Z * scatterS + Math.Sin(scatterA) * upPerp.Z * scatterS)),
                AddVelocity   = new Vec3f(0.4f, 0.3f, 0.4f),
                MinQuantity   = 1,
                LifeLength    = linger
                    ? 0.8f  + (float)(rng.NextDouble() * 1.2f)   // lingers 0.8–2s
                    : 0.05f + (float)(rng.NextDouble() * 0.08f), // fast flying spark
                MinSize       = linger ? 0.06f : 0.03f,
                MaxSize       = linger ? 0.18f : 0.12f,
                GravityEffect = linger ? 1.5f : 0.6f,
                Color         = col,
                ParticleModel        = EnumParticleModel.Quad,
                WithTerrainCollision = true,
                ShouldDieInLiquid    = true,
            });
        }

        // ── 2. Hot core streaks — fast white/yellow bolts along axis ────────────
        for (int i = 0; i < 50; i++)
        {
            double t   = rng.NextDouble();
            Vec3d  pos = origin + lookDir * (Range * t)
                       + right   * ((rng.NextDouble() - 0.5) * 0.25)
                       + upPerp  * ((rng.NextDouble() - 0.5) * 0.25);

            int col = rng.NextDouble() < 0.5
                ? ColorUtil.ColorFromRgba( 60, 220, 255, 255)  // yellow-white (B,G,R,A)
                : ColorUtil.ColorFromRgba(180, 240, 255, 255); // white-hot

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos        = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos        = new Vec3d(0.02, 0.02, 0.02),
                MinVelocity   = new Vec3f(
                    (float)(lookDir.X * 16.0 + (rng.NextDouble() - 0.5) * 2.0),
                    (float)(lookDir.Y *  7.0 + (rng.NextDouble() - 0.5) * 1.0),
                    (float)(lookDir.Z * 16.0 + (rng.NextDouble() - 0.5) * 2.0)),
                AddVelocity   = new Vec3f(0.2f, 0.1f, 0.2f),
                MinQuantity   = 1,
                LifeLength    = 0.03f + (float)(rng.NextDouble() * 0.04f),
                MinSize       = 0.03f,
                MaxSize       = 0.10f,
                GravityEffect = 0.05f,
                Color         = col,
                ParticleModel        = EnumParticleModel.Quad,
                WithTerrainCollision = false,
            });
        }

        // ── 3. Burst ring at origin — initial strike flash ───────────────────────
        for (int i = 0; i < 28; i++)
        {
            double a        = i * 2.0 * Math.PI / 28 + rng.NextDouble() * 0.15;
            double outSpeed = 5.0 + rng.NextDouble() * 5.0;
            Vec3d  pos      = origin
                            + right   * (Math.Cos(a) * 0.12)
                            + upPerp  * (Math.Sin(a) * 0.12);
            int    col      = fireColors[rng.Next(fireColors.Length)];

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos        = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos        = new Vec3d(0.04, 0.04, 0.04),
                MinVelocity   = new Vec3f(
                    (float)(lookDir.X * 4.0 + Math.Cos(a) * right.X * outSpeed + Math.Sin(a) * upPerp.X * outSpeed),
                    (float)(Math.Sin(a) * outSpeed * 0.4 + 0.8),
                    (float)(lookDir.Z * 4.0 + Math.Cos(a) * right.Z * outSpeed + Math.Sin(a) * upPerp.Z * outSpeed)),
                AddVelocity   = new Vec3f(0.4f, 0.3f, 0.4f),
                MinQuantity   = 1,
                LifeLength    = 0.4f + (float)(rng.NextDouble() * 0.6f),
                MinSize       = 0.08f,
                MaxSize       = 0.22f,
                GravityEffect = 1.2f,
                Color         = col,
                ParticleModel        = EnumParticleModel.Quad,
                WithTerrainCollision = true,
                ShouldDieInLiquid    = true,
            });
        }

        // ── 4. Falling ember sparks — bright tiny quads that drop with gravity ──
        for (int i = 0; i < 50; i++)
        {
            double t      = rng.NextDouble();
            double dist   = Range * t;
            double spread = tanA * dist;
            double a      = rng.NextDouble() * 2 * Math.PI;
            double r      = Math.Sqrt(rng.NextDouble()) * spread;

            Vec3d pos = origin
                      + lookDir * dist
                      + right   * (Math.Cos(a) * r)
                      + upPerp  * (Math.Sin(a) * r);

            bool isWhite = rng.NextDouble() < 0.3;
            int col = isWhite
                ? ColorUtil.ColorFromRgba(180, 240, 255, 255)  // white-hot (B,G,R,A)
                : ColorUtil.ColorFromRgba( 10,  80, 255, 255); // orange ember

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos        = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos        = new Vec3d(0.05, 0.05, 0.05),
                MinVelocity   = new Vec3f(
                    (float)(lookDir.X * 2.0 + (rng.NextDouble() - 0.5) * 1.0),
                    (float)(0.5 + rng.NextDouble() * 1.0),
                    (float)(lookDir.Z * 2.0 + (rng.NextDouble() - 0.5) * 1.0)),
                AddVelocity   = new Vec3f(0.3f, 0.2f, 0.3f),
                MinQuantity   = 1,
                LifeLength    = 0.6f + (float)(rng.NextDouble() * 0.8f),
                MinSize       = 0.04f,
                MaxSize       = 0.10f,
                GravityEffect = 1.5f,
                Color         = col,
                ParticleModel        = EnumParticleModel.Quad,
                WithTerrainCollision = true,
                ShouldDieInLiquid    = true,
            });
        }
    }
}
