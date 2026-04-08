using System;
using System.Collections.Generic;
using SpellsAndRunes.Spells.Air;
using SpellsAndRunes.Spells.Fire;
using SpellsAndRunes.Spells.Water;
using SpellsAndRunes.Spells.Earth;

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

        // Air
        Register(new FeatherFall());
        Register(new AirPush());
        Register(new AirKick());      // Jump Kick — prereq: FeatherFall + AirPush

        // Fire
        Register(new HotSkin());
        Register(new Spark());
        Register(new CookInHand());   // prereq: HotSkin + Spark

        // Water
        Register(new Healing());
        Register(new WaterSpray());
        Register(new HealingSprinkle()); // prereq: Healing + WaterSpray

        // Earth
        Register(new StoneSkin());
        Register(new EarthWall());
        Register(new EarthClone());   // prereq: StoneSkin + EarthWall
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
