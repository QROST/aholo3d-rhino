using Aholo.Core;
namespace Aholo.Tests;

public sealed class MeshTaskSourceTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-task-mesh-"+Guid.NewGuid().ToString("N"));
    private static readonly ConnectionState Connected=new(true,"account",false,"epoch1");
    private static Task Valid(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.CompletedTask;}
    private static LocalTask TaskRow(params TaskArtifact[] artifacts)=>new(){Kind="object",ConnectionId="account",LuxTaskId="remote1",ServiceOperation="lux.text-to-3d",Status="SUCCEEDED",Artifacts=artifacts};
    private string FilePath(string extension="usdz") {Directory.CreateDirectory(root);string path=Path.Combine(root,Guid.NewGuid()+"."+extension);File.WriteAllBytes(path,new byte[]{1,2,3});return path;}
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    [Theory]
    [InlineData("glb")][InlineData("ply")][InlineData("stl")][InlineData("3mf")]
    [InlineData("usd")][InlineData("usda")][InlineData("usdc")][InlineData("usdz")][InlineData("obj")][InlineData("obj_zip")]
    public void TaskMeshesSupportAllLocalMeshFormatsButWorldPlyIsNotAnObject(string format)
    {
        var row=TaskRow(new TaskArtifact("chosen","mesh",format));
        Assert.Equal(format,MeshTaskSelection.Capture(row).Format);
        Assert.False(MeshPlacementPolicy.CanPlaceMesh(row with{Kind="world"},row.Artifacts[0]));
        Assert.Throws<InvalidOperationException>(()=>MeshTaskSelection.Capture(row with{Kind="world"}));
        Assert.Throws<InvalidOperationException>(()=>MeshTaskSelection.Capture(row with{SchemaVersion=999}));
    }
    [Theory]
    [InlineData("zip")][InlineData("fbx_zip")]
    public void UntypedAndFbxArchivesAreNotObjectPlacementResults(string format)
    {Assert.Throws<InvalidOperationException>(()=>MeshTaskSelection.Capture(TaskRow(new TaskArtifact("chosen","mesh",format))));}
    [Fact]
    public async Task TypedObjArchiveUsesZipSuffixAndWorksOffline()
    {
        var artifact=new TaskArtifact("chosen","mesh","obj_zip",LocalPath:FilePath("zip"),Sha256:"original");var row=TaskRow(artifact);
        var result=await MeshTaskSource.AcquireAsync(MeshTaskSelection.Capture(row),row,()=>new(false,null,false),(_,_)=>throw new Exception("download not expected"),_=>throw new Exception("observe not expected"),Valid,default);
        Assert.Equal(artifact,result);Assert.True(MeshPlacementPolicy.IsLocalMeshExtension("zip"));Assert.False(MeshPlacementPolicy.IsMeshFormat("zip"));
        row=row with{Artifacts=new[]{artifact with{LocalPath=FilePath("obj")}}};
        await Assert.ThrowsAsync<InvalidDataException>(()=>MeshTaskSource.AcquireAsync(MeshTaskSelection.Capture(row),row,()=>Connected,(_,_)=>throw new Exception("no download"),_=>{},Valid,default));
    }
    [Fact]
    public async Task CachedResultWorksOfflineWithoutDownloadOrObservation()
    {
        var artifact=new TaskArtifact("chosen","mesh","usdz",LocalPath:FilePath(),Sha256:"original");var row=TaskRow(artifact);
        var result=await MeshTaskSource.AcquireAsync(MeshTaskSelection.Capture(row),row,()=>new(false,null,false),
            (_,_)=>throw new Exception("download not expected"),_=>throw new Exception("observe not expected"),Valid,default);
        Assert.Equal(artifact,result);Assert.True(File.Exists(artifact.LocalPath));
    }
    [Fact]
    public async Task KnownSlotDoesNotRefreshAndReorderedDownloadKeepsChosenIdentity()
    {
        var row=TaskRow(new TaskArtifact("chosen","mesh","usdz"),new("other","mesh","glb"));var selection=MeshTaskSelection.Capture(row);
        Assert.Same(row,await MeshTaskSource.DiscoverAsync(row,null,()=>Connected,(_,_)=>throw new Exception("refresh not expected"),_=>{},Valid,default));
        int calls=0,observed=0;string path=FilePath();
        var result=await MeshTaskSource.AcquireAsync(selection,row,()=>Connected,(request,_)=>
        {
            calls++;Assert.Equal("artifact:chosen",request.Kind);Assert.Equal(row.Id,request.Id);
            return Task.FromResult(row with{Artifacts=new[]{row.Artifacts[1],row.Artifacts[0] with{LocalPath=path,Sha256="downloaded"}}});
        },_=>observed++,Valid,default);
        Assert.Equal("chosen",result.Key);Assert.Equal(1,calls);Assert.Equal(1,observed);
        Assert.Equal(MeshPlacementPolicy.ImportId(row.Id,"chosen"),selection.ImportId);
    }
    [Theory]
    [InlineData("account")][InlineData("region")][InlineData("remote")][InlineData("id")][InlineData("operation")]
    [InlineData("format")][InlineData("hash")][InlineData("missing")][InlineData("duplicate")]
    public async Task DownloadCannotSubstituteSelectedTaskOrResult(string change)
    {
        var row=TaskRow(new TaskArtifact("chosen","mesh","usdz",Sha256:"original"));var selected=MeshTaskSelection.Capture(row);
        var ready=row with{Artifacts=new[]{row.Artifacts[0] with{LocalPath=FilePath()}}};
        var changed=change switch
        {
            "account"=>ready with{ConnectionId="different"},"region"=>ready with{Region="china"},
            "remote"=>ready with{LuxTaskId="different"},"id"=>ready with{Id=Guid.NewGuid()},"operation"=>ready with{ServiceOperation="different"},
            "format"=>ready with{Artifacts=new[]{ready.Artifacts[0] with{Format="usd"}}},
            "hash"=>ready with{Artifacts=new[]{ready.Artifacts[0] with{Sha256="different"}}},
            "missing"=>ready with{Artifacts=Array.Empty<TaskArtifact>()},
            _=>ready with{Artifacts=new[]{ready.Artifacts[0],ready.Artifacts[0]}}
        };
        int observed=0;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>MeshTaskSource.AcquireAsync(selected,row,()=>Connected,(_,_)=>Task.FromResult(changed),_=>observed++,Valid,default));
        Assert.Equal(0,observed);Assert.True(File.Exists(ready.Artifacts[0].LocalPath));
    }
    [Fact]
    public async Task AccountEpochChangeAfterDownloadDiscardsLateResult()
    {
        var row=TaskRow(new TaskArtifact("chosen","mesh","usdz"));var connection=Connected;int observed=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>MeshTaskSource.AcquireAsync(MeshTaskSelection.Capture(row),row,()=>connection,(_,_)=>
        {
            connection=connection with{CredentialEpoch="epoch2"};
            return Task.FromResult(row with{Artifacts=new[]{row.Artifacts[0] with{LocalPath=FilePath(),Sha256="hash"}}});
        },_=>observed++,Valid,default));
        Assert.Equal(0,observed);
    }
    [Fact]
    public async Task MissingOfflineCacheAndWrongExtensionNeverStartImport()
    {
        var row=TaskRow(new TaskArtifact("chosen","mesh","usdz",LocalPath:Path.Combine(root,"missing.usdz")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>MeshTaskSource.AcquireAsync(MeshTaskSelection.Capture(row),row,()=>Connected with{Connected=false},(_,_)=>throw new Exception("no download"),_=>{},Valid,default));
        row=row with{Artifacts=new[]{row.Artifacts[0] with{LocalPath=FilePath("glb")}}};
        await Assert.ThrowsAsync<InvalidDataException>(()=>MeshTaskSource.AcquireAsync(MeshTaskSelection.Capture(row),row,()=>Connected,(_,_)=>throw new Exception("no download"),_=>{},Valid,default));
    }
    [Fact]
    public async Task DiscoveryOnlyQueriesOriginalTaskOnceAndDoesNotReplaceExplicitMissingSlot()
    {
        var row=TaskRow();int queries=0;
        Task<LocalTask> Refresh(Guid id,CancellationToken ct){queries++;Assert.Equal(row.Id,id);return Task.FromResult(row with{Artifacts=new[]{new TaskArtifact("chosen","mesh","usdz")}});}
        Assert.Equal("chosen",(await MeshTaskSource.DiscoverAsync(row,null,()=>Connected,Refresh,_=>{},Valid,default)).Artifacts[0].Key);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>MeshTaskSource.DiscoverAsync(row,"missing",()=>Connected,Refresh,_=>{},Valid,default));
        Assert.Equal(1,queries);
    }
    [Fact]
    public async Task CancellationDuringDownloadDoesNotPublishLateResult()
    {
        using var cancel=new CancellationTokenSource();var row=TaskRow(new TaskArtifact("chosen","mesh","usdz"));int observed=0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>MeshTaskSource.AcquireAsync(MeshTaskSelection.Capture(row),row,()=>Connected,(_,_)=>
        {cancel.Cancel();return Task.FromResult(row with{Artifacts=new[]{row.Artifacts[0] with{LocalPath=FilePath(),Sha256="hash"}}});},_=>observed++,Valid,cancel.Token));
        Assert.Equal(0,observed);
    }
}

public sealed class MeshTaskPlacementTests
{
    private sealed class Asset {public int Disposals;}
    [Theory]
    [InlineData("Existing")][InlineData("Removed")][InlineData("Deferred")]
    public async Task ReceiptOrDeferredCheckPrecedesAnySourceAccess(string state)
    {
        var result=await MeshTaskPlacement.ExecuteAsync<Asset>(_=>Task.FromResult<MeshPlacementResult?>(new(state,"receipt")),
            _=>throw new Exception("must not download"),(_,_)=>throw new Exception("must not prepare"),(_,_)=>throw new Exception("must not commit"),_=>throw new Exception("no asset"),default);
        Assert.Equal(state,result.State);
    }
    [Theory]
    [InlineData("commit")][InlineData("duplicate")][InlineData("deferred")][InlineData("cancel")][InlineData("failure")]
    public async Task EveryPreparedAssetIsReleasedExactlyOnce(string outcome)
    {
        using var cancel=new CancellationTokenSource();var asset=new Asset();int checks=0,commits=0,releases=0;
        async Task<MeshPlacementResult> Run()=>await MeshTaskPlacement.ExecuteAsync(
            _=>Task.FromResult<MeshPlacementResult?>(++checks==3&&outcome is "duplicate" or "deferred"?new(outcome,""):null),
            _=>Task.FromResult(new TaskArtifact("a","mesh","usdz")),(_,_)=>{if(outcome=="cancel")cancel.Cancel();return Task.FromResult(asset);},
            (_,_)=>{commits++;if(outcome=="failure")throw new InvalidOperationException("commit failed");return Task.FromResult(new MeshPlacementResult("Imported",""));},
            owned=>{Assert.Same(asset,owned);owned.Disposals++;releases++;return Task.CompletedTask;},cancel.Token);
        if(outcome=="cancel")await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
        else if(outcome=="failure")await Assert.ThrowsAsync<InvalidOperationException>(Run);
        else Assert.Equal(outcome=="commit"?"Imported":outcome,(await Run()).State);
        Assert.Equal(outcome is "commit" or "failure"?1:0,commits);Assert.Equal(1,releases);Assert.Equal(1,asset.Disposals);
    }
    [Fact]
    public async Task LateReceiptAfterDownloadSkipsPreparation()
    {
        int checks=0;
        Assert.Equal("Existing",(await MeshTaskPlacement.ExecuteAsync<Asset>(_=>Task.FromResult<MeshPlacementResult?>(++checks==2?new("Existing",""):null),
            _=>Task.FromResult(new TaskArtifact("a","mesh","usd")),(_,_)=>throw new Exception("no prepare"),(_,_)=>throw new Exception("no commit"),_=>throw new Exception("no asset"),default)).State);
    }
}
