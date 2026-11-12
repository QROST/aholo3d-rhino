using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class LibraryNavigationTests
{
    private static TaskPage Page(params LocalTask[] rows)=>new(rows,0,rows.Length,false);
    [Fact] public void PassiveRefreshInheritsPendingRevealAndOnlyNewestReadApplies()
    {
        var state=new LibraryNavigation();var a=new LocalTask();var b=new LocalTask();
        state.Reveal(b.Id);var first=state.BeginRead(new(),a.Id);var refresh=state.BeginRead(new(),a.Id);
        Assert.Equal(b.Id,first.RevealTaskId);Assert.Equal(b.Id,refresh.RevealTaskId);
        Assert.False(state.TryApply(first,Page(a,b),out _));
        Assert.True(state.TryApply(refresh,Page(a,b),out var selected));Assert.Equal(b.Id,selected);
        Assert.Null(state.BeginRead(new(),selected).RevealTaskId);
    }
    [Fact] public void OldRefreshAndItsErrorCannotOverwriteRevealedTask()
    {
        var state=new LibraryNavigation();var a=new LocalTask();var b=new LocalTask();
        var old=state.BeginRead(new(),a.Id);state.Reveal(b.Id);var reveal=state.BeginRead(new(),a.Id);
        Assert.True(state.TryApply(reveal,Page(a,b),out var selected));Assert.Equal(b.Id,selected);
        Assert.False(state.TryApply(old,Page(a),out _));Assert.False(state.IsCurrent(old));
    }
    [Fact] public void UserSelectingAnotherTaskInvalidatesPendingRevealEvenBeforeNewRead()
    {
        var state=new LibraryNavigation();var b=new LocalTask();var c=new LocalTask();
        state.Reveal(b.Id);var read=state.BeginRead(new(),null);state.UserNavigated();
        Assert.False(state.TryApply(read,Page(b),out _));
        var refresh=state.BeginRead(new(),c.Id);Assert.Null(refresh.RevealTaskId);
        Assert.True(state.TryApply(refresh,Page(b,c),out var selected));Assert.Equal(c.Id,selected);
    }
    [Theory]
    [InlineData(50,null,null)]
    [InlineData(0,"world",null)]
    [InlineData(0,null,"new search")]
    public void UserPagingFilteringOrSearchingSupersedesPendingReveal(int offset,string? kind,string? search)
    {
        var state=new LibraryNavigation();var b=new LocalTask();var c=new LocalTask();
        state.Reveal(b.Id);var reveal=state.BeginRead(new(),null);state.UserNavigated();
        var query=new TaskPageRequest(offset,50,kind,search);var read=state.BeginRead(query,null);
        Assert.Equal(query,read.Page);Assert.Null(read.RevealTaskId);
        Assert.False(state.TryApply(reveal,Page(b),out _));
        Assert.True(state.TryApply(read,new(new[]{c},offset,offset+1,false),out var selected));Assert.Equal(c.Id,selected);
    }
    [Fact] public void FailedOrMissingTargetRetainsRevealForNextRefreshWithoutSelectingFirstItem()
    {
        var state=new LibraryNavigation();var a=new LocalTask();var b=new LocalTask();
        state.Reveal(b.Id);var failed=state.BeginRead(new(),a.Id);Assert.True(state.IsCurrent(failed));
        // A failed I/O never calls TryApply. The next passive refresh still locates B.
        var retry=state.BeginRead(new(),a.Id);Assert.Equal(b.Id,retry.RevealTaskId);Assert.False(state.IsCurrent(failed));
        Assert.Throws<IOException>(()=>state.TryApply(retry,Page(a),out _));
        var valid=state.BeginRead(new(),a.Id);Assert.Equal(b.Id,valid.RevealTaskId);
        Assert.True(state.TryApply(valid,Page(a,b),out var selected));Assert.Equal(b.Id,selected);
    }
    [Fact] public void NewestRevealWinsAndCannotBeAppliedTwice()
    {
        var state=new LibraryNavigation();var b=new LocalTask();var c=new LocalTask();
        state.Reveal(b.Id);var old=state.BeginRead(new(),null);state.Reveal(c.Id);var current=state.BeginRead(new(),null);
        Assert.True(state.TryApply(current,Page(c),out var selected));Assert.Equal(c.Id,selected);
        Assert.False(state.TryApply(old,Page(b),out _));Assert.False(state.IsCurrent(old));
        Assert.False(state.TryApply(current,Page(c),out _));
    }
    [Fact] public void PageContainingSnapshotSurvivesInsertionAtPageBoundary()
    {
        string root=Path.Combine(Path.GetTempPath(),"aholo3d-library-nav-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new TaskStore(root);var origin=new DateTimeOffset(2026,1,1,0,0,0,TimeSpan.Zero);
            var target=new LocalTask{CreatedAt=origin,Status="SUCCEEDED"};store.Save(target);
            for(int i=1;i<50;i++)store.Save(new LocalTask{CreatedAt=origin.AddMinutes(i),Status="SUCCEEDED"});
            var state=new LibraryNavigation();state.Reveal(target.Id);var read=state.BeginRead(new(),null);
            var snapshot=store.PageContaining(read.RevealTaskId!.Value);Assert.Equal(0,snapshot.Offset);Assert.Equal(target.Id,snapshot.Items[49].Id);
            store.Save(new LocalTask{CreatedAt=origin.AddMinutes(51),Status="SUCCEEDED"});
            Assert.DoesNotContain(store.Page(new()).Items,x=>x.Id==target.Id);
            Assert.True(state.TryApply(read,snapshot,out var selected));Assert.Equal(target.Id,selected);
            Assert.Equal(50,store.PageContaining(target.Id).Offset);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact] public void OrdinaryRefreshKeepsSelectionAndFallsBackOnlyIfItHasDisappeared()
    {
        var state=new LibraryNavigation();var a=new LocalTask();var b=new LocalTask();
        var read=state.BeginRead(new(),b.Id);Assert.True(state.TryApply(read,Page(a,b),out var selected));Assert.Equal(b.Id,selected);
        read=state.BeginRead(new(),b.Id);Assert.True(state.TryApply(read,Page(a),out selected));Assert.Equal(a.Id,selected);
        read=state.BeginRead(new(),a.Id);Assert.True(state.TryApply(read,Page(),out selected));Assert.Null(selected);
    }
}
