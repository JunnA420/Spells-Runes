using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Water;

/// <summary>
/// Tier I Enchantment — Sprinkles healing water in an area around the caster.
/// Requires Healing + Water Spray.
/// TODO: implement AoE heal
/// </summary>
public class HealingSprinkle : Spell
{
    public override string Id          => "water_healing_sprinkle";
    public override string Name        => "Healing Sprinkle";
    public override string Description => "Release a gentle shower of healing water around you, mending nearby allies.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Water;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 25f;

    // Center column, row 1
    public override (int col, int row) TreePosition => (1, 1);

    public override IReadOnlyList<string> Prerequisites => new[] { "water_healing", "water_water_spray" };

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: AoE heal around caster
    }
}
