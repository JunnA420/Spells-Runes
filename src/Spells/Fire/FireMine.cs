using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Fire;

public class FireMine : Spell
{
    public override string Id => "fire_mine";
    public override string Name => "Fire Mine";
    public override string Description => "Plants a volatile ember charge at the caster's feet.";

    public override SpellTier Tier => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType Type => SpellType.Offense;

    public override float FluxCost => 24f;
    public override float CastTime => 0.8f;

    public override string? AnimationCode => "fire_mine";
    public override bool AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["fire_cook_in_hand"];
    public override (int col, int row) TreePosition => (0, 2);

    public const float Radius = 2.4f;
    public const float Damage = 10f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        if (world.Api == null) return;

        var center = caster.SidedPos.XYZ.Add(caster.SidedPos.GetViewVector().ToVec3d().Normalize() * 1.4).Add(0, 0.05, 0);
        center = ClampToSurface(world, center, 0.05);
        FireOrb.BroadcastFx(world, "fire_mine_arm", center, new Vec3d(0, 1, 0), spellLevel);

        world.Api.Event.RegisterCallback(_ =>
        {
            if (!caster.Alive) return;
            float radius = Radius * GetRangeMultiplier(spellLevel);
            float damage = Damage * GetDamageMultiplier(spellLevel);

            world.GetEntitiesAround(center, radius, radius, e =>
            {
                if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
                double dist = Math.Max(0.2, e.SidedPos.XYZ.DistanceTo(center));
                float scaledDamage = damage * (float)Math.Max(0.25, 1.0 - dist / radius);
                e.ReceiveDamage(new DamageSource
                {
                    Source = EnumDamageSource.Entity,
                    SourceEntity = caster,
                    Type = EnumDamageType.Fire,
                }, scaledDamage);
                Vec3d away = (e.SidedPos.XYZ - center).Normalize();
                e.SidedPos.Motion.Add(away.X * 0.28, 0.18, away.Z * 0.28);
                return false;
            });

            FireOrb.BroadcastFx(world, "fire_mine_burst", center, new Vec3d(0, 1, 0), spellLevel);
        }, 900);
    }

    public static void SpawnArmFx(IWorldAccessor world, Vec3d center, int spellLevel)
    {
        var rng = world.Rand;
        int mult = 1 + (spellLevel - 1) / 4;
        for (int i = 0; i < 22 * mult; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            double r = rng.NextDouble() * 0.7;
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = new Vec3d(center.X + Math.Cos(a) * r, center.Y + 0.04, center.Z + Math.Sin(a) * r),
                AddPos = new Vec3d(0.02, 0.02, 0.02),
                MinVelocity = new Vec3f(0, 0.18f, 0),
                AddVelocity = new Vec3f(0.12f, 0.12f, 0.12f),
                LifeLength = 0.35f + (float)rng.NextDouble() * 0.2f,
                MinSize = 0.06f,
                MaxSize = 0.16f,
                GravityEffect = -0.05f,
                Color = ColorUtil.ColorFromRgba(20 + rng.Next(80), 105 + rng.Next(110), 255, 180),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = true
            });
        }
    }

    public static void SpawnBurstFx(IWorldAccessor world, Vec3d center, int spellLevel)
    {
        var rng = world.Rand;
        int mult = 1 + (spellLevel - 1) / 4;
        for (int i = 0; i < 82 * mult; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            double speed = 2.0 + rng.NextDouble() * 3.2;
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = center,
                AddPos = new Vec3d(0.18, 0.12, 0.18),
                MinVelocity = new Vec3f((float)(Math.Cos(a) * speed), 0.45f + (float)rng.NextDouble() * 1.0f, (float)(Math.Sin(a) * speed)),
                AddVelocity = new Vec3f(0.3f, 0.25f, 0.3f),
                LifeLength = 0.28f + (float)rng.NextDouble() * 0.24f,
                MinSize = 0.10f,
                MaxSize = 0.34f,
                GravityEffect = -0.08f,
                Color = ColorUtil.ColorFromRgba(10 + rng.Next(80), 95 + rng.Next(130), 255, 220),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = true
            });
        }
    }
}
