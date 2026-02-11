using System;
using System.IO;

namespace AniPlayer.UI
{
    /// <summary>
    /// Log regions allow verbose logging to be enabled per-subsystem
    /// so the app doesn't take forever to load when debugging a specific area.
    /// </summary>
    [Flags]
    public enum LogRegion
    {
        None     = 0,
        General  = 1 << 0,   // Always-on: startup, navigation, errors
        Scanner  = 1 << 1,   // ScannerService scan progress
        Parser   = 1 << 2,   // EpisodeParser element-level logging
        UI       = 1 << 3,   // Page data loading, filter results
        DB       = 1 << 4,   // Per-row DB dump in RefreshPages
        All      = General | Scanner | Parser | UI | DB,
    }

    internal static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AniPlayer",
            "debug.log");

        private static readonly object _lock = new object();

        /// <summary>
        /// Active log regions. Only messages matching these flags are written.
        /// General is always enabled. Change this to enable verbose subsystem logging.
        /// </summary>
        public static LogRegion EnabledRegions { get; set; } = LogRegion.General;

        static Logger()
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Clear old log on startup
                if (File.Exists(LogFilePath))
                {
                    File.Delete(LogFilePath);
                }

                Log("=== AniPlayer Debug Log Started ===");
                Log($"Log file location: {LogFilePath}");
                Log($"Enabled log regions: {EnabledRegions}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        /// <summary>
        /// Log a message. Defaults to General region (always logged).
        /// </summary>
        public static void Log(string message, LogRegion region = LogRegion.General)
        {
            // Skip if the region is not enabled (General is always on)
            if (region != LogRegion.General && (EnabledRegions & region) == 0)
                return;

            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] {message}";

                lock (_lock)
                {
                    // Write to console
                    Console.WriteLine(logMessage);

                    // Write to file
                    File.AppendAllText(LogFilePath, logMessage + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Logging failed: {ex.Message}");
            }
        }

        public static void LogError(string message, Exception? ex = null)
        {
            if (ex != null)
            {
                Log($"ERROR: {message} - Exception: {ex.GetType().Name}: {ex.Message}");
                Log($"Stack trace: {ex.StackTrace}");
            }
            else
            {
                Log($"ERROR: {message}");
            }
        }

        /// <summary>
        /// Enable one or more log regions for verbose debugging.
        /// </summary>
        public static void EnableRegion(LogRegion region)
        {
            EnabledRegions |= region;
            Log($"Log region enabled: {region} (now: {EnabledRegions})");
        }

        /// <summary>
        /// Disable a log region.
        /// </summary>
        public static void DisableRegion(LogRegion region)
        {
            EnabledRegions &= ~region;
            Log($"Log region disabled: {region} (now: {EnabledRegions})");
        }

        public static string GetLogFilePath() => LogFilePath;
    }
}
