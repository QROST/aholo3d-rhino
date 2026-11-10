using System.Diagnostics;
namespace Aholo.Sidecar;

/// <summary>Parent exit cancels ordinary requests so EOF cleanup is not delayed by a long upload.</summary>
public sealed class ParentProcessLifetime : IDisposable
{
    private readonly CancellationTokenSource stopping=new();
    public Task Completion{get;}
    public ParentProcessLifetime(int parentId,CancellationTokenSource parentLost)
    {
        Completion=Task.Run(async()=>
        {
            try
            {
                using var parent=Process.GetProcessById(parentId);
                await parent.WaitForExitAsync(stopping.Token).ConfigureAwait(false);
                if(!stopping.IsCancellationRequested)parentLost.Cancel();
            }
            catch(OperationCanceledException)when(stopping.IsCancellationRequested){}
            catch(Exception error)when(error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {if(!stopping.IsCancellationRequested)parentLost.Cancel();}
        });
    }
    public void Dispose()=>stopping.Cancel();
}
