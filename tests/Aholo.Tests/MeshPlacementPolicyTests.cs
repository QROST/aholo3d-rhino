using System.Security.Cryptography;
using System.Text;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class MeshPlacementPolicyTests
{
    [Fact]
    public void DirectPlacementPreservesExistingIdentityAndSeparatesResultSlots()
    {
        var id=Guid.NewGuid();var expected=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(id.ToString("N")+":0")).AsSpan(0,16));
        Assert.Equal(expected,MeshPlacementPolicy.ImportId(id,"0"));Assert.NotEqual(expected,MeshPlacementPolicy.ImportId(id,"1"));
    }
    [Fact]
    public void CachedStaticResultCanBeChosenWithoutTreatingAnimationOutputsAsAutoPlace()
    {
        var task=new LocalTask{Kind="object",Artifacts=new[]{new TaskArtifact("image","image","image"),new TaskArtifact("mesh","mesh","glb",LocalPath:"saved.glb")}};
        Assert.Equal("mesh",MeshPlacementPolicy.SelectStaticMesh(task).Key);
        Assert.Throws<InvalidOperationException>(()=>MeshPlacementPolicy.SelectStaticMesh(task,"image"));
        Assert.False(MeshPlacementPolicy.SupportsAutomaticPlacement("lux.articulation-animation"));Assert.False(MeshPlacementPolicy.SupportsAutomaticPlacement("world.generate"));
    }
    private static readonly ConnectionState Connection=new(true,"account",false,"epoch1");
    private static LocalTask Task()=>new(){Kind="object",ConnectionId="account",LuxTaskId="123",ServiceOperation="lux.text-to-3d",Status="PENDING"};
    [Fact]
    public void LateCreateResponseCannotRestoreRevokedPlacementAuthority()
    {
        var intents=new AutomaticPlacementIntents<string>();var intent=intents.Capture("original document/plane",Connection);intents.RevokeAll();
        Assert.False(intents.Attach(Task(),intent));
    }
    [Fact]
    public void RevokedPendingTaskCannotRegainPlacementWhenSuccessArrivesAndOtherDocumentIsUnaffected()
    {
        var first=new AutomaticPlacementIntents<string>();var other=new AutomaticPlacementIntents<string>();
        var pending=Task();var otherTask=Task();var intent=first.Capture("first document",Connection);
        var lateResponse=first.Capture("first document late response",Connection);
        Assert.True(first.Attach(pending,intent));Assert.True(other.Attach(otherTask,other.Capture("other document",Connection)));
        Assert.Equal(1,first.Count);first.RevokeAll();
        Assert.False(first.IsCurrent(pending.Id,intent,Connection));
        Assert.False(first.Attach(pending with{Status="SUCCEEDED"},intent));
        Assert.False(first.Attach(Task() with{Status="SUCCEEDED"},lateResponse));Assert.Equal(0,first.Count);
        Assert.True(other.TryGet(otherTask.Id,Connection,out _));Assert.Equal(1,other.Count);
        var next=Task();Assert.True(first.Attach(next,first.Capture("new explicit creation",Connection)));
        Assert.True(first.TryGet(next.Id,Connection,out _));
    }
    [Fact]
    public void RemovingOnePendingPlacementPreservesAnotherIntent()
    {
        var intents=new AutomaticPlacementIntents<string>();var first=Task();var second=Task();
        var a=intents.Capture("first",Connection);var b=intents.Capture("second",Connection);
        Assert.True(intents.Attach(first,a));Assert.True(intents.Attach(second,b));
        intents.Remove(first.Id);
        Assert.False(intents.IsCurrent(first.Id,a,Connection));Assert.True(intents.IsCurrent(second.Id,b,Connection));Assert.Equal(1,intents.Count);
    }
    [Fact]
    public void AccountEpochChangeAndExplicitRemovalInvalidateOriginalIntent()
    {
        var intents=new AutomaticPlacementIntents<string>();var task=Task();var intent=intents.Capture("target",Connection);Assert.True(intents.Attach(task,intent));
        Assert.True(intents.TryGet(task.Id,Connection,out var found));Assert.Same(intent,found);
        Assert.False(intents.TryGet(task.Id,Connection with{CredentialEpoch="epoch2"},out _));
        Assert.False(intents.TryGet(task.Id,Connection with{ConnectionId="another-account"},out _));
        Assert.False(intents.TryGet(task.Id,Connection with{Connected=false},out _));
        intents.Remove(task.Id);Assert.False(intents.IsCurrent(task.Id,intent,Connection));
    }
    [Theory]
    [InlineData("SubmissionUnknown")]
    [InlineData("FAILED")]
    [InlineData("Uploading")]
    public void UnconfirmedOrFailedSubmissionsCannotAcquirePlacementAuthority(string status)
    {
        var intents=new AutomaticPlacementIntents<string>();Assert.False(intents.Attach(Task() with{Status=status},intents.Capture("target",Connection)));
    }
}
