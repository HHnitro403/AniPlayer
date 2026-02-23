using System;
using System.IO;
using Aniplayer.Core.Constants;

namespace AniPlayer.UI
{
    internal static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AniPlayer",
            "debug.log");

        private static readonly object _lock = new object();
        
        /// <summary>
        /// Global switch for verbose logging. If false, no non-error logs are written.
        /// </summary>
        public static bool MasterLoggingEnabled { get; set; } = false;

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

                Log("=== AniPlayer Debug Log Started ===", force: true);
                Log($"Log file location: {LogFilePath}", force: true);
                Log($"Enabled log regions: {EnabledRegions}", force: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        /// <summary>
        /// Log a message. Defaults to General region (always logged).
        /// </summary>
        public static void Log(string message, LogRegion region = LogRegion.General, bool force = false)
        {
            // First, check the master switch. `force` is used for initialization messages.
            if (!MasterLoggingEnabled && !force)
                return;

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
                Log($"ERROR: {message} - Exception: {ex.GetType().Name}: {ex.Message}", force: true);
                Log($"Stack trace: {ex.StackTrace}", force: true);
            }
            else
            {
                Log($"ERROR: {message}", force: true);
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
