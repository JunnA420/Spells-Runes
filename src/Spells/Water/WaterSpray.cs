using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Water;

/// <summary>
/// Tier I Offense — Fires a pressurized jet of water.
/// TODO: implement water jet knockback
/// </summary>
public class WaterSpray : Spell
{
    public override string Id          => "water_water_spray";
    public override string Name        => "Water Spray";
    public override string Description => "Fire a pressurized jet of water, knocking back and drenching enemies.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Water;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 18f;

    // Right column, row 0
    public override (int col, int row) TreePosition => (2, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: water jet projectile
    }
}
