using Aniplayer.Core.Constants;

namespace Aniplayer.Core.Helpers;

public static class FileHelper
{
    public static bool IsSupportedVideo(string filePath) =>
        AppConstants.SupportedExtensions.Contains(Path.GetExtension(filePath));

    public static bool ContainsVideoFiles(string directory)
    {
        if (!Directory.Exists(directory))
            return false;

        return Directory.EnumerateFiles(directory).Any(IsSupportedVideo);
    }

    public static async Task<bool> WaitUntilReadyAsync(string filePath,
        CancellationToken ct = default)
    {
        for (int i = 0; i < AppConstants.FileReadyMaxRetries; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var fs = new FileStream(filePath, FileMode.Open,
                    FileAccess.Read, FileShare.Read);
                return true;
            }
            catch (IOException)
            {
                await Task.Delay(AppConstants.FileReadyRetryDelayMs, ct);
            }
        }
        return false;
    }

    public static IEnumerable<string> EnumerateVideoFiles(string directory)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            if (IsSupportedVideo(file))
                yield return file;
        }
    }
}
