using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

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
    public override float CastTime => 0f;

    public override IReadOnlyList<string> Prerequisites => ["air_feather_fall"];

    public override (int col, int row) TreePosition => (0, 2);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
