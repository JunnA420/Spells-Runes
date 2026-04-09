using System;
using System.Collections.Generic;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Common;

namespace SpellsAndRunes.Spells;

/// <summary>
/// Per-player spell data stored in entity.WatchedAttributes.
///
/// Element XP / leveling:
///   XP needed for level N = floor(100 * 1.25^(N-1))
///   Surplus XP carries over to the next level automatically.
///   Each level-up grants 1 Skill Point (per element).
///
/// Skill Points:
///   Spent (deducted) when unlocking a spell.
///   Cost = (int)spell.Tier  (Novice=1, Apprentice=2, Adept=3, Master=4)
///
/// Spell levels (1-10, per spell):
///   Gained by casting the spell. XP tracked per spell id.
///   Misscast chance: lvl 1=30%, lvl 2=22%, lvl 3=14%, lvl 4=5%, lvl 5+=0%
/// </summary>
public class PlayerSpellData
{
    // WatchedAttribute keys
    private const string AttrElementXp    = "snr:elementxp";    // ITree: element -> total raw xp
    private const string AttrElementLevel = "snr:elementlevel"; // ITree: element -> current level
    private const string AttrElementSP    = "snr:elementsp";    // ITree: element -> available SP
    private const string AttrUnlocked     = "snr:unlocked";
    private const string AttrActivators   = "snr:activators";
    private const string AttrHotbar       = "snr:hotbar";
    private const string AttrSpellXp      = "snr:spellxp";      // ITree: spellId -> total raw xp
    private const string AttrSpellLevel   = "snr:spelllevel";   // ITree: spellId -> current level
    private const int    HotbarSlots      = 3;

    private readonly Entity entity;

    public PlayerSpellData(Entity entity) { this.entity = entity; }

    // -----------------------------------------------------------------------
    // Element leveling
    // -----------------------------------------------------------------------

    /// <summary>Total raw XP stored for this element (accumulated, never reset).</summary>
    public int GetElementXp(SpellElement element)
        => GetTree(AttrElementXp).GetInt(element.ToString(), 0);

    /// <summary>Current element level (1-based, no cap).</summary>
    public int GetElementLevel(SpellElement element)
        => Math.Max(1, GetTree(AttrElementLevel).GetInt(element.ToString(), 1));

    /// <summary>Available skill points for this element.</summary>
    public int GetSkillPoints(SpellElement element)
        => GetTree(AttrElementSP).GetInt(element.ToString(), 0);

    /// <summary>
    /// Add XP for an element. Automatically levels up and grants SP if threshold crossed.
    /// Surplus carries over. Returns how many levels were gained.
    /// </summary>
    public int AddElementXp(SpellElement element, int amount)
    {
        var xpTree    = GetTree(AttrElementXp);
        var lvlTree   = GetTree(AttrElementLevel);
        var spTree    = GetTree(AttrElementSP);

        string key    = element.ToString();
        int xp        = xpTree.GetInt(key, 0) + amount;
        int level     = Math.Max(1, lvlTree.GetInt(key, 1));
        int levelsGained = 0;

        while (true)
        {
            int needed = XpForLevel(level);
            if (xp < needed) break;
            xp -= needed;
            level++;
            levelsGained++;
            spTree.SetInt(key, spTree.GetInt(key, 0) + 1);
        }

        xpTree.SetInt(key, xp);
        lvlTree.SetInt(key, level);

        if (levelsGained > 0)
        {
            entity.WatchedAttributes.MarkPathDirty(AttrElementLevel);
            entity.WatchedAttributes.MarkPathDirty(AttrElementSP);
        }
        entity.WatchedAttributes.MarkPathDirty(AttrElementXp);
        return levelsGained;
    }

    /// <summary>
    /// Spend SP for this element. Returns false if insufficient.
    /// </summary>
    public bool SpendSkillPoints(SpellElement element, int amount)
    {
        var tree = GetTree(AttrElementSP);
        string key = element.ToString();
        int current = tree.GetInt(key, 0);
        if (current < amount) return false;
        tree.SetInt(key, current - amount);
        entity.WatchedAttributes.MarkPathDirty(AttrElementSP);
        return true;
    }

    // -----------------------------------------------------------------------
    // XP curve
    // -----------------------------------------------------------------------

    /// <summary>XP required to advance FROM the given level (i.e. cost of level N).</summary>
    public static int XpForLevel(int level)
        => (int)Math.Floor(100.0 * Math.Pow(1.25, level - 1));

    /// <summary>XP already accumulated within the current level (0 .. XpForLevel(level)-1).</summary>
    public int GetElementXpInLevel(SpellElement element)
        => GetElementXp(element); // stored xp is already the remainder after last level-up

    // -----------------------------------------------------------------------
    // Unlocked spells
    // -----------------------------------------------------------------------

    public bool IsUnlocked(string spellId)
        => GetTree(AttrUnlocked).HasAttribute(spellId);

    public void Unlock(string spellId)
    {
        GetTree(AttrUnlocked).SetInt(spellId, 1);
        entity.WatchedAttributes.MarkPathDirty(AttrUnlocked);
    }

    public IEnumerable<string> GetUnlockedIds()
    {
        if (GetTree(AttrUnlocked) is TreeAttribute tree)
            foreach (var kv in tree) yield return kv.Key;
    }

    // -----------------------------------------------------------------------
    // Activators
    // -----------------------------------------------------------------------

    public bool HasActivator(string activatorId)
        => GetTree(AttrActivators).HasAttribute(activatorId);

    public void TriggerActivator(string activatorId)
    {
        GetTree(AttrActivators).SetInt(activatorId, 1);
        entity.WatchedAttributes.MarkPathDirty(AttrActivators);
    }

    /// <summary>True once the player has smoked Sylphweed — gates all Flux features.</summary>
    public bool IsFluxUnlocked => HasActivator("sylphweed");

    public void UnlockFlux() => TriggerActivator("sylphweed");

    /// <summary>True if the player has unlocked a specific element tree.</summary>
    public bool IsElementUnlocked(SpellElement element) => HasActivator($"element_{element.ToString().ToLowerInvariant()}");

    // -----------------------------------------------------------------------
    // Hotbar
    // -----------------------------------------------------------------------

    public string? GetHotbarSlot(int slot)
    {
        if (slot < 0 || slot >= HotbarSlots) return null;
        string val = GetTree(AttrHotbar).GetString($"slot{slot}", "");
        return string.IsNullOrEmpty(val) ? null : val;
    }

    public void SetHotbarSlot(int slot, string? spellId)
    {
        if (slot < 0 || slot >= HotbarSlots) return;
        GetTree(AttrHotbar).SetString($"slot{slot}", spellId ?? "");
        entity.WatchedAttributes.MarkPathDirty(AttrHotbar);
    }

    // -----------------------------------------------------------------------
    // Spell levels (per spell)
    // -----------------------------------------------------------------------

    /// <summary>Current level of a specific spell (1-10).</summary>
    public int GetSpellLevel(string spellId)
        => Math.Clamp(GetTree(AttrSpellLevel).GetInt(spellId, 1), 1, 10);

    public int SetSpellLevel(string spellId, int level)
    {
        level = Math.Clamp(level, 1, 10);
        GetTree(AttrSpellLevel).SetInt(spellId, level);
        entity.WatchedAttributes.MarkPathDirty(AttrSpellLevel);
        return level;
    }

    /// <summary>Raw XP within current spell level (remainder after last level-up).</summary>
    public int GetSpellXpInLevel(string spellId)
        => GetTree(AttrSpellXp).GetInt(spellId, 0);

    /// <summary>
    /// Add XP to a spell. Uses the same XP curve as element levels.
    /// Caps at level 10. Returns levels gained.
    /// </summary>
    public int AddSpellXp(string spellId, int amount)
    {
        var xpTree  = GetTree(AttrSpellXp);
        var lvlTree = GetTree(AttrSpellLevel);

        int xp    = xpTree.GetInt(spellId, 0) + amount;
        int level = Math.Clamp(lvlTree.GetInt(spellId, 1), 1, 10);
        int gained = 0;

        while (level < 10)
        {
            int needed = XpForLevel(level);
            if (xp < needed) break;
            xp -= needed;
            level++;
            gained++;
        }

        // At level 10 stop accumulating surplus
        if (level >= 10) xp = 0;

        xpTree.SetInt(spellId, xp);
        lvlTree.SetInt(spellId, level);

        if (gained > 0) entity.WatchedAttributes.MarkPathDirty(AttrSpellLevel);
        entity.WatchedAttributes.MarkPathDirty(AttrSpellXp);
        return gained;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private ITreeAttribute GetTree(string key)
        => entity.WatchedAttributes.GetOrAddTreeAttribute(key);

    public static PlayerSpellData For(Entity entity) => new(entity);
}
