using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using SpellsAndRunes.Commands;

namespace SpellsAndRunes.Flux;

public class EntityBehaviorFlux : EntityBehavior
{
    private float accumulator;
    private const float RegenPerSecond = 20f; // TODO: balance regen rate before release
    private const float DefaultMax = 100f;

    public EntityBehaviorFlux(Entity entity) : base(entity) { }

    public override string PropertyName() => "fluxBehavior";

    public override void OnEntitySpawn()
    {
        if (entity.Api.Side != EnumAppSide.Server) return;
        if (!entity.WatchedAttributes.HasAttribute("spellsandrunes:flux"))
        {
            entity.WatchedAttributes.SetFloat("spellsandrunes:maxflux", DefaultMax);
            entity.WatchedAttributes.SetFloat("spellsandrunes:flux", DefaultMax);
            entity.WatchedAttributes.MarkPathDirty("spellsandrunes:maxflux");
            entity.WatchedAttributes.MarkPathDirty("spellsandrunes:flux");
        }
    }

    public override void OnEntityLoaded()
    {
        if (entity.Api.Side != EnumAppSide.Server) return;

        // Always ensure maxflux is set
        if (!entity.WatchedAttributes.HasAttribute("spellsandrunes:maxflux"))
            entity.WatchedAttributes.SetFloat("spellsandrunes:maxflux", DefaultMax);

        // If flux is missing or below 0, reset to max
        if (!entity.WatchedAttributes.HasAttribute("spellsandrunes:flux"))
            entity.WatchedAttributes.SetFloat("spellsandrunes:flux", DefaultMax);

        float flux = entity.WatchedAttributes.GetFloat("spellsandrunes:flux", 0f);
        float max  = entity.WatchedAttributes.GetFloat("spellsandrunes:maxflux", DefaultMax);
        entity.Api.Logger.Notification($"[SnR] Player loaded, flux={flux}/{max}");

        entity.WatchedAttributes.MarkPathDirty("spellsandrunes:maxflux");
        entity.WatchedAttributes.MarkPathDirty("spellsandrunes:flux");

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

        float max = entity.WatchedAttributes.GetFloat("spellsandrunes:maxflux", DefaultMax);
        float current = entity.WatchedAttributes.GetFloat("spellsandrunes:flux", 0f);
        if (current >= max) return;

        float regen = DebugCommands.InfiniteFlux ? 200f : RegenPerSecond;
        float next = Math.Min(current + regen * elapsed, max);
        entity.WatchedAttributes.SetFloat("spellsandrunes:flux", next);
        entity.WatchedAttributes.MarkPathDirty("spellsandrunes:flux");
        entity.Api.Logger.Notification($"[SnR] Flux regen: {current:F1} -> {next:F1}/{max}");
    }

    /// <summary>
    /// Attempts to consume the given amount of Flux. Returns false if insufficient.
    /// Must be called server-side.
    /// </summary>
    public bool TryConsumeFlux(float amount)
    {
        float current = entity.WatchedAttributes.GetFloat("spellsandrunes:flux", 0f);
        if (current < amount) return false;

        entity.WatchedAttributes.SetFloat("spellsandrunes:flux", current - amount);
        entity.WatchedAttributes.MarkPathDirty("spellsandrunes:flux");
        return true;
    }
}
