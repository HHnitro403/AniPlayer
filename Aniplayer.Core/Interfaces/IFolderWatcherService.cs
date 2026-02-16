namespace Aniplayer.Core.Interfaces;

public interface IFolderWatcherService : IDisposable
{
    void WatchLibrary(int libraryId, string path);
    void StopWatching(int libraryId);
    void StopAll();
    event Action<int>? LibraryChanged;
}
