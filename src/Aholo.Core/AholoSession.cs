using System.Collections.Concurrent;
namespace Aholo.Core;

/// <summary>One application session per Rhino process, shared by panels and commands.</summary>
public sealed class AholoSession : IAsyncDisposable
{
    public IAholoServiceClient Client { get; }
    public OperationRegistry Operations { get; } = new();
    public TaskStore Store { get; }
    private ConnectionState connection = new(false,null,false);
    public ConnectionState Connection => Volatile.Read(ref connection);
    private volatile bool autoRefresh = true;
    public bool AutoRefresh { get => autoRefresh; set => autoRefresh=value; }
    public event Action? Changed;
    public event Action<LocalTask>? TaskChanged;
    private readonly ConcurrentDictionary<Guid,byte> renderIntents=new();
    private readonly ConcurrentDictionary<Guid,byte> paused=new();
    private readonly CancellationTokenSource lifetime=new();
    private readonly SemaphoreSlim pollGate=new(1);
    private readonly TimeSpan pollInterval;
    private readonly object startGate=new();
    private Task? loop;
    private int cursor;
    public AholoSession(IAholoServiceClient client,TaskStore store,TimeSpan? pollInterval=null)
    {
        Client=client;Store=store;this.pollInterval=pollInterval??TimeSpan.FromSeconds(8);
        Client.TransportReset+=TransportReset;
        Operations.Changed+=Notify;
    }
    public void Start()
    {lock(startGate)loop??=Task.Run(RunLoopAsync);}
    public async Task<ConnectionState> ReadConnectionAsync(CancellationToken ct=default)=>SetConnection(await Client.GetConnectionAsync(ct).ConfigureAwait(false));
    public async Task<ConnectionState> ConnectAsync(ConnectRequest request,CancellationToken ct=default)
    {renderIntents.Clear();paused.Clear();return SetConnection(await Client.ConnectAsync(request,ct).ConfigureAwait(false));}
    public async Task<ConnectionState> DisconnectAsync(CancellationToken ct=default)
    {renderIntents.Clear();paused.Clear();Operations.CancelPrefix("task:");return SetConnection(await Client.DisconnectAsync(ct).ConfigureAwait(false));}
    public Task<TaskPage> ReadHistoryAsync(TaskPageRequest request,CancellationToken ct=default)=>Task.Run(()=>Store.Page(request),ct);
    public async Task<LocalTask> RenderAsync(RenderRequest request,CancellationToken ct=default)
    {
        var task=await Client.RenderAsync(request,ct).ConfigureAwait(false);
        if(task.Status=="MeshProcessing"&&!ct.IsCancellationRequested)renderIntents.TryAdd(task.Id,0);
        Observe(task);return task;
    }
    public void PauseTask(Guid id)
    {paused[id]=0;renderIntents.TryRemove(id,out _);Operations.Cancel("task:"+id.ToString("N"));Notify();}
    public void ResumeQueries(Guid id){paused.TryRemove(id,out _);Notify();}
    public bool IsPaused(Guid id)=>paused.ContainsKey(id);
    public async Task<LocalTask> ContinueRenderAsync(Guid id,CancellationToken ct=default)
    {
        paused.TryRemove(id,out _);
        var task=await Client.ContinueRenderAsync(id,ct).ConfigureAwait(false);
        if((task.Status is "MeshProcessing" or "MeshReady")&&!ct.IsCancellationRequested&&!paused.ContainsKey(id))renderIntents[id]=0;
        else renderIntents.TryRemove(id,out _);
        Observe(task);return task;
    }
    public async Task PollOnceAsync(CancellationToken ct=default)
    {
        if(!AutoRefresh||!Connection.Connected||!await pollGate.WaitAsync(0,ct).ConfigureAwait(false))return;
        try
        {
            // Reading local history does not wait behind a long pipe request or start a new sidecar.
            var records=await Task.Run(()=>Store.ActiveTasks(Connection.ConnectionId!).Where(t=>!paused.ContainsKey(t.Id)&&(TaskCapabilities.For(t).IsRunning||renderIntents.ContainsKey(t.Id))).ToArray(),ct).ConfigureAwait(false);
            if(records.Length==0)return;
            var task=records[(int)((uint)Interlocked.Increment(ref cursor)%(uint)records.Length)];
            string scope="task:"+task.Id.ToString("N");
            if(Operations.IsActive(scope))return;
            await Operations.RunAsync(scope,"Refresh task",async token=>
            {
                var updated=await Client.RefreshAsync(task.Id,token).ConfigureAwait(false);Observe(updated);
                if(renderIntents.ContainsKey(task.Id)&&TaskCapabilities.For(updated).CanContinueRender&&!paused.ContainsKey(task.Id))
                {
                    updated=await Client.ContinueRenderAsync(task.Id,token).ConfigureAwait(false);Observe(updated);
                }
                if(updated.OperationId is not null||updated.Status is "SubmissionUnknown" or "FAILED" or "CANCELED" or "SUCCEEDED")renderIntents.TryRemove(task.Id,out _);
            },ct).ConfigureAwait(false);
        }
        finally{pollGate.Release();}
    }
    private async Task RunLoopAsync()
    {
        try{await ReadConnectionAsync(lifetime.Token).ConfigureAwait(false);}catch(Exception){ }
        using var timer=new PeriodicTimer(pollInterval);
        try
        {
            while(await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
                try{await PollOnceAsync(lifetime.Token).ConfigureAwait(false);}catch(Exception){/* Original journal and previous status remain visible; explicit refresh exposes errors. */}
        }
        catch(OperationCanceledException){ }
    }
    private ConnectionState SetConnection(ConnectionState state){Volatile.Write(ref connection,state);Notify();return state;}
    private void TransportReset()
    {
        renderIntents.Clear();SetConnection(new(false,null,false));
        // A restarted transport cannot inherit authority to continue a paid phase.
        // Reconnection is explicit; retained journals can always be viewed locally.
    }
    public void Observe(LocalTask task)
    {
        foreach(Action<LocalTask> observer in TaskChanged?.GetInvocationList()??Array.Empty<Delegate>())try{observer(task);}catch(Exception){ }
        Notify();
    }
    private void Notify(){foreach(Action observer in Changed?.GetInvocationList()??Array.Empty<Delegate>())try{observer();}catch(Exception){ }}
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();renderIntents.Clear();Operations.CancelPrefix("");
        if(loop is not null)try{await loop.ConfigureAwait(false);}catch(OperationCanceledException){ }
        Client.TransportReset-=TransportReset;Operations.Changed-=Notify;
        await Client.DisposeAsync().ConfigureAwait(false);lifetime.Dispose();pollGate.Dispose();
    }
}
