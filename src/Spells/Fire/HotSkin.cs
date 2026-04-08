using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Fire;

/// <summary>
/// Tier I Defense — Surrounds the caster in heat, burning attackers.
/// TODO: implement damage reflect aura
/// </summary>
public class HotSkin : Spell
{
    public override string Id          => "fire_hot_skin";
    public override string Name        => "Hot Skin";
    public override string Description => "Envelop yourself in searing heat. Enemies who strike you are burned.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType    Type    => SpellType.Defense;

    public override float FluxCost => 18f;

    // Left column, row 0
    public override (int col, int row) TreePosition => (0, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world)
    {
        // TODO: apply burn-on-hit aura
    }
}
