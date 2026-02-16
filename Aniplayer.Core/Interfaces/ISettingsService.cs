namespace Aniplayer.Core.Interfaces;

public interface ISettingsService
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task DeleteAsync(string key);
    Task<T?> GetAsync<T>(string key) where T : struct;
    Task<bool> GetBoolAsync(string key, bool defaultValue = false);
}
