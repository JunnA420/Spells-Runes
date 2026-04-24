using System.Collections.Generic;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Fire;

/// <summary>
/// Tier I Enchantment — Instantly cooks the item held in hand.
/// Requires Hot Skin + Spark.
/// TODO: implement item cooking logic
/// </summary>
public class CookInHand : Spell
{
    public override string Id          => "fire_cook_in_hand";
    public override string Name        => "Cook in Hand";
    public override string Description => "Channel fire through your hands, instantly cooking whatever you hold.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 12f;
    public override float CastTime => 0f;
    public override string? AnimationCode => "fire_cook_in_hand";

    // Center column, row 1 — unlocked after left + right row 0
    public override (int col, int row) TreePosition => (1, 1);

    public override IReadOnlyList<string> Prerequisites => new[] { "fire_hot_skin", "fire_spark" };

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel)
    {
        var rng = world.Rand;
        Vec3d right = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();
        Vec3d up = new Vec3d(0, 1, 0);
        int mult = 1 + (spellLevel - 1) / 4;

        for (int hand = -1; hand <= 1; hand += 2)
        {
            var handPos = origin + lookDir * 0.35 + right * (0.24 * hand) + up * 0.28;
            for (int i = 0; i < 5 * mult; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                double r = rng.NextDouble() * 0.09;
                var pos = handPos + right * (Math.Cos(a) * r) + up * (Math.Sin(a) * r);

                world.SpawnParticles(new SimpleParticleProperties
                {
                    MinQuantity = 1,
                    AddQuantity = 0,
                    MinPos = pos,
                    AddPos = new Vec3d(0.015, 0.02, 0.015),
                    MinVelocity = new Vec3f((float)(lookDir.X * 0.08), 0.22f, (float)(lookDir.Z * 0.08)),
                    AddVelocity = new Vec3f(0.12f, 0.12f, 0.12f),
                    LifeLength = 0.18f + (float)rng.NextDouble() * 0.12f,
                    MinSize = 0.035f,
                    MaxSize = 0.09f,
                    GravityEffect = -0.05f,
                    Color = ColorUtil.ColorFromRgba(30 + rng.Next(70), 120 + rng.Next(100), 255, 210),
                    ParticleModel = EnumParticleModel.Quad,
                    WithTerrainCollision = false,
                    ShouldDieInLiquid = true
                });
            }
        }
    }
}
