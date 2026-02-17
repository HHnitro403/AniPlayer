# Agent Progress Log

## Completed Tasks
(agent fills this in)

## Current Task
(agent fills this in)

## Blocked / Issues
(agent fills this in)
```

---

## Task Breakdown

Each task below is a **single agent session prompt**. They are ordered so each one has no dependencies on future tasks — only completed ones.

---

### Task 1 — Design Token System
**Prompt to give the agent:**
```
Read AGENT_CONTEXT.md first.

Task: Populate AniPlayer.UI/Styles/Theme.axaml with a complete design token system.

Requirements:
- Define these color tokens as SolidColorBrush resources:
  BgBase (#0D0D1A), BgSurface (#12121E), BgCard (#1A1A2E), BgElevated (#22223A),
  AccentPrimary (#6C6CFF), AccentHover (#8080FF), AccentSubtle (#2A2A5A),
  TextPrimary (#E8E8FF), TextSecondary (#9090B0), TextMuted (#505070),
  BorderSubtle (#1E1E35), DangerRed (#FF4444)

- Define Thickness spacing tokens:
  SpacingXS=4, SpacingS=8, SpacingM=16, SpacingL=24, SpacingXL=32, SpacingXXL=48

- Define CornerRadius tokens:
  RadiusS=4, RadiusM=8, RadiusL=12, RadiusFull=999

- After writing Theme.axaml, find every hardcoded color hex in these files and replace 
  with DynamicResource references to the tokens you just defined:
  MainWindow.axaml, Sidebar.axaml, PlayerPage.axaml, OptionsPage.axaml

Verify: dotnet build AniPlayer.UI/AniPlayer.UI.csproj must succeed with 0 errors.
Update AGENT_PROGRESS.md when done.
```

**Done condition:** Build passes, Theme.axaml has all tokens, no hardcoded hex values in the 4 target files.

---

### Task 2 — Button and Interactive Control Styles
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Populate AniPlayer.UI/Styles/Controls.axaml with styled controls.

Requirements:
- Default Button style: BgElevated background, RadiusS corners, 12,8 padding,
  TextPrimary foreground, 0.15s BrushTransition on Background on pointer over
- Button:pointerover state: AccentSubtle background
- Button.Primary class: AccentPrimary background, SemiBold font weight
- Button.Danger class: DangerRed background
- Button:disabled state: 40% opacity
- TextBox style: BgCard background, BorderSubtle border, RadiusS corners,
  TextPrimary foreground, AccentPrimary border on :focus
- Slider style: thin 4px track, AccentPrimary fill, no thumb visible when not focused,
  AccentHover thumb on :pointerover
- ScrollBar style: thin (6px), BgElevated track, TextMuted thumb, only visible on hover

Apply Primary class to all "Add Folder", "Play", and "Fetch" buttons across all pages.

Verify: dotnet build must succeed.
Update AGENT_PROGRESS.md.
```

**Done condition:** Build passes, all button states have visible hover feedback.

---

### Task 3 — SeriesCard AXAML Refactor
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Rewrite AniPlayer.UI/Views/Controls/SeriesCard.axaml to be a fully designed card.

Requirements:
- Outer Border: BgCard background, RadiusM corners, clip to bounds, 
  Width=160, cursor=Hand
- Add hover scale animation: on :pointerover, RenderTransform scale(1.03), 
  BoxShadow "0 8 24 0 #66000000", 0.15s TransformOperationsTransition
- Cover image area: 215px height, BgElevated background fallback,
  Image with UniformToFill stretch
- Placeholder text when no cover: first letter of title, large font, centered, muted
- Seasons badge: top-right overlay, AccentPrimary background, RadiusS, 
  white text, only visible when SeasonCount > 1
- Title text: 13px, SemiBold, TextPrimary, CharacterEllipsis trimming, 8px side margin
- Episode count: 11px, TextMuted, 8px side margin

The SetData method signature must stay identical. No changes to SeriesCard.axaml.cs 
beyond what's needed to support new named elements.

Verify: dotnet build must succeed.
Update AGENT_PROGRESS.md.
```

---

### Task 4 — Migrate HomePage Card Creation to AXAML
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Refactor HomePage to use SeriesCard control instead of code-behind card creation.

Requirements:
- In HomePage.axaml.cs, replace CreateSeriesCard() with instantiation of SeriesCard 
  UserControl and call SetData() on it
- Replace CreateContinueWatchingCard() with a new ContinueWatchingCard UserControl:
  - Create AniPlayer.UI/Views/Controls/ContinueWatchingCard.axaml + .cs
  - Width=190, cover image 120px height, progress bar overlay at bottom (4px, AccentPrimary),
    time remaining badge top-right, episode name SemiBold 12px, series name muted 11px
  - Hover scale same as SeriesCard
  - SetData(Episode, WatchProgress, Series?) method

All existing events (SeriesSelected, ResumeEpisodeRequested) must still fire correctly.

Verify: dotnet build must succeed.
Update AGENT_PROGRESS.md.
```

---

### Task 5 — LibraryPage Card Migration
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Refactor LibraryPage.axaml.cs to use SeriesCard instead of CreateSeriesCard().

Requirements:
- Replace CreateSeriesCard() method body with SeriesCard instantiation + SetData()
- Remove all the inline Border/StackPanel/Image construction that duplicates SeriesCard
- The ApplyFilter() method must still work identically
- SeriesSelected event must still fire

This should significantly shorten LibraryPage.axaml.cs.

Verify: dotnet build must succeed.
Update AGENT_PROGRESS.md.
```

---

### Task 6 — Player Controls Redesign
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Build out PlayerControls.axaml as a real control and integrate it into PlayerPage.

Requirements for PlayerControls.axaml:
- Replace "Welcome to Avalonia!" with a proper layout
- Progress bar row: current time label, Slider (x:Name="ProgressSlider"), total time label
- Transport row left: Play/Pause button using Path geometry (triangle for play, 
  two rectangles for pause), Stop button, Previous button, Next button — all icon-based
  using Path Data, no text labels on transport buttons
- Transport row center: now-playing TextBlock
- Transport row right: Audio label + WrapPanel for track buttons, 
  Volume slider (0-150 range, compact width 80px), Fullscreen button

Use these Path geometries:
- Play: "M8,5.14V19.14L19,12.14L8,5.14Z"
- Pause: "M14,19H18V5H14M6,19H10V5H6Z"  
- Stop: "M18,18H6V6H18V18Z"
- Next: "M6,18L14,12L6,6V18M14,6V18H16V6H14Z"
- Previous: "M6,18V6H8V18H6M10,18L18,12L10,6V18Z"

PlayerControls.axaml.cs: expose the same named elements that PlayerPage.axaml.cs 
currently references directly (ProgressSlider, PlayPauseButton, etc.) as public 
properties delegating to the internal named controls.

Do NOT move event handler logic — only the visual layout moves to PlayerControls.axaml.
PlayerPage.axaml embeds <local:PlayerControls x:Name="Controls"/> in the bottom border.

Verify: dotnet build must succeed.
Update AGENT_PROGRESS.md.
```

---

### Task 7 — Sidebar Active State and Visual Polish
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Improve Sidebar.axaml visual design.

Requirements:
- Nav buttons: remove hardcoded Background="Transparent", use Controls.axaml styles
- Active button state: left 3px border AccentPrimary, BgElevated background,
  TextPrimary foreground — applied via a "NavActive" style class
- In Sidebar.axaml.cs SetActive() method: toggle "NavActive" class on buttons 
  instead of manually setting Foreground/FontWeight
- App title area: add a simple colored dot or accent bar next to "AniPlayer" text
  using a Border with AccentPrimary background, 4px wide, 20px tall, RadiusS
- Sidebar bottom area: add a subtle top border (1px BorderSubtle) above Settings button
- Add 0.15s opacity transition on nav buttons for smooth state changes

Verify: dotnet build must succeed.
Update AGENT_PROGRESS.md.
```

---

### Task 8 — Page Transition Animations
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Populate Animations.axaml and wire page transitions in MainWindow.

Requirements in Animations.axaml:
- Define a Style targeting UserControl.PageEnter:
  Animation Duration 0:0:0.18, on attached trigger
  KeyFrame 0%: Opacity=0, TranslateTransform.Y=10
  KeyFrame 100%: Opacity=1, TranslateTransform.Y=0

In MainWindow.axaml.cs NavigateTo() method:
- After setting PageHost.Content, cast the new content to UserControl and 
  add "PageEnter" to its Classes, then remove it after 200ms via 
  Dispatcher.UIThread.Post with delay so the animation plays once

Verify: dotnet build must succeed.
Update AGENT_PROGRESS.md.
```

---

### Task 9 — ShowInfoPage Polish
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Polish ShowInfoPage.axaml and its episode rows.

Requirements:
- Header section: cover image border should have RadiusM and a subtle BoxShadow
- Genre badges: use AccentSubtle background instead of the current white-alpha
- Synopsis text: proper line height (1.5), TextSecondary color
- Episode rows: replace CreateEpisodeRow() code-behind construction with 
  EpisodeRow UserControl (it already exists in Controls/) — call SetData() on it
- EpisodeRow.axaml: add :pointerover background highlight (BgElevated), 
  0.1s BrushTransition, progress fill should use AccentPrimary brush token
- Back button: use "◀ Back" content with Primary class
- Refresh Metadata button: outlined style (transparent bg, AccentPrimary border+text)
  Add a Button.Outlined style to Controls.axaml

Verify: dotnet build must succeed.
Update AGENT_PROGRESS.md.
```

---

### Task 10 — OptionsPage and Final Cleanup
**Prompt:**
```
Read AGENT_CONTEXT.md and AGENT_PROGRESS.md first.

Task: Polish OptionsPage and do a final consistency pass across all files.

Requirements for OptionsPage:
- Section headers: add a bottom border (1px BorderSubtle) under each section title
- Library folder rows: add a hover highlight, polish the Remove button to use Danger class
- ToggleSwitch controls: verify they pick up AccentPrimary accent color from theme

Final consistency pass — search all .axaml files for:
1. Any remaining hardcoded hex color strings — replace with DynamicResource tokens
2. Any hardcoded Margin/Padding numbers that match spacing tokens — replace with StaticResource
3. Any Background="Transparent" on non-intentional controls — replace with token or remove

Verify: dotnet build must succeed with 0 errors and 0 warnings where possible.
Update AGENT_PROGRESS.md with full completion summary.
