using Vintagestory.API.Common.Entities;
using Vintagestory.API.Common;

namespace SpellsAndRunes.Spells.Activators;

/// <summary>
/// Base class for spell activators — world events or items that unlock
/// a spell branch when triggered (e.g. finding Sylphweed, surviving a storm).
/// </summary>
public abstract class SpellActivator
{
    /// <summary>Unique id matching Spell.RequiredActivator.</summary>
    public abstract string Id { get; }

    public abstract string Name { get; }
    public abstract string Description { get; }

    /// <summary>
    /// Called server-side when the activation condition is met.
    /// Triggers the activator for the given player and unlocks the branch.
    /// </summary>
    public void Trigger(EntityAgent player, IWorldAccessor world)
    {
        var data = PlayerSpellData.For(player);
        if (data.HasActivator(Id)) return; // already triggered

        data.TriggerActivator(Id);
        OnTriggered(player, world);
    }

    /// <summary>
    /// Override to add custom effects on trigger (particles, message, sound).
    /// </summary>
    protected virtual void OnTriggered(EntityAgent player, IWorldAccessor world) { }
}
