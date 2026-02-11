using System;
using System.IO;

namespace AniPlayer.UI
{
    internal static class Logger
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AniPlayer",
            "debug.log");

        private static readonly object _lock = new object();

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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
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

        public static string GetLogFilePath() => LogFilePath;
    }
}
