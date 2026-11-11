using System.Text;
using System.Diagnostics;
using System.Text.Json;
using Aholo.Core;
using Aholo.Provider;
using Aholo.Sidecar;

UiText.Initialize(null,Environment.GetEnvironmentVariable("AHOLO_UI_LANGUAGE") ?? "en");
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
var credentials = new CredentialCoordinator(CredentialStoreFactory.CreateDefault());
var realtime=new RealtimePreviewManager(credentials,new RealtimeJournal(store.Root),(key,epoch)=>
    new RealtimePreviewBackend(new AholoApiClient(apiHttp,publicHttp){RequestAdmission=token=>credentials.AcquireRequestAsync(epoch,token)},key));
RealtimeControlServer? control=null;
string? pipeName=Environment.GetEnvironmentVariable("AHOLO_REALTIME_PIPE");
string? pipeNonce=Environment.GetEnvironmentVariable("AHOLO_REALTIME_NONCE");
Environment.SetEnvironmentVariable("AHOLO_REALTIME_NONCE",null);
Environment.SetEnvironmentVariable("AHOLO_REALTIME_PIPE",null);
CredentialSnapshot? connection = null;
string? requestEpoch = null;
using var parentLost=new CancellationTokenSource();
using var oauthHttp=AholoApiClient.CreateHttpClient();
using var accounts=new RegionalAccounts(region=>new AccountSessionManager(new ManycoreOAuthService(oauthHttp,region),uri=>
{ using var browser=Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true}); },parentLost.Token,new OAuthSessionCoordinator(CredentialStoreFactory.CreateDefault(),region:region)));
if(pipeName is not null&&pipeNonce is not null)
{control=new(pipeName,pipeNonce,realtime,(region,id,ct)=>accounts.RunAsync(region,"cancel",id,null,ct));control.Start();}
using var parentLifetime=int.TryParse(Environment.GetEnvironmentVariable("AHOLO_PARENT_PID"),out int parentId)&&parentId>0?new ParentProcessLifetime(parentId,parentLost):null;
try
{
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
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(parentLost.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        var ct = deadline.Token;
        var payload = request.Payload;
        object result;
        requestEpoch = request.CredentialEpoch;
        bool authenticated = request.Method is "continueRender" or "generate" or "createService" or "render" or "refresh" or "download" or "importWorld" or "cloudHistory" or "restoreRemote";
        string? admittedEpoch=request.CredentialEpoch;
        api.RequestAdmission = authenticated ? token => credentials.AcquireRequestAsync(admittedEpoch, token) : null;
        switch (request.Method)
        {
            case "account":
                string action = payload.GetProperty("action").GetString() ?? "";
                Guid? attemptId = payload.TryGetProperty("attemptId", out var attempt) && attempt.ValueKind == JsonValueKind.String ? attempt.GetGuid() : null;
                bool? rememberAccount=payload.TryGetProperty("remember",out var savedAccount)&&savedAccount.ValueKind is JsonValueKind.True or JsonValueKind.False?savedAccount.GetBoolean():null;
                string accountRegion=payload.TryGetProperty("region",out var selectedAccountRegion)?selectedAccountRegion.GetString()??"":AholoRegions.International;
                result=await accounts.RunAsync(accountRegion,action,attemptId,rememberAccount,ct);break;
            case "connection":
                connection = await credentials.ReadAsync(ct); result = State(); break;
            case "connect":
                var input = payload.Deserialize<ConnectRequest>(AholoJson.Options) ?? throw new ArgumentException("Invalid connection request.");
                var candidate = new ApiKeyCredential(input.ApiKey,input.Region);
                string mutationStamp=await credentials.MutationStampAsync(ct);
                switch(input.ValidationService)
                {
                    case "world": await api.ValidateKeyAsync(candidate, ct); break;
                    case "lux": await api.ListLuxTasksAsync(candidate, pageSize: 1, ct: ct); break;
                    case "spatial": await api.ListSpatialTasksAsync(candidate, pageSize: 1, ct: ct); break;
                    default: throw new ArgumentException("Choose a service to validate.");
                }
                using(await realtime.BeforeCredentialChangeAsync(ct))
                {await accounts.CancelPendingAsync(ct);connection = await credentials.ConnectAsync(candidate, input.Remember, ct,mutationStamp);}
                result = State(); break;
            case "disconnect":
                using(await realtime.BeforeCredentialChangeAsync(ct))connection = await credentials.DisconnectAsync(ct,requestEpoch);
                result = State(); break;
            case "switchRegion":
                using(await realtime.BeforeCredentialChangeAsync(ct))
                {await accounts.CancelPendingAsync(ct);connection=await credentials.SwitchRegionAsync(payload.GetProperty("region").GetString()??"",ct,requestEpoch);}
                result=State();break;
            case "list": result = store.List(); break;
            case "page": result = store.Page(Read<TaskPageRequest>(payload)); break;
            case "continueRender": result = await workflow.ContinueRenderAsync(await RequireKeyAsync(ct), payload.GetProperty("id").GetGuid(), ct); break;
            case "generate": result = await workflow.GenerateAsync(await RequireKeyAsync(ct), Read<GenerateWorldRequest>(payload), ct); break;
            case "createService": result = await workflow.CreateServiceAsync(await RequireKeyAsync(ct), Read<CreateServiceRequest>(payload), ct); break;
            case "render": result = await workflow.RenderAsync(await RequireKeyAsync(ct), Read<RenderRequest>(payload), ct); break;
            case "refresh": result = await workflow.RefreshAsync(await RequireKeyAsync(ct), payload.GetProperty("id").GetGuid(), ct); break;
            case "download": result = await workflow.DownloadAsync(await RequireKeyAsync(ct), Read<DownloadRequest>(payload), ct); break;
            case "cloudHistory": result = await workflow.CloudHistoryAsync(await RequireKeyAsync(ct), Read<CloudHistoryRequest>(payload), ct); break;
            case "restoreRemote": result = await workflow.RestoreRemoteAsync(await RequireKeyAsync(ct), Read<RestoreRemoteRequest>(payload), ct); break;
            case "importWorld": result = await workflow.ImportWorldAsync(await RequireKeyAsync(ct), payload.GetProperty("worldId").GetString() ?? "", ct); break;
            default: throw new ArgumentException("Unknown background operation.");
        }
        // A workflow may have persisted a provider failure. Still signal credential invalidation to the shared host session.
        if (authenticated) { using var admission = await credentials.AcquireRequestAsync(requestEpoch, ct); }
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
        response = new RpcResponse(1, id, null, message, (error as ProviderException)?.Code);
    }
    await Console.Out.WriteLineAsync(JsonSerializer.Serialize(response, AholoJson.Options));
    await Console.Out.FlushAsync();
}
}
finally
{
try{if(control is not null)await control.DisposeAsync();}catch{ /* Always proceed to bounded server cleanup. */ }
using(var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(12)))
    try{await realtime.ShutdownAsync(cleanup.Token);}catch{ /* Durable records retain every unresolved session. */ }
}
return;

T Read<T>(JsonElement payload) => payload.Deserialize<T>(AholoJson.Options) ?? throw new ArgumentException("The request is missing parameters.");
ConnectionState State() => new(connection?.Key is not null, connection?.Key?.ConnectionId, connection?.Remembered == true, connection?.Epoch,connection?.Region??AholoRegions.International);
Task<ApiKeyCredential> RequireKeyAsync(CancellationToken ct) => credentials.RequireAsync(requestEpoch, ct);
