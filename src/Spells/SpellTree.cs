using System.Collections.Generic;
using System.Linq;

namespace SpellsAndRunes.Spells;

/// <summary>
/// Evaluates whether a player can unlock or cast a spell.
/// Unlock cost = (int)spell.Tier skill points (per element, deducted on unlock).
/// </summary>
public static class SpellTree
{
    /// <summary>
    /// Returns true if all prerequisites are unlocked and the required
    /// activator (if any) has been triggered.
    /// Does NOT check SP — use CanAffordUnlock for that.
    /// </summary>
    public static bool CanUnlock(string spellId, PlayerSpellData data)
    {
        var spell = SpellRegistry.Get(spellId);
        if (spell == null) return false;

        if (spell.RequiredActivator != null && !data.HasActivator(spell.RequiredActivator))
            return false;

        foreach (var prereq in spell.Prerequisites)
            if (!data.IsUnlocked(prereq)) return false;

        return true;
    }

    /// <summary>
    /// Returns true if player has enough SP to afford the spell.
    /// </summary>
    public static bool CanAffordUnlock(string spellId, PlayerSpellData data)
    {
        var spell = SpellRegistry.Get(spellId);
        if (spell == null) return false;
        int cost = (int)spell.Tier;
        return data.GetSkillPoints(spell.Element) >= cost;
    }

    /// <summary>
    /// Attempts to unlock a spell by spending Skill Points.
    /// Returns UnlockResult indicating outcome.
    /// </summary>
    public static UnlockResult TryUnlock(string spellId, PlayerSpellData data)
    {
        if (data.IsUnlocked(spellId))
            return UnlockResult.AlreadyUnlocked;

        var spell = SpellRegistry.Get(spellId);
        if (spell == null)
            return UnlockResult.NotFound;

        if (!CanUnlock(spellId, data))
            return UnlockResult.PrerequisitesMissing;

        int cost = (int)spell.Tier;
        if (!data.SpendSkillPoints(spell.Element, cost))
            return UnlockResult.InsufficientXp; // reused enum value = insufficient SP

        data.Unlock(spellId);
        return UnlockResult.Success;
    }

    public static IEnumerable<Spell> GetAvailable(PlayerSpellData data)
        => SpellRegistry.All.Values.Where(s => !data.IsUnlocked(s.Id) && CanUnlock(s.Id, data));

    public static IEnumerable<Spell> GetUnlocked(PlayerSpellData data)
        => SpellRegistry.All.Values.Where(s => data.IsUnlocked(s.Id));
}

public enum UnlockResult
{
    Success,
    AlreadyUnlocked,
    NotFound,
    PrerequisitesMissing,
    InsufficientXp, // also means insufficient SP
}
