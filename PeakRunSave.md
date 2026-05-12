# PeakRunSave

**Location:** `Projects/PeakRunSave/`
**Type:** BepInEx plugin (mod) for the game PEAK
**Language:** C# (.NET), built with `dotnet build`

---

## What it is

PeakRunSave is a **mod for the game PEAK** that lets you save and restore the state of an in-progress run — something the base game doesn't let you do.

A snapshot captures:
- The run's **seed**
- Current **segment** (Beach / Tropics / Alpine / Caldera / TheKiln / Peak)
- All matched **player positions**
- **Inventory item IDs** for each player
- Optional **health-like state** (`passOutValue`) and **stamina**

## Background for beginners

- **BepInEx** is a modding framework for Unity games. It loads your compiled DLL at game start and patches game code using Harmony.
- **PEAK** is a co-op climbing game. Because it's multiplayer via Photon, players have a *Photon actor number* used to identify them across sessions.
- This mod is **host-only by default** — only the lobby host needs to press save/load, and it uses PEAK's built-in RPC handlers so other clients don't need the mod installed.

## Default controls

| Key | Action |
|---|---|
| `F5` | Save snapshot |
| `F9` | Load snapshot |
| `F10` | Toggle save menu |

All keys are rebindable via the config file.

## Save menu (F10)

- Save/load the **default** save file
- Save/load numbered **slots** (e.g. `run_snapshot_slot_1.json`)
- Each slot shows its last modified timestamp, or `empty`

## Config file

Generated at `BepInEx/config/com.avious.peak.runsave.cfg`. Key options:

- `SaveKey` / `LoadKey` / `MenuKey` — rebind hotkeys
- `MenuSlots` — number of save slots (default 5)
- `HostOnlyControls` — recommended `true`
- `RestoreHealth`, `RestoreAllPlayerPositions`, `RestoreAllPlayerInventories` — toggle what gets restored
- `ExperimentalRemoteInventoryRestore` — opt-in, can hang on some setups
- `GroundRaycastHeight`, `GroundRaycastDistance` — tuning for respawn ground detection
- `SaveFile` — default filename (stored under `BepInEx/config/PeakRunSave/`)

## Build

1. Copy `Config.Build.user.props.template` → `Config.Build.user.props`.
2. Set `PeakGameRootDir` (and optionally `PeakPluginsDir`) to your PEAK install path so the build can reference PEAK's assemblies.
3. Run:

```powershell
dotnet build
```

## Known limitations

- Inventory stores item IDs only, not every per-item custom data field.
- Player matching uses Photon actor number. If the lobby composition differs from the saved run, unmatched players are skipped.
- Seed re-apply on load depends on `LevelGeneration` and `MapGenerator` being present — can be timing-sensitive across scenes.
- Does **not** persist world state (environmental changes, map modifications, unlocked progress).
- Remote inventory restore is opt-in because some setups hang when applying remote inventory RPCs.
