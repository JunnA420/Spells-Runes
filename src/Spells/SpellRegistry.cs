using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SpellsAndRunes.Spells;

public static class SpellRegistry
{
    private static readonly Dictionary<string, Spell> spells = new();
    private static bool registered = false;

    public static IReadOnlyDictionary<string, Spell> All => spells;

    public static void RegisterAll()
    {
        if (registered) return;
        registered = true;

        var spellType = typeof(Spell);
        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && spellType.IsAssignableFrom(t));

        foreach (var type in types)
        {
            var spell = (Spell)Activator.CreateInstance(type)!;
            Register(spell);
        }
    }

    public static void Register(Spell spell)
    {
        if (spells.ContainsKey(spell.Id))
            throw new InvalidOperationException($"Spell '{spell.Id}' is already registered.");
        spells[spell.Id] = spell;
    }

    public static Spell? Get(string id) => spells.TryGetValue(id, out var s) ? s : null;
    public static bool Exists(string id) => spells.ContainsKey(id);
}
