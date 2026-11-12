namespace Aholo3D.Core;

/// <summary>Retire native resources immediately, but defer their disposal while a synchronous commit borrows them.</summary>
public sealed class DeferredDisposals
{
    private readonly object gate=new();
    private readonly HashSet<IDisposable> pending=new(ReferenceEqualityComparer.Instance);
    private int borrowers;
    private bool closed;
    public IDisposable Borrow()
    {
        lock(gate){if(closed)throw new ObjectDisposedException(nameof(DeferredDisposals));borrowers++;return new Borrowed(this);}
    }
    public T RunBorrowed<T>(Func<T> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);var borrowed=Borrow();Exception? primary=null;
        try{return operation();}
        catch(Exception error){primary=error;throw;}
        finally
        {
            try{borrowed.Dispose();}
            catch(Exception cleanup) when(primary is not null)
            {throw new AggregateException(primary.Message,primary,cleanup);}
        }
    }
    public void Retire(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock(gate){if(borrowers>0){pending.Add(resource);return;}}
        resource.Dispose();
    }
    public void Close(){lock(gate)closed=true;}
    private void Release()
    {
        IDisposable[] release;
        lock(gate)
        {
            if(--borrowers>0)return;
            release=pending.ToArray();pending.Clear();
        }
        List<Exception>? failures=null;
        foreach(var resource in release)try{resource.Dispose();}catch(Exception error){(failures??=new()).Add(error);}
        if(failures is not null)throw new AggregateException("Native resource cleanup did not finish.",failures);
    }
    private sealed class Borrowed(DeferredDisposals owner) : IDisposable
    {
        private int released;
        public void Dispose(){if(Interlocked.Exchange(ref released,1)==0)owner.Release();}
    }
}
