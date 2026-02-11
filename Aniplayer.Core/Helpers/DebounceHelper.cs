namespace Aniplayer.Core.Helpers;

public sealed class DebounceHelper : IDisposable
{
    private readonly Func<Task> _action;
    private readonly int _delayMs;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    public DebounceHelper(Func<Task> action, int delayMs)
    {
        _action = action;
        _delayMs = delayMs;
    }

    public void Trigger()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_delayMs, token);
                    if (!token.IsCancellationRequested)
                        await _action();
                }
                catch (OperationCanceledException) { }
            }, token);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }
}
