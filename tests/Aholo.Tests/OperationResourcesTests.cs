using Aholo.Core;
namespace Aholo.Tests;

public sealed class OperationResourcesTests
{
    private static readonly OperationResource A=new("candidate","editor-a"),B=new("candidate","editor-b"),C=new("candidate","editor-c");
    [Fact] public void ClaimsAreAtomicAndIndependentResourcesRemainAvailable()
    {
        var resources=new OperationResources();using var first=resources.Acquire(new[]{A,B,A});
        Assert.Null(resources.TryAcquire(new[]{B,C}));Assert.False(resources.IsHeld(C));
        using var independent=resources.Acquire(new[]{C});Assert.Throws<OperationResourceBusyException>(()=>resources.Acquire(new[]{A}));
    }
    [Fact] public void ARepeatedDisposeCannotReleaseAReplacementOwner()
    {
        var resources=new OperationResources();var old=resources.Acquire(new[]{A});old.Dispose();
        using var current=resources.Acquire(new[]{A});old.Dispose();Assert.True(resources.IsHeld(A));Assert.Null(resources.TryAcquire(new[]{A}));
    }
    [Fact] public void CancellationDoesNotReleaseOwnershipBeforeItsCleanup()
    {
        var resources=new OperationResources();using var cancel=new CancellationTokenSource();
        using var owner=resources.Acquire(new[]{A},cancel.Token);cancel.Cancel();
        Assert.Null(resources.TryAcquire(new[]{A}));owner.Dispose();using var afterCleanup=resources.Acquire(new[]{A});
        Assert.ThrowsAny<OperationCanceledException>(()=>resources.Acquire(new[]{B},cancel.Token));Assert.False(resources.IsHeld(B));
    }
    [Fact] public void CallerMutationCannotChangeTheHeldResourceSet()
    {
        var resources=new OperationResources();var keys=new[]{A};using var lease=resources.Acquire(keys);keys[0]=B;
        Assert.True(resources.IsHeld(A));Assert.False(resources.IsHeld(B));lease.Dispose();Assert.False(resources.IsHeld(A));
    }
    [Fact] public async Task ConcurrentOwnersCannotBothAcquireTheSameResult()
    {
        var resources=new OperationResources();var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts=0,winners=0;
        async Task Contend()
        {
            await start.Task;using var lease=resources.TryAcquire(new[]{A});
            if(lease is not null)Interlocked.Increment(ref winners);
            if(Interlocked.Increment(ref attempts)==2)attempted.SetResult();
            await release.Task;
        }
        var first=Task.Run(Contend);var second=Task.Run(Contend);start.SetResult();
        try{await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(1,winners);Assert.True(resources.IsHeld(A));}
        finally{release.SetResult();await Task.WhenAll(first,second);}
        Assert.False(resources.IsHeld(A));
    }
    [Fact] public void PlacementResourceUsesTheReceiptSlotAndDocumentLifetime()
    {
        Guid task=Guid.NewGuid();Guid id=MeshPlacementPolicy.ImportId(task,"mesh");
        var first=MeshPlacementPolicy.ResultResource(1,"session-a",id);
        Assert.Equal(first,MeshPlacementPolicy.ResultResource(1,"session-a",MeshPlacementPolicy.ImportId(task,"mesh")));
        Assert.NotEqual(first,MeshPlacementPolicy.ResultResource(2,"session-a",id));
        Assert.NotEqual(first,MeshPlacementPolicy.ResultResource(1,"session-b",id));
        Assert.NotEqual(first,MeshPlacementPolicy.ResultResource(1,"session-a",MeshPlacementPolicy.ImportId(task,"other")));
    }
    [Fact] public void InvalidResourceSetsDoNotTakePartialOwnership()
    {
        var resources=new OperationResources();Assert.Throws<ArgumentException>(()=>resources.Acquire(new[]{A,default(OperationResource)}));
        Assert.Throws<ArgumentException>(()=>resources.Acquire(Array.Empty<OperationResource>()));Assert.False(resources.IsHeld(A));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task PlacementHoldsItsSlotUntilPreparedAssetCleanupFinishes(bool cancel)
    {
        var resources=new OperationResources();var key=MeshPlacementPolicy.ResultResource(1,"session",Guid.NewGuid());using var cancellation=new CancellationTokenSource();
        var prepared=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var proceed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposing=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var dispose=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int acquired=0,committed=0,released=0;
        Task<MeshPlacementResult> Place()=>MeshTaskPlacement.ExecuteAsync<object>(_=>Task.FromResult<MeshPlacementResult?>(null),
            _=>{acquired++;return Task.FromResult(new TaskArtifact("mesh","Mesh","glb"));},
            async(_,_)=>{prepared.TrySetResult();await proceed.Task;return new();},
            (_,_)=>{committed++;return Task.FromResult(new MeshPlacementResult("Imported","done"));},
            async _=>{released++;disposing.TrySetResult();await dispose.Task;},cancellation.Token,resources,key);
        var first=Place();await prepared.Task;
        Assert.Equal("Deferred",(await Place()).State);Assert.Equal(1,acquired);
        if(cancel)cancellation.Cancel();proceed.SetResult();await disposing.Task;
        // Cancellation does not release a prepared native asset before its UI disposal has finished.
        Assert.True(resources.IsHeld(key));Assert.Null(resources.TryAcquire(new[]{key}));dispose.SetResult();
        if(cancel)await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);else Assert.Equal("Imported",(await first).State);
        Assert.Equal(cancel?0:1,committed);Assert.Equal(1,released);Assert.False(resources.IsHeld(key));
    }
    [Fact] public async Task AutomaticAttemptCanDeferBehindARegisteredCommandAndRunAfterItFinishes()
    {
        var registry=new OperationRegistry();var resources=new OperationResources();Guid task=Guid.NewGuid();
        var key=MeshPlacementPolicy.ResultResource(1,"session",MeshPlacementPolicy.ImportId(task,"mesh"));
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command=registry.RunAsync("document:1:mesh-placement","Command",async ct=>{using var lease=resources.Acquire(new[]{key},ct);started.SetResult();await finish.Task;});
        await started.Task;int prepares=0;
        string scope=MeshPlacementPolicy.AutomaticOperationScope("document:1:",Guid.NewGuid(),task);
        Assert.StartsWith("document:1:",scope);Assert.NotEqual("document:1:mesh-placement",scope);
        Task<MeshPlacementResult> Auto()=>registry.RunAsync(scope,"Automatic",ct=>MeshTaskPlacement.ExecuteAsync<object>(
            _=>Task.FromResult<MeshPlacementResult?>(null),_=>Task.FromResult(new TaskArtifact("mesh","Mesh","glb")),
            (_,_)=>{prepares++;return Task.FromResult(new object());},(_,_)=>Task.FromResult(new MeshPlacementResult("Imported","done")),_=>Task.CompletedTask,ct,resources,key));
        try{Assert.Equal("Deferred",(await Auto()).State);Assert.Equal(0,prepares);}
        finally{finish.SetResult();await command;}
        Assert.Equal("Imported",(await Auto()).State);Assert.Equal(1,prepares);
    }
    [Fact] public async Task ReceiptAndFailureBothReleaseTheSlotWithoutReplayingWork()
    {
        var resources=new OperationResources();var key=MeshPlacementPolicy.ResultResource(1,"session",Guid.NewGuid());int downloads=0;
        Task<MeshPlacementResult> Run(bool existing)=>MeshTaskPlacement.ExecuteAsync<object>(_=>Task.FromResult<MeshPlacementResult?>(existing?new("Existing","receipt"):null),
            _=>{downloads++;throw new IOException("fixture download");},(_,_)=>throw new Exception("unexpected prepare"),(_,_)=>throw new Exception("unexpected commit"),_=>throw new Exception("unexpected cleanup"),default,resources,key);
        Assert.Equal("Existing",(await Run(true)).State);Assert.Equal(0,downloads);Assert.False(resources.IsHeld(key));
        await Assert.ThrowsAsync<IOException>(()=>Run(false));Assert.Equal(1,downloads);Assert.False(resources.IsHeld(key));
    }
}
