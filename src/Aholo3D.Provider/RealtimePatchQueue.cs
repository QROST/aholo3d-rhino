namespace Aholo3D.Provider;

/// <summary>One ordered queue per session. Consecutive pending camera edits coalesce; model edits are barriers.</summary>
public sealed class RealtimePatchQueue : IAsyncDisposable
{
    private sealed class Patch(string content, bool camera)
    {
        internal string Content = content;
        internal readonly bool Camera = camera;
        internal readonly List<TaskCompletionSource> Waiters = [];
    }
    private readonly string _sessionId;
    private readonly Func<string, string, CancellationToken, Task<RealtimeUpdateResult>> _send;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private readonly LinkedList<Patch> _pending = new();
    private TaskCompletionSource _idle = CompletedSignal();
    private bool _running;
    private bool _disposed;
    private Exception? _failure;
    private Task _worker = Task.CompletedTask;

    public RealtimePatchQueue(string sessionId, Func<string, string, CancellationToken, Task<RealtimeUpdateResult>> send)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session identity is required.", nameof(sessionId));
        ArgumentNullException.ThrowIfNull(send);
        _sessionId = sessionId;
        _send = send;
    }
    public Task EnqueueCameraAsync(string patchUsda) => Enqueue(patchUsda, camera: true);
    public Task EnqueueSceneAsync(string patchUsda) => Enqueue(patchUsda, camera: false);
    public Task FlushAsync(CancellationToken ct = default)
    {
        lock (_gate) return _idle.Task.WaitAsync(ct);
    }
    private Task Enqueue(string content, bool camera)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Patch must not be empty.", nameof(content));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_failure is not null) return Task.FromException(_failure);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Patch patch;
            if (camera && _pending.Last?.Value is { Camera: true } last) { patch = last; patch.Content = content; }
            else { patch = new(content, camera); _pending.AddLast(patch); }
            patch.Waiters.Add(completion);
            if (!_running)
            {
                _running = true;
                _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _worker = DrainAsync();
            }
            return completion.Task;
        }
    }
    private async Task DrainAsync()
    {
        await Task.Yield();
        while (true)
        {
            Patch patch;
            lock (_gate)
            {
                if (_pending.First is null) { _running = false; _idle.TrySetResult(); return; }
                patch = _pending.First.Value;
                _pending.RemoveFirst();
            }
            try
            {
                // Request IDs are generated once per actual send. No automatic replay on uncertain outcomes.
                var result = await _send(Guid.NewGuid().ToString("N"), patch.Content, _lifetime.Token).ConfigureAwait(false);
                if (result.SessionId != _sessionId || result.Status != RealtimeSessionStatus.Active)
                    throw new ProviderException("inactive_stream", "Realtime patch did not preserve the active session.");
                foreach (var waiter in patch.Waiters) waiter.TrySetResult();
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    _failure = error;
                    foreach (var waiter in patch.Waiters.Concat(_pending.SelectMany(p => p.Waiters)))
                        if (error is OperationCanceledException) waiter.TrySetCanceled(_lifetime.Token); else waiter.TrySetException(error);
                    _pending.Clear();
                    _running = false;
                    if (error is OperationCanceledException) _idle.TrySetCanceled(_lifetime.Token); else _idle.TrySetException(error);
                }
                return;
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        Task worker;
        lock (_gate)
        {
            if (!_disposed) { _disposed = true; _lifetime.Cancel(); }
            worker = _worker;
        }
        await worker.ConfigureAwait(false);
    }
    private static TaskCompletionSource CompletedSignal()
    {
        var result = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        result.SetResult(); return result;
    }
}
