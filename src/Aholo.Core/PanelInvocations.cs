namespace Aholo.Core;

public sealed record PanelInvocationStatus(Guid Id,string Name,string Progress,DateTimeOffset StartedAt);

/// <summary>Per-invocation UI authority and resource lifetime; never uses an ambient cancellation token.</summary>
public sealed class PanelInvocations
{
    private readonly object gate=new();
    private readonly OperationResources resources;
    private readonly string owner;
    private readonly Dictionary<Guid,PanelInvocation> active=new();
    private readonly TaskCompletionSource drained=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closed;
    private long revealTicket;
    public PanelInvocations(OperationResources resources,string owner)
    {ArgumentNullException.ThrowIfNull(resources);ArgumentException.ThrowIfNullOrEmpty(owner);this.resources=resources;this.owner=owner;}
    private OperationResource Action(string key)=>new("panel-action",owner,key);
    public bool CanStart(string key,IEnumerable<OperationResource> claims)
    {lock(gate)return !closed&&!resources.IsHeld(Action(key))&&!claims.Any(resources.IsHeld);}
    public int Count{get{lock(gate)return active.Count;}}
    public IReadOnlyList<PanelInvocationStatus> Snapshot()
    {lock(gate)return active.Values.OrderBy(x=>x.StartedAt).Select(x=>new PanelInvocationStatus(x.Id,x.Name,x.Progress,x.StartedAt)).ToArray();}
    public PanelInvocation Begin(string key,string name,IEnumerable<OperationResource> claims,long navigationRevision,long workspaceLease,CancellationToken ct,bool reveal=false,LocalTask? selectedTask=null,TaskArtifact? selectedArtifact=null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);ArgumentNullException.ThrowIfNull(claims);ct.ThrowIfCancellationRequested();
        // Enumerate caller input before locking; the pool copies and validates every key atomically.
        var keys=claims.Append(Action(key)).ToArray();
        lock(gate)
        {
            if(closed)throw new ObjectDisposedException(nameof(PanelInvocations));
            var lease=resources.Acquire(keys,ct);
            var invocation=new PanelInvocation(this,lease,name,navigationRevision,workspaceLease,ct,reveal?++revealTicket:0,selectedTask,selectedArtifact);
            active.Add(invocation.Id,invocation);return invocation;
        }
    }
    public bool CanReveal(PanelInvocation invocation,long navigationRevision,long workspaceLease)
    {
        lock(gate)return !closed&&active.TryGetValue(invocation.Id,out var current)&&ReferenceEquals(current,invocation)&&
            !invocation.Cancellation.IsCancellationRequested&&invocation.RevealTicket!=0&&invocation.RevealTicket==revealTicket&&
            invocation.NavigationRevision==navigationRevision&&invocation.WorkspaceLease==workspaceLease;
    }
    internal bool Report(PanelInvocation invocation,string progress)
    {
        lock(gate)
        {
            if(closed||!active.TryGetValue(invocation.Id,out var current)||!ReferenceEquals(current,invocation))return false;
            invocation.Progress=progress;return true;
        }
    }
    internal void Complete(PanelInvocation invocation,IDisposable lease)
    {
        lock(gate)
        {
            if(!active.TryGetValue(invocation.Id,out var current)||!ReferenceEquals(current,invocation))return;
            lease.Dispose();active.Remove(invocation.Id);
            if(closed&&active.Count==0)drained.TrySetResult();
        }
    }
    /// <summary>Reject later work and wait for every invocation's cleanup without blocking the UI thread.</summary>
    public Task CloseAsync()
    {lock(gate){closed=true;if(active.Count==0)drained.TrySetResult();return drained.Task;}}
}

public sealed class PanelInvocation : IDisposable
{
    private readonly PanelInvocations owner;
    private readonly IDisposable lease;
    public Guid Id{get;}=Guid.NewGuid();
    public string Name{get;}
    public LocalTask? SelectedTask{get;}
    public TaskArtifact? SelectedArtifact{get;}
    public CancellationToken Cancellation{get;}
    public long NavigationRevision{get;}
    public long WorkspaceLease{get;}
    public DateTimeOffset StartedAt{get;}=DateTimeOffset.UtcNow;
    internal long RevealTicket{get;}
    internal string Progress{get;set;}
    internal PanelInvocation(PanelInvocations owner,IDisposable lease,string name,long navigationRevision,long workspaceLease,CancellationToken ct,long revealTicket,LocalTask? selectedTask,TaskArtifact? selectedArtifact)
    {this.owner=owner;this.lease=lease;Name=Progress=name;SelectedTask=selectedTask;SelectedArtifact=selectedArtifact;NavigationRevision=navigationRevision;WorkspaceLease=workspaceLease;Cancellation=ct;RevealTicket=revealTicket;}
    public bool Report(string progress)=>owner.Report(this,progress);
    public void Dispose()=>owner.Complete(this,lease);
}
