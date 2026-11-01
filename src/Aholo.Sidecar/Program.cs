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
        var request = JsonSerializer.Deserialize<RpcRequest>(line, AholoJson.Options) ?? throw new ArgumentException("无效请求。");
        id = request.Id;
        if (request.Version != 1 || id == Guid.Empty) throw new ArgumentException("后台协议版本不匹配。");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var ct = deadline.Token;
        var payload = request.Payload;
        object result;
        switch (request.Method)
        {
            case "connection":
                await LoadKeyAsync(ct); result = State(); break;
            case "connect":
                var input = payload.Deserialize<ConnectRequest>(AholoJson.Options) ?? throw new ArgumentException("无效连接请求。");
                var candidate = new ApiKeyCredential(input.ApiKey);
                await api.ValidateKeyAsync(candidate, ct);
                if (input.Remember) await credentials.SetAsync(Account, input.ApiKey, ct);
                else await credentials.DeleteAsync(Account, ct);
                key = candidate; loaded = true; remembered = input.Remember; result = State(); break;
            case "disconnect":
                await credentials.DeleteAsync(Account, ct); key = null; loaded = true; remembered = false; result = State(); break;
            case "list": result = store.List(); break;
            case "generate": result = await workflow.GenerateAsync(await RequireKeyAsync(ct), Read<GenerateWorldRequest>(payload), ct); break;
            case "render": result = await workflow.RenderAsync(await RequireKeyAsync(ct), Read<RenderRequest>(payload), ct); break;
            case "refresh": result = await workflow.RefreshAsync(await RequireKeyAsync(ct), payload.GetProperty("id").GetGuid(), ct); break;
            case "download": result = await workflow.DownloadAsync(await RequireKeyAsync(ct), Read<DownloadRequest>(payload), ct); break;
            case "importWorld": result = await workflow.ImportWorldAsync(await RequireKeyAsync(ct), payload.GetProperty("worldId").GetString() ?? "", ct); break;
            default: throw new ArgumentException("未知后台操作。");
        }
        response = new RpcResponse(1, id, AholoJson.Element(result), null);
    }
    catch (Exception error)
    {
        string message = error switch
        {
            ProviderException => error.Message,
            ArgumentException => "输入无效，请检查文件、参数或 API Key。",
            InvalidOperationException => error.Message,
            OperationCanceledException => "操作等待超时。任务历史已保留；请勿重复提交结果未知的任务。",
            IOException => "文件或后台操作失败。请检查本地空间、文件权限，或其他 Aholo 面板是否正在操作。",
            _ => "后台操作失败。任务历史已保留；可重新打开面板重试查询。"
        };
        response = new RpcResponse(1, id, null, message);
    }
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, AholoJson.Options));
    await Console.Out.FlushAsync();
}
return;

T Read<T>(JsonElement payload) => payload.Deserialize<T>(AholoJson.Options) ?? throw new ArgumentException("请求缺少参数。");
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
    await LoadKeyAsync(ct); return key ?? throw new InvalidOperationException("请先连接国际站 API Key。");
}
