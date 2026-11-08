using System.Text;
using System.Text.Json;
using Aholo.Core;
using Aholo.Provider;
using Aholo.Sidecar;

Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
var store = new TaskStore(Environment.GetEnvironmentVariable("AHOLO_RHINO_STATE_DIR") ?? TaskStore.DefaultRoot);
using var apiHttp = AholoApiClient.CreateHttpClient();
using var publicHttp = new HttpClient(new SocketsHttpHandler
{
    AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
    ConnectCallback = PublicNetworkConnector.ConnectAsync
}) { Timeout = TimeSpan.FromMinutes(15) };
var api = new AholoApiClient(apiHttp, publicHttp);
var workflow = new Workflows(api, store, new AssetDownloader(publicHttp, store));
ICredentialStore credentials = CredentialStoreFactory.CreateDefault();
ApiKeyCredential? key = null;
bool loaded = false, remembered = false;
const string Account = "international-api-key";
while (true)
{
    string? line;
    try { line = await BoundedLine.ReadAsync(Console.In, CancellationToken.None, 128 * 1024); }
    catch (IOException) { break; }
    if (line is null) break;
    Guid id = Guid.Empty;
    RpcResponse response;
    try
    {
        var request = JsonSerializer.Deserialize<RpcRequest>(line, AholoJson.Options) ?? throw new ArgumentException("Invalid request.");
        id = request.Id;
        if (request.Version != 1 || id == Guid.Empty) throw new ArgumentException("Background protocol version mismatch.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var ct = deadline.Token;
        var payload = request.Payload;
        object result;
        switch (request.Method)
        {
            case "connection":
                await LoadKeyAsync(ct); result = State(); break;
            case "connect":
                var input = payload.Deserialize<ConnectRequest>(AholoJson.Options) ?? throw new ArgumentException("Invalid connection request.");
                var candidate = new ApiKeyCredential(input.ApiKey);
                await api.ValidateKeyAsync(candidate, ct);
                if (input.Remember) await credentials.SetAsync(Account, input.ApiKey, ct);
                else await credentials.DeleteAsync(Account, ct);
                key = candidate; loaded = true; remembered = input.Remember; result = State(); break;
            case "disconnect":
                await credentials.DeleteAsync(Account, ct); key = null; loaded = true; remembered = false; result = State(); break;
            case "list": result = store.List(); break;
            case "page": result = store.Page(Read<TaskPageRequest>(payload)); break;
            case "continueRender": result = await workflow.ContinueRenderAsync(await RequireKeyAsync(ct), payload.GetProperty("id").GetGuid(), ct); break;
            case "generate": result = await workflow.GenerateAsync(await RequireKeyAsync(ct), Read<GenerateWorldRequest>(payload), ct); break;
            case "render": result = await workflow.RenderAsync(await RequireKeyAsync(ct), Read<RenderRequest>(payload), ct); break;
            case "refresh": result = await workflow.RefreshAsync(await RequireKeyAsync(ct), payload.GetProperty("id").GetGuid(), ct); break;
            case "download": result = await workflow.DownloadAsync(await RequireKeyAsync(ct), Read<DownloadRequest>(payload), ct); break;
            case "importWorld": result = await workflow.ImportWorldAsync(await RequireKeyAsync(ct), payload.GetProperty("worldId").GetString() ?? "", ct); break;
            default: throw new ArgumentException("Unknown background operation.");
        }
        response = new RpcResponse(1, id, AholoJson.Element(result), null);
    }
    catch (Exception error)
    {
        string message = error switch
        {
            ProviderException => error.Message,
            ArgumentException => "Invalid input. Check the file, parameters or API Key.",
            InvalidOperationException => error.Message,
            OperationCanceledException => "Operation timed out. Task history is retained; do not resubmit tasks with unknown results.",
            IOException => "File or background operation failed. Check local disk space, permissions or activity in other Aholo panels.",
            _ => "Background operation failed. Task history is retained; reopen the panel and retry the query."
        };
        response = new RpcResponse(1, id, null, message);
    }
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, AholoJson.Options));
    await Console.Out.FlushAsync();
}
return;

T Read<T>(JsonElement payload) => payload.Deserialize<T>(AholoJson.Options) ?? throw new ArgumentException("The request is missing parameters.");
ConnectionState State() => new(key is not null, key?.ConnectionId, remembered);
async Task LoadKeyAsync(CancellationToken ct)
{
    if (loaded) return;
    string? stored = await credentials.GetAsync(Account, ct);
    if (!string.IsNullOrWhiteSpace(stored)) { key = new ApiKeyCredential(stored); remembered = true; }
    loaded = true;
}
async Task<ApiKeyCredential> RequireKeyAsync(CancellationToken ct)
{
    await LoadKeyAsync(ct); return key ?? throw new InvalidOperationException("Connect an international API Key first.");
}
