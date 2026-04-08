using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Earth;

/// <summary>
/// Tier I Enchantment — Creates a decoy clone made of earth to distract enemies.
/// Requires Stone Skin + Earth Wall.
/// TODO: implement decoy entity spawn
/// </summary>
public class EarthClone : Spell
{
    public override string Id          => "earth_earth_clone";
    public override string Name        => "Earth Clone";
    public override string Description => "Summon a decoy made of earth and stone that draws enemy attention.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Earth;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 22f;

    // Center column, row 1
    public override (int col, int row) TreePosition => (1, 1);

    public override IReadOnlyList<string> Prerequisites => new[] { "earth_stone_skin", "earth_earth_wall" };

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: spawn earth decoy entity
    }
}
