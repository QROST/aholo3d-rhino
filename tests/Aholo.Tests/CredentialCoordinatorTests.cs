using System.Net;
using Aholo.Provider;
namespace Aholo.Tests;

public sealed class CredentialCoordinatorTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-credential-tests-"+Guid.NewGuid().ToString("N"));
    private readonly FakeStore store=new();
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    private CredentialCoordinator Coordinator()=>new(store,root);

    [Theory][InlineData(true)][InlineData(false)]
    public async Task AnotherProcessChangingOrClearingKeyInvalidatesOldMemory(bool remember)
    {
        var a=Coordinator();var first=await a.ConnectAsync(new("fixture-key-one"),true);
        var b=Coordinator();await b.ReadAsync();
        var second=await b.ConnectAsync(new("fixture-key-two"),remember);
        Assert.NotEqual(first.Epoch,second.Epoch);
        var error=await Assert.ThrowsAsync<ProviderException>(()=>a.RequireAsync(first.Epoch));Assert.Equal("credential_changed",error.Code);
        var restored=await a.ReadAsync();Assert.Equal(remember,restored.Key is not null);
        var cleared=await b.DisconnectAsync();Assert.NotEqual(second.Epoch,cleared.Epoch);
        await Assert.ThrowsAsync<ProviderException>(()=>a.RequireAsync(restored.Epoch));
        Assert.Null((await a.ReadAsync()).Key);
        string metadata=File.ReadAllText(Assert.Single(Directory.GetFiles(root,"*.json")));
        Assert.DoesNotContain("fixture-key",metadata);Assert.DoesNotContain("ConnectionId",metadata);
    }
    [Fact]public async Task StaleRealtimeCallerCannotEraseTheNewSessionOnlyConnection()
    {
        var a=Coordinator();var old=await a.ConnectAsync(new("old-fixture-key"),false);
        var current=await a.ConnectAsync(new("new-fixture-key"),false);
        await Assert.ThrowsAsync<ProviderException>(()=>a.RequireAsync(old.Epoch));
        Assert.Equal(current.Key!.ConnectionId,(await a.RequireAsync(current.Epoch)).ConnectionId);
        Assert.Equal(current.Epoch,(await a.ReadAsync()).Epoch);
    }
    [Fact]public async Task SameKeyReconnectCannotReuseAnOldContinuationEpoch()
    {
        var a=Coordinator();var first=await a.ConnectAsync(new("same-fixture-key"),false);
        Assert.NotNull(await a.RequireAsync(first.Epoch));
        var b=Coordinator();await b.DisconnectAsync();var next=await b.ConnectAsync(new("same-fixture-key"),true);
        Assert.Equal(first.Key!.ConnectionId,next.Key!.ConnectionId);Assert.NotEqual(first.Epoch,next.Epoch);
        await Assert.ThrowsAsync<ProviderException>(()=>a.RequireAsync(first.Epoch));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task InterruptedCredentialWriteFailsClosedAndExplicitReconnectRepairs(bool saveBeforeFailure)
    {
        var a=Coordinator();var old=await a.ConnectAsync(new("old-fixture-key"),true);
        store.Fail=true;store.SaveBeforeFailure=saveBeforeFailure;
        await Assert.ThrowsAsync<IOException>(()=>Coordinator().ConnectAsync(new("new-fixture-key"),true));
        await Assert.ThrowsAsync<ProviderException>(()=>a.RequireAsync(old.Epoch));
        await Assert.ThrowsAsync<ProviderException>(()=>Coordinator().ReadAsync());
        store.Fail=false;var repaired=await Coordinator().ConnectAsync(new("repaired-fixture-key"),true);
        Assert.Equal(repaired.Epoch,(await a.ReadAsync()).Epoch);
    }
    [Fact]public async Task AdmissionBlocksCredentialChangeAndWaitIsCancellable()
    {
        var a=Coordinator();var state=await a.ConnectAsync(new("fixture-key"),true);var b=Coordinator();
        using(var request=await a.AcquireRequestAsync(state.Epoch))
        {
            using var cancel=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>b.DisconnectAsync(cancel.Token));
        }
        Assert.NotNull(await a.RequireAsync(state.Epoch));
        await b.DisconnectAsync();await Assert.ThrowsAsync<ProviderException>(()=>a.RequireAsync(state.Epoch));
    }
    [Fact]public async Task AKeyChangePreventsTheNextPaidHttpRequestFromBeingSent()
    {
        var a=Coordinator();var state=await a.ConnectAsync(new("fixture-key"),true);var handler=new CountingHandler();
        using var http=new HttpClient(handler);using var upload=new HttpClient(new CountingHandler());
        var api=new AholoApiClient(http,upload){RequestAdmission=ct=>a.AcquireRequestAsync(state.Epoch,ct)};
        await api.ValidateKeyAsync(state.Key!);Assert.Equal(1,handler.Count);
        await Coordinator().DisconnectAsync();
        var error=await Assert.ThrowsAsync<ProviderException>(()=>api.SubmitRenderAsync(state.Key!,"fixture-request","fixture-usda"));
        Assert.Equal("credential_changed",error.Code);Assert.False(error.SubmissionMayHaveSucceeded);Assert.Equal(1,handler.Count);
    }
    [Fact]public async Task CorruptEpochDoesNotSilentlyAdoptSecureStorage()
    {
        var a=Coordinator();await a.ConnectAsync(new("fixture-key"),true);
        File.WriteAllText(Assert.Single(Directory.GetFiles(root,"*.json")),"invalid");
        await Assert.ThrowsAsync<ProviderException>(()=>a.ReadAsync());
        Assert.NotNull((await a.ConnectAsync(new("new-fixture-key"),true)).Key);
    }
    [Theory][InlineData("cancel")][InlineData("lock")][InlineData("epoch")]
    public async Task FailedAdmissionKeepsOriginalRenderExplicitlyRecoverable(string failure)
    {
        var handler=new CountingHandler();using var http=new HttpClient(handler);using var upload=new HttpClient(new CountingHandler());
        var api=new AholoApiClient(http,upload);
        api.RequestAdmission=_=>System.Threading.Tasks.Task.FromException<IDisposable>(failure switch
        {"cancel"=>new OperationCanceledException(),"lock"=>new IOException("Fixture timeout"),_=>new ProviderException("credential_changed","Fixture change")});
        var taskStore=new Aholo.Core.TaskStore(Path.Combine(root,"tasks"));
        var flow=new Aholo.Sidecar.Workflows(api,taskStore,new Aholo.Sidecar.AssetDownloader(upload,taskStore));
        var key=new ApiKeyCredential("fixture-key");
        var camera=new Aholo.Core.RenderCamera(new double[]{1,0,0,0,0,1,0,0,0,0,1,0,0,0,1000,1},35,36,24,1,100000);
        var task=new Aholo.Core.LocalTask{Kind="render",ConnectionId=key.ConnectionId,Status="MeshReady",MeshTaskId="123",MeshId="mesh",Render=new("Test","unused.glb",camera)};
        taskStore.Save(task);
        var stopped=await flow.ContinueRenderAsync(key,task.Id,default);
        Assert.Equal("MeshReady",stopped.Status);Assert.True(Aholo.Core.TaskCapabilities.For(stopped).CanContinueRender);Assert.Equal(0,handler.Count);
        api.RequestAdmission=null;handler.Response="{\"operationId\":\"op\",\"done\":false}";
        var continued=await flow.ContinueRenderAsync(key,task.Id,default);
        Assert.Equal("op",continued.OperationId);Assert.Equal(1,handler.Count);
    }
    [Fact]public async Task CancellationAfterAdmissionButBeforeDispatchIsDefinitelyUnsentAndReleasesGate()
    {
        var handler=new CountingHandler();using var http=new HttpClient(handler);using var upload=new HttpClient(new CountingHandler());
        using var cancellation=new CancellationTokenSource();var gate=new TrackingGate();
        var api=new AholoApiClient(http,upload){RequestAdmission=_=>{cancellation.Cancel();return System.Threading.Tasks.Task.FromResult<IDisposable>(gate);}};
        var error=await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>api.SubmitRenderAsync(new("fixture-key"),"request","usda",cancellation.Token));
        Assert.IsType<RequestNotSentCanceledException>(error);Assert.Equal(cancellation.Token,error.CancellationToken);
        Assert.True(gate.Disposed);Assert.Equal(0,handler.Count);
    }
    [Fact]public async Task CancellationAfterDispatchStillHasUnknownSubmissionOutcome()
    {
        using var http=new HttpClient(new CancelAfterDispatch());using var upload=new HttpClient(new CountingHandler());
        var api=new AholoApiClient(http,upload);
        var error=await Assert.ThrowsAsync<ProviderException>(()=>api.SubmitRenderAsync(new("fixture-key"),"request","usda"));
        Assert.True(error.SubmissionMayHaveSucceeded);
    }
    private sealed class TrackingGate:IDisposable{public bool Disposed;public void Dispose()=>Disposed=true;}
    private sealed class CancelAfterDispatch:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>System.Threading.Tasks.Task.FromException<HttpResponseMessage>(new OperationCanceledException());
    }
    private sealed class CountingHandler:HttpMessageHandler
    {
        public int Count;public string Response="{\"result\":[]}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken ct)
        {Count++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Response)});}
    }
    private sealed class FakeStore:ICredentialStore
    {
        private string? value;public bool Fail,SaveBeforeFailure;
        public Task<string?> GetAsync(string account,CancellationToken ct=default)=>Task.FromResult(value);
        public Task SetAsync(string account,string secret,CancellationToken ct=default)
        {if(!Fail||SaveBeforeFailure)value=secret;if(Fail)throw new IOException("Fixture store failure");return Task.CompletedTask;}
        public Task DeleteAsync(string account,CancellationToken ct=default){value=null;return Task.CompletedTask;}
    }
}
