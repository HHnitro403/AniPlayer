namespace Aniplayer.Core.Constants;

public static class AppConstants
{
    // --- Paths ---
    public static string AppDataPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AniPlayer");

    public static string DbPath         => Path.Combine(AppDataPath, "aniplayer.db");
    public static string CoversPath     => Path.Combine(AppDataPath, "covers");
    public static string ThumbnailsPath => Path.Combine(AppDataPath, "thumbnails");
    public static string LogsPath       => Path.Combine(AppDataPath, "logs");

    // --- Supported video extensions ---
    public static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".mov", ".wmv"
    };

    // --- Scanner / file-readiness ---
    public const int FileReadyMaxRetries   = 30;
    public const int FileReadyRetryDelayMs = 5000;
    public const int ScanDebounceDelayMs   = 3000;

    // --- Player ---
    public const int ProgressSaveIntervalMs  = 5000;
    public const double CompletionThreshold  = 0.90;
    public const int PlayNextPromptSeconds   = 30;

    // --- AniList API ---
    public const string AniListEndpoint = "https://graphql.anilist.co";
    public const int AniListTimeoutSeconds = 15;

    // --- UI ---
    public const int ControlsAutoHideMs    = 3000;
    public const int ToastDurationMs       = 3000;
    public const int RecentlyAddedDays     = 14;
    public const int ContinueWatchingLimit = 10;
}
