# src/ — PeakRunSave plugin source (C#)

The actual BepInEx plugin code. Built with `dotnet build` from repo root.

## Layout

- `PeakRunSave/` — the plugin's .csproj + C# source files.

## Load / skip

- Read individual `.cs` files relevant to the change you're making.
- Don't read `bin/` or `obj/` build outputs — those are regenerated.

## Pipeline (what the plugin does)

1. **BepInEx loads the compiled DLL at game start**, applying Harmony patches to PEAK's assemblies.
2. On `F5`: walk PEAK's runtime state (seed, segment, players, inventories) and serialize to `BepInEx/config/PeakRunSave/run_snapshot.json` (or a numbered slot).
3. On `F9`: deserialize the snapshot, re-apply seed via `LevelGeneration`/`MapGenerator` if present, broadcast position/inventory RPCs to clients.
4. On `F10`: toggle an IMGUI save-menu overlay.

## Key gotchas (from `PeakRunSave.md`)

- Inventory serialization stores **item IDs only**, not per-item custom data.
- Player matching is by Photon actor number — lobby composition changes break it.
- Seed re-apply is timing-sensitive across scene loads.
- Remote inventory restore (`ExperimentalRemoteInventoryRestore`) is opt-in because some setups hang on the RPC.
- World state (map mods, unlocked progress, environment) is **not** persisted.

## Build dependencies

The .csproj references PEAK's assemblies. `Config.Build.user.props` (gitignored, made from the template at repo root) tells MSBuild where the game install lives.

## Relevant skills

None specific — this is straightforward C# / BepInEx / Harmony work.
