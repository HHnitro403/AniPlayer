# AniPlayer UI Agent Context

## Stack
- C# / Avalonia UI 11.3.2 / .NET 9
- No MVVM, no ViewModels, no ObservableCollection
- Code-behind pattern only (.axaml + .axaml.cs pairs)
- FluentTheme already installed — do NOT add external theme packages

## Critical Rules (never violate)
- Never add Material.Avalonia, FluentAvalonia, or Neumorphism packages
- Never use INotifyPropertyChanged or data bindings to complex objects
- Never put business logic in .axaml.cs files
- All UI colors/spacing must use DynamicResource referencing Theme.axaml tokens
- Build must pass: `dotnet build AniPlayer.UI/AniPlayer.UI.csproj`

## File locations
- Design tokens: AniPlayer.UI/Styles/Theme.axaml
- Control styles: AniPlayer.UI/Styles/Controls.axaml  
- Animations: AniPlayer.UI/Styles/Animations.axaml
- App entry: AniPlayer.UI/App.axaml (Styles already included here)

## Verify after every change
Run: dotnet build AniPlayer.UI/AniPlayer.UI.csproj
Expected: Build succeeded with 0 errors