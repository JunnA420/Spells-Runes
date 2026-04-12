using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class Updraft : Spell
{
    public override string Id          => "air_updraft";
    public override string Name        => "Updraft";
    public override string Description => "Channels a concentrated burst of air behind the caster, thrusting them in the direction they are looking.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 20f;
    public override float CastTime => 0.8f;

    public override string? AnimationCode        => "air_updraft";
    public override bool    AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["air_air_kick"];

    public override (int col, int row) TreePosition => (1, 2);

    public const float UpForce = 0.9f;
    public const float ForwardForce = 0.45f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        caster.SidedPos.Motion.Set(
            lookDir.X * ForwardForce * GetRangeMultiplier(spellLevel),
            UpForce,
            lookDir.Z * ForwardForce * GetRangeMultiplier(spellLevel));

        SpawnFx(world, caster.SidedPos.XYZ.Add(0, 0.25, 0), lookDir, spellLevel);
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1)
    {
        int mult = 1 + (spellLevel - 1) / 4;
        var rng = world.Rand;

        for (int i = 0; i < 72 * mult; i++)
        {
            double radius = rng.NextDouble() * 0.45;
            double angle = rng.NextDouble() * Math.PI * 2;
            world.SpawnParticles(new SimpleParticleProperties
            {
                MinQuantity = 1,
                AddQuantity = 0,
                Color = ColorUtil.ColorFromRgba(225, 246, 255, 150 + rng.Next(80)),
                MinPos = new Vec3d(
                    origin.X + Math.Cos(angle) * radius,
                    origin.Y,
                    origin.Z + Math.Sin(angle) * radius),
                AddPos = new Vec3d(0.025, 0.025, 0.025),
                MinVelocity = new Vec3f(
                    (float)(lookDir.X * 1.8 + (rng.NextDouble() - 0.5) * 0.9),
                    (float)(4.5 + rng.NextDouble() * 4.6),
                    (float)(lookDir.Z * 1.8 + (rng.NextDouble() - 0.5) * 0.9)),
                AddVelocity = new Vec3f(0.18f, 0.35f, 0.18f),
                LifeLength = 0.22f + (float)rng.NextDouble() * 0.12f,
                MinSize = 0.1f,
                MaxSize = 0.28f,
                GravityEffect = -0.08f,
                ParticleModel = EnumParticleModel.Quad,
                WithTerrainCollision = false,
                ShouldDieInLiquid = false
            });
        }
    }
}
