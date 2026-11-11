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
    private TaskCompletionSource? disposal;
    private int cursor;
    private readonly object intentGate = new();
    private readonly HashSet<Guid> revoked = new();
    private readonly Dictionary<Guid,long> taskVersions = new();
    private long generation;
    private CancellationTokenSource authority = new();
    private string? lastError;
    public string? LastError => Volatile.Read(ref lastError);
    public AholoSession(IAholoServiceClient client,TaskStore store,TimeSpan? pollInterval=null)
    {
        Client=client;Store=store;this.pollInterval=pollInterval??TimeSpan.FromSeconds(8);
        Client.TransportReset+=TransportReset;
        Operations.Changed+=Notify;
    }
    public void Start()
    {lock(startGate){if(disposal is not null)throw new ObjectDisposedException(nameof(AholoSession));loop??=Task.Run(RunLoopAsync);}}
    public async Task<ConnectionState> ReadConnectionAsync(CancellationToken ct=default)
    {
        long expected; lock(intentGate) expected=generation;
        try
        {
            var state=await Client.GetConnectionAsync(ct).ConfigureAwait(false);
            return SetConnection(state,expected,clearError:true);
        }
        catch { SetError("The saved connection could not be restored. Use Settings to restore or reconnect.");throw; }
    }
    public async Task<ConnectionState> ConnectAsync(ConnectRequest request,CancellationToken ct=default)
    {
        Invalidate();SetConnection(new(false,null,false,Region:Connection.Region));
        long expected;lock(intentGate)expected=generation;
        try{var state=await Client.ConnectAsync(request,ct).ConfigureAwait(false);return SetConnection(state,expected,clearError:true);}
        catch{await ReconcileFailedChangeAsync(expected,ct).ConfigureAwait(false);throw;}
    }
    public async Task<ConnectionState> DisconnectAsync(CancellationToken ct=default)
    {
        Invalidate();SetConnection(new(false,null,false,Region:Connection.Region));Operations.CancelPrefix("task:");
        long expected;lock(intentGate)expected=generation;
        try{var state=await Client.DisconnectAsync(ct).ConfigureAwait(false);return SetConnection(state,expected,clearError:true);}
        catch{await ReconcileFailedChangeAsync(expected,ct).ConfigureAwait(false);throw;}
    }
    public async Task<ConnectionState> SwitchRegionAsync(string region,CancellationToken ct=default)
    {
        region=AholoRegions.Get(region).Id;
        Invalidate();SetConnection(new(false,null,false,Region:Connection.Region));Operations.CancelPrefix("task:");
        long expected;lock(intentGate)expected=generation;
        try{var state=await Client.SwitchRegionAsync(region,ct).ConfigureAwait(false);return SetConnection(state,expected,clearError:true);}
        catch{await ReconcileFailedChangeAsync(expected,ct).ConfigureAwait(false);throw;}
    }
    private async Task ReconcileFailedChangeAsync(long expected,CancellationToken ct)
    {
        ConnectionState? retained=null;
        try{if(!ct.IsCancellationRequested)retained=await Client.GetConnectionAsync(ct).ConfigureAwait(false);}catch{ }
        lock(intentGate)
        {
            if(generation!=expected)return;
            Volatile.Write(ref lastError,"The connection change did not complete. The current saved connection was checked; restore it again if disconnected.");
        }
        if(retained is not null)SetConnection(retained,expected);else Notify();
    }
    public Task<TaskPage> ReadHistoryAsync(TaskPageRequest request,CancellationToken ct=default)=>Task.Run(()=>Store.Page(request),ct);
    public async Task<LocalTask> RenderAsync(RenderRequest request,CancellationToken ct=default)
    {
        long expected; lock(intentGate)expected=generation;
        var task=await Client.RenderAsync(request,ct).ConfigureAwait(false);
        lock(intentGate)
            if(expected==generation && task.Status=="MeshProcessing"&&!ct.IsCancellationRequested&&!paused.ContainsKey(task.Id)&&!revoked.Contains(task.Id))renderIntents.TryAdd(task.Id,0);
        Observe(task);return task;
    }
    public void PauseTask(Guid id)
    {
        lock(intentGate){paused[id]=0;revoked.Add(id);taskVersions[id]=taskVersions.GetValueOrDefault(id)+1;renderIntents.TryRemove(id,out _);}
        Operations.Cancel("task:"+id.ToString("N"));Notify();
    }
    public void ResumeQueries(Guid id){paused.TryRemove(id,out _);Notify();}
    public bool IsPaused(Guid id)=>paused.ContainsKey(id);
    public async Task<LocalTask> ContinueRenderAsync(Guid id,CancellationToken ct=default)
    {
        long expected,version;CancellationToken authorityToken;
        lock(intentGate){paused.TryRemove(id,out _);expected=generation;version=taskVersions.GetValueOrDefault(id);authorityToken=authority.Token;}
        using var continuation=CancellationTokenSource.CreateLinkedTokenSource(ct,authorityToken);
        LocalTask task;
        try{continuation.Token.ThrowIfCancellationRequested();task=await Client.ContinueRenderAsync(id,continuation.Token).ConfigureAwait(false);}
        catch(OperationCanceledException) when(authorityToken.IsCancellationRequested){throw new LocalOperationCanceledException();}
        lock(intentGate)
        {
            if(expected==generation&&version==taskVersions.GetValueOrDefault(id))
            {
                if(task.Error is null&&(task.Status is "MeshProcessing" or "MeshReady")&&!ct.IsCancellationRequested&&!paused.ContainsKey(id))renderIntents[id]=0;
                else renderIntents.TryRemove(id,out _);
            }
        }
        Observe(task);return task;
    }
    public async Task PollOnceAsync(CancellationToken ct=default)
    {
        CancellationToken authorityToken;lock(intentGate)authorityToken=authority.Token;
        using var polling=CancellationTokenSource.CreateLinkedTokenSource(ct,lifetime.Token,authorityToken);
        ct=polling.Token;
        if(!AutoRefresh||!Connection.Connected||!await pollGate.WaitAsync(0,ct).ConfigureAwait(false))return;
        LocalOperationHandle<bool>? query=null;
        try
        {
            // Reading local history does not wait behind a long pipe request or start a new sidecar.
            var records=await Task.Run(()=>Store.ActiveTasks(Connection.ConnectionId!,Connection.Region).Where(t=>!paused.ContainsKey(t.Id)&&(TaskCapabilities.For(t).IsRunning||renderIntents.ContainsKey(t.Id))).ToArray(),ct).ConfigureAwait(false);
            if(records.Length==0)return;
            var task=records[(int)((uint)Interlocked.Increment(ref cursor)%(uint)records.Length)];
            string scope="task:"+task.Id.ToString("N");
            if(Operations.IsActive(scope))return;
            query=Operations.Start(scope,"Refresh task",async token=>
            {
                if(paused.ContainsKey(task.Id))throw new LocalOperationCanceledException();
                var updated=await Client.RefreshAsync(task.Id,token).ConfigureAwait(false);Observe(updated);
                bool canContinue;
                lock(intentGate)
                {
                    canContinue=updated.Error is null&&renderIntents.ContainsKey(task.Id)&&TaskCapabilities.For(updated).CanContinueRender&&!paused.ContainsKey(task.Id);
                }
                if(canContinue)
                {
                    token.ThrowIfCancellationRequested();
                    updated=await Client.ContinueRenderAsync(task.Id,token).ConfigureAwait(false);Observe(updated);
                }
                if(updated.Error is not null||updated.OperationId is not null||updated.Status is "SubmissionUnknown" or "FAILED" or "CANCELED" or "SUCCEEDED")renderIntents.TryRemove(task.Id,out _);
                return true;
            },ct);
            await query.Completion.ConfigureAwait(false);
        }
        catch(OperationCanceledException error) when(ct.IsCancellationRequested||query?.Cancellation.IsCancellationRequested==true||error is LocalOperationCanceledException){throw;}
        catch(Exception) when(!ct.IsCancellationRequested){SetError("Background refresh failed. Restore the connection or refresh the original task; do not resubmit it.");throw;}
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
    private ConnectionState SetConnection(ConnectionState state,long? expected=null,bool clearError=false)
    {
        CancellationTokenSource? previous=null;
        lock(intentGate)
        {
            if(expected.HasValue&&expected!=generation)return Connection;
            if(clearError)Volatile.Write(ref lastError,null);
            if(Connection.Region!=state.Region||Connection.ConnectionId!=state.ConnectionId||Connection.CredentialEpoch!=state.CredentialEpoch)previous=RevokeUnderLock();
            Volatile.Write(ref connection,state);
        }
        CancelAuthority(previous);Notify();return state;
    }
    private CancellationTokenSource RevokeUnderLock()
    {
        generation++;renderIntents.Clear();paused.Clear();revoked.Clear();taskVersions.Clear();
        var previous=authority;authority=new();return previous;
    }
    private void Invalidate()
    {
        CancellationTokenSource previous;lock(intentGate)previous=RevokeUnderLock();
        CancelAuthority(previous);
    }
    private static void CancelAuthority(CancellationTokenSource? previous)
    {if(previous is not null){try{previous.Cancel();}finally{previous.Dispose();}}}
    private void SetError(string? error){Volatile.Write(ref lastError,error);Notify();}
    private void TransportReset()
    {
        Invalidate();SetConnection(new(false,null,false,Region:Connection.Region));
        SetError("The background connection stopped. Restore it in Settings; paid render continuation requires a new explicit action.");
    }
    public void Observe(LocalTask task)
    {
        foreach(Action<LocalTask> observer in TaskChanged?.GetInvocationList()??Array.Empty<Delegate>())try{observer(task);}catch(Exception){ }
        Notify();
    }
    private void Notify(){foreach(Action observer in Changed?.GetInvocationList()??Array.Empty<Delegate>())try{observer();}catch(Exception){ }}
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock(startGate)
        {
            if(disposal is not null)return new(disposal.Task);
            completion=disposal=new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        _=DisposeCoreAsync(completion);return new(completion.Task);
    }
    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        var errors=new List<Exception>();
        void Attempt(Action action){try{action();}catch(Exception error){errors.Add(error);}}
        async Task Await(Func<Task> action){try{await action().ConfigureAwait(false);}catch(Exception error){errors.Add(error);}}
        Task operations=Task.CompletedTask;
        Attempt(()=>operations=Operations.StopAsync());
        Attempt(lifetime.Cancel);Attempt(Invalidate);
        await Await(()=>operations).ConfigureAwait(false);
        if(loop is not null)await Await(async()=>{try{await loop.ConfigureAwait(false);}catch(OperationCanceledException){ }}).ConfigureAwait(false);
        // A manual poll has a finally block outside its registered request. Let it release the gate.
        await Await(()=>pollGate.WaitAsync()).ConfigureAwait(false);
        Attempt(()=>Client.TransportReset-=TransportReset);Attempt(()=>Operations.Changed-=Notify);
        await Await(()=>Client.DisposeAsync().AsTask()).ConfigureAwait(false);
        Attempt(lifetime.Dispose);Attempt(pollGate.Dispose);Attempt(authority.Dispose);
        if(errors.Count==0)completion.TrySetResult();else completion.TrySetException(new AggregateException(errors));
    }
}
