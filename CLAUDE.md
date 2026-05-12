# PeakRunSave — Router

BepInEx plugin (C# / .NET) for the Unity co-op climbing game **PEAK**. Snapshots in-progress runs (seed, segment, player positions, inventory) and restores them later. Host-only by default — uses PEAK's built-in RPC handlers so other players don't need the mod.

## Folder map

| Folder / file | What's there | Notes |
|---|---|---|
| `src/PeakRunSave/` | C# source — the actual plugin code | See `src/CONTEXT.md` |
| `PeakRunSave.sln` | Visual Studio / `dotnet` solution file | |
| `Directory.Build.props` / `.targets` | MSBuild config shared by all projects | |
| `Config.Build.user.props.template` | Copy to `Config.Build.user.props` and fill in `PeakGameRootDir` | The `.user.props` itself is gitignored |
| `.config/dotnet-tools.json` | Local dotnet tool manifest | |
| `artifacts/` | Build outputs (`bin/`, `obj/`) — **skip** | gitignored typically |
| `icon.png` | Plugin icon | |
| `CHANGELOG.md` | Version history | |
| `README.md` | User-facing usage (keybinds, config) | |
| `PeakRunSave.md` | Deeper project overview | |
| `LICENSE` | License text | |

## Conventions

- Language: C# targeting .NET (whichever PEAK's Unity/BepInEx uses).
- Build: `dotnet build` from repo root (after creating `Config.Build.user.props` from the template).
- Default hotkeys: `F5` save, `F9` load, `F10` toggle menu. All rebindable via `BepInEx/config/com.avious.peak.runsave.cfg`.
- Player identity uses Photon actor numbers — fragile across lobby composition changes.

## Task → room

| Task | Go to |
|---|---|
| Change plugin behavior / add features | `src/CONTEXT.md` |
| Build / deploy setup | root `README.md` + `Directory.Build.props` |
| Look at compiled output | `artifacts/` (skip reading) |
| User-facing config docs | `README.md` |
