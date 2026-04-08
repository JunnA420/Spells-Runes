using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

/// <summary>
/// Tier I Utility — Instantly arrests the caster's momentum mid-air,
/// firing a burst of compressed air downward beneath them.
/// </summary>
public class FeatherFall : Spell
{
    public override string Id          => "air_feather_fall";
    public override string Name        => "Feather Fall";
    public override string Description => "Instantly halts your fall, firing a burst of air beneath you.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 15f;
    public override float CastTime => 0.5f;

    public override (int col, int row) TreePosition => (0, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world)
    {
        // Motion is killed client-side via MsgFreezeMotion (player physics are client-authoritative)
        // Nothing needed here server-side
    }

    /// <summary>Called client-side from MsgSpellFx handler to spawn the downward air burst.</summary>
    public static void SpawnFx(IWorldAccessor world, Vec3d origin)
    {
        var rng = world.Rand;

        // ── Downward jet — compressed air cone beneath the caster ──────────────
        // Tight cone pointing straight down, fast velocity, short life
        for (int i = 0; i < 80; i++)
        {
            double angle  = rng.NextDouble() * 2 * Math.PI;
            double radius = Math.Sqrt(rng.NextDouble()) * 0.55;
            double speed  = 6.0 + rng.NextDouble() * 5.0;
            double spread = (rng.NextDouble() - 0.5) * 1.2;

            Vec3d pos = origin + new Vec3d(
                Math.Cos(angle) * radius * 0.3,
                -0.1,
                Math.Sin(angle) * radius * 0.3);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos               = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos               = new Vec3d(0.06, 0.04, 0.06),
                MinVelocity          = new Vec3f(
                    (float)(Math.Cos(angle) * radius * 3.0 + spread),
                    (float)(-speed),
                    (float)(Math.Sin(angle) * radius * 3.0 + spread)),
                AddVelocity          = new Vec3f(0.3f, 0.5f, 0.3f),
                MinQuantity          = 1,
                LifeLength           = 0.12f + (float)(rng.NextDouble() * 0.10f),
                MinSize              = 0.08f,
                MaxSize              = 0.30f,
                GravityEffect        = 0.3f,
                Color                = ColorUtil.ColorFromRgba(220, 242, 255, 140 + (int)(rng.NextDouble() * 100)),
                ParticleModel        = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid    = false,
            });
        }

        // ── Radial shockwave ring at foot level — expands outward ──────────────
        int pts = 40;
        for (int i = 0; i < pts; i++)
        {
            double angle = i * 2.0 * Math.PI / pts + rng.NextDouble() * 0.15;
            double r     = 0.1 + rng.NextDouble() * 0.2;
            Vec3d  pos   = origin + new Vec3d(Math.Cos(angle) * r, -0.15, Math.Sin(angle) * r);

            double outSpeed = 4.5 + rng.NextDouble() * 3.0;
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos               = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos               = new Vec3d(0.05, 0.05, 0.05),
                MinVelocity          = new Vec3f(
                    (float)(Math.Cos(angle) * outSpeed),
                    (float)(-0.3 - rng.NextDouble() * 0.4),
                    (float)(Math.Sin(angle) * outSpeed)),
                AddVelocity          = new Vec3f(0.4f, 0.2f, 0.4f),
                MinQuantity          = 1,
                LifeLength           = 0.18f + (float)(rng.NextDouble() * 0.12f),
                MinSize              = 0.10f,
                MaxSize              = 0.38f,
                GravityEffect        = 0.1f,
                Color                = ColorUtil.ColorFromRgba(210, 238, 255, 120 + (int)(rng.NextDouble() * 90)),
                ParticleModel        = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid    = false,
            });
        }

        // ── Bright core flash — tiny fast quads straight down ──────────────────
        for (int i = 0; i < 20; i++)
        {
            Vec3d pos = origin + new Vec3d(
                (rng.NextDouble() - 0.5) * 0.15,
                -rng.NextDouble() * 0.3,
                (rng.NextDouble() - 0.5) * 0.15);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinPos               = new Vec3d(pos.X, pos.Y, pos.Z),
                AddPos               = new Vec3d(0.03, 0.03, 0.03),
                MinVelocity          = new Vec3f(
                    (float)((rng.NextDouble() - 0.5) * 1.5),
                    (float)(-10.0 - rng.NextDouble() * 5.0),
                    (float)((rng.NextDouble() - 0.5) * 1.5)),
                AddVelocity          = new Vec3f(0.1f, 0.5f, 0.1f),
                MinQuantity          = 1,
                LifeLength           = 0.07f + (float)(rng.NextDouble() * 0.05f),
                MinSize              = 0.04f,
                MaxSize              = 0.12f,
                GravityEffect        = 0f,
                Color                = ColorUtil.ColorFromRgba(240, 250, 255, 210 + (int)(rng.NextDouble() * 45)),
                ParticleModel        = EnumParticleModel.Quad,
                WithTerrainCollision = false,
            });
        }
    }
}
