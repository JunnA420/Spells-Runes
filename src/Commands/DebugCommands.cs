using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using SpellsAndRunes.Flux;
using SpellsAndRunes.Spells;

namespace SpellsAndRunes.Commands;

/// <summary>
/// Debug commands for Spells & Runes.
/// Usage:
///   /snr xp &lt;element&gt; &lt;amount&gt;   — add element XP (e.g. /snr xp Air 100)
///   /snr unlock &lt;spellId&gt;         — force-unlock a spell
///   /snr activator &lt;id&gt;           — trigger an activator
///   /snr status                    — print XP and unlocked spells
/// </summary>
public static class DebugCommands
{
    private static ICoreServerAPI? sapi;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;

        api.ChatCommands
            .Create("snr")
            .WithDescription("Spells & Runes debug commands")
            .RequiresPrivilege(Privilege.controlserver)

            // /snr xp <element> <amount>
            .BeginSubCommand("xp")
                .WithDescription("Add element XP (e.g. /snr xp Air 100)")
                .WithArgs(
                    api.ChatCommands.Parsers.Word("element"),
                    api.ChatCommands.Parsers.Int("amount"))
                .HandleWith(OnXp)
            .EndSubCommand()

            // /snr unlock <spellId>
            .BeginSubCommand("unlock")
                .WithDescription("Force-unlock a spell by id")
                .WithArgs(api.ChatCommands.Parsers.Word("spellId"))
                .HandleWith(OnUnlock)
            .EndSubCommand()

            // /snr activator <id>
            .BeginSubCommand("activator")
                .WithDescription("Trigger a spell activator")
                .WithArgs(api.ChatCommands.Parsers.Word("activatorId"))
                .HandleWith(OnActivator)
            .EndSubCommand()

            // /snr status
            .BeginSubCommand("status")
                .WithDescription("Show element XP and unlocked spells")
                .HandleWith(OnStatus)
            .EndSubCommand()

            // /snr unlock-flux
            .BeginSubCommand("unlock-flux")
                .WithDescription("Unlock Flux (simulate smoking Sylphweed)")
                .HandleWith(OnUnlockFlux)
            .EndSubCommand()

            // /snr unlock-element <element>
            .BeginSubCommand("unlock-element")
                .WithDescription("Unlock an element tree (e.g. /snr unlock-element Air)")
                .WithArgs(api.ChatCommands.Parsers.Word("element"))
                .HandleWith(OnUnlockElement)
            .EndSubCommand()

            // /snr flux [on|off]
            .BeginSubCommand("flux")
                .WithDescription("Toggle infinite flux (sets regen to 200/s)")
                .WithArgs(api.ChatCommands.Parsers.OptionalWord("toggle"))
                .HandleWith(OnFlux)
            .EndSubCommand()

            .BeginSubCommand("flux-alignment")
                .WithDescription("Inspect and modify Flux Alignment")
                .BeginSubCommand("show")
                    .WithDescription("Show current Flux Alignment level and derived stats")
                    .HandleWith(OnFluxAlignmentShow)
                .EndSubCommand()
                .BeginSubCommand("set")
                    .WithDescription("Set Flux Alignment level (1 or 2)")
                    .WithArgs(api.ChatCommands.Parsers.Int("level"))
                    .HandleWith(OnFluxAlignmentSet)
                .EndSubCommand()
                .BeginSubCommand("trigger")
                    .WithDescription("Trigger the level 2 Flux Alignment activator")
                    .HandleWith(OnFluxAlignmentTrigger)
                .EndSubCommand()
                .BeginSubCommand("reset")
                    .WithDescription("Remove the level 2 activator and reset Flux Alignment to level 1")
                    .HandleWith(OnFluxAlignmentReset)
                .EndSubCommand()
            .EndSubCommand()

            // /snr spell_lvl [on|off]
            .BeginSubCommand("spell_lvl")
                .WithDescription("Get specific level for spell  (e.g. /snr spell_lvl fire_spark 3)")
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("spellId"),
                        api.ChatCommands.Parsers.Int("level"))
                .HandleWith(OnSpellLevel)
            .EndSubCommand()

            // /snr findsylphweed [radius] [max]
            .BeginSubCommand("findsylphweed")
                .WithDescription("Find nearby sylphweed blocks (usage: /snr findsylphweed 128 10)")
                .WithArgs(
                    api.ChatCommands.Parsers.OptionalWord("radius"),
                    api.ChatCommands.Parsers.OptionalWord("max"))
                .HandleWith(OnFindSylphweed)
            .EndSubCommand();
    }

    public static bool InfiniteFlux { get; private set; } = false;

    private static TextCommandResult OnFlux(TextCommandCallingArgs args)
    {
        string raw = args[0] as string ?? "";
        string toggle = raw.ToLowerInvariant();
        InfiniteFlux = toggle == "off" ? false : toggle == "on" ? true : !InfiniteFlux;
        return TextCommandResult.Success($"Infinite flux: {(InfiniteFlux ? "ON" : "OFF")}");
    }

    private static TextCommandResult OnFluxAlignmentShow(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        if (entity.GetBehavior<EntityBehaviorFlux>() is not { } behavior)
            return TextCommandResult.Error("Flux behavior is not attached to this entity.");

        int level = behavior.GetFluxAlignmentLevel();
        float maxFlux = behavior.GetMaxFluxForLevel(level);
        float regen = behavior.GetRegenForLevel(level);
        float currentFlux = entity.WatchedAttributes.GetFloat("spellsandrunes:flux", 0f);
        return TextCommandResult.Success($"Flux Alignment level {level}. Flux: {currentFlux:0.#}/{maxFlux:0.#}. Regen: {regen:0.#}/s.");
    }

    private static TextCommandResult OnFluxAlignmentSet(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        if (entity.GetBehavior<EntityBehaviorFlux>() is not { } behavior)
            return TextCommandResult.Error("Flux behavior is not attached to this entity.");

        int level = (int)args[0];
        if (level is < 1 or > 2)
            return TextCommandResult.Error("Flux Alignment level must be 1 or 2.");

        int applied = behavior.SetFluxAlignmentLevel(level);
        float maxFlux = behavior.GetMaxFluxForLevel(applied);
        float regen = behavior.GetRegenForLevel(applied);
        return TextCommandResult.Success($"Flux Alignment set to level {applied}. Effective stats: {maxFlux:0.#} max flux, {regen:0.#}/s regen.");
    }

    private static TextCommandResult OnFluxAlignmentTrigger(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        if (entity.GetBehavior<EntityBehaviorFlux>() is not { } behavior)
            return TextCommandResult.Error("Flux behavior is not attached to this entity.");

        var activators = entity.WatchedAttributes.GetOrAddTreeAttribute("snr:activators");
        activators.SetInt(EntityBehaviorFlux.FluxAlignmentLevel2Activator, 1);
        entity.WatchedAttributes.MarkPathDirty("snr:activators");

        behavior.TryPromoteAlignmentFromActivators();
        int level = behavior.GetFluxAlignmentLevel();
        float maxFlux = behavior.GetMaxFluxForLevel(level);
        float regen = behavior.GetRegenForLevel(level);
        return TextCommandResult.Success($"Triggered Flux Alignment activator '{EntityBehaviorFlux.FluxAlignmentLevel2Activator}'. Level is now {level} ({maxFlux:0.#} max, {regen:0.#}/s regen).");
    }

    private static TextCommandResult OnFluxAlignmentReset(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        if (entity.GetBehavior<EntityBehaviorFlux>() is not { } behavior)
            return TextCommandResult.Error("Flux behavior is not attached to this entity.");

        if (entity.WatchedAttributes.GetTreeAttribute("snr:activators") is TreeAttribute activators)
        {
            activators.RemoveAttribute(EntityBehaviorFlux.FluxAlignmentLevel2Activator);
            entity.WatchedAttributes.MarkPathDirty("snr:activators");
        }

        behavior.SetFluxAlignmentLevel(1);
        return TextCommandResult.Success("Flux Alignment reset to level 1 and level 2 activator removed.");
    }

    private static TextCommandResult OnSpellLevel(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        var data = PlayerSpellData.For(entity);
        string spellId = (string)args[0];
        int level = (int)args[1];
        data.SetSpellLevel(spellId, level);
        // Implement logic to set the spell level here
        return TextCommandResult.Success($"Set spell '{spellId}' to level {level}.");

    }

    private static TextCommandResult OnXp(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        string elementStr = (string)args[0];
        int amount        = (int)args[1];

        if (!Enum.TryParse<SpellElement>(elementStr, ignoreCase: true, out var element))
            return TextCommandResult.Error($"Unknown element '{elementStr}'. Valid: {string.Join(", ", Enum.GetNames<SpellElement>())}");

        var data = PlayerSpellData.For(entity);
        data.AddElementXp(element, amount);
        return TextCommandResult.Success($"Added {amount} {element} XP. Total: {data.GetElementXp(element)}");
    }

    private static TextCommandResult OnUnlock(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        string spellId = (string)args[0];
        if (!SpellRegistry.Exists(spellId))
            return TextCommandResult.Error($"Unknown spell id: '{spellId}'");

        var data = PlayerSpellData.For(entity);
        if (data.IsUnlocked(spellId))
            return TextCommandResult.Success($"'{spellId}' already unlocked.");

        data.Unlock(spellId);
        return TextCommandResult.Success($"Unlocked '{spellId}'.");
    }

    private static TextCommandResult OnUnlockFlux(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        var data = PlayerSpellData.For(entity);
        if (data.IsFluxUnlocked)
            return TextCommandResult.Success("Flux already unlocked.");

        data.UnlockFlux();
        return TextCommandResult.Success("Flux unlocked.");
    }

    private static TextCommandResult OnUnlockElement(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        string elementStr = (string)args[0];
        if (!Enum.TryParse<SpellElement>(elementStr, ignoreCase: true, out var element))
            return TextCommandResult.Error($"Unknown element '{elementStr}'. Valid: {string.Join(", ", Enum.GetNames<SpellElement>())}");

        var data = PlayerSpellData.For(entity);
        data.TriggerActivator($"element_{element.ToString().ToLowerInvariant()}");
        return TextCommandResult.Success($"Element '{element}' unlocked.");
    }

    private static TextCommandResult OnActivator(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        string activatorId = (string)args[0];
        var data = PlayerSpellData.For(entity);
        data.TriggerActivator(activatorId);
        return TextCommandResult.Success($"Activator '{activatorId}' triggered.");
    }

    private static TextCommandResult OnStatus(TextCommandCallingArgs args)
    {
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        var data = PlayerSpellData.For(entity);

        var xpLines = new System.Text.StringBuilder();
        foreach (SpellElement el in Enum.GetValues<SpellElement>())
        {
            int xp = data.GetElementXp(el);
            if (xp > 0) xpLines.AppendLine($"  {el} XP: {xp}");
        }

        var unlocked = string.Join(", ", data.GetUnlockedIds());
        return TextCommandResult.Success(
            $"Element XP:\n{(xpLines.Length > 0 ? xpLines.ToString() : "  none")}" +
            $"Unlocked: {(string.IsNullOrEmpty(unlocked) ? "none" : unlocked)}"
        );
    }

    private static TextCommandResult OnFindSylphweed(TextCommandCallingArgs args)
    {
        if (sapi == null) return TextCommandResult.Error("Server API not initialized.");
        if (args.Caller.Entity is not { } entity)
            return TextCommandResult.Error("No player entity found.");

        int radius = TryParsePositiveInt(args[0] as string, 800);
        int max = TryParsePositiveInt(args[1] as string, 10);
        max = Math.Clamp(max, 1, 50);

        var target = sapi.World.GetBlock(new AssetLocation("spellsandrunes:sylphweed"));
        if (target == null || target.BlockId == 0)
            return TextCommandResult.Error("Block spellsandrunes:sylphweed is not loaded.");

        var center = entity.Pos.AsBlockPos;
        int yMin = Math.Max(1, center.Y - 80);
        int yMax = center.Y + 80;
        int r2 = radius * radius;

        var results = new System.Collections.Generic.List<(BlockPos pos, int dist2)>();
        var ba = sapi.World.BlockAccessor;

        var pos = new BlockPos(0, 0, 0, entity.Pos.Dimension);

        for (int x = center.X - radius; x <= center.X + radius; x++)
        {
            for (int z = center.Z - radius; z <= center.Z + radius; z++)
            {
                int dx = x - center.X;
                int dz = z - center.Z;
                int hDist2 = dx * dx + dz * dz;
                if (hDist2 > r2) continue;

                for (int y = yMin; y <= yMax; y++)
                {
                    pos.Set(x, y, z);
                    var block = ba.GetBlock(pos);
                    if (block?.BlockId != target.BlockId) continue;

                    int dy = y - center.Y;
                    int dist2 = hDist2 + dy * dy;
                    results.Add((new BlockPos(x, y, z), dist2));
                }
            }
        }

        if (results.Count == 0)
            return TextCommandResult.Success($"No sylphweed found within radius {radius} (Y +/- 80).");

        results.Sort((a, b) => a.dist2.CompareTo(b.dist2));

        var sb = new System.Text.StringBuilder();
        int shown = Math.Min(max, results.Count);
        sb.AppendLine($"Found {results.Count} sylphweed block(s). Showing {shown} nearest:");
        for (int i = 0; i < shown; i++)
        {
            var hit = results[i];
            int dist = (int)Math.Round(Math.Sqrt(hit.dist2));
            sb.AppendLine($"  {i + 1}. {hit.pos.X}, {hit.pos.Y}, {hit.pos.Z} (~{dist}m)");
        }

        return TextCommandResult.Success(sb.ToString());
    }

    private static int TryParsePositiveInt(string? raw, int fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return int.TryParse(raw, out int val) && val > 0 ? val : fallback;
    }
}
