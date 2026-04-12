using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells.Flux;

/// <summary>
/// Flux Alignment Tier I — The first stirring of raw flux within the body.
/// A faint shimmer radiates outward, unsettling nearby entities.
/// </summary>
public class FluxExpressionWhisper : Spell
{
    public override string Id          => "flux_expression_1";
    public override string Name        => "Flux Expression: Whisper";
    public override string Description => "The body breathes flux for the first time. A faint emanation ripples outward — animals feel it, and lesser beings flinch.";

    public override SpellTier    Tier    => SpellTier.Novice;
    public override SpellElement Element => SpellElement.Flux;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 20f;
    public override float CastTime => 1.0f;

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: emit a short flux shimmer aura, spook passive animals in radius 6
    }
}

/// <summary>
/// Flux Alignment Tier II — Directed surge of raw flux intent.
/// Nearby hostile entities stagger and lose focus momentarily.
/// </summary>
public class FluxExpressionSurge : Spell
{
    public override string Id          => "flux_expression_2";
    public override string Name        => "Flux Expression: Surge";
    public override string Description => "A visible burst of flux erupts from the body. Those caught in the wave feel their will falter and their limbs grow heavy.";

    public override SpellTier    Tier    => SpellTier.Apprentice;
    public override SpellElement Element => SpellElement.Flux;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 45f;
    public override float CastTime => 1.5f;

    public override IReadOnlyList<string> Prerequisites => new[] { "flux_expression_1" };

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: stagger nearby hostile entities in radius 10, brief slowness debuff
    }
}

/// <summary>
/// Flux Alignment Tier III — Sustained resonance of body and flux.
/// The caster radiates an aura that continuously drains the will of surrounding foes.
/// </summary>
public class FluxExpressionMantle : Spell
{
    public override string Id          => "flux_expression_3";
    public override string Name        => "Flux Expression: Mantle";
    public override string Description => "Flux saturates the body and bleeds outward as a visible mantle. Within its reach, enemies feel the weight of a crushing presence.";

    public override SpellTier    Tier    => SpellTier.Adept;
    public override SpellElement Element => SpellElement.Flux;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 80f;
    public override float CastTime => 2.0f;

    public override IReadOnlyList<string> Prerequisites => new[] { "flux_expression_2" };

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: sustained 8s aura, enemies in radius 14 receive fatigue + stagger on entry
    }
}

/// <summary>
/// Flux Alignment Tier IV — Complete sovereignty of the body's flux.
/// The release is absolute — those unprepared are overwhelmed entirely.
/// </summary>
public class FluxExpressionSovereignty : Spell
{
    public override string Id          => "flux_expression_4";
    public override string Name        => "Flux Expression: Sovereignty";
    public override string Description => "The body and flux speak as one voice. All who are touched by this overwhelming wave are forced to their knees — or are simply broken.";

    public override SpellTier    Tier    => SpellTier.Master;
    public override SpellElement Element => SpellElement.Flux;
    public override SpellType    Type    => SpellType.Enchantment;

    public override float FluxCost => 140f;
    public override float CastTime => 2.5f;

    public override IReadOnlyList<string> Prerequisites => new[] { "flux_expression_3" };

    public override void Execute(EntityAgent caster, IWorldAccessor world, int spellLevel)
    {
        // TODO: massive radius 20 knockdown, weaker entities lose consciousness briefly
    }
}
