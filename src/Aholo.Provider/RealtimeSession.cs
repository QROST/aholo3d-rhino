namespace Aholo.Provider;

/// <summary>
/// Owns a known ACTIVE session. Creating and persisting an operation is deliberately separate;
/// connection failures never create another session. Close failures retain a visible cleanup obligation.
/// </summary>
public sealed class RealtimeSession : IAsyncDisposable
{
    private readonly AholoApiClient _api;
    private readonly ApiKeyCredential _key;
    private readonly RealtimeConnection _connection;
    private readonly RealtimeStream _stream;
    private readonly RealtimePatchQueue _updates;
    private readonly object _closeGate = new();
    private Task? _closing;
    private bool _closeStarted;
    private volatile bool _ready;
    public string SessionId { get; }
    public bool NeedsServerCleanup { get; private set; } = true;

    public RealtimeSession(AholoApiClient api, ApiKeyCredential key, RealtimeOperation operation, IRealtimeWebSocketTransport transport)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(transport);
        if (!operation.Done || operation.Status != RealtimeSessionStatus.Active || operation.Connection is null || operation.Error is not null ||
            string.IsNullOrEmpty(operation.OperationId) || operation.OperationId.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException("Only a known active realtime operation can be connected.", nameof(operation));
        _api = api; _key = key; _connection = operation.Connection; SessionId = operation.OperationId;
        _stream = new RealtimeStream(transport);
        _updates = new RealtimePatchQueue(SessionId, (id, usda, ct) => _api.PushRealtimeAsync(_key, SessionId, id, usda, ct));
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        try { await _stream.ConnectAsync(_connection, ct).ConfigureAwait(false); }
        catch { await CloseAfterFailureAsync().ConfigureAwait(false); throw; }
    }
    public async Task<RealtimeStreamMessage> ReadNextAsync(CancellationToken ct = default)
    {
        try
        {
            var message = await _stream.ReadNextAsync(ct).ConfigureAwait(false);
            if (message is RealtimeStatusMessage { Code: RealtimeStatusCode.Initialized })
            {
                lock (_closeGate)
                    if (!_closeStarted) _ready = true;
            }
            if (message is RealtimeStreamClosed or RealtimeStatusMessage { Code: RealtimeStatusCode.NodeOverloaded or RealtimeStatusCode.InitializationFailed or RealtimeStatusCode.SlaveLimitExceeded })
                await CloseAfterFailureAsync().ConfigureAwait(false);
            return message;
        }
        catch { await CloseAfterFailureAsync().ConfigureAwait(false); throw; }
    }
    public Task EnqueueCameraAsync(string patchUsda)
    {
        lock (_closeGate) { RequireReady(); return ObserveUpdateAsync(_updates.EnqueueCameraAsync(patchUsda)); }
    }
    public Task EnqueueSceneAsync(string patchUsda)
    {
        lock (_closeGate) { RequireReady(); return ObserveUpdateAsync(_updates.EnqueueSceneAsync(patchUsda)); }
    }
    private void RequireReady()
    {
        if (!_ready || _closeStarted) throw new InvalidOperationException("Wait for realtime streaming initialization before sending patches.");
    }
    // At session level, cancellation of Flush cancels the live session as well. Retaining an
    // unobserved update queue after the caller abandons its final acknowledgement would keep billing.
    public Task FlushAsync(CancellationToken ct = default) => ObserveUpdateAsync(_updates.FlushAsync(ct));
    private async Task ObserveUpdateAsync(Task update)
    {
        try { await update.ConfigureAwait(false); }
        catch { await CloseAfterFailureAsync().ConfigureAwait(false); throw; }
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        Task task = StartClosing(allowExplicitRetry: true);
        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }
    private Task CloseAfterFailureAsync() => StartClosing(allowExplicitRetry: false);
    private Task StartClosing(bool allowExplicitRetry)
    {
        lock (_closeGate)
        {
            _closeStarted = true;
            _ready = false;
            // Several failed queued updates share this same cleanup attempt. Only a later explicit
            // Close may retry a failed DELETE; fault observers must not trigger hidden retries.
            if (allowExplicitRetry && (_closing?.IsFaulted == true || _closing?.IsCanceled == true)) _closing = null;
            return _closing ??= CloseCoreAsync();
        }
    }
    private async Task CloseCoreAsync()
    {
        _ready = false;
        try
        {
            // Stop edits first, then close the WebSocket before deleting the server-side session.
            try { await _updates.DisposeAsync().ConfigureAwait(false); }
            finally { await _stream.CloseAsync().ConfigureAwait(false); }
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _api.CloseRealtimeAsync(_key, SessionId, cleanupTimeout.Token).ConfigureAwait(false);
                NeedsServerCleanup = false;
            }
            catch (Exception e) when (e is ProviderException or OperationCanceledException)
            {
                throw new ProviderException("stream_cleanup_required", "Realtime session cleanup is not confirmed. Retain the session ID and explicitly retry closing it.");
            }
        }
    }
    public override string ToString() => "Aholo realtime session (credentials redacted)";
    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
}
