using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class WindClone : Spell
{
    public override string Id          => "air_wind_clone";
    public override string Name        => "Wind Clone";
    public override string Description => "Shapes compressed air into a copy of the caster. When the clone is struck, it disperses into a blinding smoke veil.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 40f;
    public override float CastTime => 1.0f;

    public override IReadOnlyList<string> Prerequisites => ["air_windy_dash"];

    public override (int col, int row) TreePosition => (0, 4);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
