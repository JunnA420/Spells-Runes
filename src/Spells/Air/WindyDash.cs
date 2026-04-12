using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class WindyDash : Spell
{
    public override string Id          => "air_windy_dash";
    public override string Name        => "Windy Dash";
    public override string Description => "Wraps the caster in a burst of wind, propelling them forward in an instant.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 20f;
    public override float CastTime => 0.4f;

    public override string? AnimationCode        => "air_windy_dash";
    public override bool    AnimationUpperBodyOnly => false;

    public override IReadOnlyList<string> Prerequisites => ["air_feather_fall"];

    public override (int col, int row) TreePosition => (0, 3);

    public const float ForwardForce = 1.25f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        caster.SidedPos.Motion.Set(
            lookDir.X * ForwardForce * GetRangeMultiplier(spellLevel),
            Math.Max(caster.SidedPos.Motion.Y, 0.05),
            lookDir.Z * ForwardForce * GetRangeMultiplier(spellLevel));

        SpawnFx(world, caster.SidedPos.XYZ.Add(0, 0.5, 0), lookDir, spellLevel);
    }

    public static void SpawnFx(IWorldAccessor world, Vec3d origin, Vec3d lookDir, int spellLevel = 1)
    {
        int mult = 1 + (spellLevel - 1) / 4;
        var rng = world.Rand;
        Vec3d right = lookDir.Cross(new Vec3d(0, 1, 0)).Normalize();

        for (int i = 0; i < 5 * mult; i++)
        {
            double bladeOffset = (i - (5 * mult - 1) * 0.5) * 0.12;
            for (int s = 0; s < 26; s++)
            {
                double t = s / 25.0;
                var pos = origin + right * bladeOffset + lookDir * (t * 2.2) + new Vec3d(0, (rng.NextDouble() - 0.5) * 0.08, 0);
                world.SpawnParticles(new SimpleParticleProperties
                {
                    MinQuantity = 1,
                    AddQuantity = 0,
                    Color = ColorUtil.ColorFromRgba(215, 242, 255, 160 + rng.Next(70)),
                    MinPos = pos,
                    AddPos = new Vec3d(0.012, 0.012, 0.012),
                    MinVelocity = new Vec3f(
                        (float)(lookDir.X * (9.5 + rng.NextDouble() * 2.4)),
                        (float)((rng.NextDouble() - 0.5) * 0.12),
                        (float)(lookDir.Z * (9.5 + rng.NextDouble() * 2.4))),
                    AddVelocity = new Vec3f(0.05f, 0.05f, 0.05f),
                    LifeLength = 0.11f + (float)rng.NextDouble() * 0.05f,
                    MinSize = 0.08f,
                    MaxSize = 0.2f,
                    GravityEffect = 0f,
                    ParticleModel = EnumParticleModel.Quad,
                    WithTerrainCollision = false,
                    ShouldDieInLiquid = false
                });
            }
        }
    }
}
