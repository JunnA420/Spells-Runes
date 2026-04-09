using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class WindSpear : Spell
{
    public override string Id          => "air_wind_spear";
    public override string Name        => "Wind Spear";
    public override string Description => "Compresses air into a narrow point and launches it forward at high velocity. Pierces through targets rather than pushing them.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 50f;
    public override float CastTime => 1.0f;

    public override IReadOnlyList<string> Prerequisites => ["air_triple_wind_slash"];

    public override (int col, int row) TreePosition => (2, 5);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
