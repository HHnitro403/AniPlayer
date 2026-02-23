using System.Data;
using Aniplayer.Core.Constants;
using Aniplayer.Core.Database;
using Aniplayer.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Aniplayer.Core.Services;

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly DatabaseInitializer _initializer;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(ILogger<DatabaseService> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _connectionString = $"Data Source={AppConstants.DbPath}";
        _initializer = new DatabaseInitializer(
            loggerFactory.CreateLogger<DatabaseInitializer>());
    }

    public IDbConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // FK enforcement is per-connection in SQLite
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();

        return connection;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("DatabaseService.InitializeAsync");

        // [Changed] Await the new async backup method
        await BackupDatabaseAsync();

        await _initializer.InitializeAsync();
    }

    // [Changed] Method is now Async and uses SQLite VACUUM INTO
    private async Task BackupDatabaseAsync()
    {
        if (!File.Exists(AppConstants.DbPath))
            return;

        try
        {
            var backupDir = Path.Combine(AppConstants.AppDataPath, "backups");
            Directory.CreateDirectory(backupDir);

            var backupPath = Path.Combine(backupDir,
                $"aniplayer-{DateTime.Now:yyyyMMdd-HHmmss}.db");

            // [Fix] Use VACUUM INTO for atomic, safe WAL-mode backups.
            // File.Copy is unsafe because it misses the -wal file content.
            using (var sourceConnection = new SqliteConnection(_connectionString))
            {
                await sourceConnection.OpenAsync();

                using var cmd = sourceConnection.CreateCommand();
                // Note: VACUUM INTO expects a string literal for the path. 
                // We escape single quotes just to be safe, though our timestamped path is generally safe.
                var safePath = backupPath.Replace("'", "''");
                cmd.CommandText = $"VACUUM INTO '{safePath}';";

                await cmd.ExecuteNonQueryAsync();
            }

            _logger.LogInformation("Database backed up to {Path}", backupPath);

            // Cleanup: Keep only the 3 most recent backups
            CleanupOldBackups(backupDir);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database backup failed — continuing without backup");
        }
    }

    private void CleanupOldBackups(string backupDir)
    {
        try
        {
            var backups = Directory.GetFiles(backupDir, "aniplayer-*.db")
                .OrderByDescending(f => f)
                .Skip(3)
                .ToArray();

            foreach (var old in backups)
            {
                File.Delete(old);
                _logger.LogDebug("Deleted old backup: {Path}", old);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to clean up old backups: {Message}", ex.Message);
        }
    }
}