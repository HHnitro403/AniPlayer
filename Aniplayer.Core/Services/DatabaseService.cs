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
        try
        {
            await _initializer.InitializeAsync();
        }
        catch (SqliteException ex) when (ex.Message.Contains("malformed"))
        {
            _logger.LogCritical("Database corrupted, attempting backup and recreation");
            var backup = $"{AppConstants.DbPath}.backup-{DateTime.Now:yyyyMMdd-HHmmss}";
            if (File.Exists(AppConstants.DbPath))
                File.Move(AppConstants.DbPath, backup);
            _logger.LogWarning("Backed up corrupted DB to {Path}", backup);
            await _initializer.InitializeAsync();
        }
    }
}
