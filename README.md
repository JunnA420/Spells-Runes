# Spells & Runes

A fast-paced elemental magic mod for Vintage Story. Cast spells with a flux-based mana system and chain abilities into quick combos. Master multiple elements, each with their own spell tree — built for fluid, dynamic combat.

## Features

- **Flux system** — mana resource that regenerates over time, consumed on cast
- **Spell trees** — unlock spells per element, level them up through use
- **Radial spell selector** — quick hotbar with up to 3 memorized spells
- **Cast bar** — channeled spells with cast times

---

## Spell Trees

Each element has its own tree. Unlock the two base spells to access the combined spell in the center.

Legend: `⚔ Offense` `🛡 Defense` `✦ Enchantment`

---

### 🌬 Air

```
[Feather Fall ✦]           [Air Push ⚔]
  Halt your fall               Cone knockback
  15 flux · 0.5s               25 flux · 1.5s
        \                       /
         \                     /
          \                   /
           [  Air Kick ⚔  ]
           Launch + projectile
           30 flux · 3.0s
```

---

### 🔥 Fire

```
[Hot Skin 🛡]              [Spark ⚔]
  Burn on contact             Fire damage bolt
  18 flux                     15 flux
        \                       /
         \                     /
          \                   /
           [ Cook in Hand ✦ ]
           Instantly cook held item
           12 flux
```

---

### 💧 Water

```
[Healing ✦]               [Water Spray ⚔]
  Mend your wounds            Knockback jet
  20 flux                     18 flux
        \                       /
         \                     /
          \                   /
           [Healing Sprinkle ✦]
           AoE heal around you
           25 flux
```

---

### 🪨 Earth

```
[Stone Skin 🛡]            [Earth Wall 🛡]
  Reduce damage               Raise a barrier
  14 flux                     20 flux
        \                       /
         \                     /
          \                   /
           [ Earth Clone ✦ ]
           Summon a decoy
           22 flux
```

---

### ⚡ Element Combinations *(planned)*

Mastering two elements unlocks a combined spell tree with unique abilities.

| Elements | Combined | Example |
|---|---|---|
| 🌬 Air + 🔥 Fire | ⚡ Lightning | High-speed electric strikes |
| 🌬 Air + 💧 Water | 🌀 Storm | Wind and rain control |
| 🌬 Air + 🪨 Earth | 🌪 Dust | Sandstorm, debris projectiles |
| 🔥 Fire + 💧 Water | ☁ Steam | Scalding cloud, vision obscure |
| 🔥 Fire + 🪨 Earth | 🌋 Magma | Lava, heat-based earth spells |
| 💧 Water + 🪨 Earth | 🌿 Nature | Vines, poison, growth |

---

## Branch Flow

```
junna / blitz  →  dev  →  master
```

- `master` — stable releases only
- `dev` — integration branch, tested before merge to master
- `junna` / `blitz` — personal dev branches, PR into dev

## Building

Requires [Vintage Story](https://www.vintagestory.at/) installed at `C:\Games\Vintagestory`.

```bash
dotnet build SpellsAndRunes.csproj -c Release
```

Output zip is automatically copied to `%APPDATA%\VintagestoryData\Mods\`.

## Authors

- JunnA
- BL1TZ
