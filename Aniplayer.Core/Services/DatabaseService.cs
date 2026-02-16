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

    public DatabaseService(ILogger<DatabaseService> logger)
    {
        _logger = logger;
        _connectionString = $"Data Source={AppConstants.DbPath}";
        _initializer = new DatabaseInitializer(
            new LoggerFactory().CreateLogger<DatabaseInitializer>());
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

        // Auto-backup existing database before init (keeps last 3 backups)
        BackupDatabase();

        await _initializer.InitializeAsync();
    }

    private void BackupDatabase()
    {
        if (!File.Exists(AppConstants.DbPath))
            return;

        try
        {
            var backupDir = Path.Combine(AppConstants.AppDataPath, "backups");
            Directory.CreateDirectory(backupDir);

            var backupPath = Path.Combine(backupDir,
                $"aniplayer-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            File.Copy(AppConstants.DbPath, backupPath, overwrite: true);
            _logger.LogInformation("Database backed up to {Path}", backupPath);

            // Keep only the 3 most recent backups
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
            _logger.LogWarning(ex, "Database backup failed — continuing without backup");
        }
    }
}
