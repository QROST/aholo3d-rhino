using Aholo3D.Core;
using System.Text.Json;
namespace Aholo3D.Tests;

public sealed class TaskStoreHistoryIndexTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo3d-index-"+Guid.NewGuid().ToString("N"));
    private readonly TaskStore store;
    public TaskStoreHistoryIndexTests()=>store=new(root);
    private string FileFor(Guid id)=>Path.Combine(root,"tasks",id.ToString("N")+".json");
    public void Dispose()=>Directory.Delete(root,true);
    [Fact] public void ExternalAtomicReplacementAdditionAndDeletionAreVisible()
    {
        var task=new LocalTask{Name="first",Status="SUCCEEDED"};store.Save(task);Assert.Single(store.Page(new()).Items);
        var external=new TaskStore(root);external.Save(task with{Name="replacement with different length"});
        Assert.Equal(0,store.Page(new(Search:"first")).Total);Assert.Equal(1,store.Page(new(Search:"replacement")).Total);
        external.Save(new LocalTask{Name="second",Status="SUCCEEDED"});Assert.Equal(2,store.Page(new()).Total);
        File.Delete(FileFor(task.Id));Assert.Equal("second",Assert.Single(store.Page(new()).Items).Name);
    }
    [Theory][InlineData("Submitting","SubmissionUnknown")][InlineData("Preparing","Interrupted")][InlineData("Uploading","Interrupted")]
    public void LockDependentRecoveryIsNeverCached(string status,string recovered)
    {
        var task=new LocalTask{Status=status};store.Save(task);
        Assert.Equal(recovered,Assert.Single(store.Page(new()).Items).Status);
        using(store.LockTask(task.Id))Assert.Equal(status,Assert.Single(store.Page(new()).Items).Status);
        Assert.Equal(recovered,Assert.Single(store.Page(new()).Items).Status);
    }
    [Fact] public void PageCopiesAllNestedMutableState()
    {
        var task=new LocalTask{Status="SUCCEEDED",Artifacts=new[]{new TaskArtifact("0","mesh","glb")},
            AdditionalFields=new(){{"extra",Aholo3DJson.Element(3)}},Render=new("test","unused",new(new double[16],1,1,1,1,100))};
        store.Save(task);
        var first=Assert.Single(store.Page(new()).Items);first.Artifacts[0]=new("bad","bad","bad");first.Render!.Camera.Transform[0]=123;first.AdditionalFields!.Clear();
        var second=Assert.Single(store.Page(new()).Items);Assert.Equal("0",second.Artifacts[0].Key);Assert.Equal(0,second.Render!.Camera.Transform[0]);Assert.True(second.AdditionalFields!.ContainsKey("extra"));
    }
    [Fact] public void ManualReloadAndRevealBypassUnchangedMetadata()
    {
        var task=new LocalTask{Name="Alpha",Status="SUCCEEDED"};store.Save(task);_=store.Page(new());
        string path=FileFor(task.Id);var stamp=File.GetLastWriteTimeUtc(path);var length=new FileInfo(path).Length;
        new TaskStore(root).Save(task with{Name="Bravo"});Assert.Equal(length,new FileInfo(path).Length);File.SetLastWriteTimeUtc(path,stamp);
        Assert.Equal("Alpha",Assert.Single(store.Page(new()).Items).Name);
        Assert.Equal("Bravo",Assert.Single(store.PageContaining(task.Id).Items).Name);
        new TaskStore(root).Save(task with{Name="Alpha"});File.SetLastWriteTimeUtc(path,stamp);
        Assert.Equal("Alpha",Assert.Single(store.Page(new(),forceRefresh:true).Items).Name);
    }
    [Fact] public void SameStoreSaveInvalidatesEvenRestoredMetadata()
    {
        var task=new LocalTask{Name="Alpha",Status="SUCCEEDED"};store.Save(task);_=store.Page(new());var stamp=File.GetLastWriteTimeUtc(FileFor(task.Id));
        store.Save(task with{Name="Bravo"});File.SetLastWriteTimeUtc(FileFor(task.Id),stamp);Assert.Equal("Bravo",Assert.Single(store.Page(new()).Items).Name);
    }
    [Fact] public void TieOrderingAndContainingPageStayDeterministic()
    {
        var time=DateTimeOffset.UnixEpoch;var rows=Enumerable.Range(0,6).Select(_=>new LocalTask{Status="SUCCEEDED",CreatedAt=time}).ToArray();foreach(var row in rows)store.Save(row);
        var expected=rows.OrderBy(t=>t.Id).ToArray();var page=store.PageContaining(expected[3].Id,2);
        Assert.Equal(2,page.Offset);Assert.Equal(expected.Skip(2).Take(2).Select(t=>t.Id),page.Items.Select(t=>t.Id));
    }
    [Fact] public void CancellationAndUnreadableReplacementRetainJournal()
    {
        var task=new LocalTask{Status="SUCCEEDED"};store.Save(task);_=store.Page(new());
        using var ct=new CancellationTokenSource();ct.Cancel();Assert.ThrowsAny<OperationCanceledException>(()=>store.Page(new(),ct.Token));
        File.WriteAllText(FileFor(task.Id),"{broken");Assert.Equal("RecordError",Assert.Single(store.Page(new()).Items).Status);Assert.Equal("{broken",File.ReadAllText(FileFor(task.Id)));
    }
}
