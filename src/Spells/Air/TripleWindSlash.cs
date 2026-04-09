using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class TripleWindSlash : Spell
{
    public override string Id          => "air_triple_wind_slash";
    public override string Name        => "Triple Wind Slash";
    public override string Description => "Shapes three wind blades in quick succession and launches them forward, each following the last.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 45f;
    public override float CastTime => 1.2f;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_slash"];

    public override (int col, int row) TreePosition => (2, 4);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
