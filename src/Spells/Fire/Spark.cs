using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Fire;

/// <summary>
/// Tier I Offense — Hurls a spark at a target.
/// TODO: implement projectile
/// </summary>
public class Spark : Spell
{
    public override string Id          => "fire_spark";
    public override string Name        => "Spark";
    public override string Description => "Hurl a crackling spark at your enemy, dealing fire damage.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Fire;
    public override SpellType    Type    => SpellType.Offense;

    public override float FluxCost => 15f;

    // Right column, row 0
    public override (int col, int row) TreePosition => (2, 0);

    public override void Execute(EntityAgent caster, IWorldAccessor world)
    {
        // TODO: spawn spark projectile
    }
}
