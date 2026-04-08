using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Earth;

/// <summary>
/// Tier I Defense — Raises a wall of earth in front of the caster.
/// TODO: implement block placement wall
/// </summary>
public class EarthWall : Spell
{
    public override string Id          => "earth_earth_wall";
    public override string Name        => "Earth Wall";
    public override string Description => "Raise a wall of earth and stone to shield yourself from enemies.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Earth;
    public override SpellType    Type    => SpellType.Defense;

    public override float FluxCost => 20f;

    // Right column, row 0
    public override (int col, int row) TreePosition => (2, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: place earth wall blocks
    }
}
