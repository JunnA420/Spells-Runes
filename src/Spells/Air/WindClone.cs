using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class WindClone : Spell
{
    public override string Id          => "air_wind_clone";
    public override string Name        => "Wind Clone";
    public override string Description => "Shapes compressed air into a copy of the caster. When the clone is struck, it disperses into a blinding smoke veil.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 40f;
    public override float CastTime => 1.0f;
    public override string? AnimationCode => "air_wind_clone";
    public override bool AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["air_windy_dash"];

    public override (int col, int row) TreePosition => (0, 4);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        SpawnFx(world, caster.SidedPos.XYZ.Add(0, 0.7, 0), spellLevel);
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, int spellLevel = 1)
    {
        int mult = 1 + (spellLevel - 1) / 4;
        var rng = world.Rand;

        for (int i = 0; i < 50 * mult; i++)
        {
            double radius = Math.Sqrt(rng.NextDouble()) * 0.7;
            double angle = rng.NextDouble() * Math.PI * 2;
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = ColorUtil.ColorFromRgba(190, 215, 225, 90 + rng.Next(70)),
                MinPos = new Vec3d(
                    origin.X + Math.Cos(angle) * radius,
                    origin.Y + rng.NextDouble() * 1.5 - 0.3,
                    origin.Z + Math.Sin(angle) * radius),
                AddPos = new Vec3d(0.08, 0.08, 0.08),
                MinVelocity = new Vec3f(
                    (float)((rng.NextDouble() - 0.5) * 0.5),
                    (float)(0.2 + rng.NextDouble() * 0.8),
                    (float)((rng.NextDouble() - 0.5) * 0.5)),
                AddVelocity = new Vec3f(0.08f, 0.12f, 0.08f),
                LifeLength = 0.8f + (float)rng.NextDouble() * 0.6f,
                MinSize = 0.16f,
                MaxSize = 0.42f,
                GravityEffect = -0.01f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
}
