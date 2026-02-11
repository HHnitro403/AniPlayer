using Aniplayer.Core.Database;
using Aniplayer.Core.Interfaces;
using Dapper;

namespace Aniplayer.Core.Services;

public class SettingsService : ISettingsService
{
    private readonly IDatabaseService _db;

    public SettingsService(IDatabaseService db)
    {
        _db = db;
    }

    public async Task<string?> GetAsync(string key)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<string>(
            Queries.GetSetting, new { key });
    }

    public async Task SetAsync(string key, string value)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.UpsertSetting, new { key, value });
    }

    public async Task DeleteAsync(string key)
    {
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(Queries.DeleteSetting, new { key });
    }

    public async Task<T?> GetAsync<T>(string key) where T : struct
    {
        var raw = await GetAsync(key);
        if (raw == null) return null;

        try { return (T)Convert.ChangeType(raw, typeof(T)); }
        catch { return null; }
    }

    public async Task<bool> GetBoolAsync(string key, bool defaultValue = false)
    {
        var raw = await GetAsync(key);
        if (raw == null) return defaultValue;
        return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
