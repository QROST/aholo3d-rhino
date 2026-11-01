using System.Text;
using System.Text.Json;

namespace Aholo.Provider;

public sealed partial class AholoApiClient
{
    public async Task<RealtimeOperation> CreateRealtimeAsync(ApiKeyCredential key, string requestId, string usda,
        bool autoEnhanceAI = false, CancellationToken ct = default)
    {
        var body = new { requestId = Identifier(requestId), usdContent = RealtimeContent(usda), settings = new { autoEnhanceAI } };
        using var request = Gateway(key, HttpMethod.Post, "rendercloud/v1/streams", body);
        return await CreateAsync(request, ParseRealtimeOperation, ct).ConfigureAwait(false);
    }

    public async Task<RealtimeOperation> GetRealtimeAsync(ApiKeyCredential key, string operationId, CancellationToken ct = default)
    {
        operationId = RealtimeSessionIdentifier(operationId);
        using var request = Gateway(key, HttpMethod.Post, "rendercloud/v1/streams", new { operationId });
        var result = ParseRealtimeOperation(await SendAsync(_apiHttp, request, ct).ConfigureAwait(false));
        if (result.OperationId != operationId) throw InvalidResponse();
        return result;
    }

    public async Task<RealtimeUpdateResult> PushRealtimeAsync(ApiKeyCredential key, string sessionId, string requestId,
        string patchUsda, CancellationToken ct = default)
    {
        sessionId = RealtimeSessionIdentifier(sessionId);
        using var request = Gateway(key, HttpMethod.Post, "rendercloud/v1/streams/" + Uri.EscapeDataString(sessionId) + ":push",
            new { requestId = Identifier(requestId), usdContent = RealtimeContent(patchUsda) });
        // An interrupted push may already have been applied. Never automatically resend or reorder it.
        return await CreateAsync(request, root =>
        {
            var result = new RealtimeUpdateResult(RequiredIdentifier(root, "sessionId"), ParseRealtimeStatus(RequiredString(root, "status")));
            if (result.SessionId != sessionId || result.Status != RealtimeSessionStatus.Active) throw InvalidResponse();
            return result;
        }, ct).ConfigureAwait(false);
    }

    public async Task<RealtimeCloseResult> CloseRealtimeAsync(ApiKeyCredential key, string sessionId, CancellationToken ct = default)
    {
        sessionId = RealtimeSessionIdentifier(sessionId);
        using var request = Gateway(key, HttpMethod.Delete, "rendercloud/v1/streams/" + Uri.EscapeDataString(sessionId));
        var root = await SendAsync(_apiHttp, request, ct).ConfigureAwait(false);
        if (RequiredIdentifier(root, "sessionId") != sessionId || !root.TryGetProperty("closed", out var closed) || closed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidResponse();
        return new(sessionId, closed.GetBoolean());
    }

    private static string RealtimeSessionIdentifier(string value)
    {
        value = Identifier(value);
        if (value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw InvalidResponse();
        return value;
    }

    private static string RealtimeContent(string usda) => !string.IsNullOrWhiteSpace(usda)
        ? Convert.ToBase64String(Encoding.UTF8.GetBytes(usda)) : throw new ArgumentException("Realtime USDA content cannot be empty.", nameof(usda));

    private static RealtimeOperation ParseRealtimeOperation(JsonElement root)
    {
        string id = RealtimeSessionIdentifier(RequiredIdentifier(root, "operationId"));
        if (!root.TryGetProperty("done", out var done) || done.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !root.TryGetProperty("result", out var result) || !root.TryGetProperty("error", out var error)) throw InvalidResponse();
        if (!done.GetBoolean())
        {
            if (result.ValueKind != JsonValueKind.Null || error.ValueKind != JsonValueKind.Null) throw InvalidResponse();
            return new(id, false, RealtimeSessionStatus.Initializing, null, null, null);
        }
        if (error.ValueKind == JsonValueKind.Object)
        {
            if (result.ValueKind != JsonValueKind.Null) throw InvalidResponse();
            return new(id, true, RealtimeSessionStatus.Failed, null, null, "Realtime service reported failure. Check the session on the official platform.");
        }
        if (error.ValueKind != JsonValueKind.Null || result.ValueKind != JsonValueKind.Object || RequiredIdentifier(result, "sessionId") != id) throw InvalidResponse();
        var state = ParseRealtimeStatus(RequiredString(result, "status"));
        if (state == RealtimeSessionStatus.Initializing) throw InvalidResponse();
        int? queue = null;
        if (result.TryGetProperty("currentQueuePosition", out var position))
        {
            if (position.ValueKind != JsonValueKind.Number || !position.TryGetInt32(out int number) || number < 0) throw InvalidResponse();
            queue = number;
        }
        RealtimeConnection? connection = null;
        if (state == RealtimeSessionStatus.Active)
        {
            try { connection = new(RequiredString(result, "host"), RequiredString(result, "nodeId"), RequiredString(result, "streamId"), RequiredString(result, "token")); }
            catch (ArgumentException) { throw InvalidResponse(); }
        }
        return new(id, true, state, queue, connection, state == RealtimeSessionStatus.Failed ? "Realtime session failed." : null);
    }

    private static RealtimeSessionStatus ParseRealtimeStatus(string value) => value switch
    {
        "INITIALIZING" => RealtimeSessionStatus.Initializing,
        "ACTIVE" => RealtimeSessionStatus.Active,
        "FAILED" => RealtimeSessionStatus.Failed,
        "CLOSED" => RealtimeSessionStatus.Closed,
        _ => throw InvalidResponse()
    };
}
