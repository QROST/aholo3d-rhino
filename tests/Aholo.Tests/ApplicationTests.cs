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
    private sealed class FakeClient(TaskStore store) : IAholoServiceClient
    {
        public event Action? TransportReset;
        public void Reset()=>TransportReset?.Invoke();
        public LocalTask Task {get;private set;}=new(){Kind="render",ConnectionId="account",Status="MeshProcessing",MeshTaskId="mesh-task",Render=Request};
        public int Continuations,Queries;
        public Task<ConnectionState> GetConnectionAsync(CancellationToken ct=default)=>System.Threading.Tasks.Task.FromResult(new ConnectionState(true,"account",false));
        public Task<ConnectionState> ConnectAsync(ConnectRequest request,CancellationToken ct=default)=>GetConnectionAsync(ct);
        public Task<ConnectionState> DisconnectAsync(CancellationToken ct=default)=>System.Threading.Tasks.Task.FromResult(new ConnectionState(false,null,false));
        public Task<LocalTask> RenderAsync(RenderRequest request,CancellationToken ct=default){store.Save(Task);return System.Threading.Tasks.Task.FromResult(Task);}
        public Task<LocalTask> RefreshAsync(Guid id,CancellationToken ct=default)
        {Queries++;Task=store.Get(id);if(Task.Status=="MeshProcessing")Task=Task with{Status="MeshReady",MeshId="mesh"};store.Save(Task);return System.Threading.Tasks.Task.FromResult(Task);}
        public Task<LocalTask> ContinueRenderAsync(Guid id,CancellationToken ct=default)
        {Continuations++;Task=store.Get(id) with{Status="Running",OperationId="op"};store.Save(Task);return System.Threading.Tasks.Task.FromResult(Task);}
        public Task<IReadOnlyList<LocalTask>> GetTasksAsync(CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<TaskPage> GetTaskPageAsync(TaskPageRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> CreateServiceAsync(CreateServiceRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> GenerateWorldAsync(GenerateWorldRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> DownloadAsync(DownloadRequest request,CancellationToken ct=default)=>throw new NotSupportedException();
        public Task<LocalTask> ImportWorldAsync(string worldId,CancellationToken ct=default)=>throw new NotSupportedException();
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
