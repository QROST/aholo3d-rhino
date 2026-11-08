namespace Aholo.Core;

public sealed record LocalOperation(Guid Id,string Scope,string Name,string State,DateTimeOffset StartedAt,DateTimeOffset? FinishedAt=null,string? Error=null);

/// <summary>Shared local operation identity and cancellation. It does not cancel cloud jobs.</summary>
public sealed class OperationRegistry
{
    private sealed record Entry(LocalOperation Value,CancellationTokenSource Cancellation);
    private readonly object gate=new();
    private readonly Dictionary<string,Entry> active=new(StringComparer.Ordinal);
    private readonly Queue<LocalOperation> completed=new();
    public event Action? Changed;
    public IReadOnlyList<LocalOperation> Snapshot()
    {lock(gate)return active.Values.Select(x=>x.Value).Concat(completed).OrderByDescending(x=>x.StartedAt).ToArray();}
    public bool IsActive(string scope){lock(gate)return active.ContainsKey(scope);}
    public async Task<T> RunAsync<T>(string scope,string name,Func<CancellationToken,Task<T>> work,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(work);ct.ThrowIfCancellationRequested();
        var cancel=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var item=new LocalOperation(Guid.NewGuid(),scope,name,"Running",DateTimeOffset.UtcNow);
        lock(gate)
        {
            if(active.ContainsKey(scope)){cancel.Dispose();throw new InvalidOperationException("This operation is already running. Wait or cancel it locally.");}
            active.Add(scope,new(item,cancel));
        }
        Notify();string state="Succeeded";string? error=null;
        try{return await work(cancel.Token).ConfigureAwait(false);}
        catch(OperationCanceledException){state="Cancelled";throw;}
        catch(Exception ex){state="Failed";error=TaskStore.DisplayText(ex.Message,1024);throw;}
        finally
        {
            lock(gate)
            {
                active.Remove(scope);completed.Enqueue(item with{State=state,Error=error,FinishedAt=DateTimeOffset.UtcNow});
                while(completed.Count>32)completed.Dequeue();
            }
            cancel.Dispose();Notify();
        }
    }
    public Task RunAsync(string scope,string name,Func<CancellationToken,Task> work,CancellationToken ct=default)=>
        RunAsync(scope,name,async token=>{await work(token).ConfigureAwait(false);return true;},ct);
    public void Cancel(string scope)
    {
        Entry? item;lock(gate)active.TryGetValue(scope,out item);
        try{item?.Cancellation.Cancel();}catch(ObjectDisposedException){ }
    }
    public void CancelPrefix(string prefix)
    {string[] scopes;lock(gate)scopes=active.Keys.Where(x=>x.StartsWith(prefix,StringComparison.Ordinal)).ToArray();foreach(string scope in scopes)Cancel(scope);}
    private void Notify()
    {
        // A view that has closed must not prevent operation cleanup or change its outcome.
        foreach(Action observer in Changed?.GetInvocationList()??Array.Empty<Delegate>())try{observer();}catch(Exception){ }
    }
}
