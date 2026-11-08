using System.Text;
using System.Text.Json;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class TaskStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aholo-journal-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    [Fact]
    public void ActiveOwnerIsNotRecoveredByAnotherReader()
    {
        var writer = new TaskStore(root); var reader = new TaskStore(root);
        var task = new LocalTask { Status = "Submitting" };
        using (writer.LockTask(task.Id))
        {
            writer.Save(task);
            Assert.Equal("Submitting", reader.Get(task.Id).Status);
            Assert.Equal("Submitting", Assert.Single(reader.List()).Status);
            Assert.Throws<IOException>(() => reader.LockTask(task.Id));
            using var unrelated = reader.LockTask(Guid.NewGuid());
        }
        Assert.Equal("SubmissionUnknown", reader.Get(task.Id).Status);
    }
    [Fact]
    public void LegacyWriteBacksUpOriginalAndPreservesUnknownFields()
    {
        var store = new TaskStore(root); var id=Guid.NewGuid();
        string path=Path.Combine(root,"tasks",id.ToString("N")+".json");
        byte[] original=JsonSerializer.SerializeToUtf8Bytes(new { id, kind="world", status="RUNNING", worldId="original", futureMetadata=new { opaque="keep" } });
        File.WriteAllBytes(path,original);
        var task=store.Get(id); Assert.Equal(1,task.SchemaVersion);
        store.Save(task with {Status="SUCCEEDED"});
        Assert.Equal(original,File.ReadAllBytes(Assert.Single(Directory.GetFiles(Path.Combine(root,"tasks","backups")))));
        Assert.Equal("keep",store.Get(id).AdditionalFields!["futureMetadata"].GetProperty("opaque").GetString());
        store.Save(store.Get(id));
        Assert.Single(Directory.GetFiles(Path.Combine(root,"tasks","backups")));
    }
    [Fact]
    public void FutureSchemaRemainsVisibleAndCannotBeOverwritten()
    {
        var store=new TaskStore(root); var task=new LocalTask {SchemaVersion=99,Status="RUNNING"};
        string path=Path.Combine(root,"tasks",task.Id.ToString("N")+".json");
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(task,AholoJson.Options);File.WriteAllBytes(path,bytes);
        var read=store.Get(task.Id);Assert.Equal("UnsupportedSchema",read.Status);
        Assert.False(TaskCapabilities.For(read).CanQuery);
        Assert.Throws<IOException>(()=>store.Save(read));
        Assert.Throws<IOException>(()=>store.Save(read with {SchemaVersion=1}));
        Assert.Equal(bytes,File.ReadAllBytes(path));
        Assert.Single(store.List());
    }
    [Fact]
    public void PaginationAndServiceIdentityPreserveLargeIds()
    {
        var store=new TaskStore(root);
        for(int i=0;i<103;i++)store.Save(new LocalTask {Kind="object",Name="Model "+i,Status="PENDING",LuxTaskId="9007199254740993",CreatedAt=DateTimeOffset.UnixEpoch.AddSeconds(i)});
        var first=store.Page(new(Limit:100,Kind:"object"));var second=store.Page(new(Offset:100,Limit:100,Kind:"object"));
        Assert.Equal(103,first.Total);Assert.True(first.HasMore);Assert.False(second.HasMore);
        Assert.Equal(3,second.Items.Count);Assert.Empty(first.Items.Select(t=>t.Id).Intersect(second.Items.Select(t=>t.Id)));
        var identity=TaskIdentity.From(first.Items[0]);Assert.Equal(TaskService.Lux3D,identity.Service);Assert.Equal("9007199254740993",identity.RemoteId);
        Assert.Equal(103,store.Page(new(Search:"model")).Total);
    }
    [Theory]
    [InlineData("RenderSubmitting")]
    [InlineData("SubmissionUnknown")]
    [InlineData("FAILED")]
    public void UnknownOrFailedRenderCannotBeContinued(string status)
    {
        var task=new LocalTask {Kind="render",Status=status,MeshId="m",MeshTaskId="1"};
        Assert.False(TaskCapabilities.For(task).CanContinueRender);
    }
}
