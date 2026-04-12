using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using SpellsAndRunes.Commands;

namespace SpellsAndRunes.Flux;

public class EntityBehaviorFlux : EntityBehavior
{
    private float accumulator;
    private const string AttrFlux = "spellsandrunes:flux";
    private const string AttrMaxFlux = "spellsandrunes:maxflux";
    private const string AttrFluxAlignmentLevel = "spellsandrunes:fluxalignmentlevel";
    private const string AttrActivators = "snr:activators";
    public const string FluxAlignmentLevel2Activator = "fluxalignment_2";

    private const int DefaultAlignmentLevel = 1;
    private const int MaxAlignmentLevel = 2;
    private const float Level1MaxFlux = 100f;
    private const float Level2MaxFlux = 150f;
    private const float Level1RegenPerSecond = 5f;
    private const float Level2RegenPerSecond = 6f;

    public EntityBehaviorFlux(Entity entity) : base(entity) { }

    public override string PropertyName() => "fluxBehavior";

    public override void OnEntitySpawn()
    {
        if (entity.Api.Side != EnumAppSide.Server) return;

        EnsureAlignmentInitialized();
        TryPromoteAlignmentFromActivators();
        RefreshFluxStatsFromAlignment();

        if (!entity.WatchedAttributes.HasAttribute(AttrFlux))
        {
            entity.WatchedAttributes.SetFloat(AttrFlux, entity.WatchedAttributes.GetFloat(AttrMaxFlux, GetMaxFluxForLevel(DefaultAlignmentLevel)));
            entity.WatchedAttributes.MarkPathDirty(AttrFlux);
        }
    }

    public override void OnEntityLoaded()
    {
        if (entity.Api.Side != EnumAppSide.Server) return;

        EnsureAlignmentInitialized();
        TryPromoteAlignmentFromActivators();
        RefreshFluxStatsFromAlignment();

        if (!entity.WatchedAttributes.HasAttribute(AttrFlux))
            entity.WatchedAttributes.SetFloat(AttrFlux, entity.WatchedAttributes.GetFloat(AttrMaxFlux, GetMaxFluxForLevel(DefaultAlignmentLevel)));

        float flux = entity.WatchedAttributes.GetFloat(AttrFlux, 0f);
        float max  = entity.WatchedAttributes.GetFloat(AttrMaxFlux, GetMaxFluxForLevel(GetFluxAlignmentLevel()));
        entity.Api.Logger.Notification($"[SnR] Player loaded, flux={flux}/{max}, alignment={GetFluxAlignmentLevel()}");

        entity.WatchedAttributes.MarkPathDirty(AttrMaxFlux);
        entity.WatchedAttributes.MarkPathDirty(AttrFlux);
        entity.WatchedAttributes.MarkPathDirty(AttrFluxAlignmentLevel);

        // Force-sync all spell data trees to the client on load
        foreach (var key in new[] {
            "snr:unlocked", "snr:hotbar", "snr:elementxp", "snr:elementlevel",
            "snr:elementsp", "snr:spellxp", "snr:spelllevel", "snr:activators"
        })
        {
            if (entity.WatchedAttributes.HasAttribute(key))
                entity.WatchedAttributes.MarkPathDirty(key);
        }
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.Api.Side != EnumAppSide.Server) return;

        accumulator += deltaTime;
        if (accumulator < 0.25f) return;
        float elapsed = accumulator;
        accumulator = 0f;

        TryPromoteAlignmentFromActivators();

        float max = entity.WatchedAttributes.GetFloat(AttrMaxFlux, GetMaxFluxForLevel(GetFluxAlignmentLevel()));
        float current = entity.WatchedAttributes.GetFloat(AttrFlux, 0f);
        if (current >= max) return;

        float regen = DebugCommands.InfiniteFlux ? 200f : GetRegenForLevel(GetFluxAlignmentLevel());
        float next = Math.Min(current + regen * elapsed, max);
        entity.WatchedAttributes.SetFloat(AttrFlux, next);
        entity.WatchedAttributes.MarkPathDirty(AttrFlux);
        entity.Api.Logger.Notification($"[SnR] Flux regen: {current:F1} -> {next:F1}/{max}");
    }

    public int GetFluxAlignmentLevel()
        => Math.Clamp(entity.WatchedAttributes.GetInt(AttrFluxAlignmentLevel, DefaultAlignmentLevel), 1, MaxAlignmentLevel);

    public int SetFluxAlignmentLevel(int level)
    {
        level = Math.Clamp(level, 1, MaxAlignmentLevel);
        entity.WatchedAttributes.SetInt(AttrFluxAlignmentLevel, level);
        entity.WatchedAttributes.MarkPathDirty(AttrFluxAlignmentLevel);
        RefreshFluxStatsFromAlignment();
        return level;
    }

    public float GetMaxFluxForLevel(int level)
        => Math.Clamp(level, 1, MaxAlignmentLevel) switch
        {
            2 => Level2MaxFlux,
            _ => Level1MaxFlux,
        };

    public float GetRegenForLevel(int level)
        => Math.Clamp(level, 1, MaxAlignmentLevel) switch
        {
            2 => Level2RegenPerSecond,
            _ => Level1RegenPerSecond,
        };

    public void RefreshFluxStatsFromAlignment()
    {
        int level = GetFluxAlignmentLevel();
        float maxFlux = GetMaxFluxForLevel(level);

        entity.WatchedAttributes.SetFloat(AttrMaxFlux, maxFlux);
        entity.WatchedAttributes.MarkPathDirty(AttrMaxFlux);
    }

    public bool TryPromoteAlignmentFromActivators()
    {
        if (GetFluxAlignmentLevel() >= MaxAlignmentLevel) return false;
        if (entity.WatchedAttributes.GetTreeAttribute(AttrActivators) is not TreeAttribute activators) return false;
        if (!activators.HasAttribute(FluxAlignmentLevel2Activator)) return false;

        SetFluxAlignmentLevel(2);
        return true;
    }

    /// <summary>
    /// Attempts to consume the given amount of Flux. Returns false if insufficient.
    /// Must be called server-side.
    /// </summary>
    public bool TryConsumeFlux(float amount)
    {
        float current = entity.WatchedAttributes.GetFloat(AttrFlux, 0f);
        if (current < amount) return false;

        entity.WatchedAttributes.SetFloat(AttrFlux, current - amount);
        entity.WatchedAttributes.MarkPathDirty(AttrFlux);
        return true;
    }

    private void EnsureAlignmentInitialized()
    {
        if (!entity.WatchedAttributes.HasAttribute(AttrFluxAlignmentLevel))
        {
            entity.WatchedAttributes.SetInt(AttrFluxAlignmentLevel, DefaultAlignmentLevel);
            entity.WatchedAttributes.MarkPathDirty(AttrFluxAlignmentLevel);
        }
    }
}
