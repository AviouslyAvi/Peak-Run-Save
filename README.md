# PeakRunSave

Save and restore your current PEAK run state:
- seed
- segment (Beach/Tropics/Alpine/Caldera/TheKiln/Peak)
- all matched player positions
- inventory item IDs
- optional health-like state (`passOutValue`) and stamina

## Controls

- `F5`: Save snapshot
- `F9`: Load snapshot
- `F10`: Toggle save menu

## Config

Generated in `BepInEx/config/com.avious.peak.runsave.cfg`.

- `SaveKey` (`F5` default)
- `LoadKey` (`F9` default)
- `MenuKey` (`F10` default)
- `MenuSlots` (`5` default)
- `HostOnlyControls` (`true` default, recommended)
- `RestoreHealth` (`true` default)
- `RestoreAllPlayerPositions` (`true` default)
- `RestoreAllPlayerInventories` (`true` default)
- `ExperimentalRemoteInventoryRestore` (`false` default, safer)
- `GroundRaycastHeight` (`6` default)
- `GroundRaycastDistance` (`64` default)
- `SaveFile` (`run_snapshot.json` default, stored under `BepInEx/config/PeakRunSave/`)

## Save menu

- Open with `F10`.
- Includes buttons for:
  - saving/loading the default save file
  - saving/loading numbered slot files (`run_snapshot_slot_1.json`, etc.)
- Shows each slot's last modified timestamp or `empty`.

## Build and deploy

1. Copy `Config.Build.user.props.template` to `Config.Build.user.props`.
2. Set `PeakGameRootDir` and optionally `PeakPluginsDir`.
3. Build:

```powershell
dotnet build
```

## Known limitations

- Inventory serialization currently stores item IDs only, not every per-item custom data value.
- Player matching is done by Photon actor number. If your lobby composition is different from the saved run, unmatched players are skipped.
- Host-only mode uses PEAK's built-in RPC handlers, so non-host clients do not need this mod installed.
- Remote inventory restore is now opt-in (`ExperimentalRemoteInventoryRestore`) because some setups can hang while applying remote inventory RPC updates.
- Load attempts to reapply seed when `LevelGeneration` and `MapGenerator` are present, but this can still be timing-sensitive across scenes.
- This does not persist world state (position, unlocked progress, map modifications).
