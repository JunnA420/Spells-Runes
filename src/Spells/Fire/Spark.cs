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

    public override string? AnimationCode => "sparkcast";

    public override (int col, int row) TreePosition => (2, 0);

    public const float Range        = 6f;
    public const float ConeAngleDeg = 40f;
    public const float Damage       = 4f;

    private static readonly Vec3d Up      = new Vec3d(0, 1, 0);
    private static readonly float CosAngle = (float)Math.Cos(ConeAngleDeg * Math.PI / 180.0);
    private static readonly double TanAngle = Math.Tan(ConeAngleDeg * Math.PI / 180.0);

    private static readonly int[] FireColors =
    {
        ColorUtil.ColorFromRgba(180, 240, 255, 255),
        ColorUtil.ColorFromRgba( 60, 210, 255, 255),
        ColorUtil.ColorFromRgba( 20, 140, 255, 255),
        ColorUtil.ColorFromRgba(  5,  80, 255, 255),
        ColorUtil.ColorFromRgba(  0,  30, 220, 240),
        ColorUtil.ColorFromRgba(  0,  10, 180, 220),
    };

    [ThreadStatic] private static SimpleParticleProperties? _pool;
    private static SimpleParticleProperties Pool => _pool ??= new SimpleParticleProperties();

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var   origin   = caster.SidedPos.XYZ.Add(0, 0.9, 0);
        var   lookDir  = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        float range    = Range * GetRangeMultiplier(spellLevel);
        float dmg      = Damage * GetDamageMultiplier(spellLevel);

        world.GetEntitiesAround(origin, range + 1, range + 1, e =>
        {
            if (e.EntityId == caster.EntityId) return false;
            if (e is not EntityAgent)          return false;

            Vec3d targetPos = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0);
            Vec3d toEntity  = targetPos - origin;
            if (toEntity.Length() > range) return false;
            if (lookDir.Dot(toEntity.Normalize()) < CosAngle) return false;
            BlockSelection? bsel = null; EntitySelection? esel = null;
            world.RayTraceForSelection(origin, targetPos, ref bsel, ref esel);
            if (bsel != null) return false;

            e.ReceiveDamage(new DamageSource
            {
                Source       = EnumDamageSource.Entity,
                SourceEntity = caster,
                Type         = EnumDamageType.Fire,
            }, dmg);
            return false;
        });

        SpawnFx(world, origin, lookDir, spellLevel, range);
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1, float? scaledRange = null)
    {
        float  range = scaledRange ?? Range;
        Vec3d  right  = lookDir.Cross(Up).Normalize();
        Vec3d  upPerp = lookDir.Cross(right).Normalize();
        var    rng    = world.Rand;
        int    mult   = 1 + (spellLevel - 1) / 4;
        var    p      = Pool;

        p.ParticleModel     = EnumParticleModel.Quad;
        p.ShouldDieInLiquid = true;
        p.MinQuantity       = 1;
        p.AddQuantity       = 0;

        // ── 1. Dense spark shower ─────────────────────────────────────────────────
        p.WithTerrainCollision = true;
        p.AddVelocity          = new Vec3f(0.4f, 0.3f, 0.4f);
        p.AddPos               = new Vec3d(0.05, 0.05, 0.05);

        for (int i = 0; i < 140 * mult; i++)
        {
            double t       = rng.NextDouble();
            double dist    = range * t;
            double spread  = TanAngle * dist;
            double a       = rng.NextDouble() * 2 * Math.PI;
            double r       = Math.Sqrt(rng.NextDouble()) * spread;
            double cosA    = Math.Cos(a);
            double sinA    = Math.Sin(a);
            double scatterA = rng.NextDouble() * 2 * Math.PI;
            double scatterS = 2.0 + rng.NextDouble() * 4.0;
            double cosSA   = Math.Cos(scatterA);
            double sinSA   = Math.Sin(scatterA);
            bool   linger  = rng.NextDouble() < 0.4;

            Vec3d pos = origin + lookDir * dist + right * (cosA * r) + upPerp * (sinA * r);

            p.MinPos      = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 5.0 + cosSA * right.X * scatterS + sinSA * upPerp.X * scatterS),
                (float)(lookDir.Y * 2.0 + sinSA * scatterS * 0.5 + (linger ? 1.0 : 0.2)),
                (float)(lookDir.Z * 5.0 + cosSA * right.Z * scatterS + sinSA * upPerp.Z * scatterS));
            p.LifeLength    = linger ? 0.8f + (float)(rng.NextDouble() * 1.2f) : 0.05f + (float)(rng.NextDouble() * 0.08f);
            p.MinSize       = linger ? 0.06f : 0.03f;
            p.MaxSize       = linger ? 0.18f : 0.12f;
            p.GravityEffect = linger ? 1.5f  : 0.6f;
            p.Color         = FireColors[rng.Next(FireColors.Length)];

            world.SpawnParticles(p);
        }

        // ── 2. Burst ring at origin ───────────────────────────────────────────────
        p.AddVelocity = new Vec3f(0.4f, 0.3f, 0.4f);
        p.AddPos      = new Vec3d(0.04, 0.04, 0.04);
        p.MinSize     = 0.08f;
        p.MaxSize     = 0.22f;
        p.GravityEffect = 1.2f;

        for (int i = 0; i < 28 * mult; i++)
        {
            double a       = i * 2.0 * Math.PI / 28 + rng.NextDouble() * 0.15;
            double cosA    = Math.Cos(a);
            double sinA    = Math.Sin(a);
            double outSpeed = 5.0 + rng.NextDouble() * 5.0;

            Vec3d pos = origin + right * (cosA * 0.12) + upPerp * (sinA * 0.12);

            p.MinPos      = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 4.0 + cosA * right.X * outSpeed + sinA * upPerp.X * outSpeed),
                (float)(sinA * outSpeed * 0.4 + 0.8),
                (float)(lookDir.Z * 4.0 + cosA * right.Z * outSpeed + sinA * upPerp.Z * outSpeed));
            p.LifeLength = 0.4f + (float)(rng.NextDouble() * 0.6f);
            p.Color      = FireColors[rng.Next(FireColors.Length)];

            world.SpawnParticles(p);
        }

        // ── 3. Falling ember sparks ───────────────────────────────────────────────
        p.AddVelocity   = new Vec3f(0.3f, 0.2f, 0.3f);
        p.AddPos        = new Vec3d(0.05, 0.05, 0.05);
        p.MinSize       = 0.04f;
        p.MaxSize       = 0.10f;
        p.GravityEffect = 1.5f;

        int whiteColor  = ColorUtil.ColorFromRgba(180, 240, 255, 255);
        int orangeColor = ColorUtil.ColorFromRgba( 10,  80, 255, 255);

        for (int i = 0; i < 50 * mult; i++)
        {
            double t      = rng.NextDouble();
            double dist   = range * t;
            double spread = TanAngle * dist;
            double a      = rng.NextDouble() * 2 * Math.PI;
            double r      = Math.Sqrt(rng.NextDouble()) * spread;

            Vec3d pos = origin + lookDir * dist + right * (Math.Cos(a) * r) + upPerp * (Math.Sin(a) * r);

            p.MinPos      = new Vec3d(pos.X, pos.Y, pos.Z);
            p.MinVelocity = new Vec3f(
                (float)(lookDir.X * 2.0 + (rng.NextDouble() - 0.5) * 1.0),
                (float)(0.5 + rng.NextDouble() * 1.0),
                (float)(lookDir.Z * 2.0 + (rng.NextDouble() - 0.5) * 1.0));
            p.LifeLength = 0.6f + (float)(rng.NextDouble() * 0.8f);
            p.Color      = rng.NextDouble() < 0.3 ? whiteColor : orangeColor;

            world.SpawnParticles(p);
        }
    }
}
