using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells;

/// <summary>
/// Abstract base class for all spells in Spells & Runes.
///
/// Casting flow (called by the cast system, server-side):
///   1. Check flux via EntityBehaviorFlux.TryConsumeFlux()
///   2. Call TryCast() — handles misscast roll and delegates to Execute()
///   3. Award spell XP via PlayerSpellData.AddSpellXp()
///
/// Misscast chance by spell level:
///   Lvl 1 = 30%, Lvl 2 = 22%, Lvl 3 = 14%, Lvl 4 = 5%, Lvl 5+ = 0%
/// </summary>
public abstract class Spell
{
    private static readonly float[] MisscastChance = { 0.30f, 0.22f, 0.14f, 0.05f };

    // ---- Identity ----

    /// <summary>Unique identifier, e.g. "air_feather_fall"</summary>
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    // ---- Classification ----

    public abstract SpellTier    Tier    { get; }
    public abstract SpellElement Element { get; }
    public abstract SpellType    Type    { get; }

    // ---- Costs ----

    /// <summary>Flux (mana) consumed on cast.</summary>
    public abstract float FluxCost { get; }

    // ---- Prerequisites ----

    /// <summary>Spell ids that must be unlocked before this one.</summary>
    public virtual IReadOnlyList<string> Prerequisites => Array.Empty<string>();

    /// <summary>Activator item required to unlock this branch. Null = none.</summary>
    public virtual string? RequiredActivator => null;

    // ---- Tree layout ----

    /// <summary>
    /// Position in the element spell tree. col = horizontal (0 = left),
    /// row = vertical (0 = bottom, rows grow upward).
    /// </summary>
    public virtual (int col, int row) TreePosition => (0, 0);

    // ---- Spell level / XP ----

    /// <summary>XP granted to the spell on a successful cast.</summary>
    public virtual int XpPerCast => 10;

    /// <summary>XP granted to the element on a successful cast.</summary>
    public virtual int ElementXpPerCast => 5;

    /// <summary>
    /// How long (in seconds) the cast takes before Execute is called.
    /// 0 = instant. Override per spell for slower/faster casts.
    /// </summary>
    public virtual float CastTime => 1.0f;

    // ---- Casting ----

    /// <summary>
    /// Entry point for the cast system.
    /// Rolls misscast based on spell level, then calls Execute() on success.
    /// Returns false if miscast occurred (caller should still award XP).
    /// </summary>
    public bool TryCast(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        float chance = MisscastChanceForLevel(spellLevel);
        if (chance > 0f && (float)world.Rand.NextDouble() < chance)
        {
            OnMisscast(caster, world);
            return false;
        }

        Execute(caster, world, spellLevel);
        return true;
    }

    /// <summary>
    /// Returns the misscast chance (0-1) for the given spell level.
    /// Lvl 1=30%, Lvl 2=22%, Lvl 3=14%, Lvl 4=5%, Lvl 5+=0%.
    /// </summary>
    public static float MisscastChanceForLevel(int spellLevel)
    {
        int idx = spellLevel - 1;
        if (idx < 0) return MisscastChance[0];
        if (idx >= MisscastChance.Length) return 0f;
        return MisscastChance[idx];
    }

    /// <summary>
    /// Called when a misscast occurs. Override for custom misscast effects.
    /// Default: no-op (spell simply fizzles).
    /// </summary>
    protected virtual void OnMisscast(EntityAgent caster, IWorldAccessor world) { }

    /// <summary>
    /// Execute the spell effect. Called server-side after a successful cast roll.
    /// </summary>
    public abstract void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel);

    // ---- Scaling ----

    public const int MaxSpellLevel = 10;

    /// <summary>Damage multiplier at given spell level. +15% per level.</summary>
    public virtual float GetDamageMultiplier(int spellLevel) => 1f + 0.15f * (spellLevel - 1);

    /// <summary>Cast time multiplier: -5% per level, min 0.5x (50% faster at lvl10).</summary>
    public virtual float GetCastTimeMultiplier(int spellLevel) => Math.Max(0.5f, 1f - 0.05f * (spellLevel - 1));

    /// <summary>Range multiplier for Offense spells: +10% per level.</summary>
    public virtual float GetRangeMultiplier(int spellLevel) => 1f + 0.10f * (spellLevel - 1);

    /// <summary>Particle multiplier: 1x at lvl1-4, 2x at lvl5-8, 3x at lvl9-10.</summary>
    public virtual int GetParticleMultiplier(int spellLevel) => 1 + (spellLevel - 1) / 4;

    /// <summary>Called every tick while the spell is active (channeled/persistent spells).</summary>
    public virtual void OnTick(EntityAgent caster, IWorldAccessor world, float deltaTime) { }

    /// <summary>Called when the spell effect ends or is interrupted.</summary>
    public virtual void OnEnd(EntityAgent caster, IWorldAccessor world) { }
}
