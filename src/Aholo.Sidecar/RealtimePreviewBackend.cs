using Aholo.Provider;
namespace Aholo.Sidecar;

public interface IRealtimePreviewBackend
{
    Task<RealtimeOperation> CreateAsync(string requestId,string usda,CancellationToken ct);
    Task<RealtimeOperation> QueryAsync(string sessionId,CancellationToken ct);
    Task ConnectAsync(RealtimeOperation operation,CancellationToken ct);
    Task<RealtimeStreamMessage> ReadAsync(CancellationToken ct);
    Task CameraAsync(string patch,CancellationToken ct);
    Task SceneAsync(string patch,CancellationToken ct);
    Task CloseAsync(string sessionId,bool explicitRetry);
}

public sealed class RealtimePreviewBackend(AholoApiClient api,ApiKeyCredential key) : IRealtimePreviewBackend
{
    private RealtimeSession? session;
    public Task<RealtimeOperation> CreateAsync(string requestId,string usda,CancellationToken ct)=>api.CreateRealtimeAsync(key,requestId,usda,ct:ct);
    public Task<RealtimeOperation> QueryAsync(string sessionId,CancellationToken ct)=>api.GetRealtimeAsync(key,sessionId,ct);
    public async Task ConnectAsync(RealtimeOperation operation,CancellationToken ct)
    {session=new(api,key,operation,new RealtimeWebSocketTransport());await session.ConnectAsync(ct).ConfigureAwait(false);}
    public Task<RealtimeStreamMessage> ReadAsync(CancellationToken ct)=>session!.ReadNextAsync(ct);
    public Task CameraAsync(string patch,CancellationToken ct)=>UpdateAsync(patch,false,ct);
    public Task SceneAsync(string patch,CancellationToken ct)=>UpdateAsync(patch,true,ct);
    private async Task UpdateAsync(string patch,bool scene,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(90));
        try{await (scene?session!.EnqueueSceneAsync(patch):session!.EnqueueCameraAsync(patch)).WaitAsync(deadline.Token).ConfigureAwait(false);}
        catch{await session!.CloseOnceAsync().ConfigureAwait(false);throw;}
    }
    public async Task CloseAsync(string sessionId,bool explicitRetry)
    {
        if(session is not null){if(explicitRetry)await session.CloseAsync().ConfigureAwait(false);else await session.CloseOnceAsync().ConfigureAwait(false);}
        else
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            // Per the public contract, HTTP 200 closed=false also confirms an already absent session.
            await api.CloseRealtimeAsync(key,sessionId,timeout.Token).ConfigureAwait(false);
        }
    }
}
