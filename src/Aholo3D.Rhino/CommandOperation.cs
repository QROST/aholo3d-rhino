using Aholo3D.Core;
namespace Aholo3D.Rhino;

internal static class CommandOperation
{
    internal static Task WaitForIdleAsync(CancellationToken ct)=>OperationSignal.WaitAsync(
        handler=>global::Rhino.RhinoApp.Idle+=handler,handler=>global::Rhino.RhinoApp.Idle-=handler,ct);

    internal static void Start(uint serial,string scope,string name,Func<CancellationToken,Task<string>> work)
    {
        var handle=Aholo3DPlugin.Session.Operations.Start(Aholo3DPlugin.DocumentScope(serial)+scope,name,work);
        Write(UiText.Format("Operation {0} · document {1} · {2}: started. Use Aholo3DCancel to stop locally.",handle.Id,serial,UiText.Get(name)));
        _=ReportAsync(handle);
    }
    private static async Task ReportAsync(LocalOperationHandle<string> handle)
    {
        string message;
        try{message=UiText.Format("Operation {0}: completed. {1}",handle.Id,await handle.Completion.ConfigureAwait(false));}
        catch(OperationCanceledException error) when(handle.Cancellation.IsCancellationRequested||error is LocalOperationCanceledException){message=UiText.Format("Operation {0}: cancelled locally. Remote tasks are not cancelled; already committed objects remain available for Undo.",handle.Id);}
        catch(Exception error)
        {
            string detail=error is OperationCanceledException
                ?UiText.Get("The operation stopped unexpectedly without a local cancellation request. Check the connection and retry the original task; do not resubmit it.")
                :Aholo3DHost.FriendlyError(error);
            message=UiText.Format("Operation {0}: failed. {1}",handle.Id,detail);
        }
        Write(message);
    }
    private static void Write(string message)
    {
        // Rhino may already be shutting down when asynchronous cleanup finishes.
        try{Aholo3DHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(message));}catch(ObjectDisposedException){ }
    }
}
