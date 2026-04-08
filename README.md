# Spells & Runes

A fast-paced elemental magic mod for Vintage Story. Cast spells with a flux-based mana system and chain abilities into quick combos. Master multiple elements, each with their own spell tree — built for fluid, dynamic combat.

## Features

- **Flux system** — mana resource that regenerates over time, consumed on cast
- **Spell trees** — unlock spells per element, level them up through use
- **Radial spell selector** — quick hotbar with up to 3 memorized spells
- **Cast bar** — channeled spells with cast times
- **Air element** (implemented)
  - **Air Push** — cone knockback burst
  - **Feather Fall** — instantly arrests your momentum mid-air
  - **Air Kick** — launch yourself upward and fire a projectile

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
