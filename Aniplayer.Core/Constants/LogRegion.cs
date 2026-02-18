namespace Aniplayer.Core.Constants;

[Flags]
public enum LogRegion
{
    None     = 0,
    General  = 1 << 0,   // Always-on: startup, navigation, errors
    Scanner  = 1 << 1,   // ScannerService scan progress
    Parser   = 1 << 2,   // EpisodeParser element-level logging
    UI       = 1 << 3,   // Page data loading, filter results
    DB       = 1 << 4,   // Per-row DB dump in RefreshPages
    Progress = 1 << 5,   // Watch progress saving
    All      = General | Scanner | Parser | UI | DB | Progress,
}
