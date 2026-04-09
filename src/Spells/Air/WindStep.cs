using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

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

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
