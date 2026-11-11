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
