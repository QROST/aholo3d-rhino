namespace Aholo3D.Core;

/// <summary>Await a host readiness event without losing cancellation while queued.</summary>
public static class OperationSignal
{
    public static async Task WaitAsync(Action<EventHandler> subscribe,Action<EventHandler> unsubscribe,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler=(_,_)=>ready.TrySetResult();
        try
        {
            subscribe(handler);
            using var cancel=ct.Register(()=>ready.TrySetCanceled(ct));
            await ready.Task.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        finally{unsubscribe(handler);}
    }
}
