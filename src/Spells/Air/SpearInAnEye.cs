using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class SpearInAnEye : Spell
{
    public override string Id          => "air_spear_in_an_eye";
    public override string Name        => "Spear in an Eye";
    public override string Description => "Creates a large Storm's Eye, then continuously fires Wind Spears into it. Targets held within are struck repeatedly.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 80f;
    public override float CastTime => 2.0f;

    public override IReadOnlyList<string> Prerequisites => ["air_storms_eye", "air_wind_spear"];

    public override (int col, int row) TreePosition => (1, 5);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
