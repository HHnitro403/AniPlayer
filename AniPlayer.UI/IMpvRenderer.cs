using System;

namespace AniPlayer.UI
{
    public interface IMpvRenderer : IDisposable
    {
        bool IsInitialized { get; }
        void Render();
        void Resize(int width, int height);
    }
}
