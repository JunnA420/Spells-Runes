using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SpellsAndRunes.Spells;

/// <summary>
/// Client-side helper for playing spell animations on player entities.
/// Handles upper-body masking so legs continue their locomotion animation.
/// </summary>
public static class SpellAnimations
{
    // Leg bones that should NOT be affected by upper-body spell animations
    private static readonly string[] LegBones =
    {
        "LowerTorso",
        "UpperFootL", "LowerFootL", "FootAttachmentL",
        "UpperFootR", "LowerFootR", "FootAttachmentR",
    };

    /// <summary>
    /// Play a spell animation on the given entity.
    /// If upperBodyOnly=true, leg bones are given weight 0 so locomotion continues normally.
    /// </summary>
    public static void Play(EntityAgent entity, string animCode, bool upperBodyOnly = true, float animationSpeed = 1f)
    {
        var meta = new AnimationMetaData
        {
            Code             = animCode,
            Animation        = animCode,
            BlendMode        = EnumAnimationBlendMode.Add,
            EaseInSpeed      = 10f,
            EaseOutSpeed     = 10f,
            Weight           = 1f,
            AnimationSpeed   = animationSpeed,
        };

        entity.AnimManager?.StartAnimation(meta);
    }

    /// <summary>Stop a spell animation by code.</summary>
    public static void Stop(EntityAgent entity, string animCode)
    {
        entity.AnimManager?.StopAnimation(animCode);
    }
}
