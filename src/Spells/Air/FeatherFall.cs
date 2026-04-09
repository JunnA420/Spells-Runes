using System;
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

    public override string? AnimationCode => "air_wind_feather_fall";
    public override bool AnimationUpperBodyOnly => false;

    public override (int col, int row) TreePosition => (0, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // Motion is killed client-side via MsgFreezeMotion (player physics are client-authoritative)
    }

    [ThreadStatic] private static SimpleParticleProperties? _pool;
    private static SimpleParticleProperties Pool => _pool ??= new SimpleParticleProperties();

    /// <summary>Called client-side from MsgSpellFx handler to spawn the downward air burst.</summary>
    public static void SpawnFx(IWorldAccessor world, Vec3d origin)
    {
        var rng = world.Rand;
        var p   = Pool;

        p.ParticleModel     = EnumParticleModel.Quad;
        p.ShouldDieInLiquid = false;
        p.MinQuantity       = 1;
        p.AddQuantity       = 0;

        // ── Downward jet ──────────────────────────────────────────────────────────
        p.WithTerrainCollision = false;
        p.GravityEffect        = 0.3f;
        p.AddVelocity          = new Vec3f(0.3f, 0.5f, 0.3f);
        p.AddPos               = new Vec3d(0.06, 0.04, 0.06);
        p.MinSize              = 0.08f;
        p.MaxSize              = 0.30f;

        for (int i = 0; i < 80; i++)
        {
            double angle  = rng.NextDouble() * 2 * Math.PI;
            double cosA   = Math.Cos(angle);
            double sinA   = Math.Sin(angle);
            double radius = Math.Sqrt(rng.NextDouble()) * 0.55;
            double speed  = 6.0 + rng.NextDouble() * 5.0;
            double spread = (rng.NextDouble() - 0.5) * 1.2;

            p.MinPos = new Vec3d(
                origin.X + cosA * radius * 0.3,
                origin.Y - 0.1,
                origin.Z + sinA * radius * 0.3);
            p.MinVelocity = new Vec3f(
                (float)(cosA * radius * 3.0 + spread),
                (float)(-speed),
                (float)(sinA * radius * 3.0 + spread));
            p.LifeLength = 0.12f + (float)(rng.NextDouble() * 0.10f);
            p.Color      = ColorUtil.ColorFromRgba(220, 242, 255, 140 + (int)(rng.NextDouble() * 100));

            world.SpawnParticles(p);
        }

        // ── Radial shockwave ring ─────────────────────────────────────────────────
        p.GravityEffect = 0.1f;
        p.AddVelocity   = new Vec3f(0.4f, 0.2f, 0.4f);
        p.AddPos        = new Vec3d(0.05, 0.05, 0.05);
        p.MinSize       = 0.10f;
        p.MaxSize       = 0.38f;

        for (int i = 0; i < 40; i++)
        {
            double angle    = i * 2.0 * Math.PI / 40 + rng.NextDouble() * 0.15;
            double cosA     = Math.Cos(angle);
            double sinA     = Math.Sin(angle);
            double r        = 0.1 + rng.NextDouble() * 0.2;
            double outSpeed = 4.5 + rng.NextDouble() * 3.0;

            p.MinPos = new Vec3d(
                origin.X + cosA * r,
                origin.Y - 0.15,
                origin.Z + sinA * r);
            p.MinVelocity = new Vec3f(
                (float)(cosA * outSpeed),
                (float)(-0.3 - rng.NextDouble() * 0.4),
                (float)(sinA * outSpeed));
            p.LifeLength = 0.18f + (float)(rng.NextDouble() * 0.12f);
            p.Color      = ColorUtil.ColorFromRgba(210, 238, 255, 120 + (int)(rng.NextDouble() * 90));

            world.SpawnParticles(p);
        }

        // ── Bright core flash ─────────────────────────────────────────────────────
        p.GravityEffect = 0f;
        p.AddVelocity   = new Vec3f(0.1f, 0.5f, 0.1f);
        p.AddPos        = new Vec3d(0.03, 0.03, 0.03);
        p.MinSize       = 0.04f;
        p.MaxSize       = 0.12f;

        for (int i = 0; i < 20; i++)
        {
            p.MinPos = new Vec3d(
                origin.X + (rng.NextDouble() - 0.5) * 0.15,
                origin.Y - rng.NextDouble() * 0.3,
                origin.Z + (rng.NextDouble() - 0.5) * 0.15);
            p.MinVelocity = new Vec3f(
                (float)((rng.NextDouble() - 0.5) * 1.5),
                (float)(-10.0 - rng.NextDouble() * 5.0),
                (float)((rng.NextDouble() - 0.5) * 1.5));
            p.LifeLength = 0.07f + (float)(rng.NextDouble() * 0.05f);
            p.Color      = ColorUtil.ColorFromRgba(240, 250, 255, 210 + (int)(rng.NextDouble() * 45));

            world.SpawnParticles(p);
        }
    }
}
