namespace Aniplayer.Core.Interfaces;

public interface IScannerService
{
    Task ScanLibraryAsync(int libraryId, CancellationToken ct = default);
    Task ScanAllLibrariesAsync(CancellationToken ct = default);
    event Action<string>? ScanProgress;
}
