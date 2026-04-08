using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Fire;

/// <summary>
/// Tier I Enchantment — Instantly cooks the item held in hand.
/// Requires Hot Skin + Spark.
/// TODO: implement item cooking logic
/// </summary>
public class CookInHand : Spell
{
    public override string Id          => "fire_cook_in_hand";
    public override string Name        => "Cook in Hand";
    public override string Description => "Channel fire through your hands, instantly cooking whatever you hold.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 12f;

    // Center column, row 1 — unlocked after left + right row 0
    public override (int col, int row) TreePosition => (1, 1);

    public override IReadOnlyList<string> Prerequisites => new[] { "fire_hot_skin", "fire_spark" };

    public override void Execute(EntityAgent caster, IWorldAccessor world)
    {
        // TODO: cook held item
    }
}
