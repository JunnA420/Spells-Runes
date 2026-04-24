using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Fire;

/// <summary>
/// Tier I Defense — Surrounds the caster in heat, burning attackers.
/// TODO: implement damage reflect aura
/// </summary>
public class HotSkin : Spell
{
    public override string Id          => "fire_hot_skin";
    public override string Name        => "Hot Skin";
    public override string Description => "Envelop yourself in searing heat. Enemies who strike you are burned.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType    Type    => SpellType.Defense;

    public override float FluxCost => 18f;
    public override float CastTime => 0f;

    public const float Radius = 2.2f;
    public const float DamagePerSecond = 3f;

    // Left column, row 0
    public override (int col, int row) TreePosition => (0, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
    }

    public override void OnTick(EntityAgent caster, IWorldAccessor world, float deltaTime, int spellLevel = 1)
    {
        var center = caster.SidedPos.XYZ.Add(0, 0.9, 0);
        float radius = Radius * GetRangeMultiplier(spellLevel);

        world.GetEntitiesAround(center, radius, radius, e =>
        {
            if (e.EntityId == caster.EntityId || e is not EntityAgent) return false;
            e.ReceiveDamage(new DamageSource
            {
                Source = EnumDamageSource.Entity,
                SourceEntity = caster,
                Type = EnumDamageType.Fire,
            }, DamagePerSecond * deltaTime);
            return false;
        });
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d center, int spellLevel)
    {
        var rng = world.Rand;
        int mult = 1 + (spellLevel - 1) / 4;
        for (int i = 0; i < 8 * mult; i++)
        {
            double a = rng.NextDouble() * Math.PI * 2;
            double y = rng.NextDouble() * 1.15 - 0.15;
            double r = 0.35 + rng.NextDouble() * 0.65;
            var pos = new Vec3d(center.X + Math.Cos(a) * r, center.Y + y, center.Z + Math.Sin(a) * r);
            var drift = new Vec3d(Math.Cos(a) * 0.15, 0.18 + rng.NextDouble() * 0.22, Math.Sin(a) * 0.15);

            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                MinPos = pos,
                AddPos = new Vec3d(0.03, 0.04, 0.03),
                MinVelocity = new Vec3f((float)drift.X, (float)drift.Y, (float)drift.Z),
                AddVelocity = new Vec3f(0.08f, 0.12f, 0.08f),
                LifeLength = 0.22f + (float)rng.NextDouble() * 0.12f,
                MinSize = 0.045f,
                MaxSize = 0.12f,
                GravityEffect = -0.08f,
                Color = ColorUtil.ColorFromRgba(30 + rng.Next(60), 115 + rng.Next(100), 255, 190),
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = true
            });
        }
    }
}
