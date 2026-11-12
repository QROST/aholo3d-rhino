using Aholo.Core;
namespace Aholo.Tests;

public sealed class DeferredDisposalTests
{
    private sealed class Asset(Action? onDispose=null) : IDisposable
    {public int Disposals;public void Dispose(){Disposals++;onDispose?.Invoke();}}
    [Theory][InlineData(false)][InlineData(true)]
    public void LifecycleReentryRetiresButDoesNotDestroyAnAssetBorrowedByCommit(bool close)
    {
        var owner=new DeferredDisposals();var asset=new Asset();bool allowed=true;
        void Lifecycle(){allowed=false;if(close)owner.Close();owner.Retire(asset);}
        using(var borrowed=owner.Borrow())
        {
            Lifecycle();Assert.False(allowed);Assert.Equal(0,asset.Disposals);
            // Native commit can finish validation/rollback using its still-live prepared asset.
            Assert.Equal(0,asset.Disposals);
        }
        Assert.Equal(1,asset.Disposals);
    }
    [Fact] public void NestedBorrowersAndRepeatedRetirementDisposeOnlyAfterTheLastBorrow()
    {
        var owner=new DeferredDisposals();var asset=new Asset();var first=owner.Borrow();var second=owner.Borrow();
        owner.Retire(asset);owner.Retire(asset);first.Dispose();first.Dispose();Assert.Equal(0,asset.Disposals);
        second.Dispose();second.Dispose();Assert.Equal(1,asset.Disposals);
    }
    [Fact] public void CleanupFailureStillAttemptsEveryRetiredAssetExactlyOnce()
    {
        var owner=new DeferredDisposals();var first=new Asset(()=>throw new IOException("fixture disposal"));var second=new Asset();
        var borrowed=owner.Borrow();owner.Retire(first);owner.Retire(second);owner.Close();
        var error=Assert.Throws<AggregateException>(()=>borrowed.Dispose());Assert.Single(error.InnerExceptions);
        Assert.Equal(1,first.Disposals);Assert.Equal(1,second.Disposals);borrowed.Dispose();Assert.Equal(1,second.Disposals);
    }
    [Fact] public void CommitFailureAndCleanupFailureBothRemainAvailableForRecovery()
    {
        var owner=new DeferredDisposals();var primary=new InvalidOperationException("fixture rollback unconfirmed");var cleanup=new IOException("fixture cleanup failure");
        var asset=new Asset(()=>throw cleanup);
        var error=Assert.Throws<AggregateException>(()=>owner.RunBorrowed<bool>(()=>{owner.Retire(asset);throw primary;}));
        Assert.Same(primary,error.InnerExceptions[0]);Assert.Same(cleanup,Assert.IsType<AggregateException>(error.InnerExceptions[1]).InnerException);
        Assert.Contains("rollback unconfirmed",error.Message);Assert.Contains("cleanup failure",error.Message);Assert.Equal(1,asset.Disposals);
    }
    [Fact] public void CommitFailureWithoutCleanupFailurePreservesItsOriginalException()
    {
        var owner=new DeferredDisposals();var primary=new OperationCanceledException("fixture cancelled");var asset=new Asset();
        Assert.Same(primary,Assert.Throws<OperationCanceledException>(()=>owner.RunBorrowed<bool>(()=>{owner.Retire(asset);throw primary;})));
        Assert.Equal(1,asset.Disposals);
    }
    [Fact] public void CloseRejectsNewBorrowersButAllowsFinalResourceRetirement()
    {
        var owner=new DeferredDisposals();owner.Close();Assert.Throws<ObjectDisposedException>(()=>owner.Borrow());
        var asset=new Asset();owner.Retire(asset);Assert.Equal(1,asset.Disposals);
    }
    [Fact] public void DisposalCallbackCanRetireAnotherResourceWithoutRetainingOwnership()
    {
        var owner=new DeferredDisposals();var second=new Asset();var first=new Asset(()=>owner.Retire(second));
        using(var borrowed=owner.Borrow())owner.Retire(first);
        Assert.Equal(1,first.Disposals);Assert.Equal(1,second.Disposals);
    }
}
