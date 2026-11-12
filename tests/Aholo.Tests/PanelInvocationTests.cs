using Aholo.Core;
namespace Aholo.Tests;

public sealed class PanelInvocationTests
{
    private static readonly OperationResource Interaction=new("interaction","panel");
    [Fact] public void FinishingOneInvocationDoesNotChangeAnotherTokenOrProgress()
    {
        var tracker=new PanelInvocations(new(),"panel");using var a=new CancellationTokenSource();using var b=new CancellationTokenSource();
        using var download=tracker.Begin("download","Download",Array.Empty<OperationResource>(),7,1,a.Token);
        using var export=tracker.Begin("export","Export",Array.Empty<OperationResource>(),7,1,b.Token);
        Assert.True(download.Report("Downloading"));export.Dispose();
        var remaining=Assert.Single(tracker.Snapshot());Assert.Equal(download.Id,remaining.Id);Assert.Equal("Downloading",remaining.Progress);
        Assert.False(export.Report("late export callback"));b.Cancel();Assert.False(download.Cancellation.IsCancellationRequested);
        a.Cancel();Assert.True(download.Cancellation.IsCancellationRequested);
    }
    [Fact] public void DuplicateOrResourceConflictCannotReplaceAnAcceptedRevealTicket()
    {
        var resources=new OperationResources();var tracker=new PanelInvocations(resources,"panel");
        using var create=tracker.Begin("create","Create",new[]{Interaction},3,9,default,reveal:true);
        Assert.Throws<OperationResourceBusyException>(()=>tracker.Begin("create","Duplicate",Array.Empty<OperationResource>(),3,9,default,reveal:true));
        Assert.Throws<OperationResourceBusyException>(()=>tracker.Begin("render","Render",new[]{Interaction},3,9,default,reveal:true));
        Assert.True(tracker.CanReveal(create,3,9));Assert.False(tracker.CanStart("accept",new[]{Interaction}));
        using var export=tracker.Begin("export","Export",Array.Empty<OperationResource>(),3,9,default);
        Assert.True(tracker.CanReveal(create,3,9));
    }
    [Fact] public void LatestInitiatedRevealWinsWithoutLettingPassiveWorkChangeTheWinner()
    {
        var tracker=new PanelInvocations(new(),"panel");
        using var a=tracker.Begin("a","A",Array.Empty<OperationResource>(),4,2,default,reveal:true);
        using var b=tracker.Begin("b","B",Array.Empty<OperationResource>(),4,2,default,reveal:true);
        using var passive=tracker.Begin("download","Download",Array.Empty<OperationResource>(),4,2,default);
        Assert.False(tracker.CanReveal(a,4,2));Assert.True(tracker.CanReveal(b,4,2));Assert.False(tracker.CanReveal(passive,4,2));
        b.Dispose();Assert.False(tracker.CanReveal(a,4,2));Assert.False(tracker.CanReveal(b,4,2));
    }
    [Fact] public void UserNavigationOwnerTransferAndCancellationEachRevokeAutomaticReveal()
    {
        var tracker=new PanelInvocations(new(),"panel");using var cancel=new CancellationTokenSource();
        using var work=tracker.Begin("create","Create",Array.Empty<OperationResource>(),2,11,cancel.Token,reveal:true);
        Assert.False(tracker.CanReveal(work,3,11));Assert.False(tracker.CanReveal(work,2,12));Assert.True(tracker.CanReveal(work,2,11));
        cancel.Cancel();Assert.False(tracker.CanReveal(work,2,11));
    }
    [Fact] public async Task ClosingWaitsForEveryCleanupAndRejectsNewWorkAndLateProgress()
    {
        var resources=new OperationResources();var tracker=new PanelInvocations(resources,"panel");
        var a=tracker.Begin("a","A",new[]{Interaction},1,1,default);var b=tracker.Begin("b","B",Array.Empty<OperationResource>(),1,1,default);
        var close=tracker.CloseAsync();Assert.Same(close,tracker.CloseAsync());Assert.False(close.IsCompleted);Assert.False(a.Report("late"));
        Assert.False(tracker.CanStart("c",Array.Empty<OperationResource>()));Assert.Throws<ObjectDisposedException>(()=>tracker.Begin("c","C",Array.Empty<OperationResource>(),1,1,default));
        Assert.True(resources.IsHeld(Interaction));a.Dispose();Assert.False(close.IsCompleted);Assert.False(resources.IsHeld(Interaction));
        a.Dispose();Assert.Equal(1,tracker.Count);b.Dispose();await close;Assert.Empty(tracker.Snapshot());
    }
    [Fact] public void AStaleInvocationCannotReleaseOrUpdateItsReplacement()
    {
        var tracker=new PanelInvocations(new(),"panel");var old=tracker.Begin("a","Old",new[]{Interaction},1,1,default);old.Dispose();
        using var current=tracker.Begin("a","Current",new[]{Interaction},1,1,default);
        old.Dispose();Assert.False(old.Report("old"));Assert.Equal("Current",Assert.Single(tracker.Snapshot()).Progress);
        Assert.False(tracker.CanStart("a",new[]{Interaction}));
    }
    [Fact] public void SelectedTaskAndArtifactStayBoundToTheInvocation()
    {
        var tracker=new PanelInvocations(new(),"panel");var task=new LocalTask{Name="original"};var artifact=new TaskArtifact("mesh","original mesh","glb");
        using var work=tracker.Begin("download","Download",Array.Empty<OperationResource>(),1,1,default,selectedTask:task,selectedArtifact:artifact);
        task=new LocalTask{Name="later"};artifact=new("other","later image","image");
        Assert.Equal("original",work.SelectedTask!.Name);Assert.Equal("mesh",work.SelectedArtifact!.Key);
    }
    [Fact] public void TwoPanelsShareTaskClaimsButNotIndependentActionClaims()
    {
        var resources=new OperationResources();var first=new PanelInvocations(resources,"first");var second=new PanelInvocations(resources,"second");
        var task=new OperationResource("task",Guid.NewGuid().ToString("N"));
        using var query=first.Begin("refresh","Refresh",new[]{task},1,1,default);
        Assert.False(second.CanStart("download",new[]{task}));
        Assert.Throws<OperationResourceBusyException>(()=>second.Begin("download","Download",new[]{task},1,2,default));
        using var export=second.Begin("export","Export",Array.Empty<OperationResource>(),1,2,default);query.Dispose();
        using var download=second.Begin("download","Download",new[]{task},1,2,default);Assert.Equal(2,second.Count);
    }
    [Fact] public async Task PreciseOperationCancellationLeavesTheOtherInvocationAndDrainIntact()
    {
        var registry=new OperationRegistry();var tracker=new PanelInvocations(new(),"panel");
        var firstReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var secondReady=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFinish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first=registry.Start("document:1:first","First",async ct=>
        {using var work=tracker.Begin("first","First",Array.Empty<OperationResource>(),1,1,ct);firstReady.SetResult();await Task.Delay(Timeout.Infinite,ct);return true;});
        var second=registry.Start("document:1:second","Second",async ct=>
        {using var work=tracker.Begin("second","Second",Array.Empty<OperationResource>(),1,1,ct);secondReady.SetResult();await secondFinish.Task.WaitAsync(ct);return true;});
        await Task.WhenAll(firstReady.Task,secondReady.Task);
        Assert.True(registry.Cancel(first.Id,"document:1:"));await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first.Completion);
        Assert.False(second.Cancellation.IsCancellationRequested);Assert.Single(tracker.Snapshot());
        var drain=tracker.CloseAsync();Assert.False(drain.IsCompleted);secondFinish.SetResult();await second.Completion;await drain;
    }
}
