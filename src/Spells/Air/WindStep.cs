using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class WindStep : Spell
{
    public override string Id          => "air_wind_step";
    public override string Name        => "Wind Step";
    public override string Description => "Envelops the caster in a wind blade and launches them at great speed toward where they are looking. Cuts through anything in the path.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 35f;
    public override float CastTime => 0.5f;

    public override IReadOnlyList<string> Prerequisites => ["air_windy_dash", "air_wind_slash"];

    public override (int col, int row) TreePosition => (1, 3);

    public const float ForwardForce = 1.65f;
    public const float Damage = 14f;
    public const float HitRadius = 1.15f;
    public const float Range = 8f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        var origin = caster.SidedPos.XYZ.Add(0, caster.LocalEyePos.Y - 0.15, 0);

        WindSlash.HitAlongLine(caster, world, origin, lookDir, Range * GetRangeMultiplier(spellLevel), HitRadius, Damage * GetDamageMultiplier(spellLevel));
        caster.SidedPos.Motion.Set(
            lookDir.X * ForwardForce * GetRangeMultiplier(spellLevel),
            Math.Max(caster.SidedPos.Motion.Y, 0.05),
            lookDir.Z * ForwardForce * GetRangeMultiplier(spellLevel));

        WindSlash.SpawnFx(world, origin, lookDir, spellLevel, Range * GetRangeMultiplier(spellLevel));
    }
}
