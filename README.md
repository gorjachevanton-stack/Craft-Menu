# Craft Menu (Blocks Menu)

**A production catalog and order-management interface for RimWorld 1.6 and 1.7.**

> **Status: In Development.** The author has tested the mod in both RimWorld 1.6 and RimWorld 1.7 and confirms that it works on both versions. This is not yet a claim of compatibility with every mod list or every third-party mod.

## Install — no manual DLL setup

The repository includes the already-built mod assembly at `1.6/Assemblies/BlocksMenu.dll`. **Players do not need Visual Studio, .NET SDK, game DLLs, Unity DLLs, Harmony DLLs, or any other files to build the mod.**

1. Download this repository using **Code → Download ZIP**.
2. Extract the ZIP.
3. Copy the `Craft-Menu` folder into `RimWorld/Mods/`.
4. Enable **Blocks Menu** in the in-game Mods menu and restart if requested.

The installed mod folder must contain:
- `About/About.xml`
- `1.6/Assemblies/BlocksMenu.dll`
- `1.6/Defs/`
- `Textures/` (if present in this build)

If the mod reports a missing dependency in-game, share the exact error and your mod list; do not download random DLLs from third-party sites.

## What it does

- Browse production recipes in one catalog and search for recipes.
- Organize recipes into personal folders, with automatic population based on selected workbenches and manual inclusion or exclusion.
- View active production bills from multiple workbenches in one list while keeping them attached to their original RimWorld `BillStack` and workbenches.
- Browse apparel and professional equipment/tool catalogs.
- Open the menu from the main button labelled **«Крафт»**.

The mod is designed to work with RimWorld's existing production bills and queues rather than replacing the game's production system.

## Compatibility

- **RimWorld 1.6 — tested by the author.**
- **RimWorld 1.7 — tested by the author.**
- Compatibility with specific third-party mods and unusual item definitions may vary.

## Build from source (developers only)

Building the source is separate from installing the mod. Developers need the .NET Framework targeting pack/reference assemblies and local reference DLLs from their own RimWorld installation under `Lib/`. These game/Unity reference assemblies are **not required by players** and are intentionally not part of the player installation procedure.

Run `Build-Mod.bat` for a Release build, or `Build-Mod-Force-Rebuild.bat` for a forced rebuild. The expected output is `1.6/Assemblies/BlocksMenu.dll`.

## Project notes

- `Craft-Menu/README_RU.md` — detailed Russian project documentation.
- `Craft-Menu/CHANGES_STAGE1_RU.md` — Stage 1 change notes.
- `Craft-Menu/PERFORMANCE_UPDATE_RU.md` — performance-related notes.
- `Craft-Menu/RECIPE_AVAILABILITY_RULE_RU.md` — recipe availability rules.

The main menu definition retains the icon path `UI/Buttons/BlocksMenu`, with its texture at `Textures/UI/Buttons/BlocksMenu.png`.

## Bug reports

Please include the RimWorld version, mod list, reproduction steps, and relevant `Player.log` excerpts. Distinguish confirmed bugs from compatibility questions.
