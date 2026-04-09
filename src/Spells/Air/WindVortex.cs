using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Air;

public class WindVortex : Spell
{
    public override string Id          => "air_wind_vortex";
    public override string Name        => "Wind Vortex";
    public override string Description => "Surrounds the caster with a sustained sphere of wind while the button is held. Pushes away nearby entities. Releasing the cast collapses the sphere outward.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Air;
    public override SpellType    Type    => SpellType.Defense;

    public override float FluxCost => 60f;
    public override float CastTime => 0f;

    public override IReadOnlyList<string> Prerequisites => ["air_wind_spear"];

    public override (int col, int row) TreePosition => (2, 6);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO
    }
}
