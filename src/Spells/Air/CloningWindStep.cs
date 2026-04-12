using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Spells.Air;

public class CloningWindStep : Spell
{
    public override string Id          => "air_cloning_wind_step";
    public override string Name        => "Cloning Wind Step";
    public override string Description => "Performs Wind Step, leaving a Wind Clone at the point of departure.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 50f;
    public override float CastTime => 0.5f;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_clone", "air_wind_step"];

    public override (int col, int row) TreePosition => (1, 4);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        var origin = caster.SidedPos.XYZ.Add(0, 0.7, 0);
        WindClone.SpawnFx(world, origin, spellLevel);

        var lookDir = caster.SidedPos.GetViewVector().ToVec3d().Normalize();
        WindSlash.HitAlongLine(caster, world, caster.SidedPos.XYZ.Add(0, caster.LocalEyePos.Y - 0.15, 0), lookDir,
            WindStep.Range * GetRangeMultiplier(spellLevel), WindStep.HitRadius, WindStep.Damage * GetDamageMultiplier(spellLevel));

        caster.SidedPos.Motion.Set(
            lookDir.X * WindStep.ForwardForce * GetRangeMultiplier(spellLevel),
            Math.Max(caster.SidedPos.Motion.Y, 0.05),
            lookDir.Z * WindStep.ForwardForce * GetRangeMultiplier(spellLevel));

        WindSlash.SpawnFx(world, origin, lookDir, spellLevel, WindStep.Range * GetRangeMultiplier(spellLevel));
    }
}
