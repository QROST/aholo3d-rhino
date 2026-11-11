using Aholo.Core;
namespace Aholo.Tests;

public sealed class ApplicationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aholo-session-tests-"+Guid.NewGuid().ToString("N"));
    private TaskStore Store => new(root);
    private static RenderRequest Request => new("Render", "fixture.glb", new(new double[16],50,36,20,1,1000));
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    [Fact] public async Task OriginalRenderIntentContinuesOnceAndQueryNeverReplaysIt()
    {
        var store=Store;var client=new FakeClient(store);
        await using var session=new AholoSession(client,store);
        await session.ReadConnectionAsync();
        await session.RenderAsync(Request);
        await session.PollOnceAsync();await session.PollOnceAsync();
        Assert.Equal(1,client.Continuations);Assert.Equal("op",store.Get(client.Task.Id).OperationId);
    }
    [Fact] public async Task RecreatedSessionDoesNotInheritPaidContinuationIntent()
    {
        var store=Store;var client=new FakeClient(store);
        await using(var first=new AholoSession(client,store)){await first.RenderAsync(Request);}
        await using var second=new AholoSession(client,store);await second.ReadConnectionAsync();
        await second.PollOnceAsync();await second.PollOnceAsync();
        Assert.Equal(0,client.Continuations);Assert.Equal("MeshReady",store.Get(client.Task.Id).Status);
        await second.ContinueRenderAsync(client.Task.Id);Assert.Equal(1,client.Continuations);
    }
    [Fact] public async Task PauseRemovesContinuationAuthorityEvenAfterQueriesResume()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new AholoSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);
        session.PauseTask(client.Task.Id);await session.PollOnceAsync();Assert.Equal(0,client.Queries);
        session.ResumeQueries(client.Task.Id);await session.PollOnceAsync();Assert.Equal(1,client.Queries);Assert.Equal(0,client.Continuations);
    }
    [Fact] public async Task TransportResetDoesNotReauthorizePaidContinuationOnReconnect()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new AholoSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);client.Reset();
        Assert.False(session.Connection.Connected);await session.ReadConnectionAsync();await session.PollOnceAsync();
        Assert.Equal(0,client.Continuations);
    }
    [Fact] public async Task LocalHistoryDoesNotCallSidecarOrNeedConnection()
    {
        var store=Store;store.Save(new LocalTask {Name="Offline",Status="SUCCEEDED"});
        var client=new FakeClient(store);await using var session=new AholoSession(client,store);
        Assert.Equal("Offline",Assert.Single((await session.ReadHistoryAsync(new())).Items).Name);
        Assert.Equal(0,client.Queries);Assert.False(session.Connection.Connected);
    }
    [Fact] public async Task ScopedCancellationDoesNotCancelOtherDocumentsOrMisreportCompletedCommit()
    {
        var registry=new OperationRegistry();var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first=registry.RunAsync("document:1:gs","Import",async token=>{started.SetResult();await Task.Delay(Timeout.Infinite,token);});
        await started.Task;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>registry.RunAsync("document:1:gs","Duplicate",_=>Task.CompletedTask));
        Assert.Equal(42,await registry.RunAsync("document:2:gs","Commit",_=>{registry.CancelPrefix("document:1:");return Task.FromResult(42);}));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
        Assert.Contains(registry.Snapshot(),x=>x.Scope=="document:1:gs"&&x.State=="Cancelled");
        Assert.Contains(registry.Snapshot(),x=>x.Scope=="document:2:gs"&&x.State=="Succeeded");
        await registry.RunAsync("document:2:gs","Committed",_=>{registry.Cancel("document:2:gs");return Task.CompletedTask;});
        Assert.Contains(registry.Snapshot(),x=>x.Name=="Committed"&&x.State=="Succeeded");
    }
    [Fact] public async Task BrokenViewObserverDoesNotChangeCompletedOperation()
    {
        var registry=new OperationRegistry();registry.Changed+=()=>throw new ObjectDisposedException("closed view");
        await registry.RunAsync("local","Work",_=>Task.CompletedTask);Assert.False(registry.IsActive("local"));
        Assert.Equal("Succeeded",Assert.Single(registry.Snapshot()).State);
    }
    [Theory][InlineData("reset")][InlineData("disconnect")][InlineData("connect")][InlineData("pause")]
    public async Task LateRenderCannotReauthorizeAfterConnectionOrPauseChange(string change)
    {
        var store=Store;var client=new FakeClient(store){RenderCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new AholoSession(client,store);await session.ReadConnectionAsync();
        var pending=session.RenderAsync(Request);
        switch(change)
        {
            case "reset":client.Reset();break;
            case "disconnect":await session.DisconnectAsync();break;
            case "connect":await session.ConnectAsync(new("fixture",false));break;
            default:session.PauseTask(client.Task.Id);session.ResumeQueries(client.Task.Id);break;
        }
        client.RenderCompletion.SetResult(client.Task);await pending;
        await session.ReadConnectionAsync();await session.PollOnceAsync();Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task LateExplicitContinuationCannotRestoreIntentAfterReset()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new AholoSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);session.PauseTask(client.Task.Id);
        client.ContinueCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=session.ContinueRenderAsync(client.Task.Id);client.Reset();
        client.ContinueCompletion.SetResult(client.Task);await pending;await session.ReadConnectionAsync();
        await session.PollOnceAsync();Assert.Equal(1,client.Continuations);
    }
    [Fact]public async Task FailedInitialConnectionCanBeExplicitlyRestoredWithoutPaidWork()
    {
        var store=Store;var client=new FakeClient(store){FailConnection=true};await using var session=new AholoSession(client,store);
        await Assert.ThrowsAsync<IOException>(()=>session.ReadConnectionAsync());Assert.NotNull(session.LastError);
        client.FailConnection=false;await session.ReadConnectionAsync();await session.PollOnceAsync();
        Assert.True(session.Connection.Connected);Assert.Null(session.LastError);Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task EpochChangeWithSameAccountRevokesOriginalRenderIntent()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new AholoSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);client.Epoch="changed";
        await session.ReadConnectionAsync();await session.PollOnceAsync();Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task IdleExitedTransportRejectsTheCurrentContinuationBeforeRestart()
    {
        using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"../../..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet")),"--version")
            {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})!;
        await process.WaitForExitAsync();
        await using var client=new AholoClient(root);int resets=0;client.TransportReset+=()=>resets++;
        typeof(AholoClient).GetField("_process",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(client,process);
        await Assert.ThrowsAsync<IOException>(()=>client.ContinueRenderAsync(Guid.NewGuid()));
        Assert.Equal(1,resets);Assert.Null(typeof(AholoClient).GetField("_process",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(client));
    }
    [Fact]public async Task DisconnectCancelsManualContinuationStillWaitingToSend()
    {
        var store=Store;var client=new FakeClient(store){WaitBeforeContinue=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new AholoSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        var pending=session.ContinueRenderAsync(client.Task.Id);await session.DisconnectAsync();
        client.WaitBeforeContinue.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task LateConnectCannotOverrideNewerDisconnect()
    {
        var client=new FakeClient(Store){ConnectCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new AholoSession(client,Store);
        var pending=session.ConnectAsync(new("fixture",false));await session.DisconnectAsync();
        client.ConnectCompletion.SetResult(new(true,"account",false,"old"));await pending;
        Assert.False(session.Connection.Connected);
    }
    [Fact]public async Task RegionSwitchRevokesQueuedContinuationAndRejectsLateConnect()
    {
        var store=Store;var client=new FakeClient(store){WaitBeforeContinue=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new AholoSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        var continuation=session.ContinueRenderAsync(client.Task.Id);
        client.ConnectCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var connect=session.ConnectAsync(new("fixture",false));
        await session.SwitchRegionAsync("china");
        client.ConnectCompletion.SetResult(new(true,"account",false,"stale","international"));await connect;
        client.WaitBeforeContinue.SetResult();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>continuation);
        Assert.Equal("china",session.Connection.Region);Assert.False(session.Connection.Connected);Assert.Equal(0,client.Continuations);
        client.Reset();Assert.Equal("china",session.Connection.Region);
    }
    [Theory][InlineData("connect")][InlineData("switch")][InlineData("disconnect")]
    public async Task FailedConnectionMutationRestoresRetainedStateWithoutPaidContinuation(string action)
    {
        var store=Store;var client=new FakeClient(store);await using var session=new AholoSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);client.FailMutation=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>action switch
        {"connect"=>session.ConnectAsync(new("fixture",false)),"switch"=>session.SwitchRegionAsync("china"),_=>session.DisconnectAsync()});
        Assert.True(session.Connection.Connected);Assert.Equal("international",session.Connection.Region);Assert.NotNull(session.LastError);
        await session.PollOnceAsync();Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task FailedMutationRecoveryCannotOverwriteNewerRegion()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new AholoSession(client,store);await session.ReadConnectionAsync();
        client.FailMutation=true;client.ConnectionReadCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed=session.ConnectAsync(new("fixture",false));client.FailMutation=false;await session.SwitchRegionAsync("china");
        client.ConnectionReadCompletion.SetResult(new(true,"account",false,"old","international"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>failed);Assert.Equal("china",session.Connection.Region);Assert.Null(session.LastError);
    }
    private sealed class FakeClient(TaskStore store) : IAholoServiceClient
    {
        public event Action? TransportReset;
        public void Reset()=>TransportReset?.Invoke();
        public LocalTask Task {get;private set;}=new(){Kind="render",ConnectionId="account",Status="MeshProcessing",MeshTaskId="mesh-task",Render=Request};
        public int Continuations,Queries;
        public bool FailConnection,FailMutation;public string Epoch="initial";
        public TaskCompletionSource<LocalTask>? RenderCompletion,ContinueCompletion;
        public TaskCompletionSource? WaitBeforeContinue;
        public TaskCompletionSource<ConnectionState>? ConnectCompletion,ConnectionReadCompletion;
        public Task<ConnectionState> GetConnectionAsync(CancellationToken ct=default)=>ConnectionReadCompletion?.Task??(FailConnection?System.Threading.Tasks.Task.FromException<ConnectionState>(new IOException("Fixture unavailable")):System.Threading.Tasks.Task.FromResult(new ConnectionState(true,"account",false,Epoch)));
        public Task<ConnectionState> ConnectAsync(ConnectRequest request,CancellationToken ct=default)=>FailMutation?System.Threading.Tasks.Task.FromException<ConnectionState>(new InvalidOperationException("Fixture rejected")):ConnectCompletion?.Task??GetConnectionAsync(ct);
        public Task<ConnectionState> SwitchRegionAsync(string region,CancellationToken ct=default)=>FailMutation?System.Threading.Tasks.Task.FromException<ConnectionState>(new InvalidOperationException("Fixture rejected")):System.Threading.Tasks.Task.FromResult(new ConnectionState(false,null,false,Guid.NewGuid().ToString("N"),region));
        public Task<ConnectionState> DisconnectAsync(CancellationToken ct=default)=>FailMutation?System.Threading.Tasks.Task.FromException<ConnectionState>(new InvalidOperationException("Fixture rejected")):System.Threading.Tasks.Task.FromResult(new ConnectionState(false,null,false));
        public Task<LocalTask> RenderAsync(RenderRequest request,CancellationToken ct=default){store.Save(Task);return RenderCompletion?.Task??System.Threading.Tasks.Task.FromResult(Task);}
        public Task<LocalTask> RefreshAsync(Guid id,CancellationToken ct=default)
        {Queries++;Task=store.Get(id);if(Task.Status=="MeshProcessing")Task=Task with{Status="MeshReady",MeshId="mesh"};store.Save(Task);return System.Threading.Tasks.Task.FromResult(Task);}
        public async Task<LocalTask> ContinueRenderAsync(Guid id,CancellationToken ct=default)
        {
            if(WaitBeforeContinue is not null)await WaitBeforeContinue.Task.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();Continuations++;
            if(ContinueCompletion is not null)return await ContinueCompletion.Task;
            Task=store.Get(id) with{Status="Running",OperationId="op"};store.Save(Task);return Task;
        }
        public Task<IReadOnlyList<LocalTask>> GetTasksAsync(CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<TaskPage> GetTaskPageAsync(TaskPageRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> CreateServiceAsync(CreateServiceRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> GenerateWorldAsync(GenerateWorldRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> DownloadAsync(DownloadRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> ImportWorldAsync(string worldId,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<CloudHistoryPage> CloudHistoryAsync(CloudHistoryRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> RestoreRemoteAsync(RestoreRemoteRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
