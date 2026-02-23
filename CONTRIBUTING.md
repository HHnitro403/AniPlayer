# Contributing to AniPlayer

Thank you for your interest in contributing to AniPlayer! This document provides guidelines and requirements for contributing to the project.

---

## 📋 Before You Start

### Required Reading

**You MUST read these documents before contributing:**
1. **[AniPlayer_ProjectScope.md](AniPlayer_ProjectScope.md)** — Complete architectural guidelines and rules
2. **[README.md](README.md)** — Project overview and features

**Failure to follow the architectural guidelines will result in pull requests being rejected.**

---

## 🏗️ Architecture Overview

AniPlayer uses a **Service-Based Passive View** architecture with strict layer separation:

### Two-Layer Architecture

| Layer | Project | Responsibilities | Dependencies Allowed |
|-------|---------|-----------------|---------------------|
| **Core** | `Aniplayer.Core` | Business logic, database operations, file I/O, API calls | SQLite, Dapper, AnitomySharp, System libraries |
| **UI** | `AniPlayer.UI` | Rendering, user interaction, navigation | Avalonia, Core project |

### The Five Commandments

These rules apply **everywhere, always, with no exceptions:**

1. 🔴 **No MVVM** — No ViewModels, no `INotifyPropertyChanged`, no `ReactiveObject`, no `ObservableCollection` as a binding target.
2. 🔴 **No Entity Framework** — SQLite access is via Dapper only. All SQL is written by hand as `const string` values in `Queries.cs`.
3. 🔴 **No `.Result` or `.Wait()`** — Every async operation is awaited. No synchronous blocking on Tasks anywhere.
4. 🔴 **No logic in UI files** — If a method in a `.axaml.cs` file does anything other than update a control or call a service, it is in the wrong place.
5. 🔴 **No parameter-passing navigation** — Services are resolved from DI. Pages receive context via a typed method call after construction, not constructor parameters.

### Special Exemptions

**The following files have architectural exemptions due to native video rendering requirements:**
- `AniPlayer.UI/Views/Pages/PlayerPage.axaml`
- `AniPlayer.UI/Views/Pages/PlayerPage.axaml.cs`

These files contain direct MPV integration and playback logic that cannot be easily separated. Changes to these files should still minimize business logic where possible.

---

## 🔧 Development Setup

### Prerequisites

**Required:**
- **.NET 9 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Git** — For version control
- **libmpv** — **REQUIRED DEPENDENCY** (must be downloaded separately, NOT included in repository)

### Installing libmpv

**⚠️ CRITICAL:** `libmpv-2.dll` is a **required dependency** and **must be downloaded separately**. The project will NOT build or run without it.

#### Windows

1. Download libmpv from one of these sources:
   - [mpv.io official builds](https://mpv.io/installation/)
   - [Shinchiro's builds](https://sourceforge.net/projects/mpv-player-windows/files/libmpv/)

2. Extract `libmpv-2.dll` from the archive

3. Place it in: `AniPlayer.UI/lib/win-x64/libmpv-2.dll`

4. Verify the file structure:
   ```
   AniPlayer.UI/
   └── lib/
       └── win-x64/
           └── libmpv-2.dll  ← Should be here
   ```

#### Linux

Install via package manager:
```bash
# Debian/Ubuntu
sudo apt install libmpv-dev

# Arch Linux
sudo pacman -S mpv

# Fedora
sudo dnf install mpv-libs-devel
```

#### macOS

Install via Homebrew:
```bash
brew install mpv
```

### Build Steps

```bash
# Clone the repository
git clone https://github.com/YOUR_USERNAME/AniPlayer.git
cd AniPlayer

# Ensure libmpv-2.dll is in place (Windows only)
# See instructions above

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run the application
dotnet run --project AniPlayer.UI
```

---

## 📝 Code Style & Guidelines

### General Rules

- Use **C# 12** features where appropriate
- Follow **standard C# naming conventions** (PascalCase for classes/methods, camelCase for private fields)
- Add **XML doc comments** for public APIs in Core services
- Keep methods **focused and single-purpose**
- **No magic numbers** — use constants in `AppConstants.cs`

### Core Project (`Aniplayer.Core`)

#### Service Implementation

✅ **Correct:**
```csharp
public class LibraryService : ILibraryService
{
    private readonly IDatabaseService _db;

    public LibraryService(IDatabaseService db)
    {
        _db = db;
    }

    public async Task<IEnumerable<Series>> GetAllSeriesAsync()
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryAsync<Series>(Queries.GetAllSeries);
    }
}
```

❌ **Wrong:**
```csharp
// ❌ No Entity Framework
public class LibraryService : DbContext { }

// ❌ No inline SQL
var series = await conn.QueryAsync<Series>("SELECT * FROM Series");

// ❌ No UI dependencies
using Avalonia.Controls;
```

#### Database Queries

All SQL goes in `Aniplayer.Core/Database/Queries.cs`:

```csharp
public static class Queries
{
    public const string GetAllSeries = @"
        SELECT id AS Id, title AS Title, ...
        FROM Series
        ORDER BY title";
}
```

### UI Project (`AniPlayer.UI`)

#### Page Pattern

✅ **Correct:**
```csharp
public partial class ShowInfoPage : UserControl
{
    private readonly ILibraryService _libraryService;

    public ShowInfoPage()
    {
        InitializeComponent();
        _libraryService = App.Services.GetRequiredService<ILibraryService>();
    }

    // Data loaded via typed method call, not constructor
    public async void LoadSeriesData(List<Series> series, List<Episode> episodes)
    {
        // Update UI controls only - no business logic
        TitleText.Text = series.First().Title;
        EpisodesList.ItemsSource = episodes;
    }
}
```

❌ **Wrong:**
```csharp
// ❌ No ViewModels
public class ShowInfoViewModel : INotifyPropertyChanged { }

// ❌ No constructor parameters for data
public ShowInfoPage(int seriesId) { }

// ❌ No business logic in UI
public void LoadSeriesData(...)
{
    // Calculate complex stats
    var averageScore = series.Average(s => s.Score); // ❌ Business logic!
}
```

#### Button Focusable Rule

**All buttons and interactive controls MUST have `Focusable="False"`** to prevent keyboard focus issues:

```xml
<Button Content="Play"
        Focusable="False"
        Click="PlayButton_Click"/>
```

---

## 🔀 Git Workflow

### Branching Strategy

- **`main`** — Stable releases
- **Feature branches** — `feature/your-feature-name`
- **Bug fixes** — `fix/issue-description`

### Commit Messages

Follow the existing style (lowercase, concise):

✅ **Good:**
```
fixed keyboard shortcuts not working when UI is visible
added seek buttons with keyboard integration
updated progress tracking to prevent database spam
```

❌ **Bad:**
```
Fixed stuff
WIP
asdfasdf
Update PlayerPage.axaml.cs
```

### Pull Request Process

1. **Fork** the repository
2. **Create a feature branch** from `main`
3. **Follow the architecture** — read `AniPlayer_ProjectScope.md`
4. **Test thoroughly** — ensure no regressions
5. **Write clear commit messages**
6. **Submit a PR** with:
   - Clear description of changes
   - Screenshots/videos if UI changes
   - Reference to related issues

---

## 🧪 Testing

Before submitting a PR:

- [ ] **Build succeeds** — `dotnet build` runs without errors
- [ ] **App runs** — `dotnet run --project AniPlayer.UI` launches successfully
- [ ] **No regressions** — Existing features still work
- [ ] **Test your changes** — Manually verify new functionality
- [ ] **Check logs** — No unexpected errors in `debug.log`

---

## 📁 File Organization

### Where to Put New Code

**Business logic:**
- New service: `Aniplayer.Core/Services/YourService.cs`
- Interface: `Aniplayer.Core/Interfaces/IYourService.cs`
- Model: `Aniplayer.Core/Models/YourModel.cs`
- SQL: `Aniplayer.Core/Database/Queries.cs`
- Constants: `Aniplayer.Core/Constants/YourConstants.cs`

**UI code:**
- New page: `AniPlayer.UI/Views/Pages/YourPage.axaml` + `.cs`
- New control: `AniPlayer.UI/Views/Controls/YourControl.axaml` + `.cs`
- Styles: `AniPlayer.UI/Styles/Theme.axaml`

### Service Registration

Add new services to `AniPlayer.UI/App.axaml.cs`:

```csharp
services.AddSingleton<IYourService, YourService>();
```

---

## ❓ Questions?

- **Architecture questions:** See [AniPlayer_ProjectScope.md](AniPlayer_ProjectScope.md)
- **Bug reports:** Use the [Bug Report template](.github/ISSUE_TEMPLATE/bug_report.md)
- **Feature requests:** Use the [Feature Request template](.github/ISSUE_TEMPLATE/feature_request.md)

---

## 📜 License

By contributing to AniPlayer, you agree that your contributions will be licensed under the [Apache License 2.0](LICENSE.txt).
