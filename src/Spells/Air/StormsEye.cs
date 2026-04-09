using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class StormsEye : Spell
{
    public override string Id          => "air_storms_eye";
    public override string Name        => "Storm's Eye";
    public override string Description => "Creates a localized eye of turbulence around a target. The winds within hold them in place.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 55f;
    public override float CastTime => 1.5f;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_clone"];

    public override (int col, int row) TreePosition => (0, 5);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
