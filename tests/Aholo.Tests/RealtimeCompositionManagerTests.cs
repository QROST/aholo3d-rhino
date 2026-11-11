using Aholo.Core;
using Aholo.Provider;
using Aholo.Sidecar;
namespace Aholo.Tests;

public sealed partial class RealtimePreviewManagerTests
{
    private static RealtimeSceneLayout Committed(Harness h,Guid id)=>h.Manager.Get(id,long.MaxValue,true).Reply.State!.Scene!;
    private static async Task Acknowledge(Harness h,Guid id,long revision)
    {var sent=await h.Backend.Scenes.Reader.ReadAsync().AsTask().WaitAsync(Timeout);sent.Done.SetResult();await Eventually(()=>h.Manager.Get(id,0).Reply.State!.AcknowledgedScene==revision);}
    [Fact]public async Task CompositionAccumulatesOnlyAfterAcknowledgementAndNeverReusesDeletedIds()
    {
        await using var h=await Harness.New();var start=await h.StartStreaming();var original=Committed(h,start.Id);
        var added=original with{Assets=original.Assets.Append(RealtimeLayoutTests.Asset("Model_gs",true)).ToArray(),Lights=new[]{RealtimeLayoutTests.Light()}};
        var accepted=await h.Manager.CompositionAsync(new(start.Id,1,0,added),h.Epoch,default);
        Assert.Equal(1,accepted.Reply.State!.AcceptedScene);Assert.Single(Committed(h,start.Id).Assets);Assert.Empty(Committed(h,start.Id).Lights);
        await h.Backend.Messages.Writer.WriteAsync(new RealtimeJpegFrame(new byte[]{255,216,255,217}));
        await Eventually(()=>h.Manager.Get(start.Id,0).Jpeg is not null);Assert.Equal(0,h.Manager.Get(start.Id,0).Reply.State!.AcknowledgedScene);
        await Acknowledge(h,start.Id,1);Assert.Equal(2,Committed(h,start.Id).Assets.Length);
        var removed=added with{Assets=new[]{added.Assets[1]},Lights=Array.Empty<RealtimeLightSlot>()};
        await h.Manager.CompositionAsync(new(start.Id,2,1,removed),h.Epoch,default);var sent=await h.Backend.Scenes.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Contains("over Mesh \"Geom\" (",sent.Patch);Assert.Contains("over SphereLight \"Light_a\" (",sent.Patch);sent.Done.SetResult();
        await Eventually(()=>h.Manager.Get(start.Id,0).Reply.State!.AcknowledgedScene==2);Assert.Single(Committed(h,start.Id).Assets);Assert.Empty(Committed(h,start.Id).Lights);
        await Assert.ThrowsAsync<ArgumentException>(()=>h.Manager.CompositionAsync(new(start.Id,3,2,added),h.Epoch,default));Assert.Equal(2,h.Backend.SceneCount);
        var fresh=removed with{Assets=removed.Assets.Append(RealtimeLayoutTests.Asset("Model_new")).ToArray()};
        await h.Manager.CompositionAsync(new(start.Id,3,2,fresh),h.Epoch,default);await Acknowledge(h,start.Id,3);Assert.Equal(2,Committed(h,start.Id).Assets.Length);
    }
    [Fact]public async Task SceneRevisionContentAndBaseAreValidatedWithoutReplaying()
    {
        await using var h=await Harness.New();var start=await h.StartStreaming();var changed=Committed(h,start.Id) with{Lights=new[]{RealtimeLayoutTests.Light()}};
        var request=new RealtimeCompositionRequest(start.Id,1,0,changed);
        await h.Manager.CompositionAsync(request,h.Epoch,default);await h.Manager.CompositionAsync(request,h.Epoch,default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.CompositionAsync(request with{Scene=changed with{EnvironmentIntensity=2}},h.Epoch,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.CompositionAsync(request with{Revision=2},h.Epoch,default));
        await Acknowledge(h,start.Id,1);await h.Manager.CompositionAsync(request,h.Epoch,default);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.CompositionAsync(request with{Revision=2},h.Epoch,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>h.Manager.SceneAsync(new(start.Id,2,new(),2,Array.Empty<RenderLight>()),h.Epoch,default));
        Assert.Equal(1,h.Backend.SceneCount);
    }
    [Fact]public async Task SnapshotBeforeCredentialWaitAndReturnedCopiesCannotChangeTheCommittedScene()
    {
        await using var h=await Harness.New();var start=await h.StartStreaming();var next=Committed(h,start.Id) with{Lights=new[]{RealtimeLayoutTests.Light()}};
        Task<RealtimePreviewPacket> sending;
        using(await h.Credentials.AcquireRequestAsync(h.Epoch))
        {
            sending=h.Manager.CompositionAsync(new(start.Id,1,0,next),h.Epoch,default);
            next.Lights[0].Light.Color[0]=99;next.Lights[0].Light.Transform[12]=888;next.Lights[0]=RealtimeLayoutTests.Light("Light_changed");
        }
        await sending;var patch=await h.Backend.Scenes.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Contains("Light_a",patch.Patch);Assert.DoesNotContain("888",patch.Patch);Assert.DoesNotContain("Light_changed",patch.Patch);
        patch.Done.SetResult();await Eventually(()=>h.Manager.Get(start.Id,0).Reply.State!.AcknowledgedScene==1);
        var copy=Committed(h,start.Id);Assert.Equal(1000,copy.Lights[0].Light.Transform[12]);copy.Lights[0].Light.Color[0]=99;
        Assert.Equal(1,Committed(h,start.Id).Lights[0].Light.Color[0]);Assert.Null(h.Manager.Get(start.Id,0).Reply.State!.Scene);
    }
    [Fact]public async Task UncertainCompositionKeepsLastConfirmedSceneAndClosesWithoutReplay()
    {
        await using var h=await Harness.New();var start=await h.StartStreaming();var original=Committed(h,start.Id);
        var next=original with{Assets=original.Assets.Append(RealtimeLayoutTests.Asset("Model_g",true)).ToArray()};
        await h.Manager.CompositionAsync(new(start.Id,1,0,next),h.Epoch,default);
        var update=await h.Backend.Scenes.Reader.ReadAsync().AsTask().WaitAsync(Timeout);update.Done.SetException(new IOException("reply lost"));
        await Eventually(()=>h.Journal.Read(start.Id).Stage=="Closed");
        Assert.Equal(RealtimeLayout.Fingerprint(original),RealtimeLayout.Fingerprint(Committed(h,start.Id)));Assert.Equal(0,h.Manager.Get(start.Id,0).Reply.State!.AcknowledgedScene);
        Assert.Equal(1,h.Backend.SceneCount);Assert.Equal(new[]{false},h.Backend.CloseRetries.ToArray());
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task LateBackendAcknowledgementCannotCommitAfterStopOrCredentialChange(bool changeCredential)
    {
        await using var h=await Harness.New();var start=await h.StartStreaming();h.Backend.IgnoreSceneCancellation=true;
        var original=Committed(h,start.Id);await h.Manager.CompositionAsync(new(start.Id,1,0,original with{Lights=new[]{RealtimeLayoutTests.Light()}}),h.Epoch,default);
        var update=await h.Backend.Scenes.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Task<RealtimePreviewPacket>? stopping=null;
        if(changeCredential)await h.Credentials.DisconnectAsync();else stopping=h.Manager.StopAsync(start.Id,h.Epoch,default);
        await h.Backend.SceneCancelled.Task.WaitAsync(Timeout);update.Done.SetResult();if(stopping is not null)await stopping.WaitAsync(Timeout);
        await Eventually(()=>h.Journal.Read(start.Id).Stage=="Closed");
        Assert.Empty(Committed(h,start.Id).Lights);Assert.Equal(0,h.Manager.Get(start.Id,0).Reply.State!.AcknowledgedScene);
    }
    [Fact]public async Task CompositionBarrierPreservesCameraOrderAndRequiresAcknowledgement()
    {
        await using var h=await Harness.New();var start=await h.StartStreaming();
        await h.Manager.CameraAsync(new(start.Id,1,Camera(1)),h.Epoch,default);var first=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        await h.Manager.CameraAsync(new(start.Id,2,Camera(2)),h.Epoch,default);
        var next=Committed(h,start.Id) with{Lights=new[]{RealtimeLayoutTests.Light()}};await h.Manager.CompositionAsync(new(start.Id,1,0,next),h.Epoch,default);
        await h.Manager.CameraAsync(new(start.Id,3,Camera(3)),h.Epoch,default);await h.Manager.CameraAsync(new(start.Id,4,Camera(4)),h.Epoch,default);
        first.Done.SetResult();var second=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);Assert.Equal(RealtimeScene.CameraPatch(start with{Camera=Camera(2)}),second.Patch);
        Assert.Equal(0,h.Backend.SceneCount);second.Done.SetResult();var scene=await h.Backend.Scenes.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
        Assert.Equal(2,h.Backend.CameraCount);Assert.Empty(Committed(h,start.Id).Lights);scene.Done.SetResult();
        var last=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);Assert.Equal(RealtimeScene.CameraPatch(start with{Camera=Camera(4)}),last.Patch);last.Done.SetResult();
        await Eventually(()=>h.Manager.Get(start.Id,0).Reply.State!.AcknowledgedCamera==4);Assert.Single(Committed(h,start.Id).Lights);
    }
    [Fact]public async Task CancelledAdmissionNeverQueuesAnEditAndCameraSnapshotsBeforeWaiting()
    {
        await using var h=await Harness.New();var start=await h.StartStreaming();Task<RealtimePreviewPacket> scene,camera;
        using var ct=new CancellationTokenSource();var pose=Camera(20);
        using(await h.Credentials.AcquireRequestAsync(h.Epoch))
        {
            scene=h.Manager.CompositionAsync(new(start.Id,1,0,Committed(h,start.Id) with{EnvironmentIntensity=2}),h.Epoch,ct.Token);ct.Cancel();
            camera=h.Manager.CameraAsync(new(start.Id,1,pose),h.Epoch,default);pose.Transform[12]=999;
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>scene);await camera;
        var sent=await h.Backend.Cameras.Reader.ReadAsync().AsTask().WaitAsync(Timeout);Assert.Equal(RealtimeScene.CameraPatch(start with{Camera=Camera(20)}),sent.Patch);sent.Done.SetResult();Assert.Equal(0,h.Backend.SceneCount);
    }
    [Fact]public async Task CompositionAndCommittedSceneTravelThroughAuthenticatedPrivatePipe()
    {
        await using var h=await Harness.New();var start=await h.StartStreaming();
        string name="aholo-rt-"+Guid.NewGuid().ToString("N")[..16],nonce=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        await using var server=new RealtimeControlServer(name,nonce,h.Manager);server.Start();using var deadline=new CancellationTokenSource(Timeout);
        async Task<RealtimePreviewPacket> Send(string method,object payload)
        {
            using var pipe=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous|System.IO.Pipes.PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token);await RealtimeWire.WriteJsonAsync(pipe,new RealtimeControlRequest(1,Guid.NewGuid(),nonce,method,AholoJson.Element(payload),h.Epoch),RealtimeWire.MaximumRequestBytes,deadline.Token);
            await pipe.FlushAsync(deadline.Token);return await RealtimeWire.ReadPacketAsync(pipe,deadline.Token);
        }
        var next=Committed(h,start.Id) with{Lights=new[]{RealtimeLayoutTests.Light()}};
        var queued=await Send("composition",new RealtimeCompositionRequest(start.Id,1,0,next));Assert.Null(queued.Reply.Error);Assert.Equal(1,queued.Reply.State!.AcceptedScene);
        var pending=await Send("get",new{id=start.Id,afterFrame=0,includeScene=true});Assert.Empty(pending.Reply.State!.Scene!.Lights);
        await Acknowledge(h,start.Id,1);var confirmed=await Send("get",new{id=start.Id,afterFrame=0,includeScene=true});Assert.Single(confirmed.Reply.State!.Scene!.Lights);
    }
}
