namespace Aholo.Core;

public sealed record LocalOperation(Guid Id,string Scope,string Name,string State,DateTimeOffset StartedAt,DateTimeOffset? FinishedAt=null,string? Error=null);
/// <summary>Explicit local intent revocation or a dismissed choice, independent of a transport timeout.</summary>
public sealed class LocalOperationCanceledException(string? message=null) : OperationCanceledException(message);

public sealed record LocalOperationHandle<T>(LocalOperation Operation,Task<T> Completion,CancellationToken Cancellation)
{
    public Guid Id => Operation.Id;
}

/// <summary>Shared local operation identity and cancellation. It does not cancel cloud jobs.</summary>
public sealed class OperationRegistry
{
    private sealed record Entry(LocalOperation Value,CancellationTokenSource Cancellation)
    {
        public TaskCompletionSource Finished { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private readonly object gate=new();
    private readonly Dictionary<string,Entry> active=new(StringComparer.Ordinal);
    private readonly Queue<LocalOperation> completed=new();
    private Task? stopped;
    public event Action? Changed;
    public IReadOnlyList<LocalOperation> Snapshot()
    {lock(gate)return active.Values.Select(x=>x.Value).Concat(completed).OrderByDescending(x=>x.StartedAt).ToArray();}
    public bool IsActive(string scope){lock(gate)return active.ContainsKey(scope);}
    public LocalOperationHandle<T> Start<T>(string scope,string name,Func<CancellationToken,Task<T>> work,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(work);ArgumentException.ThrowIfNullOrEmpty(scope);ct.ThrowIfCancellationRequested();
        Entry entry;
        lock(gate)
        {
            if(stopped is not null)throw new ObjectDisposedException(nameof(OperationRegistry));
            if(active.ContainsKey(scope))throw new InvalidOperationException("This operation is already running. Wait or cancel it locally.");
            entry=new(new(Guid.NewGuid(),scope,name,"Running",DateTimeOffset.UtcNow),CancellationTokenSource.CreateLinkedTokenSource(ct));
            active.Add(scope,entry);
        }
        Notify();
        // Preserve the caller's context for work that captures native UI state before its first await.
        var token=entry.Cancellation.Token;
        return new(entry.Value,ExecuteAsync(entry,work),token);
    }
    public async Task<T> RunAsync<T>(string scope,string name,Func<CancellationToken,Task<T>> work,CancellationToken ct=default)
        =>await Start(scope,name,work,ct).Completion.ConfigureAwait(false);
    private async Task<T> ExecuteAsync<T>(Entry entry,Func<CancellationToken,Task<T>> work)
    {
        string state="Succeeded";string? error=null;
        try
        {
            entry.Cancellation.Token.ThrowIfCancellationRequested();
            return await work(entry.Cancellation.Token).ConfigureAwait(false);
        }
        catch(OperationCanceledException cancellation) when(entry.Cancellation.IsCancellationRequested||cancellation is LocalOperationCanceledException){state="Cancelled";throw;}
        catch(Exception ex){state="Failed";error=TaskStore.DisplayText(ex.Message,1024);throw;}
        finally
        {
            lock(gate)
            {
                active.Remove(entry.Value.Scope);completed.Enqueue(entry.Value with{State=state,Error=error,FinishedAt=DateTimeOffset.UtcNow});
                while(completed.Count>32)completed.Dequeue();
            }
            entry.Cancellation.Dispose();Notify();entry.Finished.TrySetResult();
        }
    }
    public Task RunAsync(string scope,string name,Func<CancellationToken,Task> work,CancellationToken ct=default)=>
        RunAsync(scope,name,async token=>{await work(token).ConfigureAwait(false);return true;},ct);
    public void Cancel(string scope)
    {
        Entry? item;lock(gate)active.TryGetValue(scope,out item);
        CancelEntry(item);
    }
    public bool Cancel(Guid id)=>CancelMatching(id,null);
    public bool Cancel(Guid id,string requiredScopePrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(requiredScopePrefix);
        return CancelMatching(id,requiredScopePrefix);
    }
    private bool CancelMatching(Guid id,string? prefix)
    {
        Entry? item;lock(gate)item=active.Values.FirstOrDefault(x=>x.Value.Id==id&&(prefix is null||x.Value.Scope.StartsWith(prefix,StringComparison.Ordinal)));
        CancelEntry(item);return item is not null;
    }
    public void CancelPrefix(string prefix)
    {
        Entry[] items;lock(gate)items=active.Values.Where(x=>x.Value.Scope.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
        foreach(var item in items)CancelEntry(item);
    }
    /// <summary>Reject new operations, request cancellation and wait for all registered cleanup.</summary>
    public Task StopAsync()
    {
        Entry[] items;Task result;
        lock(gate)
        {
            if(stopped is not null)return stopped;
            items=active.Values.ToArray();result=stopped=Task.WhenAll(items.Select(x=>x.Finished.Task));
        }
        foreach(var item in items)CancelEntry(item);
        return result;
    }
    private static void CancelEntry(Entry? item)
    {
        try{item?.Cancellation.Cancel();}
        catch(ObjectDisposedException){ }
        // Cancellation callbacks cannot prevent other selected operations from being cancelled.
        catch(AggregateException){ }
    }
    private void Notify()
    {
        // A view that has closed must not prevent operation cleanup or change its outcome.
        foreach(Action observer in Changed?.GetInvocationList()??Array.Empty<Delegate>())try{observer();}catch(Exception){ }
    }
}
