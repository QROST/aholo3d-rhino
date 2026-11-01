using System.Text.Json.Serialization;

namespace Aholo.Provider;

public enum RealtimeSessionStatus { Initializing, Active, Failed, Closed }

/// <summary>Ephemeral stream credentials. Serialization and diagnostic formatting omit the token.</summary>
public sealed class RealtimeConnection
{
    public string Host { get; }
    public string NodeId { get; }
    public string StreamId { get; }
    private readonly string _token;
    public RealtimeConnection(string host, string nodeId, string streamId, string token)
    {
        Host = RealtimeNetwork.ValidateHost(host);
        NodeId = ValidateValue(nodeId);
        StreamId = ValidateValue(streamId);
        _token = ValidateValue(token);
    }
    // Deliberately a method, never a serializable property. Call only at the transport boundary.
    public Uri CreateWebSocketUri() => new("wss://" + Host + "/streaming/ws/session?nid=" + Uri.EscapeDataString(NodeId) + "&token=" + Uri.EscapeDataString(_token));
    public override string ToString() => "Aholo realtime connection (credentials redacted)";
    private static string ValidateValue(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 8192 && !value.Any(char.IsControl)
        ? value : throw new ArgumentException("Invalid realtime connection credentials.");
}

public sealed record RealtimeOperation(string OperationId, bool Done, RealtimeSessionStatus? Status,
    int? QueuePosition, RealtimeConnection? Connection, string? Error);
public sealed record RealtimeUpdateResult(string SessionId, RealtimeSessionStatus Status);
public sealed record RealtimeCloseResult(string SessionId, bool Closed);

/// <summary>Only published capabilities; absence here is not a claim about private server features.</summary>
public static class RealtimeCapabilities
{
    public const string ImageFormat = "JPEG";
    public const bool HasDepth = false;
    public const bool HasAlpha = false;
    public const bool HasObjectIds = false;
    public const bool HasCameraFrameAssociation = false;
    public const bool HasPointCloudPrim = false;
    public const bool HasMeshPrim = true;
    public const bool HasGaussianSplatPrim = true;
}

public enum RealtimeStatusCode { Initialized, NodeOverloaded, InitializationFailed, SlaveLimitExceeded, RenderError, EngineError, Unknown }
public abstract record RealtimeStreamMessage;
public sealed record RealtimeStatusMessage(RealtimeStatusCode Code) : RealtimeStreamMessage;
public sealed record RealtimeJpegFrame(byte[] Bytes) : RealtimeStreamMessage
{
    public bool HasCameraAssociation => false;
}
public sealed record RealtimeStreamClosed : RealtimeStreamMessage;
