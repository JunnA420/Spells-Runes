# Spells & Runes

A fast-paced elemental magic mod for Vintage Story. Cast spells using a flux-based mana system, chain abilities into combos, and master multiple elements each with their own unlockable spell tree.

---

## Roadmap

**[View full roadmap with spell trees and progress](https://junnA420.github.io/Spells-Runes/roadmap.html)**

---

## Getting Started

Flux is locked until you awaken it. Find **Sylphweed** — a rare glowing plant — and process it:

1. Pick Sylphweed and dry it in an **oven**
2. Grind the dried herb in a **quern**
3. Smoke the ground herb using the **Sylphweed Pipe**

Once done, Flux is permanently unlocked. Open the **Spellbook** with `K`, unlock spells, and assign them to the **Radial Menu** with `R`.

---

## Core Systems

**Flux** — mana resource, regenerates 1/s, max 100. Displayed as an arc in the bottom-left corner. Hidden until awakened.

**Spell trees** — each element has its own tree unlocked independently. Spells level up through use, reducing cost and cast time at higher levels.

**Radial menu** — hold `R` to open, release over a spell to select. Up to 8 slots.

**Cast bar** — channeled spells show a progress bar. Right-click cancels.

**Animations** — spell casts play upper-body animations blended on top of locomotion.

---

## Elements

| Element | Status |
|---|---|
| Air | Implemented |
| Fire | Partial |
| Water | Placeholder |
| Earth | Placeholder |

---

## Branch Flow

```
junna / blitz  ->  dev  ->  master
```

- `master` — stable releases
- `dev` — integration, tested before merge
- `junna` / `blitz` — personal branches, PR into dev

---

## Building

Requires [Vintage Story](https://www.vintagestory.at/) at `C:\Games\Vintagestory`.

```bash
dotnet build SpellsAndRunes.csproj -c Release
```

Output is copied automatically to `%APPDATA%\VintagestoryData\Mods\`.

---

## Authors

- JunnA
- BL1TZ
