using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Aholo3D.Provider;

public readonly record struct RealtimeSocketChunk(int Count, WebSocketMessageType MessageType, bool EndOfMessage);

/// <summary>Injectable byte transport. It receives stream credentials only, never a gateway API key.</summary>
public interface IRealtimeWebSocketTransport : IAsyncDisposable
{
    Task ConnectAsync(RealtimeConnection connection, CancellationToken ct);
    Task SendTextAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct);
    ValueTask<RealtimeSocketChunk> ReceiveAsync(Memory<byte> buffer, CancellationToken ct);
    Task CloseAsync(CancellationToken ct);
    void Abort();
}

/// <summary>Production transport: TLS, port 443, no cookies/proxy/redirects, pinned public DNS addresses.</summary>
public sealed class RealtimeWebSocketTransport : IRealtimeWebSocketTransport
{
    private readonly ClientWebSocket _socket = new();
    private readonly HttpMessageInvoker _http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        ConnectCallback = RealtimeNetwork.ConnectAsync
    });
    private int _started;
    public async Task ConnectAsync(RealtimeConnection connection, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Stream transport cannot be reused.");
        try { await _socket.ConnectAsync(connection.CreateWebSocketUri(), _http, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { _socket.Abort(); throw new OperationCanceledException(ct); }
        catch (Exception e) when (e is WebSocketException or HttpRequestException or IOException or ArgumentException)
        { _socket.Abort(); throw new ProviderException("stream_connect_failed", "Realtime stream connection failed."); }
    }
    public async Task SendTextAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        try { await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw new OperationCanceledException(ct); }
        catch (Exception e) when (e is WebSocketException or IOException)
        { throw new ProviderException("stream_send_failed", "Realtime stream handshake failed."); }
    }
    public async ValueTask<RealtimeSocketChunk> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
    {
        try
        {
            var result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            return new(result.Count, result.MessageType, result.EndOfMessage);
        }
        catch (OperationCanceledException) { throw new OperationCanceledException(ct); }
        catch (Exception e) when (e is WebSocketException or IOException)
        { throw new ProviderException("stream_receive_failed", "Realtime stream disconnected or returned invalid data."); }
    }
    public async Task CloseAsync(CancellationToken ct)
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try { await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client closed", ct).ConfigureAwait(false); }
            catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException) { _socket.Abort(); }
        }
    }
    public void Abort() => _socket.Abort();
    public ValueTask DisposeAsync() { _socket.Abort(); _socket.Dispose(); _http.Dispose(); return ValueTask.CompletedTask; }
}

/// <summary>Reassembles protocol messages. This is not a viewport renderer or a depth compositor.</summary>
public sealed class RealtimeStream : IAsyncDisposable
{
    public const int DefaultMaximumImageMessageBytes = 32 * 1024 * 1024;
    public const int MaximumStatusMessageBytes = 64 * 1024;
    private readonly IRealtimeWebSocketTransport _transport;
    private readonly int _maximumImageMessageBytes;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _reader = new(1, 1);
    private readonly object _closeGate = new();
    private Task? _closing;
    private int _started;
    private bool _connected;

    public RealtimeStream(IRealtimeWebSocketTransport transport, int maximumImageMessageBytes = DefaultMaximumImageMessageBytes)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (maximumImageMessageBytes < 4) throw new ArgumentOutOfRangeException(nameof(maximumImageMessageBytes));
        _transport = transport;
        _maximumImageMessageBytes = maximumImageMessageBytes;
    }
    public async Task ConnectAsync(RealtimeConnection connection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Stream cannot be reconnected; create a new transport explicitly.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        try
        {
            await _transport.ConnectAsync(connection, linked.Token).ConfigureAwait(false);
            byte[] handshake = JsonSerializer.SerializeToUtf8Bytes(new { type = 3, slaveInit = new { masterStreamingId = connection.StreamId, receiveImage = true } });
            await _transport.SendTextAsync(handshake, linked.Token).ConfigureAwait(false);
            _connected = true;
        }
        catch { await CloseAsync().ConfigureAwait(false); throw; }
    }
    public async Task<RealtimeStreamMessage> ReadNextAsync(CancellationToken ct = default)
    {
        if (!_connected) throw new InvalidOperationException("Stream is not connected.");
        if (!await _reader.WaitAsync(0, ct).ConfigureAwait(false)) throw new InvalidOperationException("Only one stream reader is supported.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        try
        {
            byte[] chunk = new byte[8192];
            using var message = new MemoryStream();
            WebSocketMessageType? type = null;
            while (true)
            {
                var received = await _transport.ReceiveAsync(chunk, linked.Token).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await CloseAsync().ConfigureAwait(false);
                    return new RealtimeStreamClosed();
                }
                type ??= received.MessageType;
                if (received.Count < 0 || received.Count > chunk.Length || received.MessageType != type || type is not (WebSocketMessageType.Text or WebSocketMessageType.Binary)) throw InvalidMessage();
                int limit = type == WebSocketMessageType.Text ? MaximumStatusMessageBytes : _maximumImageMessageBytes;
                if (message.Length + received.Count > limit) throw new ProviderException("stream_message_too_large", "Realtime message exceeds the configured wire-message byte capacity.");
                message.Write(chunk, 0, received.Count);
                if (!received.EndOfMessage) continue;
                byte[] bytes = message.ToArray();
                if (type == WebSocketMessageType.Binary)
                {
                    if (bytes.Length < 4 || bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9) throw InvalidMessage();
                    return new RealtimeJpegFrame(bytes);
                }
                return ParseStatus(bytes);
            }
        }
        catch { await CloseAsync().ConfigureAwait(false); throw; }
        finally { _reader.Release(); }
    }
    private static RealtimeStatusMessage ParseStatus(byte[] bytes)
    {
        try
        {
            // JsonDocument rejects invalid UTF-8, malformed text and non-JSON protocol messages.
            using var json = JsonDocument.Parse(bytes);
            if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("code", out var value) || value.ValueKind != JsonValueKind.String) throw InvalidMessage();
            return new(value.GetString() switch
            {
                "0" => RealtimeStatusCode.Initialized,
                "1000" => RealtimeStatusCode.NodeOverloaded,
                "1001" => RealtimeStatusCode.InitializationFailed,
                "1003" => RealtimeStatusCode.SlaveLimitExceeded,
                "2001" => RealtimeStatusCode.RenderError,
                "2002" => RealtimeStatusCode.EngineError,
                _ => RealtimeStatusCode.Unknown
            });
        }
        catch (JsonException) { throw InvalidMessage(); }
    }
    private static ProviderException InvalidMessage() => new("invalid_stream_message", "Realtime stream returned an unsupported or malformed message.");
    // Close before deleting the REST session. This closes the socket only; the caller retains the
    // operation/session ID and is responsible for CloseRealtimeAsync even after stream failure.
    public Task CloseAsync(CancellationToken ct = default)
    {
        Task task;
        lock (_closeGate) task = _closing ??= CloseCoreAsync();
        return ct.CanBeCanceled ? task.WaitAsync(ct) : task;
    }
    private async Task CloseCoreAsync()
    {
        _connected = false;
        _lifetime.Cancel();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await _transport.CloseAsync(timeout.Token).ConfigureAwait(false); }
        finally { _transport.Abort(); await _transport.DisposeAsync().ConfigureAwait(false); }
    }
    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);
}
