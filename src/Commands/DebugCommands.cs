using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
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
    public static void Register(ICoreServerAPI api)
    {
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
            // /snr spell_lvl [on|off]
            .BeginSubCommand("spell_lvl")
                .WithDescription("Get specific level for spell  (e.g. /snr spell_lvl fire_spark 3)")
                    .WithArgs(
                        api.ChatCommands.Parsers.Word("spellId"),
                        api.ChatCommands.Parsers.Int("level"))
                .HandleWith(OnSpellLevel)
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
}
