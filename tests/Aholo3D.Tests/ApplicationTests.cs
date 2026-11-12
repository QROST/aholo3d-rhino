using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class ApplicationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aholo3d-session-tests-"+Guid.NewGuid().ToString("N"));
    private TaskStore Store => new(root);
    private static RenderRequest Request => new("Render", "fixture.glb", new(new double[16],50,36,20,1,1000));
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}

    [Fact] public async Task OriginalRenderIntentContinuesOnceAndQueryNeverReplaysIt()
    {
        var store=Store;var client=new FakeClient(store);
        await using var session=new Aholo3DSession(client,store);
        await session.ReadConnectionAsync();
        await session.RenderAsync(Request);
        await session.PollOnceAsync();await session.PollOnceAsync();
        Assert.Equal(1,client.Continuations);Assert.Equal("op",store.Get(client.Task.Id).OperationId);
    }
    [Fact] public async Task RecreatedSessionDoesNotInheritPaidContinuationIntent()
    {
        var store=Store;var client=new FakeClient(store);
        await using(var first=new Aholo3DSession(client,store)){await first.RenderAsync(Request);}
        await using var second=new Aholo3DSession(client,store);await second.ReadConnectionAsync();
        await second.PollOnceAsync();await second.PollOnceAsync();
        Assert.Equal(0,client.Continuations);Assert.Equal("MeshReady",store.Get(client.Task.Id).Status);
        await second.ContinueRenderAsync(client.Task.Id);Assert.Equal(1,client.Continuations);
    }
    [Fact] public async Task PauseRemovesContinuationAuthorityEvenAfterQueriesResume()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);
        session.PauseTask(client.Task.Id);await session.PollOnceAsync();Assert.Equal(0,client.Queries);
        session.ResumeQueries(client.Task.Id);await session.PollOnceAsync();Assert.Equal(1,client.Queries);Assert.Equal(0,client.Continuations);
    }
    [Fact] public async Task TransportResetDoesNotReauthorizePaidContinuationOnReconnect()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);client.Reset();
        Assert.False(session.Connection.Connected);await session.ReadConnectionAsync();await session.PollOnceAsync();
        Assert.Equal(0,client.Continuations);
    }
    [Fact] public async Task LocalHistoryDoesNotCallSidecarOrNeedConnection()
    {
        var store=Store;store.Save(new LocalTask {Name="Offline",Status="SUCCEEDED"});
        var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);
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
        await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();
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
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);session.PauseTask(client.Task.Id);
        client.ContinueCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending=session.ContinueRenderAsync(client.Task.Id);client.Reset();
        client.ContinueCompletion.SetResult(client.Task);await pending;await session.ReadConnectionAsync();
        await session.PollOnceAsync();Assert.Equal(1,client.Continuations);
    }
    [Fact]public async Task FailedInitialConnectionCanBeExplicitlyRestoredWithoutPaidWork()
    {
        var store=Store;var client=new FakeClient(store){FailConnection=true};await using var session=new Aholo3DSession(client,store);
        await Assert.ThrowsAsync<IOException>(()=>session.ReadConnectionAsync());Assert.NotNull(session.LastError);
        client.FailConnection=false;await session.ReadConnectionAsync();await session.PollOnceAsync();
        Assert.True(session.Connection.Connected);Assert.Null(session.LastError);Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task EpochChangeWithSameAccountRevokesOriginalRenderIntent()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);client.Epoch="changed";
        await session.ReadConnectionAsync();await session.PollOnceAsync();Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task IdleExitedTransportRejectsTheCurrentContinuationBeforeRestart()
    {
        using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"../../..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet")),"--version")
            {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true})!;
        await process.WaitForExitAsync();
        await using var client=new Aholo3DClient(root);int resets=0;client.TransportReset+=()=>resets++;
        typeof(Aholo3DClient).GetField("_process",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(client,process);
        await Assert.ThrowsAsync<IOException>(()=>client.ContinueRenderAsync(Guid.NewGuid()));
        Assert.Equal(1,resets);Assert.Null(typeof(Aholo3DClient).GetField("_process",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(client));
    }
    [Fact]public async Task DisconnectCancelsManualContinuationStillWaitingToSend()
    {
        var store=Store;var client=new FakeClient(store){WaitBeforeContinue=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        var pending=session.ContinueRenderAsync(client.Task.Id);await session.DisconnectAsync();
        client.WaitBeforeContinue.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task LateConnectCannotOverrideNewerDisconnect()
    {
        var client=new FakeClient(Store){ConnectCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new Aholo3DSession(client,Store);
        var pending=session.ConnectAsync(new("fixture",false));await session.DisconnectAsync();
        client.ConnectCompletion.SetResult(new(true,"account",false,"old"));await pending;
        Assert.False(session.Connection.Connected);
    }
    [Fact]public async Task RegionSwitchRevokesQueuedContinuationAndRejectsLateConnect()
    {
        var store=Store;var client=new FakeClient(store){WaitBeforeContinue=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
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
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);
        await session.ReadConnectionAsync();await session.RenderAsync(Request);client.FailMutation=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>action switch
        {"connect"=>session.ConnectAsync(new("fixture",false)),"switch"=>session.SwitchRegionAsync("china"),_=>session.DisconnectAsync()});
        Assert.True(session.Connection.Connected);Assert.Equal("international",session.Connection.Region);Assert.NotNull(session.LastError);
        await session.PollOnceAsync();Assert.Equal(0,client.Continuations);
    }
    [Fact]public async Task FailedMutationRecoveryCannotOverwriteNewerRegion()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();
        client.FailMutation=true;client.ConnectionReadCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed=session.ConnectAsync(new("fixture",false));client.FailMutation=false;await session.SwitchRegionAsync("china");
        client.ConnectionReadCompletion.SetResult(new(true,"account",false,"old","international"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>failed);Assert.Equal("china",session.Connection.Region);Assert.Null(session.LastError);
    }
    [Fact] public async Task PausingInFlightRefreshDoesNotReportConnectionFailure()
    {
        var store=Store;var client=new FakeClient(store){RefreshCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        var poll=session.PollOnceAsync();await client.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        session.PauseTask(client.Task.Id);await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>poll);
        Assert.Null(session.LastError);Assert.Equal(0,client.Continuations);
    }
    [Fact] public async Task ActualRefreshFailureStillReportsRecoveryAdvice()
    {
        var store=Store;var client=new FakeClient(store){RefreshCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        var poll=session.PollOnceAsync();await client.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.RefreshCompletion.SetException(new IOException("fixture unavailable"));await Assert.ThrowsAsync<IOException>(()=>poll);
        Assert.Contains("Background refresh failed",session.LastError);Assert.Equal(0,client.Continuations);
    }
    [Fact] public async Task UnexpectedRefreshTimeoutStillReportsFailure()
    {
        var store=Store;var client=new FakeClient(store){RefreshCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        var poll=session.PollOnceAsync();await client.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        client.RefreshCompletion.SetException(new TaskCanceledException("fixture transport timeout"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>poll);Assert.Contains("Background refresh failed",session.LastError);
        var operation=Assert.Single(session.Operations.Snapshot());Assert.Equal("Failed",operation.State);Assert.NotNull(operation.Error);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task ConnectionChangeCancelsAutomaticContinuationWithoutRefreshFailure(bool reset)
    {
        var store=Store;var client=new FakeClient(store){WaitBeforeContinue=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        var poll=session.PollOnceAsync();await client.ContinueStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if(reset)client.Reset();else await session.ConnectAsync(new("fixture",false));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>poll);
        Assert.DoesNotContain("Background refresh failed",session.LastError??"");Assert.Equal(0,client.Continuations);
        Assert.Equal("Cancelled",Assert.Single(session.Operations.Snapshot()).State);
    }
    [Fact] public async Task DisposalWaitsForRegisteredCleanupBeforeDisposingClient()
    {
        var store=Store;var client=new FakeClient(store);var session=new Aholo3DSession(client,store);
        var cleaning=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work=session.Operations.RunAsync("document:1:work","Cleanup",async ct=>
        {try{await System.Threading.Tasks.Task.Delay(Timeout.Infinite,ct);}finally{cleaning.SetResult();await release.Task;Assert.Equal(0,client.DisposeCalls);}});
        var disposal=session.DisposeAsync().AsTask();await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(disposal.IsCompleted);Assert.Same(disposal,session.DisposeAsync().AsTask());
        Assert.Throws<ObjectDisposedException>(()=>session.Start());
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>session.Operations.RunAsync("new","New",_=>System.Threading.Tasks.Task.CompletedTask));
        release.SetResult();await disposal;await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>work);Assert.Equal(1,client.DisposeCalls);
    }
    [Fact] public async Task DisposalCancelsManualPollWithoutDisposingItsGateEarly()
    {
        var store=Store;var client=new FakeClient(store){RefreshCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        var poll=session.PollOnceAsync();await client.RefreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.DisposeAsync();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>poll);Assert.Equal(1,client.DisposeCalls);Assert.Null(session.LastError);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task DisposalCompletesRemainingCleanupAfterCallbackOrClientFailure(bool clientFailure)
    {
        var store=Store;var client=new FakeClient(store){ThrowOnConnectionCancellation=true,ThrowOnDispose=clientFailure};
        var session=new Aholo3DSession(client,store);session.Start();await client.ConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var disposal=session.DisposeAsync().AsTask();await Assert.ThrowsAsync<AggregateException>(()=>disposal);
        Assert.Equal(1,client.DisposeCalls);Assert.True(client.CancellationCallbackRan);Assert.Same(disposal,session.DisposeAsync().AsTask());
        Assert.Throws<ObjectDisposedException>(()=>session.Start());
        await Assert.ThrowsAsync<ObjectDisposedException>(()=>session.Operations.RunAsync("new","New",_=>System.Threading.Tasks.Task.CompletedTask));
    }
    [Fact] public async Task DisposalDuringOperationNotificationDoesNotStartWork()
    {
        var client=new FakeClient(Store);var session=new Aholo3DSession(client,Store);Task? stopped=null;bool invoked=false;
        session.Operations.Changed+=()=>stopped=session.DisposeAsync().AsTask();
        var handle=session.Operations.Start("scope","Work",_=>{invoked=true;return System.Threading.Tasks.Task.FromResult(1);});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>handle.Completion);
        Assert.NotNull(stopped);await stopped.WaitAsync(TimeSpan.FromSeconds(5));Assert.False(invoked);Assert.Equal(1,client.DisposeCalls);
    }
    [Theory][InlineData("disconnect")][InlineData("connect")][InlineData("region")][InlineData("epoch")][InlineData("reset")][InlineData("caller")]
    public async Task PreparedRenderNeverSubmitsAfterAuthorityChangesDuringLocalWork(string change)
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);
        await session.ReadConnectionAsync();using var cancel=new CancellationTokenSource();
        var ready=new TaskCompletionSource<RenderRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed=default;
        var render=session.RenderPreparedAsync(ct=>{observed=ct;return ready.Task;},cancel.Token);
        switch(change)
        {
            case "disconnect":await session.DisconnectAsync();break;
            case "connect":await session.ConnectAsync(new("fixture-key",false));break;
            case "region":await session.SwitchRegionAsync("china");break;
            case "epoch":client.Epoch="replacement";await session.ReadConnectionAsync();break;
            case "reset":client.Reset();break;
            case "caller":cancel.Cancel();break;
        }
        Assert.True(observed.IsCancellationRequested);
        ready.SetResult(Request); // An uncooperative preparer must not bypass the final authority check.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>render);
        Assert.Equal(0,client.PinnedRenders);Assert.Empty(store.List());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task PreparedRenderNeedsACompleteConnectionBeforeDoingAnyLocalWork(bool missingEpoch)
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);
        if(missingEpoch){client.Epoch="";await session.ReadConnectionAsync();}
        int preparations=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>session.RenderPreparedAsync(_=>{preparations++;return Task.FromResult(Request);}));
        Assert.Equal(0,preparations);Assert.Equal(0,client.PinnedRenders);
    }
    [Fact] public async Task PreparedRenderCapturesEpochAndContinuesItsOriginalIntentExactlyOnce()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();
        int preparations=0;
        await session.RenderPreparedAsync(_=>{preparations++;return Task.FromResult(Request);});
        await session.PollOnceAsync();await session.PollOnceAsync();
        Assert.Equal(1,preparations);Assert.Equal(1,client.PinnedRenders);Assert.Equal("initial",client.PinnedEpoch);Assert.Equal(1,client.Continuations);
    }
    [Fact] public async Task LatePreparedRenderReplyIsRetainedWithoutGrantingRevokedContinuation()
    {
        var store=Store;var client=new FakeClient(store){RenderCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously)};
        await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();
        LocalTask? observed=null;session.TaskChanged+=task=>observed=task;
        var rendering=session.RenderPreparedAsync(_=>Task.FromResult(Request));
        Assert.Equal(1,client.PinnedRenders);await session.DisconnectAsync();
        client.RenderCompletion.SetResult(client.Task);
        Assert.Equal(client.Task.Id,(await rendering).Id);Assert.Equal(client.Task.Id,observed!.Id);
        await session.ReadConnectionAsync();await session.PollOnceAsync();
        Assert.Equal("MeshReady",store.Get(client.Task.Id).Status);Assert.Equal(0,client.Continuations);
    }
    [Fact] public async Task PreparationFailureAndServiceTimeoutAreNotReportedAsLocalCancellation()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();
        var failure=new IOException("fixture preparation");
        Assert.Same(failure,await Assert.ThrowsAsync<IOException>(()=>session.RenderPreparedAsync(_=>Task.FromException<RenderRequest>(failure))));
        Assert.Equal(0,client.PinnedRenders);
        client.RenderCompletion=new(TaskCreationOptions.RunContinuationsAsynchronously);
        var rendering=session.RenderPreparedAsync(_=>Task.FromResult(Request));var timeout=new OperationCanceledException("fixture transport timeout");
        client.RenderCompletion.SetException(timeout);
        Assert.Same(timeout,await Assert.ThrowsAsync<OperationCanceledException>(()=>rendering));
    }

    [Fact] public async Task BackgroundPollSkipsTaskOwnedByAVisibleOperationAndResumesAfterRelease()
    {
        var store=Store;var client=new FakeClient(store);await using var session=new Aholo3DSession(client,store);await session.ReadConnectionAsync();await session.RenderAsync(Request);
        using(var claim=session.Resources.Acquire(new[]{new OperationResource("task",client.Task.Id.ToString("N"))}))
        {await session.PollOnceAsync();Assert.Equal(0,client.Queries);Assert.Equal(0,client.Continuations);}
        var observed=new List<bool>();session.Changed+=()=>observed.Add(session.Resources.IsHeld(new OperationResource("task",client.Task.Id.ToString("N"))));
        await session.PollOnceAsync();Assert.Equal(1,client.Queries);Assert.Equal(1,client.Continuations);
        Assert.NotEmpty(observed);Assert.False(observed[^1]);
    }
    private sealed class FakeClient(TaskStore store) : IAholo3DServiceClient
    {
        public event Action? TransportReset;
        public void Reset()=>TransportReset?.Invoke();
        public LocalTask Task {get;private set;}=new(){Kind="render",ConnectionId="account",Status="MeshProcessing",MeshTaskId="mesh-task",Render=Request};
        public int Continuations,Queries,DisposeCalls,PinnedRenders;
        public string? PinnedEpoch;
        public TaskCompletionSource<LocalTask>? RefreshCompletion;
        public TaskCompletionSource RefreshStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailConnection,FailMutation,ThrowOnConnectionCancellation,CancellationCallbackRan,ThrowOnDispose;
        public TaskCompletionSource ConnectionStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);public string Epoch="initial";
        public TaskCompletionSource<LocalTask>? RenderCompletion,ContinueCompletion;
        public TaskCompletionSource? WaitBeforeContinue;
        public TaskCompletionSource ContinueStarted=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ConnectionState>? ConnectCompletion,ConnectionReadCompletion;
        public Task<ConnectionState> GetConnectionAsync(CancellationToken ct=default)
        {
            if(ThrowOnConnectionCancellation)ct.Register(()=>{CancellationCallbackRan=true;throw new InvalidOperationException("fixture callback");});
            ConnectionStarted.TrySetResult();
            return ConnectionReadCompletion?.Task??(FailConnection?System.Threading.Tasks.Task.FromException<ConnectionState>(new IOException("Fixture unavailable")):System.Threading.Tasks.Task.FromResult(new ConnectionState(true,"account",false,Epoch)));
        }
        public Task<ConnectionState> ConnectAsync(ConnectRequest request,CancellationToken ct=default)=>FailMutation?System.Threading.Tasks.Task.FromException<ConnectionState>(new InvalidOperationException("Fixture rejected")):ConnectCompletion?.Task??GetConnectionAsync(ct);
        public Task<ConnectionState> SwitchRegionAsync(string region,CancellationToken ct=default)=>FailMutation?System.Threading.Tasks.Task.FromException<ConnectionState>(new InvalidOperationException("Fixture rejected")):System.Threading.Tasks.Task.FromResult(new ConnectionState(false,null,false,Guid.NewGuid().ToString("N"),region));
        public Task<ConnectionState> DisconnectAsync(CancellationToken ct=default)=>FailMutation?System.Threading.Tasks.Task.FromException<ConnectionState>(new InvalidOperationException("Fixture rejected")):System.Threading.Tasks.Task.FromResult(new ConnectionState(false,null,false));
        public Task<LocalTask> RenderAsync(RenderRequest request,CancellationToken ct=default){store.Save(Task);return RenderCompletion?.Task??System.Threading.Tasks.Task.FromResult(Task);}
        public Task<LocalTask> RenderWithEpochAsync(RenderRequest request,string expectedEpoch,CancellationToken ct=default)
        {PinnedRenders++;PinnedEpoch=expectedEpoch;return RenderAsync(request,ct);}
        public async Task<LocalTask> RefreshAsync(Guid id,CancellationToken ct=default)
        {Queries++;RefreshStarted.TrySetResult();if(RefreshCompletion is not null)return await RefreshCompletion.Task.WaitAsync(ct);Task=store.Get(id);if(Task.Status=="MeshProcessing")Task=Task with{Status="MeshReady",MeshId="mesh"};store.Save(Task);return Task;}
        public async Task<LocalTask> ContinueRenderAsync(Guid id,CancellationToken ct=default)
        {
            ContinueStarted.TrySetResult();
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
        public ValueTask DisposeAsync(){DisposeCalls++;if(ThrowOnDispose)throw new IOException("fixture dispose");return ValueTask.CompletedTask;}
    }
}
