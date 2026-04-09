using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class Tornado : Spell
{
    public override string Id          => "air_tornado";
    public override string Name        => "Tornado";
    public override string Description => "Summons a tornado at the target location. The tornado moves on its own and cannot be controlled once cast.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 70f;
    public override float CastTime => 2.0f;

    public override IReadOnlyList<string> Prerequisites => ["air_storms_eye"];

    public override (int col, int row) TreePosition => (0, 6);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
