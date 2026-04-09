using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class Updraft : Spell
{
    public override string Id          => "air_updraft";
    public override string Name        => "Updraft";
    public override string Description => "Channels a concentrated burst of air behind the caster, thrusting them in the direction they are looking.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 20f;
    public override float CastTime => 0f;

    public override IReadOnlyList<string> Prerequisites => ["air_air_kick"];

    public override (int col, int row) TreePosition => (1, 2);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
