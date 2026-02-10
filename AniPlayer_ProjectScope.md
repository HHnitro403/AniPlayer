# AniPlayer — Project Scope & Architecture Reference
> **Primary audience:** Claude Code and any developer implementing this project.
> **Stack:** C# · Avalonia UI 11 · LibMpv (homov/LibMpv) · SQLite + Dapper · AniList GraphQL API · Microsoft.Extensions.DependencyInjection

---

## ⚠️ HOW TO READ THIS DOCUMENT

Rules are marked with one of three tags:

- 🔴 **MUST** — Non-negotiable. Never deviate. If you think you need to, re-read the rule.
- 🟡 **SHOULD** — Strong default. Deviation requires explicit justification in a code comment.
- 🟢 **MAY** — Permitted flexibility. Use judgment.

---

## 1. Core Philosophy

### Architecture: Service-Based Passive View

The application has two strict layers with a hard boundary between them.

**AniPlayer.Core — The Brain**
- Contains all business logic, database queries, file I/O, API calls, and hardware interaction.
- 🔴 **MUST** have zero dependency on Avalonia or any UI namespace.
- 🔴 **MUST** expose all functionality through interfaces (`ILibraryService`, `IPlayerService`, etc.).
- 🔴 **MUST** be platform-agnostic — no Windows-only APIs except where explicitly noted in file header comments.

**AniPlayer.UI — The Face**
- Uses Avalonia purely as a rendering engine.
- 🔴 **MUST** contain zero business logic. No calculations, no data transformations, no decisions.
- 🔴 **MUST** use code-behind (`.axaml.cs`) for all UI interaction — No MVVM, no ViewModels, no `INotifyPropertyChanged`.
- 🔴 **MUST** drive UI updates via service events and direct control assignment, not data bindings to complex objects.
- 🟡 **SHOULD** resolve services directly from `App.Services` in the code-behind constructor.

### The Five Commandments

These apply everywhere, always, with no exceptions:

1. 🔴 **No MVVM** — No ViewModels, no `INotifyPropertyChanged`, no `ReactiveObject`, no `ObservableCollection` as a binding target.
2. 🔴 **No Entity Framework** — SQLite access is via Dapper only. All SQL is written by hand as `const string` values in `Queries.cs`.
3. 🔴 **No `.Result` or `.Wait()`** — Every async operation is awaited. No synchronous blocking on Tasks anywhere.
4. 🔴 **No logic in UI files** — If a method in a `.axaml.cs` file does anything other than update a control or call a service, it is in the wrong place.
5. 🔴 **No parameter-passing navigation** — Services are resolved from DI. Pages receive context (e.g., a series ID) via a typed method call after construction, not constructor parameters.

---

## 2. Technology Stack

| Concern              | Library / Tool                                        | Locked? |
|----------------------|-------------------------------------------------------|---------|
| UI Framework         | Avalonia UI 11 (latest stable)                        | 🔴 Yes  |
| Video Playback       | LibMpv — homov/LibMpv on GitHub                       | 🔴 Yes  |
| Database             | SQLite via `Microsoft.Data.Sqlite` + `Dapper`         | 🔴 Yes  |
| Metadata API         | AniList GraphQL API (free, no API key required)       | 🔴 Yes  |
| DI Container         | `Microsoft.Extensions.DependencyInjection`            | 🔴 Yes  |
| Logging              | `Microsoft.Extensions.Logging` + file sink            | 🔴 Yes  |
| HTTP Client          | `System.Net.Http.HttpClient` (singleton via DI)       | 🔴 Yes  |
| File Watching        | `System.IO.FileSystemWatcher` + debounce pattern      | 🔴 Yes  |
| Image/Cover Storage  | Local filesystem under app data path                  | 🔴 Yes  |
| mpv Cache Strategy   | mpv built-in cache via property configuration         | 🔴 Yes  |

🟢 **MAY** use additional NuGet packages for: JSON serialization (`System.Text.Json`), file logging sink, image format handling. **MUST NOT** introduce new architectural dependencies (no MediatR, no Prism, no ReactiveUI).

---

## 3. Solution Structure

🔴 **MUST** follow this structure exactly. Do not merge projects, rename folders, or add new top-level folders without a documented reason.

```
AniPlayer.sln
├── AniPlayer.Core/                  # Zero UI dependencies
│   ├── Models/                      # Plain C# data classes — no attributes, no ORM
│   │   ├── Library.cs
│   │   ├── Series.cs
│   │   ├── Episode.cs
│   │   ├── WatchProgress.cs
│   │   ├── TrackPreferences.cs
│   │   └── AniListMetadata.cs
│   ├── Interfaces/                  # One interface per file
│   │   ├── ILibraryService.cs
│   │   ├── IMetadataService.cs
│   │   ├── IWatchProgressService.cs
│   │   ├── IPlayerService.cs
│   │   ├── IScannerService.cs
│   │   ├── IFolderWatcherService.cs
│   │   ├── IDatabaseService.cs
│   │   └── ISettingsService.cs
│   ├── Services/                    # One class per file, no exceptions
│   │   ├── LibraryService.cs
│   │   ├── MetadataService.cs
│   │   ├── WatchProgressService.cs
│   │   ├── PlayerService.cs
│   │   ├── ScannerService.cs
│   │   ├── FolderWatcherService.cs
│   │   ├── DatabaseService.cs
│   │   └── SettingsService.cs
│   ├── Database/
│   │   ├── DatabaseInitializer.cs   # Schema creation + PRAGMA setup
│   │   └── Queries.cs               # ALL SQL as public const string — no inline SQL elsewhere
│   ├── Helpers/
│   │   ├── FileHelper.cs            # Path helpers, extension checks, IsFileReady()
│   │   ├── EpisodeParser.cs         # Filename → episode number + type detection
│   │   └── DebounceHelper.cs        # Reusable per-key debounce logic
│   └── Constants/
│       ├── AppConstants.cs          # Timeouts, limits, supported extensions, retry counts
│       └── EpisodeTypes.cs          # EPISODE | SPECIAL | OVA | OAD | NCOP | NCED
│
└── AniPlayer.UI/                    # Avalonia — rendering only
    ├── App.axaml
    ├── App.axaml.cs                 # DI wiring, global exception handlers, startup
    ├── Views/
    │   ├── MainWindow.axaml         # Shell: sidebar + content area
    │   ├── MainWindow.axaml.cs
    │   ├── Pages/
    │   │   ├── HomePage.axaml
    │   │   ├── HomePage.axaml.cs
    │   │   ├── LibraryPage.axaml
    │   │   ├── LibraryPage.axaml.cs
    │   │   ├── ShowInfoPage.axaml
    │   │   ├── ShowInfoPage.axaml.cs
    │   │   ├── PlayerPage.axaml
    │   │   ├── PlayerPage.axaml.cs
    │   │   ├── OptionsPage.axaml
    │   │   └── OptionsPage.axaml.cs
    │   └── Controls/
    │       ├── Sidebar.axaml / .cs
    │       ├── SeriesCard.axaml / .cs
    │       ├── EpisodeRow.axaml / .cs
    │       └── PlayerControls.axaml / .cs
    ├── Styles/
    │   ├── Theme.axaml              # Color tokens, font definitions, spacing scale
    │   ├── Controls.axaml           # Control style overrides
    │   └── Animations.axaml
    └── Assets/
        ├── placeholder_cover.png
        └── icons/
```

🟢 **MAY** add files within existing folders as implementation grows. 🔴 **MUST NOT** add a new service class without a corresponding interface in `Interfaces/`.

---

## 4. Database

### 4.1 Initialization Pragmas

🔴 **MUST** execute all of the following in `DatabaseInitializer.cs` before any other DB operation, in this order:

```sql
PRAGMA journal_mode = WAL;        -- Non-blocking concurrent reads + writes
PRAGMA foreign_keys = ON;         -- Enforce all ON DELETE CASCADE rules
PRAGMA synchronous = NORMAL;      -- Safe with WAL, better performance than FULL
```

**Why WAL is non-negotiable:** The scanner (writer) and progress saver (writer, every 5s) run simultaneously during playback. Without WAL, `database is locked` errors are guaranteed under normal usage. This is not optional and must not be removed.

### 4.2 Schema

🔴 **MUST** match this schema exactly. All tables created by `DatabaseInitializer.cs`. **MUST NOT** create tables inline in service code.

```sql
CREATE TABLE IF NOT EXISTS Libraries (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    path        TEXT NOT NULL UNIQUE,
    label       TEXT,
    created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS Series (
    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
    library_id           INTEGER NOT NULL REFERENCES Libraries(id) ON DELETE CASCADE,
    folder_name          TEXT NOT NULL,
    path                 TEXT NOT NULL UNIQUE,
    anilist_id           INTEGER,
    title_romaji         TEXT,
    title_english        TEXT,
    title_native         TEXT,
    cover_image_path     TEXT,
    synopsis             TEXT,
    genres               TEXT,          -- JSON array stored as plain text
    average_score        REAL,
    total_episodes       INTEGER,
    status               TEXT,          -- FINISHED | RELEASING | NOT_YET_RELEASED | CANCELLED
    metadata_fetched_at  TEXT,
    created_at           TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS Episodes (
    id               INTEGER PRIMARY KEY AUTOINCREMENT,
    series_id        INTEGER NOT NULL REFERENCES Series(id) ON DELETE CASCADE,
    file_path        TEXT NOT NULL UNIQUE,
    title            TEXT,
    episode_number   REAL,              -- REAL supports recap episodes (e.g. 12.5)
    episode_type     TEXT NOT NULL DEFAULT 'EPISODE',
                                        -- EPISODE | SPECIAL | OVA | OAD | NCOP | NCED
    duration_seconds INTEGER,
    thumbnail_path   TEXT,
    anilist_ep_id    INTEGER,
    created_at       TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS WatchProgress (
    id                INTEGER PRIMARY KEY AUTOINCREMENT,
    episode_id        INTEGER NOT NULL UNIQUE REFERENCES Episodes(id) ON DELETE CASCADE,
    position_seconds  INTEGER NOT NULL DEFAULT 0,
    duration_seconds  INTEGER,
    is_completed      INTEGER NOT NULL DEFAULT 0,   -- SQLite boolean: 0 = false, 1 = true
    last_watched_at   TEXT
);

CREATE TABLE IF NOT EXISTS TrackPreferences (
    id                          INTEGER PRIMARY KEY AUTOINCREMENT,
    episode_id                  INTEGER REFERENCES Episodes(id) ON DELETE CASCADE,
    series_id                   INTEGER REFERENCES Series(id) ON DELETE CASCADE,
    preferred_audio_language    TEXT,   -- BCP-47: 'jpn', 'eng', etc.
    preferred_subtitle_language TEXT,
    preferred_subtitle_name     TEXT,   -- Exact mpv track title when language is ambiguous
    CHECK (
        (episode_id IS NOT NULL AND series_id IS NULL) OR
        (episode_id IS NULL     AND series_id IS NOT NULL)
    )
);

CREATE TABLE IF NOT EXISTS Settings (
    key    TEXT PRIMARY KEY,
    value  TEXT
);
```

**Required indexes:**
```sql
CREATE INDEX IF NOT EXISTS idx_episodes_series_id      ON Episodes(series_id);
CREATE INDEX IF NOT EXISTS idx_watch_progress_ep_id    ON WatchProgress(episode_id);
CREATE INDEX IF NOT EXISTS idx_series_library_id       ON Series(library_id);
CREATE INDEX IF NOT EXISTS idx_track_prefs_episode_id  ON TrackPreferences(episode_id);
CREATE INDEX IF NOT EXISTS idx_track_prefs_series_id   ON TrackPreferences(series_id);
```

### 4.3 SQL Rules

🔴 **MUST** keep every SQL string in `Queries.cs` as a `public const string`. No inline SQL anywhere else.

```csharp
// Queries.cs — correct pattern
public static class Queries
{
    public const string GetSeriesById =
        "SELECT * FROM Series WHERE id = @id";

    public const string UpsertWatchProgress = @"
        INSERT INTO WatchProgress (episode_id, position_seconds, duration_seconds, last_watched_at)
        VALUES (@episodeId, @positionSeconds, @durationSeconds, datetime('now'))
        ON CONFLICT(episode_id) DO UPDATE SET
            position_seconds = excluded.position_seconds,
            duration_seconds = excluded.duration_seconds,
            last_watched_at  = excluded.last_watched_at";
}
```

🔴 **MUST** wrap multi-step write operations (scan inserts, metadata save) in a SQLite transaction. Commit only if all steps succeed. Roll back on any exception.

---

## 5. The "Silent Watcher" Scanner

### 5.1 Folder Structure Convention

```
<Library Root>/
└── <Series Folder>/              → 1 row in Series table
    ├── Episode01.mkv             → type: EPISODE
    ├── Episode02.mkv
    ├── Specials/                 → type: SPECIAL
    │   └── Special01.mkv
    ├── OVA/                      → type: OVA
    ├── OAD/                      → type: OAD
    ├── NCOP/                     → type: NCOP (clean openings)
    └── NCED/                     → type: NCED (clean endings)
```

Episode type is determined by **parent folder name only** (case-insensitive string match). Files directly in the series folder are always `EPISODE`.

### 5.2 The IO Quieting State Machine

🔴 **MUST** implement all four phases in order. Skipping any phase causes either file-lock crashes or wasted/broken scans.

**Phase 1 — Passive Watch (zero CPU)**
- `FileSystemWatcher` active on each library root with `IncludeSubdirectories = true`
- Listen to: `Created`, `Renamed`, `Deleted`, `Changed`
- On event: record the affected folder path, fire Phase 2. Do nothing else.

**Phase 2 — Accumulation / Debounce**
- Use `ConcurrentDictionary<string, CancellationTokenSource>` keyed by **normalized folder path**
- On every event for a folder: cancel existing `CancellationTokenSource`, create a new one, start `Task.Delay(3000, newToken)`
- A single 50GB file copy generates thousands of `Changed` events — all collapse into one delayed action
- 🔴 **MUST** debounce per folder. Copying to FolderA must not reset FolderB's timer.

**Phase 3 — File Readiness Check**

🔴 **MUST NOT** scan immediately when the debounce timer expires. First verify the file is no longer locked.

```csharp
// FileHelper.cs
public static async Task<bool> WaitUntilFileReadyAsync(
    string path,
    CancellationToken ct,
    int maxRetries = 30,       // 30 × 5s = 2.5 minute hard cap
    int retryDelayMs = 5000)
{
    for (int i = 0; i < maxRetries; i++)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);   // Exclusive open — throws IOException if file is in use
            return true;
        }
        catch (IOException)
        {
            // File still locked (copy still in progress)
            await Task.Delay(retryDelayMs, ct).ConfigureAwait(false);
        }
    }
    return false; // Hit cap — log Warning and skip, do not throw
}
```

- 🔴 **MUST** cap retries at 30 (2.5 minutes maximum). A file still locked after that is logged as `Warning` and skipped.
- 🟢 **MAY** expose retry count and delay as constants in `AppConstants.cs`.

**Phase 4 — Transactional Scan**
- Open a SQLite **transaction** before any inserts
- Parse all video files in the target subfolder using `EpisodeParser`
- `INSERT OR REPLACE` series and episode rows
- 🔴 **MUST** commit only if all operations succeed. Roll back on any exception.
- 🔴 **MUST** log each file at `Debug` level, each error at `Warning` level
- 🔴 **MUST NOT** abort the entire scan when a single file fails — log it, skip it, continue

### 5.3 EpisodeParser Rules (`EpisodeParser.cs`)

Episode number extraction — regex priority order:

1. ` - ##` pattern: `Show Name - 01.mkv`
2. `E##` or `EP##` pattern: `ShowS01E05.mkv`
3. Trailing number before extension: `Show 12.mkv`
4. Fall back to `null` — never guess

Title cleaning — strip in this order:
1. Release group: `[SubGroup]`
2. Quality: `[1080p]`, `[720p]`, `[4K]`
3. Codec: `[HEVC]`, `[x265]`, `[AVC]`, `[AAC]`
4. Checksum: `[ABCD1234]`
5. Normalize remaining whitespace

Supported extensions (in `AppConstants.cs`): `.mkv`, `.mp4`, `.avi`, `.m4v`, `.mov`, `.wmv`

---

## 6. The "Smart Stream" Player

### 6.1 mpv Cache Configuration — Primary Strategy

🔴 **MUST** configure mpv's built-in cache properties before considering any custom buffering. mpv handles demuxing and read-ahead natively. A proxy stream on top of mpv adds complexity without benefit and risks timing conflicts.

Apply these in `PlayerService` on mpv initialization:

```csharp
// PlayerService.cs
mpv.SetProperty("cache",               "yes");
mpv.SetProperty("demuxer-max-bytes",   "150MiB");
mpv.SetProperty("demuxer-readahead-secs", "30");
mpv.SetProperty("cache-secs",          "120");
mpv.SetProperty("hwdec",               "auto");    // GPU hardware decode
mpv.SetProperty("vo",                  "gpu");     // GPU video output
```

🟢 **MAY** expose these as advanced user-configurable values in `OptionsPage` in a future iteration.

### 6.2 OS File Lock — The "Back Off" Guard

🔴 **MUST** open a dedicated `FileStream` with `FileShare.None` immediately when playback begins. This stream is held open for the entire duration of playback and is separate from mpv's own file handle.

**Purpose:** Tells Windows, Explorer, and all other processes that the file cannot be renamed, deleted, or moved while playing.

```csharp
// PlayerService.cs
private FileStream? _lockStream;

public async Task LoadAsync(string filePath, int resumePositionSeconds = 0)
{
    // Acquire OS-level exclusive read lock
    _lockStream = new FileStream(
        filePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.None,
        bufferSize: 1,               // Minimal — mpv does its own buffering
        FileOptions.Asynchronous);

    // Pass path to mpv — mpv opens its own independent handle
    await _mpvContext.CommandAsync("loadfile", filePath);

    if (resumePositionSeconds > 0)
        await SeekAsync(resumePositionSeconds);
}

public async Task StopAsync()
{
    await _mpvContext.CommandAsync("stop");
    _lockStream?.Dispose();
    _lockStream = null;
}
```

🔴 **MUST** dispose `_lockStream` in `StopAsync()`, on navigation away from the player, and in the service `Dispose()`.
🟡 **SHOULD** catch `IOException` on lock acquisition and surface a toast error rather than crashing. The file may already be locked by another process.

### 6.3 SmartBufferedStream — Conditional Only

🟢 **MAY** implement `SmartBufferedStream` only after confirming that mpv cache tuning alone is insufficient for observable stuttering on slow HDDs (5400RPM spinning disk).

If implemented, these rules are **non-negotiable**:
- 🔴 **MUST NOT** replace mpv's file handle or intercept mpv's read calls
- 🔴 **MUST** live in `AniPlayer.Core/Helpers/SmartBufferedStream.cs` — not inside `PlayerService`
- Buffer size formula: `Math.Min(fileSize / 8, 1_073_741_824L)` — 1/8th of file, hard capped at 1 GB
- Sequential read-ahead: 64 KB chunks ahead of playhead
- Seek inside buffer: instant RAM seek, no disk I/O
- Seek outside buffer: flush buffer, reposition disk head, refill

---

## 7. Service Interface Contracts

### ILibraryService
```csharp
Task<IEnumerable<Library>> GetLibrariesAsync();
Task                        AddLibraryAsync(string path, string? label = null);
Task                        RemoveLibraryAsync(int libraryId);
Task<IEnumerable<Series>>  GetAllSeriesAsync(SeriesFilter filter, SeriesSort sort);
Task<Series?>              GetSeriesWithEpisodesAsync(int seriesId);
Task<IEnumerable<Series>>  GetRecentlyAddedSeriesAsync(int days = 14);
```

### IMetadataService
```csharp
Task<AniListMetadata?> SearchAniListAsync(string title, CancellationToken ct = default);
Task                    FetchAndSaveMetadataAsync(int seriesId, CancellationToken ct = default);
Task<string?>          DownloadCoverImageAsync(string imageUrl, int seriesId, CancellationToken ct = default);
```

### IWatchProgressService
```csharp
Task<WatchProgress?>                                          GetProgressAsync(int episodeId);
Task<IEnumerable<(Episode episode, WatchProgress progress)>> GetRecentlyWatchedAsync(int limit = 10);
Task<IEnumerable<WatchProgress>>                             GetProgressForSeriesAsync(int seriesId);
Task                                                          SaveProgressAsync(int episodeId, int positionSeconds, int durationSeconds);
Task                                                          MarkCompletedAsync(int episodeId);
```

### IPlayerService
```csharp
Task                         LoadAsync(string filePath, int resumePositionSeconds = 0);
Task                         PlayAsync();
Task                         PauseAsync();
Task                         StopAsync();
Task                         SeekAsync(int positionSeconds);
Task                         SetAudioTrackAsync(int trackId);
Task                         SetSubtitleTrackAsync(int trackId);
Task                         SetSpeedAsync(double speed);
Task<IEnumerable<MpvTrack>> GetAudioTracksAsync();
Task<IEnumerable<MpvTrack>> GetSubtitleTracksAsync();

// Events fire on ThreadPool — UI MUST marshal to UI thread before updating controls
event EventHandler<int>          PositionChanged;   // ~every 1 second during playback
event EventHandler<PlayerState>  StateChanged;
event EventHandler<string>       ErrorOccurred;
```

### IScannerService
```csharp
Task ScanFolderAsync(string path, int libraryId, CancellationToken ct = default);
Task ScanAllLibrariesAsync(CancellationToken ct = default);
```

### IFolderWatcherService
```csharp
void StartWatching(string libraryPath, int libraryId);
void StopWatching(string libraryPath);
void StopAll();
Task RestoreWatchersAsync(); // Called on startup — re-attaches watchers for all saved libraries
```

---

## 8. UI Patterns

### 8.1 Service Injection in Code-Behind

🔴 **MUST** use this pattern in every page and control that needs a service. No exceptions.

```csharp
// LibraryPage.axaml.cs
public partial class LibraryPage : UserControl
{
    private readonly ILibraryService _libraryService;
    private readonly ILogger<LibraryPage> _logger;

    public LibraryPage()
    {
        InitializeComponent();
        _libraryService = App.Services.GetRequiredService<ILibraryService>();
        _logger         = App.Services.GetRequiredService<ILogger<LibraryPage>>();
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var series = await _libraryService.GetAllSeriesAsync(
                SeriesFilter.All, SeriesSort.TitleAsc);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SeriesGrid.ItemsSource = series;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load library page");
            // Show toast — never swallow silently
        }
    }
}
```

### 8.2 UI Thread Rule

🔴 **MUST** marshal every control update that originates from a background thread or service event through `Dispatcher.UIThread.InvokeAsync()`.

```csharp
// PlayerPage.axaml.cs
_playerService.PositionChanged += async (_, seconds) =>
{
    await Dispatcher.UIThread.InvokeAsync(() =>
    {
        SeekBar.Value     = seconds;
        PositionLabel.Text = TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss");
    });
};
```

🔴 **MUST** unsubscribe from all service events when a page is detached or disposed. Failure causes memory leaks and events firing on dead controls.

🔴 **MUST NOT** use `ConfigureAwait(false)` in `AniPlayer.UI` code-behind. The UI must resume on the UI thread after awaits that touch controls.

### 8.3 Navigation Pattern

Navigation is managed by `MainWindow` replacing the `ContentControl.Content`:

```csharp
// MainWindow.axaml.cs
public void NavigateTo<TPage>() where TPage : Control, new()
{
    MainContent.Content = new TPage();
}

public void NavigateToShowInfo(int seriesId)
{
    var page = new ShowInfoPage();
    page.LoadSeries(seriesId); // Page fetches its own data via DI
    MainContent.Content = page;
}
```

🔴 **MUST NOT** pass service instances through navigation.
🔴 **MUST NOT** pass model objects through constructors. Pass only primitive IDs. Pages load their own data.

### 8.4 Sidebar Behaviour

- Default state: expanded (icon + label)
- Collapsed state: icon-only
- 🔴 **MUST NOT** fully hide sidebar during normal page navigation
- 🔴 **MUST** fully hide sidebar when player enters fullscreen
- 🔴 **MUST** restore sidebar immediately when player exits fullscreen

---

## 9. Threading & Safety Rules

| Operation                       | Thread                                            | Rule    |
|---------------------------------|---------------------------------------------------|---------|
| All UI control updates          | UI thread via `Dispatcher.UIThread.InvokeAsync()` | 🔴 MUST |
| All DB reads/writes             | ThreadPool — `ConfigureAwait(false)` in Core      | 🔴 MUST |
| File system scan                | ThreadPool — with `CancellationToken`             | 🔴 MUST |
| FileSystemWatcher callbacks     | ThreadPool (already off UI thread)                | —       |
| AniList API calls               | ThreadPool — `CancellationToken` + timeout        | 🔴 MUST |
| Image download                  | ThreadPool                                        | 🔴 MUST |
| mpv playback engine             | mpv's own internal threads — never block them     | 🔴 MUST |
| Progress save (every 5s)        | ThreadPool — debounced, not on every tick         | 🔴 MUST |
| OS file lock (`FileShare.None`) | Held for full playback duration                   | 🔴 MUST |

**Non-negotiable global rules:**
- 🔴 Never call `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` anywhere in the codebase
- 🔴 Every cancellable operation must accept a `CancellationToken` parameter
- 🔴 `ConfigureAwait(false)` on every `await` inside `AniPlayer.Core`
- 🔴 `ConfigureAwait(false)` is **intentionally absent** in `AniPlayer.UI` code-behind — UI must return to the UI thread

---

## 10. Metadata Flow

🔴 **MUST** follow this exact flow. No shortcuts, no variations.

1. Scanner detects series folder → insert row: `anilist_id = NULL`, `metadata_fetched_at = NULL`
2. User opens `ShowInfoPage`:
   - If `metadata_fetched_at IS NULL` → auto-trigger `FetchAndSaveMetadataAsync(seriesId)`
   - If `metadata_fetched_at IS NOT NULL` → load from DB only, zero API calls
3. `SearchAniListAsync(folderName)`:
   - POST to `https://graphql.anilist.co`
   - 🔴 Headers: `Content-Type: application/json`, `Accept: application/json`
   - 🔴 Handle HTTP 429: read `Retry-After` header, wait, then retry once
   - Take first result. If empty: set `metadata_fetched_at = datetime('now')` anyway to prevent repeated failed requests
4. `DownloadCoverImageAsync()`:
   - Target: `{AppDataPath}/covers/{seriesId}.jpg`
   - 🔴 Check file exists before downloading — do not re-download
5. Save all fields to `Series` in a single `UPDATE`, set `metadata_fetched_at = datetime('now')`
6. UI re-queries DB after save — no in-memory state shared between service and UI

🔴 **MUST NOT** call AniList API on every page open if metadata already exists. The database is the cache.
🟡 **SHOULD** strip season suffixes from folder name before searching (`Season 2`, `S2`, `Part 2`, `Cour 2`) to improve AniList match accuracy.

### AniList GraphQL Query Template

```graphql
query ($search: String) {
  Media(search: $search, type: ANIME) {
    id
    title { romaji english native }
    coverImage { large }
    description(asHtml: false)
    genres
    averageScore
    episodes
    status
    startDate { year }
  }
}
```

---

## 11. Track Preference Resolution

🔴 **MUST** resolve in this exact priority order every time an episode is opened:

1. `TrackPreferences` where `episode_id = current episode` → highest priority (user explicitly set this episode)
2. `TrackPreferences` where `series_id = current series AND episode_id IS NULL` → series-wide default
3. `Settings` keys `default_audio_lang` and `default_sub_lang` → app-wide default
4. mpv's own auto-detection → final fallback

**Write rules:**
- User changes track in player → 🔴 **MUST** save as episode-level preference immediately (upsert)
- User sets preference in ShowInfoPage → save as series-level preference (upsert)
- 🔴 **MUST NOT** overwrite an existing episode-level preference when the user updates the series-level preference

---

## 12. Error Handling & Logging

### Global Exception Handlers

🔴 **MUST** register both of these in `App.axaml.cs` before any UI is shown:

```csharp
// Faulted Tasks that nobody awaited — prevent silent process termination
TaskScheduler.UnobservedTaskException += (_, e) =>
{
    _logger.LogError(e.Exception, "Unobserved task exception");
    e.SetObserved();
};

// Truly unhandled exceptions — log then allow crash with useful info
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    _logger.LogCritical(e.ExceptionObject as Exception, "Unhandled fatal exception");
};
```

### Service Rules

- 🔴 Every service receives `ILogger<T>` via DI constructor injection
- 🔴 Debug mode (Settings toggle) sets minimum log level to `Trace`
- 🔴 AniList API failures are **non-fatal** — surface error toast, allow user retry
- 🔴 Individual file scan failures are **non-fatal** — log `Warning`, skip file, continue
- 🔴 Player errors surface as dismissible overlay in `PlayerPage` — never crash
- 🟡 Log file path: `{AppDataPath}/logs/aniplayer-{yyyy-MM-dd}.log`

### Log Level Guide

| Level     | Use for                                                          |
|-----------|------------------------------------------------------------------|
| `Trace`   | Every file examined, every DB query executed                     |
| `Debug`   | Scan results, metadata fetched, tracks detected                  |
| `Info`    | Library added/removed, playback started, settings changed        |
| `Warning` | File skipped, API returned no result, lock retry attempted       |
| `Error`   | Unexpected exceptions in services, DB operation failed           |
| `Critical`| App-crashing unhandled exception                                 |

---

## 13. Screens & Behaviour

### MainWindow
- Fixed shell: `Sidebar` (left) + `ContentControl` (right)
- Sidebar: expanded default, collapsible to icon-only, **never hidden except fullscreen player**
- Page navigation replaces `ContentControl.Content`

### HomePage
- **Continue Watching:** Last 10 episodes with `position_seconds > 0 AND is_completed = 0`, ordered by `last_watched_at DESC`. Shown as `SeriesCard` with progress bar overlay.
- **Recently Added:** Series with `created_at > now - 14 days`. Horizontal scroll row.
- 🔴 Empty state required when no watch history exists — friendly message, not a blank panel

### LibraryPage
- Grid of `SeriesCard` controls
- Filter bar: All | Watching | Completed | Not Started
- Sort: Title A-Z | Recently Added | Recently Watched
- Search: local string filter on cached titles — 🔴 **MUST NOT** call API for search
- 🔴 Empty state required when no series exist
- Non-blocking scan progress indicator when scan is active

### ShowInfoPage
- Hero: cover image, preferred title (English → Romaji → folder name fallback), synopsis, genres, score, status
- Auto-fetch metadata on open if `metadata_fetched_at IS NULL`
- "Refresh Metadata" button always visible regardless of metadata state
- Episode list grouped: **Episodes → Specials → OVAs → Other**, each group collapsible
- `EpisodeRow`: thumbnail, title, episode number, duration, progress bar, completed checkmark
- Series-level audio/subtitle preference selector — saved to `TrackPreferences`
- 🔴 Graceful "No metadata found" state: show folder name as title, placeholder cover, "Retry" button

### PlayerPage
- 🔴 Sidebar hidden in fullscreen, restored on exit
- Controls overlay: auto-hide after 3s inactivity, reappear on any mouse movement
- 🔴 Resume from `WatchProgress.position_seconds` automatically on load
- 🔴 Save progress every 5 seconds (debounced — do not write on every position tick)
- 🔴 Mark `is_completed = true` when `position >= duration × 0.90`
- "Play Next Episode" prompt when `remaining < 30 seconds`

**`PlayerControls` must include:**
- Seek bar with current position + total duration
- Play/Pause, Previous Episode, Next Episode
- Volume slider + mute toggle
- Audio track selector (populated from mpv track list at load time)
- Subtitle track selector (populated from mpv track list at load time)
- Playback speed selector: 0.5× / 0.75× / 1× / 1.25× / 1.5× / 2×
- Fullscreen toggle

### OptionsPage
- **Libraries:** list, add (folder picker dialog), remove (confirmation dialog required)
- **Playback:** default audio language, default subtitle language, auto-play next toggle
- **Appearance:** theme (Dark / Light / System), sidebar default state
- **Debug:** verbose logging toggle, "Open Log Folder" button
- **About:** app version string

---

## 14. App Data Paths

🔴 **MUST** resolve base path at runtime — never hardcode absolute paths:

```csharp
// AppConstants.cs
public static string AppDataPath =>
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AniPlayer");

public static string DbPath         => Path.Combine(AppDataPath, "aniplayer.db");
public static string CoversPath     => Path.Combine(AppDataPath, "covers");
public static string ThumbnailsPath => Path.Combine(AppDataPath, "thumbnails");
public static string LogsPath       => Path.Combine(AppDataPath, "logs");
```

On Linux, `SpecialFolder.ApplicationData` resolves to `~/.config` — correct behaviour, no special case needed.
🔴 **MUST** call `Directory.CreateDirectory()` on all subdirectories during `DatabaseInitializer.InitializeAsync()`.

---

## 15. DI Registration (App.axaml.cs)

🔴 **MUST** register in this order. All services are `Singleton`.

```csharp
public static IServiceProvider Services { get; private set; } = null!;

// In OnFrameworkInitializationCompleted():
var services = new ServiceCollection();

// Infrastructure
services.AddSingleton<HttpClient>();
services.AddLogging(b => b.AddConsole().AddFile(/* path from AppConstants */));

// Core services
services.AddSingleton<IDatabaseService,      DatabaseService>();
services.AddSingleton<ISettingsService,      SettingsService>();
services.AddSingleton<ILibraryService,       LibraryService>();
services.AddSingleton<IMetadataService,      MetadataService>();
services.AddSingleton<IWatchProgressService, WatchProgressService>();
services.AddSingleton<IScannerService,       ScannerService>();
services.AddSingleton<IFolderWatcherService, FolderWatcherService>();
services.AddSingleton<IPlayerService,        PlayerService>();

Services = services.BuildServiceProvider();

// Startup sequence — ORDER MATTERS
await Services.GetRequiredService<IDatabaseService>().InitializeAsync();   // 1. DB + dirs
await Services.GetRequiredService<IFolderWatcherService>().RestoreWatchersAsync(); // 2. Watchers
// 3. Show main window only after both complete
```

---

## 16. Implicit Requirements (Must Implement)

| Feature                      | Requirement                                                                                       |
|------------------------------|---------------------------------------------------------------------------------------------------|
| **First Run Wizard**         | One-page prompt to add first library. Shown when `Libraries` table is empty. Must block navigation until at least one library is added. |
| **Empty States**             | HomePage (no history) and LibraryPage (no series) must show friendly UI, not blank white panels.  |
| **Toast Notifications**      | Non-blocking 3-second auto-dismiss toasts for: scan complete, metadata fetched, file skipped, errors. |
| **Confirmation Dialogs**     | Required before: removing a library, clearing watch history for a series.                         |
| **Metadata Not Found State** | ShowInfoPage shows folder name as title, placeholder cover, "No metadata found — Retry" button.   |
| **Scan Progress Indicator**  | Non-blocking overlay or spinner in LibraryPage during active scans.                               |
| **App Startup Order**        | DB init → directory creation → watcher restoration → show window. Never show UI before DB is ready. |

---

## 17. Out of Scope (v1)

🔴 **MUST NOT** implement any of the following. If suggested during development, decline and reference this list:

- Online streaming or IPTV playback
- AniList user account sync or watch scrobbling
- Torrent client integration or download management
- External subtitle download from the internet
- Mobile or WebAssembly build targets
- Multiple user profiles
- Trailer or preview video playback
- Manual episode ↔ AniList ID mapping UI
- Recommendation engine or "discover" features
- Social or community features of any kind
