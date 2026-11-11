using System.Collections.Concurrent;
using System.Threading.Channels;
using Aholo.Core;
using Aholo.Provider;
using Aholo.Sidecar;
namespace Aholo.Tests;

public sealed partial class RealtimePreviewManagerTests
{
    private static readonly TimeSpan Timeout=TimeSpan.FromSeconds(5);
    internal static RenderCamera Camera(double x=0)=>new(new[]{1d,0,0,0,0,1,0,0,0,0,1,0,x,0,3000,1},35,36,20.25,.1,100000);
    private static async Task Eventually(Func<bool> predicate){using var timeout=new CancellationTokenSource(Timeout);while(!predicate())await Task.Delay(10,timeout.Token);}
    [Fact]
    public async Task DurableIntentPrecedesCreateAndStartDoesNotWaitForRemoteInitialization()
    {
        await using var h=await Harness.New();var request=h.Request();
        var response=await h.Manager.StartAsync(request,h.Epoch,default).WaitAsync(Timeout);
        await h.Backend.Created.Task.WaitAsync(Timeout);
        Assert.Equal("Creating",h.Journal.Read(request.Id).Stage);Assert.Null(response.Reply.State!.Record.SessionId);
        Assert.Equal(request.Id.ToString("N"),h.Backend.RequestId);Assert.False(h.Backend.CreateReply.Task.IsCompleted);
        h.Backend.CreateReply.SetException(new ProviderException("unknown","The create reply was lost.",true));
        await Eventually(()=>h.Journal.Read(request.Id).Stage=="SubmissionUnknown");
        Assert.True(h.Journal.Read(request.Id).NeedsCleanup);Assert.Equal(1,h.Backend.Creates);
    }
    [Fact]
    public async Task LostReplySurvivesRestartAndBlocksAnotherCreateUntilExplicitExternalResolution()
    {
        await using var h=await Harness.New();var request=h.Request();await h.Manager.StartAsync(request,h.Epoch,default);
        await h.Backend.Created.Task.WaitAsync(Timeout);h.Backend.CreateReply.SetException(new ProviderException("unknown","Lost reply.",true));
        await Eventually(()=>h.Journal.Read(request.Id).Stage=="SubmissionUnknown");
        await h.Manager.ShutdownAsync(default).WaitAsync(Timeout);
        var restarted=h.NewManager();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>restarted.StartAsync(h.Request(),h.Epoch,default));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>restarted.StopAsync(request.Id,h.Epoch,default));
        Assert.Contains("may exist",error.Message);Assert.Equal(1,h.Backend.Creates);
        var acknowledged=await restarted.ConfirmExternalResolutionAsync(request.Id,h.Epoch,default);
        Assert.Equal("ExternallyResolved",acknowledged.Reply.State!.Record.Stage);Assert.False(acknowledged.Reply.State.Record.NeedsCleanup);
        Assert.Empty(h.Journal.Pending());await restarted.ShutdownAsync(default);
    }
    [Fact]
    public async Task FailedCleanupRemainsDurableAndOnlyExplicitStopRetries()
    {
        await using var h=await Harness.New();h.Backend.FailFirstClose=true;var request=await h.StartStreaming();
        var stopped=await h.Manager.StopAsync(request.Id,h.Epoch,default).WaitAsync(Timeout);
        Assert.Equal("CleanupRequired",stopped.Reply.State!.Record.Stage);Assert.True(h.Journal.Read(request.Id).NeedsCleanup);
        Assert.Equal(new[]{false},h.Backend.CloseRetries.ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.StartAsync(h.Request(),h.Epoch,default));
        var retried=await h.Manager.StopAsync(request.Id,h.Epoch,default);
        Assert.False(retried.Reply.State!.Record.NeedsCleanup);Assert.Empty(h.Journal.Pending());Assert.Equal(new[]{false,true},h.Backend.CloseRetries.ToArray());
    }
    [Fact]
    public async Task CancellingExplicitCleanupWaitDoesNotReleaseOwnershipBeforeItsResultIsSaved()
    {
        await using var h=await Harness.New();h.Backend.FailFirstClose=true;var request=await h.StartStreaming();
        await h.Manager.StopAsync(request.Id,h.Epoch,default);h.Backend.HoldExplicitClose=true;
        using var cancellation=new CancellationTokenSource();var stop=h.Manager.StopAsync(request.Id,h.Epoch,cancellation.Token);
        await h.Backend.ExplicitClosing.Task.WaitAsync(Timeout);cancellation.Cancel();
        Assert.False(stop.IsCompleted);Assert.Throws<IOException>(()=>h.Journal.Lock(request.Id));
        h.Backend.CloseReply.TrySetResult();var result=await stop.WaitAsync(Timeout);Assert.False(result.Reply.State!.Record.NeedsCleanup);
    }
    [Fact]
    public async Task CameraEditsCoalesceFinalValueAndImageArrivalDoesNotAcknowledgeCamera()
    {
        await using var h=await Harness.New();var request=await h.StartStreaming();
        await h.Manager.CameraAsync(new(request.Id,1,Camera(1)),h.Epoch,default);
        var first=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        await h.Manager.CameraAsync(new(request.Id,2,Camera(2)),h.Epoch,default);
        await h.Manager.CameraAsync(new(request.Id,3,Camera(3)),h.Epoch,default);
        await h.Backend.Messages.Writer.WriteAsync(new RealtimeJpegFrame(new byte[]{255,216,255,217}));
        await Eventually(()=>h.Manager.Get(request.Id,0).Jpeg is not null);
        Assert.Equal(0,h.Manager.Get(request.Id,0).Reply.State!.AcknowledgedCamera);
        first.Done.TrySetResult();var last=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(RealtimeScene.CameraPatch(request with{Camera=Camera(3)}),last.Patch);Assert.Equal(2,h.Backend.CameraCount);
        last.Done.TrySetResult();await Eventually(()=>h.Manager.Get(request.Id,0).Reply.State!.AcknowledgedCamera==3);
        Assert.Null(h.Manager.Get(request.Id,1).Jpeg);Assert.False(RealtimeCapabilities.HasCameraFrameAssociation);
    }
    [Fact]
    public async Task CredentialChangeWaitsForPreviewCleanupAndRejectsOldEpoch()
    {
        await using var h=await Harness.New();var request=await h.StartStreaming();
        using(await h.Manager.BeforeCredentialChangeAsync(default))await h.Credentials.DisconnectAsync();
        Assert.False(h.Journal.Read(request.Id).NeedsCleanup);
        await Assert.ThrowsAsync<ProviderException>(()=>h.Manager.CameraAsync(new(request.Id,1,Camera()),h.Epoch,default));
        Assert.Equal(1,h.Backend.Creates);
    }
    [Fact]
    public async Task StopBeforeStartRecordsATombstoneAndPreventsLatePaidCreation()
    {
        await using var h=await Harness.New();var request=h.Request();
        var stopped=await h.Manager.StopAsync(request.Id,h.Epoch,default);
        Assert.Equal("NotSubmitted",stopped.Reply.State!.Record.Stage);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.StartAsync(request,h.Epoch,default));
        Assert.Equal(0,h.Backend.Creates);Assert.False(h.Journal.Read(request.Id).NeedsCleanup);
    }
    [Fact]
    public async Task InvalidCameraCannotCreateAnIntentOrSendARequest()
    {
        await using var h=await Harness.New();var request=h.Request() with{Camera=Camera() with{Near=double.NaN}};
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>h.Manager.StartAsync(request,h.Epoch,default));
        Assert.False(h.Journal.Exists(request.Id));Assert.Equal(0,h.Backend.Creates);
    }
    [Fact]
    public async Task CameraCoalescingCannotCrossASceneBarrierOrAcknowledgeItEarly()
    {
        await using var h=await Harness.New();var request=await h.StartStreaming();
        await h.Manager.CameraAsync(new(request.Id,1,Camera(1)),h.Epoch,default);
        var first=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        await h.Manager.CameraAsync(new(request.Id,2,Camera(2)),h.Epoch,default);
        var change=new RealtimeSceneRequest(request.Id,1,new(100,0,0),.8,Array.Empty<RenderLight>());
        var accepted=await h.Manager.SceneAsync(change,h.Epoch,default);
        Assert.Equal(1,accepted.Reply.State!.AcceptedScene);Assert.Equal(0,accepted.Reply.State.AcknowledgedScene);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.SceneAsync(change with{Revision=2},h.Epoch,default));
        await h.Manager.SceneAsync(change,h.Epoch,default); // Same accepted revision cannot enqueue twice.
        await h.Manager.CameraAsync(new(request.Id,3,Camera(3)),h.Epoch,default);
        await h.Manager.CameraAsync(new(request.Id,4,Camera(4)),h.Epoch,default);
        first.Done.SetResult();var second=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(RealtimeScene.CameraPatch(request with{Camera=Camera(2)}),second.Patch);Assert.Equal(0,h.Backend.SceneCount);
        second.Done.SetResult();var scene=await h.Backend.Scenes.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(RealtimeScene.ScenePatch(request,change),scene.Patch);Assert.Equal(0,h.Manager.Get(request.Id,0).Reply.State!.AcknowledgedScene);
        scene.Done.SetResult();var final=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(RealtimeScene.CameraPatch(request with{Camera=Camera(4)}),final.Patch);final.Done.SetResult();
        await Eventually(()=>h.Manager.Get(request.Id,0).Reply.State!.AcknowledgedCamera==4);
        Assert.Equal(1,h.Manager.Get(request.Id,0).Reply.State!.AcknowledgedScene);Assert.Equal(1,h.Backend.SceneCount);Assert.Equal(3,h.Backend.CameraCount);
    }
    [Fact]
    public async Task UncertainSceneReplyClosesOriginalSessionWithoutReplayOrFalseAcknowledgement()
    {
        await using var h=await Harness.New();var request=await h.StartStreaming();
        await h.Manager.SceneAsync(new(request.Id,1,new(),2,Array.Empty<RenderLight>()),h.Epoch,default);
        var scene=await h.Backend.Scenes.Reader.ReadAsync().AsTask().WaitAsync(Timeout);scene.Done.SetException(new IOException("reply lost"));
        await Eventually(()=>h.Journal.Read(request.Id).Stage=="Closed");
        Assert.Equal(1,h.Backend.Creates);Assert.Equal(1,h.Backend.SceneCount);Assert.Equal(0,h.Manager.Get(request.Id,0).Reply.State!.AcknowledgedScene);
        Assert.False(h.Journal.Read(request.Id).NeedsCleanup);Assert.Equal(new[]{false},h.Backend.CloseRetries.ToArray());
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.SceneAsync(new(request.Id,2,new(),3,Array.Empty<RenderLight>()),h.Epoch,default));
        Assert.Contains("closing or closed",error.Message);
    }
    [Fact]
    public async Task InvalidSceneAndWrongEpochCannotEnterQueue()
    {
        await using var h=await Harness.New();var request=await h.StartStreaming();var patch=new RealtimeSceneRequest(request.Id,1,new(),1,Array.Empty<RenderLight>());
        await Assert.ThrowsAsync<ProviderException>(()=>h.Manager.SceneAsync(patch,"old-epoch",default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>h.Manager.SceneAsync(patch with{EnvironmentIntensity=0},h.Epoch,default));
        await Assert.ThrowsAsync<ArgumentException>(()=>h.Manager.SceneAsync(patch with{Revision=0},h.Epoch,default));
        Assert.Equal(0,h.Backend.SceneCount);Assert.Equal(0,h.Manager.Get(request.Id,0).Reply.State!.AcceptedScene);
    }
    [Fact]
    public async Task RealPipeSceneReplySeparatesQueueAcceptanceFromServerAcknowledgement()
    {
        await using var h=await Harness.New();var request=await h.StartStreaming();
        string name="aholo-rt-"+Guid.NewGuid().ToString("N"),nonce=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using var server=new RealtimeControlServer(name,nonce,h.Manager);server.Start();
        using var deadline=new CancellationTokenSource(Timeout);
        async Task<RealtimePreviewPacket> Send(string method,object payload)
        {
            using var pipe=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous|System.IO.Pipes.PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token);
            await RealtimeWire.WriteJsonAsync(pipe,new RealtimeControlRequest(1,Guid.NewGuid(),nonce,method,AholoJson.Element(payload),h.Epoch),RealtimeWire.MaximumRequestBytes,deadline.Token);
            await pipe.FlushAsync(deadline.Token);return await RealtimeWire.ReadPacketAsync(pipe,deadline.Token);
        }
        var accepted=await Send("scene",new RealtimeSceneRequest(request.Id,1,new(),2,Array.Empty<RenderLight>()));
        Assert.Null(accepted.Reply.Error);Assert.Equal(1,accepted.Reply.State!.AcceptedScene);Assert.Equal(0,accepted.Reply.State.AcknowledgedScene);
        var update=await h.Backend.Scenes.Reader.ReadAsync(deadline.Token);update.Done.SetResult();
        await Eventually(()=>h.Manager.Get(request.Id,0).Reply.State!.AcknowledgedScene==1);
        var committed=await Send("get",new{id=request.Id,afterFrame=0});Assert.Equal(1,committed.Reply.State!.AcknowledgedScene);
    }
    [Fact]public async Task ChinaPreviewRecordsRegionAndSwitchCleansUsingOriginalIdentity()
    {
        await using var h=await Harness.New("china");var request=await h.StartStreaming();
        Assert.Equal("china",h.Journal.Read(request.Id).Region);Assert.Equal("china",h.BackendRegion);
        using(await h.Manager.BeforeCredentialChangeAsync(default))await h.Credentials.SwitchRegionAsync("international");
        Assert.False(h.Journal.Read(request.Id).NeedsCleanup);Assert.Equal("china",h.BackendRegion);
        await Assert.ThrowsAsync<ProviderException>(()=>h.Manager.CameraAsync(new(request.Id,1,Camera()),h.Epoch,default));
    }
    [Fact]public async Task CleanupCannotUseSameKeyTextFromAnotherRegion()
    {
        await using var h=await Harness.New("china");h.Backend.FailFirstClose=true;var request=await h.StartStreaming();
        await h.Manager.StopAsync(request.Id,h.Epoch,default);
        var next=await h.Credentials.ConnectAsync(new("fixture-only","international"),false);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.StopAsync(request.Id,next.Epoch,default));
        Assert.True(h.Journal.Read(request.Id).NeedsCleanup);Assert.Single(h.Backend.CloseRetries);
        var original=await h.Credentials.ConnectAsync(new("fixture-only","china"),false);
        await h.Manager.StopAsync(request.Id,original.Epoch,default);Assert.Equal("china",h.BackendRegion);Assert.False(h.Journal.Read(request.Id).NeedsCleanup);
    }
    [Fact]public async Task UnknownCleanupResolutionRequiresOriginalRegion()
    {
        await using var h=await Harness.New("china");var request=h.Request();await h.Manager.StartAsync(request,h.Epoch,default);
        await h.Backend.Created.Task.WaitAsync(Timeout);h.Backend.CreateReply.SetException(new ProviderException("unknown","fixture",true));
        await Eventually(()=>h.Journal.Read(request.Id).Stage=="SubmissionUnknown");
        // Follow the production credential-change barrier: the unknown journal state
        // precedes completion of the worker that still owns its file lease.
        string nextEpoch;
        using(await h.Manager.BeforeCredentialChangeAsync(default).WaitAsync(Timeout))
            nextEpoch=(await h.Credentials.ConnectAsync(new("fixture-only","international"),false)).Epoch;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.ConfirmExternalResolutionAsync(request.Id,nextEpoch,default));
        string originalEpoch;
        using(await h.Manager.BeforeCredentialChangeAsync(default).WaitAsync(Timeout))
            originalEpoch=(await h.Credentials.ConnectAsync(new("fixture-only","china"),false)).Epoch;
        await h.Manager.ConfirmExternalResolutionAsync(request.Id,originalEpoch,default);Assert.False(h.Journal.Read(request.Id).NeedsCleanup);
    }
    private sealed class FakeBackend:IRealtimePreviewBackend
    {
        internal readonly TaskCompletionSource Created=new(TaskCreationOptions.RunContinuationsAsynchronously),Connected=new(TaskCreationOptions.RunContinuationsAsynchronously),ExplicitClosing=new(TaskCreationOptions.RunContinuationsAsynchronously),CloseReply=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<RealtimeOperation> CreateReply=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SceneCancelled=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly Channel<RealtimeStreamMessage> Messages=Channel.CreateUnbounded<RealtimeStreamMessage>();
        internal readonly Channel<(string Patch,TaskCompletionSource Done)> Cameras=Channel.CreateUnbounded<(string,TaskCompletionSource)>();
        internal readonly Channel<(string Patch,TaskCompletionSource Done)> Scenes=Channel.CreateUnbounded<(string,TaskCompletionSource)>();
        internal readonly ConcurrentQueue<bool> CloseRetries=new();
        internal int Creates,CameraCount,SceneCount;internal bool FailFirstClose,HoldExplicitClose,IgnoreSceneCancellation;internal string? RequestId;
        public async Task<RealtimeOperation> CreateAsync(string requestId,string usda,CancellationToken ct){Interlocked.Increment(ref Creates);RequestId=requestId;Created.TrySetResult();return await CreateReply.Task.WaitAsync(ct);}
        public Task<RealtimeOperation> QueryAsync(string id,CancellationToken ct)=>throw new InvalidOperationException("Not used.");
        public Task ConnectAsync(RealtimeOperation operation,CancellationToken ct){Connected.TrySetResult();return Task.CompletedTask;}
        public async Task<RealtimeStreamMessage> ReadAsync(CancellationToken ct)=>await Messages.Reader.ReadAsync(ct);
        public async Task SceneAsync(string patch,CancellationToken ct){Interlocked.Increment(ref SceneCount);var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);await Scenes.Writer.WriteAsync((patch,done),ct);using var registration=ct.Register(()=>SceneCancelled.TrySetResult());if(IgnoreSceneCancellation)await done.Task;else await done.Task.WaitAsync(ct);}
        public async Task CameraAsync(string patch,CancellationToken ct){Interlocked.Increment(ref CameraCount);var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);await Cameras.Writer.WriteAsync((patch,done),ct);await done.Task.WaitAsync(ct);}
        public async Task CloseAsync(string id,bool retry)
        {
            CloseRetries.Enqueue(retry);if(FailFirstClose&&CloseRetries.Count==1)throw new ProviderException("cleanup","Cleanup failed.");
            if(retry&&HoldExplicitClose){ExplicitClosing.TrySetResult();await CloseReply.Task;}
        }
    }
    private sealed class MemoryStore:ICredentialStore
    {
        private readonly Dictionary<string,string> values=new();
        public Task<string?> GetAsync(string account,CancellationToken ct=default)=>Task.FromResult(values.GetValueOrDefault(account));
        public Task SetAsync(string account,string value,CancellationToken ct=default){values[account]=value;return Task.CompletedTask;}
        public Task DeleteAsync(string account,CancellationToken ct=default){values.Remove(account);return Task.CompletedTask;}
    }
    private sealed class Harness:IAsyncDisposable
    {
        private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-realtime-tests-"+Guid.NewGuid());
        internal readonly FakeBackend Backend=new();internal CredentialCoordinator Credentials=null!;internal RealtimeJournal Journal=null!;internal RealtimePreviewManager Manager=null!;internal string Epoch="";
        internal string? BackendRegion;
        internal static async Task<Harness> New(string region="international"){var h=new Harness();h.Credentials=new(new MemoryStore(),Path.Combine(h.root,"credentials"));h.Epoch=(await h.Credentials.ConnectAsync(new ApiKeyCredential("fixture-only",region),false)).Epoch;h.Journal=new(h.root);h.Manager=h.NewManager();return h;}
        internal RealtimePreviewManager NewManager()=>new(Credentials,Journal,(key,_)=>{BackendRegion=key.Region;return Backend;},TimeSpan.FromMilliseconds(1));
        internal RealtimeStartRequest Request()=>new(Guid.NewGuid(),"mesh-fixture",Camera());
        internal async Task<RealtimeStartRequest> StartStreaming(RealtimeSceneLayout? scene=null)
        {
            var request=Request() with{Scene=scene};await Manager.StartAsync(request,Epoch,default);
            await Backend.Created.Task.WaitAsync(Timeout);Backend.CreateReply.SetResult(new("session-1",true,RealtimeSessionStatus.Active,0,null,null));
            await Backend.Connected.Task.WaitAsync(Timeout);await Backend.Messages.Writer.WriteAsync(new RealtimeStatusMessage(RealtimeStatusCode.Initialized));
            await Eventually(()=>Manager.Get(request.Id,0).Reply.State!.Record.Stage=="Streaming");return request;
        }
        public async ValueTask DisposeAsync(){Backend.CloseReply.TrySetResult();using var stop=new CancellationTokenSource(Timeout);await Manager.ShutdownAsync(stop.Token);Directory.Delete(root,true);}
    }
}
