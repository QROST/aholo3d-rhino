using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aholo.Provider;

/// <summary>Region-bound Aholo REST API. Creates are submitted once; API keys never go to OUS or a CDN.</summary>
public sealed partial class AholoApiClient
{
    private readonly HttpClient _apiHttp;
    private readonly HttpClient _uploadHttp;
    public Func<CancellationToken, Task<IDisposable>>? RequestAdmission { get; set; }
    private const int JsonLimit = 2 * 1024 * 1024;
    public AholoApiClient(HttpClient apiHttp, HttpClient uploadHttp)
    {
        _apiHttp = apiHttp;
        _uploadHttp = uploadHttp;
        if (uploadHttp.DefaultRequestHeaders.Contains("Authorization")) throw new ArgumentException("The upload client must not include API authorization.", nameof(uploadHttp));
    }
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(90) };

    public async Task ValidateKeyAsync(ApiKeyCredential key, CancellationToken ct = default) => _ = await ListWorldsAsync(key, 0, ct).ConfigureAwait(false);

    public async Task<string> GenerateWorldAsync(ApiKeyCredential key, string name, string prompt, string? imageUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt) && string.IsNullOrWhiteSpace(imageUrl)) throw new ArgumentException("Enter a world description or choose a reference image.", nameof(prompt));
        var body = new Dictionary<string, object> { ["name"] = name.Trim() };
        if (!string.IsNullOrWhiteSpace(prompt)) body["prompt"] = prompt.Trim();
        if (!string.IsNullOrWhiteSpace(imageUrl)) body["resources"] = new[] { new { url = HttpsUrl(imageUrl), type = "image" } };
        using var request = Gateway(key, HttpMethod.Post, "world/v1/generations", body);
        return await CreateAsync(request, root => RequiredIdentifier(root, "worldId"), ct).ConfigureAwait(false);
    }

    public async Task<WorldResult> GetWorldAsync(ApiKeyCredential key, string worldId, CancellationToken ct = default)
    {
        worldId = Identifier(worldId);
        using var request = Gateway(key, HttpMethod.Get, "world/v1/" + Uri.EscapeDataString(worldId));
        var world = ParseWorld(await SendAsync(_apiHttp, request, ct).ConfigureAwait(false));
        if (world.WorldId != worldId) throw InvalidResponse();
        return world;
    }

    public async Task<IReadOnlyList<WorldResult>> ListWorldsAsync(ApiKeyCredential key, int page, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        using var request = Gateway(key, HttpMethod.Post, "world/v1/list", new { pageNum = page, pageSize = 20 });
        var root = await SendAsync(_apiHttp, request, ct).ConfigureAwait(false);
        if (!root.TryGetProperty("result", out var rows) || rows.ValueKind != JsonValueKind.Array) throw InvalidResponse();
        return rows.EnumerateArray().Select(ParseWorld).ToArray();
    }

    public Task<string> CreateMeshAsync(ApiKeyCredential key, string url, CancellationToken ct = default) => CreateMeshAsync(key, url, true, ct);

    public async Task<string> CreateMeshAsync(ApiKeyCredential key, string url, bool fastUpload, CancellationToken ct = default)
    {
        using var request = Gateway(key, HttpMethod.Post, "rendercloud/v1/mesh-upload-process/task/create", new { originMeshUrl = HttpsUrl(url), fastUpload });
        return await CreateAsync(request, root => NumericId(MeshData(root)), ct).ConfigureAwait(false);
    }

    public async Task<MeshResult> GetMeshAsync(ApiKeyCredential key, string taskId, CancellationToken ct = default)
    {
        taskId = NumericId(taskId);
        using var request = Gateway(key, HttpMethod.Get, "rendercloud/v1/mesh-upload-process/task/get?taskid=" + taskId);
        var data = MeshData(await SendAsync(_apiHttp, request, ct).ConfigureAwait(false));
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("taskId", out var id) || NumericId(id) != taskId || !data.TryGetProperty("status", out var status) || !status.TryGetInt32(out var state)) throw InvalidResponse();
        string? meshId = null; string[] meshIds = Array.Empty<string>();
        if (state == 3 && data.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array)
        {
            var matches = outputs.EnumerateArray().Select(item => OptionalString(item, "content")).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            meshIds = matches.Select(x => Identifier(x!)).ToArray();
            if (meshIds.Length == 1) meshId = meshIds[0];
        }
        return new(taskId, state, meshId) { MeshIds = meshIds };
    }

    public async Task<RenderOperation> SubmitRenderAsync(ApiKeyCredential key, string requestId, string usda, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(usda)) throw new ArgumentException("The render scene cannot be empty.", nameof(usda));
        using var request = Gateway(key, HttpMethod.Post, "rendercloud/v1/jobs", new { requestId = Identifier(requestId), usdContent = Convert.ToBase64String(Encoding.UTF8.GetBytes(usda)) });
        return await CreateAsync(request, ParseRender, ct).ConfigureAwait(false);
    }

    public async Task<RenderOperation> GetRenderAsync(ApiKeyCredential key, string operationId, CancellationToken ct = default)
    {
        operationId = Identifier(operationId);
        using var request = Gateway(key, HttpMethod.Post, "rendercloud/v1/jobs", new { operationId });
        var result = ParseRender(await SendAsync(_apiHttp, request, ct).ConfigureAwait(false));
        if (result.OperationId != operationId) throw InvalidResponse();
        return result;
    }

    private async Task<JsonElement> SendOusAsync(Uri host, string token, string path, HttpMethod method, HttpContent? content, CancellationToken ct)
    {
        if (_uploadHttp.DefaultRequestHeaders.Contains("Authorization")) throw new InvalidOperationException("The upload client must not include API authorization.");
        using var request = new HttpRequestMessage(method, new Uri(host, "ous/api/v2/" + path));
        request.Headers.Add("ous-token-v2", token);
        request.Content = content;
        var root = await SendAsync(_uploadHttp, request, ct).ConfigureAwait(false);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("c", out var code) || !root.TryGetProperty("d", out var data)) throw InvalidResponse();
        if (Scalar(code) != "0") throw new ProviderException("upload_rejected", "The upload service rejected the request.");
        return data;
    }

    private static HttpRequestMessage Gateway(ApiKeyCredential key, HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(Aholo.Core.AholoRegions.Get(key.Region).ApiBase, path));
        request.Headers.Add("Authorization", key.Value);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<T> CreateAsync<T>(HttpRequestMessage request, Func<JsonElement, T> parse, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) throw new RequestNotSentCanceledException(ct);
        try { return parse(await SendAsync(_apiHttp, request, ct, isCreate: true).ConfigureAwait(false)); }
        catch (ProviderException ex) when (ex.Code == "invalid_response") { throw UnknownSubmission(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException) { throw UnknownSubmission(); }
    }

    private async Task<JsonElement> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken ct, bool isCreate = false)
    {
        IDisposable? admission;
        try { admission = RequestAdmission is null ? null : await RequestAdmission(ct).ConfigureAwait(false); }
        catch (Exception ex) when (isCreate && ex is IOException or OperationCanceledException) { throw NotSent(); }
        try
        {
            HttpResponseMessage admitted;
            using (admission)
            {
                if (isCreate && ct.IsCancellationRequested) throw new RequestNotSentCanceledException(ct);
                admitted = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            }
            using var response = admitted;
            if (!response.IsSuccessStatusCode)
            {
                string code = response.StatusCode switch { HttpStatusCode.Unauthorized => "unauthenticated", HttpStatusCode.Forbidden => "permission_denied", HttpStatusCode.TooManyRequests => "rate_limited", _ => "http_error" };
                throw new ProviderException(code, code == "unauthenticated" ? "The API Key is invalid or expired for the selected region." : string.Format(System.Globalization.CultureInfo.CurrentCulture, "Service request failed (HTTP {0}).", (int)response.StatusCode), isCreate && ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout));
            }
            if (response.Content.Headers.ContentLength > JsonLimit) throw new JsonException();
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > JsonLimit) throw new JsonException();
                buffer.Write(chunk, 0, count);
            }
            using var document = JsonDocument.Parse(buffer.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            return document.RootElement.Clone();
        }
        catch (Exception ex) when (ex is not RequestNotSentCanceledException && ex is HttpRequestException or JsonException or OperationCanceledException or IOException)
        {
            if (isCreate) throw UnknownSubmission();
            if (ex is OperationCanceledException) throw;
            throw new ProviderException("request_failed", "The service request failed or its response format is invalid.");
        }
    }

    private static ProviderException NotSent() => new("request_not_sent", "The request stopped before it was sent. Restore the connection and explicitly continue the original task.");

    private static WorldResult ParseWorld(JsonElement root)
    {
        string id = RequiredIdentifier(root, "worldId");
        string status = RequiredString(root, "status");
        double? progress = root.TryGetProperty("progress", out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) ? number : null;
        string? up = null, ply = null, spz = null, image = null, lod = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Object)
        {
            if (assets.TryGetProperty("semanticsMetadata", out var semantics)) up = OptionalString(semantics, "upAxis");
            if (assets.TryGetProperty("imagery", out var imagery)) image = OptionalString(imagery, "panoUrl");
            if (assets.TryGetProperty("splats", out var splats) && splats.ValueKind == JsonValueKind.Object && splats.TryGetProperty("urls", out var urls))
            { ply = OptionalString(urls, "plyPath"); spz = OptionalString(urls, "spzPath"); lod = OptionalString(urls, "lodMetaPath"); }
        }
        return new(id, OptionalString(root, "name") ?? id, status, progress, up, ply, spz, image)
        { CoverUrl = OptionalString(root, "cover"), Scene = OptionalString(root, "scene"), LodMetaUrl = lod,
          Created = OptionalLong(root, "createTime"), Updated = OptionalLong(root, "updateTime") };
    }

    private static RenderOperation ParseRender(JsonElement root)
    {
        string id = RequiredIdentifier(root, "operationId");
        if (!root.TryGetProperty("done", out var done) || done.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidResponse();
        string? resultUrl = null, error = null;
        if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object) resultUrl = OptionalString(result, "resultUrl");
        if (root.TryGetProperty("error", out var errorValue) && errorValue.ValueKind != JsonValueKind.Null)
            error = "The render service reported failure. Check the task on the official platform."; // Do not reflect provider text or credentials.
        return new(id, done.GetBoolean(), resultUrl, error);
    }

    private static JsonElement MeshData(JsonElement root)
    {
        if (!root.TryGetProperty("success", out var success) || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw InvalidResponse();
        if (!success.GetBoolean()) throw new ProviderException("business_rejected", "The service rejected the task. Check account permissions, balance and parameters.");
        if (!root.TryGetProperty("data", out var data)) throw InvalidResponse();
        return data;
    }
    private static string NumericId(JsonElement value) => NumericId(Scalar(value) ?? "");
    private static string NumericId(string value)
    {
        if (string.IsNullOrEmpty(value) || !value.All(char.IsAsciiDigit) || !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) throw InvalidResponse();
        return id.ToString(CultureInfo.InvariantCulture);
    }
    private static string RequiredIdentifier(JsonElement value, string property) => Identifier(RequiredString(value, property));
    private static string Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Any(char.IsControl) || value.Any(char.IsWhiteSpace)) throw InvalidResponse();
        return value;
    }
    private static string HttpsUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.IsLoopback || Uri.CheckHostName(uri.Host) != UriHostNameType.Dns)
            throw new ProviderException("invalid_url", "Service assets must use a valid HTTPS address.");
        return uri.AbsoluteUri;
    }
    private static string RequiredString(JsonElement value, string property) => OptionalString(value, property) is { Length: > 0 } text ? text : throw InvalidResponse();
    private static string? OptionalString(JsonElement value, string property) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static string? Scalar(JsonElement value) => value.ValueKind switch { JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetRawText(), _ => null };
    private static ProviderException InvalidResponse() => new("invalid_response", "The service response is missing required fields or its task identity does not match.");
    private static ProviderException UnknownSubmission() => new("submission_unknown", "The task may have been created, but submission is unconfirmed. Check task history before submitting again.", true);

    private sealed class SnapshotContent(Stream source, long offset, long length) : HttpContent
    {
        protected override bool TryComputeLength(out long contentLength) { contentLength = length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => CopyAsync(stream, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => CopyAsync(stream, cancellationToken);
        private async Task CopyAsync(Stream target, CancellationToken ct)
        {
            source.Position = offset;
            byte[] buffer = new byte[65536];
            long remaining = length;
            while (remaining > 0)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException();
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                remaining -= read;
            }
        }
    }
}
