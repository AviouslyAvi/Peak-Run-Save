# PeakRunSave

BepInEx plugin (C# / .NET) for the Unity co-op climbing game **PEAK**. Snapshots an in-progress run — seed, segment, all matched player positions, inventory item IDs, and optional health/stamina — and restores it later.

Host-only by default. Uses PEAK's built-in RPC handlers so other players in the lobby do **not** need this mod installed.

## What it saves

- Run seed
- Current segment (Beach / Tropics / Alpine / Caldera / TheKiln / Peak)
- Matched players' positions
- Inventory item IDs
- Optional: `passOutValue` (health-like) and stamina

## Controls

| Key | Action |
|---|---|
| `F5` | Save snapshot |
| `F9` | Load snapshot |
| `F10` | Toggle save menu |

The save menu (F10) shows numbered slot files (`run_snapshot_slot_1.json`, etc.) with last-modified timestamps, plus save/load buttons for each.

## Config

Generated at `BepInEx/config/com.avious.peak.runsave.cfg`:

- `SaveKey` (default `F5`), `LoadKey` (`F9`), `MenuKey` (`F10`)
- `MenuSlots` (`5`)
- `HostOnlyControls` (`true`, recommended)
- `RestoreHealth` (`true`)
- `RestoreAllPlayerPositions` (`true`)
- `RestoreAllPlayerInventories` (`true`)
- `ExperimentalRemoteInventoryRestore` (`false` — opt-in; can hang on some setups)
- `GroundRaycastHeight` (`6`), `GroundRaycastDistance` (`64`)
- `SaveFile` (`run_snapshot.json`, stored under `BepInEx/config/PeakRunSave/`)

## Build and deploy

1. Copy `Config.Build.user.props.template` to `Config.Build.user.props`.
2. Set `PeakGameRootDir` (and optionally `PeakPluginsDir`) in the new file.
3. Build:

   ```powershell
   dotnet build
   ```

Build outputs land in `artifacts/` (gitignored). The compiled DLL goes into your PEAK install's `BepInEx/plugins/` folder (manually, or via `PeakPluginsDir`).

## Folder layout

```
src/PeakRunSave/             # C# source — the plugin itself
PeakRunSave.sln              # Solution file
Directory.Build.props/.targets   # Shared MSBuild config
Config.Build.user.props.template # Copy this, fill in PeakGameRootDir
.config/dotnet-tools.json    # Local dotnet tool manifest
artifacts/                   # Build outputs (gitignored)
PeakRunSave.md               # Deeper project overview
CHANGELOG.md                 # Version history
```

## Requirements

- A working PEAK install (Steam).
- **BepInEx** installed in the PEAK game folder.
- .NET SDK (whatever version matches PEAK's Unity/BepInEx — typically .NET Standard 2.1).
- Windows or macOS with `dotnet` available.

## Known limitations

- Inventory serialization stores item IDs only, not every per-item custom data value.
- Player matching is by Photon actor number — if lobby composition changes between save and load, unmatched players are skipped.
- Reapplying the seed on load (`LevelGeneration` + `MapGenerator`) is timing-sensitive across scene loads.
- Does **not** persist world state — only run state.
- Remote inventory restore is opt-in (`ExperimentalRemoteInventoryRestore`) because some setups hang while applying remote inventory RPC updates.

## License

See `LICENSE`.

## For contributors / AI

`CLAUDE.md` at the repo root is the router. Source-level conventions live in `src/CONTEXT.md`.
