using System.Diagnostics;
using System.Reflection;
using System.Text;
using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class RenderTransportTests
{
    [Fact] public async Task QueuedRenderKeepsItsOriginalEpochAndImmutableRequestOnTheActualPipe()
    {
        string root=FindRoot();
#if DEBUG
        const string configuration="Debug";
#else
        const string configuration="Release";
#endif
        string fixture=Path.Combine(root,"tools","Aholo3D.Reference","bin",configuration,"net8.0","Aholo3D.Reference.dll");
        Assert.True(File.Exists(fixture),"Build Aholo3D.sln before running transport tests.");
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
         StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=new UTF8Encoding(false)};
        start.ArgumentList.Add(fixture);start.ArgumentList.Add("--rpc-render-echo");
        using var process=Process.Start(start)!;
        await using var client=new Aholo3DClient(Path.GetDirectoryName(fixture)!);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(Aholo3DClient).GetField("_process",flags)!.SetValue(client,process);
        var epoch=typeof(Aholo3DClient).GetField("_credentialEpoch",flags)!;
        var gate=(SemaphoreSlim)typeof(Aholo3DClient).GetField("_gate",flags)!.GetValue(client)!;
        var camera=new double[16];camera[0]=1;
        var lightTransform=new double[16];lightTransform[12]=7;
        var color=new double[]{.1,.2,.3};
        var lights=new[]{new RenderLight("Sphere",lightTransform,color,5,Radius:10)};
        var request=new RenderRequest("fixture","fixture.glb",new(camera,50,36,20,1,1000),Lights:lights);
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Task<LocalTask> pending;
        await gate.WaitAsync(timeout.Token);
        try
        {
            epoch.SetValue(client,"current-at-entry");
            pending=client.RenderWithEpochAsync(request,"original-before-preparation",timeout.Token);
            epoch.SetValue(client,"current-after-queue");camera[0]=999;lightTransform[12]=999;color[0]=999;lights[0]=lights[0] with{Intensity=999};
        }
        finally{gate.Release();}
        var reply=await pending;
        Assert.Equal("original-before-preparation",reply.Name);var sent=reply.Render!;
        Assert.Equal(1,sent.Camera.Transform[0]);Assert.Equal(7,sent.Lights![0].Transform[12]);Assert.Equal(.1,sent.Lights[0].Color[0]);Assert.Equal(5,sent.Lights[0].Intensity);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RealPipeTimeoutRemainsAFailureAfterTransportResetRevokesAuthority(bool continuing)
    {
        string root=FindRoot();
#if DEBUG
        const string configuration="Debug";
#else
        const string configuration="Release";
#endif
        string fixture=Path.Combine(root,"tools","Aholo3D.Reference","bin",configuration,"net8.0","Aholo3D.Reference.dll");
        var start=new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")??"dotnet")
        {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
         StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=new UTF8Encoding(false)};
        start.ArgumentList.Add(fixture);start.ArgumentList.Add("--rpc-render-stall");
        using var process=Process.Start(start)!;
        await using var client=new Aholo3DClient(Path.GetDirectoryName(fixture)!);
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        typeof(Aholo3DClient).GetField("_process",flags)!.SetValue(client,process);
        string tasks=Path.Combine(Path.GetTempPath(),"aholo3d-render-timeout-"+Guid.NewGuid().ToString("N"));
        try
        {
            await using var session=new Aholo3DSession(client,new TaskStore(tasks));
            await session.ReadConnectionAsync();Assert.True(session.Connection.Connected);
            typeof(Aholo3DClient).GetField("_requestTimeout",flags)!.SetValue(client,TimeSpan.FromMilliseconds(150));
            var request=new RenderRequest("fixture","fixture.glb",new(new double[16],50,36,20,1,1000));
            var operation=session.Operations.RunAsync("render","Render",ct=>continuing?session.ContinueRenderAsync(Guid.NewGuid(),ct):session.RenderPreparedAsync(_=>Task.FromResult(request),ct));
            var error=await Assert.ThrowsAsync<TimeoutException>(()=>operation.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);Assert.False(session.Connection.Connected);
            Assert.Equal("Failed",Assert.Single(session.Operations.Snapshot()).State);
        }
        finally{if(Directory.Exists(tasks))Directory.Delete(tasks,true);}
    }
    [Fact] public async Task MissingEpochCannotStartAChildProcess()
    {
        await using var client=new Aholo3DClient(Path.GetTempPath());
        var request=new RenderRequest("fixture","fixture.glb",new(new double[16],50,36,20,1,1000));
        await Assert.ThrowsAsync<ArgumentException>(()=>client.RenderWithEpochAsync(request,""));
        Assert.Null(typeof(Aholo3DClient).GetField("_process",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(client));
    }
    private static string FindRoot()
    {
        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory is not null;directory=directory.Parent)
            if(File.Exists(Path.Combine(directory.FullName,"Aholo3D.sln")))return directory.FullName;
        throw new DirectoryNotFoundException("Aholo3D.sln");
    }
}
