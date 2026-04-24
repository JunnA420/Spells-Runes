using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Fire;

public class FireFlamethrower : Spell
{
    public override string Id => "fire_flamethrower";
    public override string Name => "Flamethrower";
    public override string Description => "Channels a sustained stream of flame from the caster's hands.";

    public override SpellTier Tier => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType Type => SpellType.Offense;

    public override float FluxCost => 30f;
    public override float CastTime => 0f;

    public override string? AnimationCode => "fire_flamethrower";
    public override bool AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["fire_spark"];
    public override (int col, int row) TreePosition => (3, 1);

    public const float Range = 7f;
    public const float ConeAngleDeg = 24f;
    public const float DamagePerSecond = 5f;
    private const double IgniteChancePerTick = 0.025;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
    }

    public override void OnTick(EntityAgent caster, IWorldAccessor world, float deltaTime, int spellLevel = 1)
    {
        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        var origin = caster.SidedPos.XYZ.Add(lookDir * 1.05).Add(0, caster.LocalEyePos.Y - 0.32, 0);
        float range = Range * GetRangeMultiplier(spellLevel);
        float cosAngle = (float)Math.Cos(ConeAngleDeg * Math.PI / 180.0);

        world.GetEntitiesAround(origin, range + 1, range + 1, e =>
        {
            if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
            Vec3d target = e.SidedPos.XYZ.Add(0, e.LocalEyePos.Y * 0.5, 0);
            Vec3d toEntity = target - origin;
            if (toEntity.Length() > range) return false;
            if (lookDir.Dot(toEntity.Normalize()) < cosAngle) return false;

            e.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Entity,
                SourceEntity = caster,
                Type = EnumDamageType.Fire,
            }, DamagePerSecond * deltaTime);
            return false;
        });

        TryIgniteAimedBlock(caster, world, origin, lookDir, range);
    }

    private static void TryIgniteAimedBlock(EntityAgent caster, IWorldAccessor world, Vec3d origin, Vec3d lookDir, float range)
    {
        if (world.Rand.NextDouble() > IgniteChancePerTick) return;

        Vec3d target = origin + lookDir * range;
        BlockSelection? bsel = null;
        EntitySelection? esel = null;
        world.RayTraceForSelection(origin, target, ref bsel, ref esel);
        if (bsel == null) return;

        var fire = world.GetBlock(new AssetLocation("fire"));
        if (fire == null || fire.BlockId == 0) return;

        BlockPos firePos = bsel.Position.AddCopy(bsel.Face);
        var existing = world.BlockAccessor.GetBlock(firePos);
        if (existing == null || existing.BlockId != 0 && existing.Replaceable < fire.Replaceable) return;

        world.BlockAccessor.SetBlock(fire.BlockId, firePos);
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel)
    {
        var rng = world.Rand;
        Vec3d right = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d up = right.Cross(lookDir).Normalize();
        int mult = 1 + (spellLevel - 1) / 4;

        for (int i = 0; i < 24 * mult; i++)
        {
            double dist = rng.NextDouble() * Range;
            double spread = Math.Tan(ConeAngleDeg * Math.PI / 180.0) * (dist + 0.4);
            double a = rng.NextDouble() * Math.PI * 2;
            double r = Math.Sqrt(rng.NextDouble()) * spread;
            var pos = origin + lookDir * dist + right * (Math.Cos(a) * r) + up * (Math.Sin(a) * r);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = pos,
                AddPos = new Vec3d(0.08, 0.08, 0.08),
                MinVelocity = new Vec3f((float)(lookDir.X * 5.5), (float)(lookDir.Y * 2.0 + 0.6), (float)(lookDir.Z * 5.5)),
                AddVelocity = new Vec3f(0.75f, 0.45f, 0.75f),
                LifeLength = 0.22f + (float)rng.NextDouble() * 0.18f,
                MinSize = 0.12f,
                MaxSize = 0.32f,
                GravityEffect = -0.2f,
                Color = ColorUtil.ColorFromRgba(20 + rng.Next(80), 100 + rng.Next(120), 255, 210),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = true
            });
        }
    }
}
