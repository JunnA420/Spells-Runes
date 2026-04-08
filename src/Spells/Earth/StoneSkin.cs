using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Earth;

/// <summary>
/// Tier I Defense — Hardens the caster's skin like stone, reducing damage.
/// TODO: implement damage reduction buff
/// </summary>
public class StoneSkin : Spell
{
    public override string Id          => "earth_stone_skin";
    public override string Name        => "Stone Skin";
    public override string Description => "Harden your skin with the resilience of stone, reducing incoming damage.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Earth;
    public override SpellType    Type    => SpellType.Defense;

    public override float FluxCost => 14f;

    // Left column, row 0
    public override (int col, int row) TreePosition => (0, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world)
    {
        // TODO: apply damage reduction buff
    }
}
