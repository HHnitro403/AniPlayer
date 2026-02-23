# AniPlayer

A cross-platform desktop anime media player built with C# and Avalonia UI. AniPlayer scans your local anime library folders, organizes series and episodes, fetches metadata from AniList, and plays video files with mpv.

## Features

- **Library Management** — Add folders containing anime. AniPlayer auto-detects series, episodes, specials, OVAs, and more from your folder structure.
- **Smart Episode Parsing** — Uses AnitomySharp to parse anime filenames (release groups, episode numbers, titles, resolution, etc.) with regex fallback.
- **AniList Metadata** — Automatically fetches cover images, synopses, genres, scores, and episode counts from the AniList GraphQL API.
- **mpv Video Playback** — Hardware-accelerated playback via libmpv with audio/subtitle track selection, volume control, and keyboard shortcuts.
- **Watch Progress Tracking** — Automatically saves playback position every 5 seconds and resumes where you left off. Marks episodes as completed at 90%.
- **File Watching** — Monitors library folders for new/renamed/deleted files and re-scans automatically with a debounce pattern.
- **Cross-Platform** — Targets .NET 9 and Avalonia UI 11, runs on Windows, Linux, and macOS.

## Architecture

AniPlayer uses a **Service-Based Passive View** architecture with two strict layers:

| Layer | Project | Role |
|-------|---------|------|
| **Core (Brain)** | `Aniplayer.Core` | Business logic, DB queries, file I/O, API calls. Zero UI dependencies. |
| **UI (Face)** | `AniPlayer.UI` | Avalonia rendering only. Code-behind pattern, no MVVM. |

### Key Design Decisions

- **No MVVM** — All UI interaction is in `.axaml.cs` code-behind files. No ViewModels, no `INotifyPropertyChanged`.
- **SQLite + Dapper** — Hand-written SQL in `Queries.cs`. No Entity Framework.
- **DI via `Microsoft.Extensions.DependencyInjection`** — All services are singletons resolved from `App.Services`.
- **Pages receive data via typed method calls**, not constructor parameters or bindings.

## Tech Stack

| Concern | Library |
|---------|---------|
| UI Framework | Avalonia UI 11 |
| Video Playback | libmpv |
| Database | SQLite via Microsoft.Data.Sqlite + Dapper |
| Metadata API | AniList GraphQL API (free, no key required) |
| Filename Parsing | AnitomySharp.NET6 |
| DI Container | Microsoft.Extensions.DependencyInjection |
| Target Framework | .NET 9.0 |

## Project Structure

```
AniPlayer.sln
├── Aniplayer.Core/              # Zero UI dependencies
│   ├── Models/                  # Plain C# data classes
│   ├── Interfaces/              # Service contracts (one per file)
│   ├── Services/                # Service implementations
│   ├── Database/
│   │   ├── DatabaseInitializer.cs
│   │   └── Queries.cs           # All SQL as const strings
│   ├── Helpers/
│   │   ├── EpisodeParser.cs     # AnitomySharp + regex fallback
│   │   ├── FileHelper.cs        # Extension checks, file readiness
│   │   └── DebounceHelper.cs
│   └── Constants/
│       ├── AppConstants.cs      # Supported extensions, timeouts
│       └── EpisodeTypes.cs      # EPISODE | SPECIAL | OVA | OAD | NCOP | NCED
│
└── AniPlayer.UI/                # Avalonia rendering only
    ├── MainWindow.axaml/.cs     # Shell: sidebar + content area, event wiring
    ├── Views/
    │   ├── Pages/
    │   │   ├── HomePage          # Continue Watching, Recently Added, Quick Actions
    │   │   ├── LibraryPage       # Series grid with search/filter
    │   │   ├── ShowInfoPage      # Series detail with episode list
    │   │   ├── PlayerPage        # mpv video player
    │   │   └── OptionsPage       # Library management, settings
    │   └── Controls/
    │       ├── Sidebar           # Navigation sidebar
    │       ├── SeriesCard        # Series thumbnail card
    │       ├── EpisodeRow        # Episode list row
    │       └── PlayerControls    # Playback controls overlay
    ├── Logger.cs                # Debug logger with region-based filtering
    └── Styles/                  # Theme, control overrides, animations
```

## Database Schema

SQLite with WAL mode. Tables: `Libraries`, `Series`, `Episodes`, `WatchProgress`, `TrackPreferences`, `Settings`.

- **Libraries** — Root folder paths added by the user
- **Series** — One per anime folder, linked to a library
- **Episodes** — Video files with parsed episode numbers and types
- **WatchProgress** — Per-episode playback position and completion state
- **TrackPreferences** — Audio/subtitle language preferences per episode or series

## Debug Logging

AniPlayer writes debug logs to `%APPDATA%/AniPlayer/debug.log` (Windows) or `~/.config/AniPlayer/debug.log` (Linux).

Logging is controlled by **regions** to avoid flooding the log with verbose output:

| Region | What it logs |
|--------|-------------|
| `General` | Always on — startup, navigation, errors, add/remove library |
| `Scanner` | ScannerService scan progress (per-folder, per-file) |
| `Parser` | EpisodeParser AnitomySharp element-level output |
| `UI` | Page data loading, filter results, card counts |
| `DB` | Per-row database dumps in RefreshPages |

Enable verbose regions in code: `Logger.EnabledRegions = LogRegion.All;`

## Keyboard Shortcuts

AniPlayer supports the following keyboard shortcuts when the player page is active:

| Key | Action |
|-----|--------|
| **Space** | Play / Pause |
| **Left Arrow** | Seek backward 5 seconds |
| **Right Arrow** | Seek forward 5 seconds |
| **Up Arrow** | Increase volume (+10) |
| **Down Arrow** | Decrease volume (-5) |
| **A** | Cycle audio track |
| **S** | Cycle subtitle track |
| **M** | Mute / Unmute |
| **N** | Next episode in playlist |
| **F** | Toggle fullscreen |
| **F11** | Toggle fullscreen (alternative) |
| **Escape** | Exit fullscreen |

## Building

```bash
dotnet restore
dotnet build
dotnet run --project AniPlayer.UI
```

Requires .NET 9 SDK. On Windows, `libmpv-2.dll` is bundled in `AniPlayer.UI/lib/win-x64/`. On Linux, install mpv via your package manager.

## License

Apache License 2.0 — see [LICENSE.txt](LICENSE.txt).
