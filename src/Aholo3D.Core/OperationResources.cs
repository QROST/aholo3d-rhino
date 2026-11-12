namespace Aholo3D.Core;

/// <summary>A resource identity, independent of the visible operation ID and its cancellation scope.</summary>
public readonly record struct OperationResource(string Kind,string Owner,string Slot="");
public sealed class OperationResourceBusyException() : InvalidOperationException("Another local operation is using this resource. Wait for it to finish or cancel it.");

/// <summary>Nonblocking, atomic resource admission shared by one application session. Leases outlive cancellation until cleanup finishes.</summary>
public sealed class OperationResources
{
    private readonly object gate=new();
    private readonly Dictionary<OperationResource,Lease> held=new();
    public bool IsHeld(OperationResource resource){lock(gate)return held.ContainsKey(resource);}
    public IDisposable Acquire(IEnumerable<OperationResource> resources,CancellationToken ct=default)
        =>TryAcquire(resources,ct)??throw new OperationResourceBusyException();
    public IDisposable? TryAcquire(IEnumerable<OperationResource> resources,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(resources);ct.ThrowIfCancellationRequested();
        var keys=resources.Distinct().ToArray();
        if(keys.Length==0||keys.Any(k=>string.IsNullOrEmpty(k.Kind)||string.IsNullOrEmpty(k.Owner)||k.Slot is null))throw new ArgumentException("Resource claims require nonempty kind and owner identities.",nameof(resources));
        lock(gate)
        {
            ct.ThrowIfCancellationRequested();
            if(keys.Any(held.ContainsKey))return null;
            var lease=new Lease(this,keys);
            foreach(var key in keys)held.Add(key,lease);
            return lease;
        }
    }
    private sealed class Lease(OperationResources owner,OperationResource[] keys) : IDisposable
    {
        private bool disposed;
        public void Dispose()
        {
            lock(owner.gate)
            {
                if(disposed)return;disposed=true;
                foreach(var key in keys)
                    if(owner.held.TryGetValue(key,out var current)&&ReferenceEquals(current,this))owner.held.Remove(key);
            }
        }
    }
}
