using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class WindSlash : Spell
{
    public override string Id          => "air_wind_slash";
    public override string Name        => "Wind Slash";
    public override string Description => "Shapes compressed air into a curved blade and launches it forward. Cuts through the air at considerable speed.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 25f;
    public override float CastTime => 0.8f;

    public override IReadOnlyList<string> Prerequisites => ["air_air_push"];

    public override (int col, int row) TreePosition => (2, 2);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
