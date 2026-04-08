using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Water;

/// <summary>
/// Tier I Enchantment — Slowly restores the caster's health.
/// TODO: implement health regen
/// </summary>
public class Healing : Spell
{
    public override string Id          => "water_healing";
    public override string Name        => "Healing";
    public override string Description => "Channel the restorative flow of water to slowly mend your wounds.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Water;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 20f;

    // Left column, row 0
    public override (int col, int row) TreePosition => (0, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: apply health regen over time
    }
}
