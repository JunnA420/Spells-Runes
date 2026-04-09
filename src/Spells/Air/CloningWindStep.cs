using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

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
        // TODO
    }
}
