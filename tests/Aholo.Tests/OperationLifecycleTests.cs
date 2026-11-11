using Aholo.Core;
namespace Aholo.Tests;

public sealed class OperationLifecycleTests
{
    [Fact] public async Task PrefixCancellationNeverCancelsReplacementInSelectedScope()
    {
        var registry=new OperationRegistry();var oldDone=new TaskCompletionSource<int>();
        var replacementDone=new TaskCompletionSource<int>();LocalOperationHandle<int>? replacement=null;
        Task<int>? old=null;bool replacementCancelled=false;
        var first=registry.Start("document:1:first","First",async ct=>
        {
            using var registration=ct.Register(()=>
            {
                oldDone.SetResult(1);
                old!.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                replacement=registry.Start("document:1:second","Replacement",token=>
                {token.Register(()=>replacementCancelled=true);return replacementDone.Task;});
            });
            await Task.Delay(Timeout.Infinite,ct);return 0;
        });
        old=registry.RunAsync("document:1:second","Old",_=>oldDone.Task);
        registry.CancelPrefix("document:1:");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first.Completion);
        Assert.NotNull(replacement);Assert.False(replacementCancelled);Assert.Equal(1,await old);
        replacementDone.SetResult(2);Assert.Equal(2,await replacement.Completion);
    }
    [Fact] public async Task OldIdCannotCancelReplacementAndActiveIdCan()
    {
        var registry=new OperationRegistry();var old=registry.Start("scope","Old",_=>Task.FromResult(3));
        Assert.Equal(3,await old.Completion);
        var current=registry.Start("scope","Current",async ct=>{await Task.Delay(Timeout.Infinite,ct);return 4;});
        Assert.False(registry.Cancel(old.Id));Assert.False(current.Completion.IsCompleted);
        Assert.True(registry.Cancel(current.Id));await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>current.Completion);
        Assert.Equal("Cancelled",registry.Snapshot().Single(x=>x.Id==current.Id).State);
    }
    [Fact] public async Task CancellationDuringStartNotificationDoesNotInvokeWork()
    {
        var registry=new OperationRegistry();bool invoked=false;
        registry.Changed+=()=>registry.CancelPrefix("scope");
        var handle=registry.Start("scope","Queued",_=>{invoked=true;return Task.FromResult(1);});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>handle.Completion);
        Assert.False(invoked);Assert.Equal(handle.Id,Assert.Single(registry.Snapshot()).Id);
    }
    [Fact] public async Task ThrowingCancellationCallbackDoesNotStopOtherCancellationsOrDrain()
    {
        var registry=new OperationRegistry();bool callbackRan=false;
        var first=registry.RunAsync("one","First",async ct=>
        {var delay=Task.Delay(Timeout.Infinite,ct);using var broken=ct.Register(()=>{callbackRan=true;throw new InvalidOperationException("broken callback");});await delay;});
        var second=registry.RunAsync("two","Second",ct=>Task.Delay(Timeout.Infinite,ct));
        await registry.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>second);
        Assert.True(callbackRan);Assert.All(registry.Snapshot(),x=>Assert.Equal("Cancelled",x.State));
    }
    [Fact] public async Task StopRejectsNewWorkAndWaitsForCleanupAndIsIdempotent()
    {
        var registry=new OperationRegistry();var cleaning=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var work=registry.RunAsync("scope","Work",async ct=>
        {try{await Task.Delay(Timeout.Infinite,ct);}finally{cleaning.SetResult();await release.Task;}});
        var stopped=registry.StopAsync();await cleaning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stopped.IsCompleted);Assert.Same(stopped,registry.StopAsync());
        Assert.Throws<ObjectDisposedException>(()=>registry.Start("other","Rejected",_=>Task.FromResult(1)));
        release.SetResult();await stopped;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>work);
        Assert.Equal("Cancelled",Assert.Single(registry.Snapshot()).State);
    }
    [Fact] public async Task SynchronousFailureKeepsIdentityAndAllowsSubsequentWork()
    {
        var registry=new OperationRegistry();
        var failed=registry.Start<int>("scope","Failed",_=>throw new IOException("fixture failure"));
        await Assert.ThrowsAsync<IOException>(()=>failed.Completion);
        var history=Assert.Single(registry.Snapshot());Assert.Equal(failed.Id,history.Id);Assert.Equal("Failed",history.State);Assert.NotNull(history.FinishedAt);
        Assert.Equal(7,await registry.RunAsync("scope","Next",_=>Task.FromResult(7)));
    }
    [Fact] public async Task StopDuringStartNotificationCancelsBeforeInvokingWork()
    {
        var registry=new OperationRegistry();Task? stopped=null;bool invoked=false;
        registry.Changed+=()=>stopped=registry.StopAsync();
        var handle=registry.Start("scope","Work",_=>{invoked=true;return Task.FromResult(1);});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>handle.Completion);
        Assert.NotNull(stopped);await stopped.WaitAsync(TimeSpan.FromSeconds(5));Assert.False(invoked);
    }
    [Fact] public async Task QueuedWorkCanBeCancelledBeforeHostSignalAndUnsubscribes()
    {
        var registry=new OperationRegistry();EventHandler? signal=null;bool invoked=false;int unsubscribed=0;
        var handle=registry.Start("document:1:placement","Place",async ct=>
        {
            await OperationSignal.WaitAsync(h=>signal+=h,h=>{signal-=h;unsubscribed++;},ct);
            invoked=true;return 1;
        });
        Assert.NotNull(signal);registry.Cancel(handle.Id);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>handle.Completion);
        signal?.Invoke(null,EventArgs.Empty);Assert.Null(signal);Assert.False(invoked);Assert.Equal(1,unsubscribed);
    }
    [Fact] public async Task HostSignalRunsWorkOnceAndRemovesHandler()
    {
        EventHandler? signal=null;int invoked=0;
        async Task Work(){await OperationSignal.WaitAsync(h=>signal+=h,h=>signal-=h,CancellationToken.None);invoked++;}
        var work=Work();signal?.Invoke(null,EventArgs.Empty);signal?.Invoke(null,EventArgs.Empty);await work;
        Assert.Null(signal);Assert.Equal(1,invoked);
    }
    [Fact] public async Task CancellationDuringSubscriptionWinsEvenIfSignalAlreadyArrived()
    {
        using var stop=new CancellationTokenSource();EventHandler? signal=null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>OperationSignal.WaitAsync(h=>
        {signal+=h;h(null,EventArgs.Empty);stop.Cancel();},h=>signal-=h,stop.Token));
        Assert.Null(signal);
    }
    [Fact] public async Task PrecancelledQueueDoesNotSubscribe()
    {
        using var stop=new CancellationTokenSource();stop.Cancel();bool subscribed=false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>OperationSignal.WaitAsync(_=>subscribed=true,_=>{},stop.Token));Assert.False(subscribed);
    }
    [Fact] public async Task ExplicitIntentRevocationIsCancelledButUnexpectedCancellationIsFailed()
    {
        var registry=new OperationRegistry();
        var intentional=registry.Start<int>("scope","Dismissed choice",_=>throw new LocalOperationCanceledException());
        await Assert.ThrowsAsync<LocalOperationCanceledException>(()=>intentional.Completion);
        var unexpected=registry.Start<int>("scope","Transport timeout",_=>throw new OperationCanceledException("timeout"));
        await Assert.ThrowsAsync<OperationCanceledException>(()=>unexpected.Completion);
        Assert.Equal("Cancelled",registry.Snapshot().Single(x=>x.Id==intentional.Id).State);
        var failed=registry.Snapshot().Single(x=>x.Id==unexpected.Id);Assert.Equal("Failed",failed.State);Assert.Equal("timeout",failed.Error);
    }
}
