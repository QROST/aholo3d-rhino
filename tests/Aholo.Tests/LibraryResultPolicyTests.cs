using Aholo.Core;
namespace Aholo.Tests;

public sealed class LibraryResultPolicyTests
{
    private static LocalTask Model(params TaskArtifact[] rows)=>new(){Kind="object",ServiceOperation="lux.multi-format-export",LuxTaskId="123",ConnectionId="owner",Status="SUCCEEDED",Artifacts=rows};
    private static readonly ConnectionState Connected=new(true,"owner",false);
    private static readonly TaskArtifact Zip=new("0","Archive","zip"),Glb=new("1","Model","glb"),Image=new("2","Image","image");
    private static bool Exists(string path)=>path.StartsWith("present",StringComparison.Ordinal);
    private static LibraryResultAccess Access(LocalTask task,TaskArtifact result,ConnectionState? connection=null)=>LibraryResultPolicy.Access(task,LibraryResultPolicy.Outputs(task),result,connection??Connected,Exists,"en");
    [Theory]
    [InlineData("lux.material-transfer")][InlineData("lux.multi-format-export")]
    public void ActualOrderedLuxResultsRecommendGlbWithoutChangingServiceOrder(string operation)
    {
        var rows=LuxArtifacts.Decode(operation,new[]{"https://example.invalid/models.zip","https://example.invalid/model.glb","https://example.invalid/model.usdz"});
        var task=Model(rows) with{ServiceOperation=operation};
        Assert.Equal(1,new LibraryResultSelection().Choose(task,rows,Exists));Assert.Equal(new[]{"0","1","2"},rows.Select(a=>a.Key));
    }
    [Fact]
    public void AutomaticChoiceImprovesWhenAPlaceableResultArrives()
    {
        var task=Model(Zip);var selection=new LibraryResultSelection();Assert.Equal(0,selection.Choose(task,task.Artifacts,Exists));
        task=task with{Artifacts=new[]{Zip,Glb}};Assert.Equal(1,selection.Choose(task,task.Artifacts,Exists));
    }
    [Fact]
    public void ExplicitArchiveSurvivesRefreshReorderNewResultsAndErrors()
    {
        var task=Model(Zip,Glb);var selection=new LibraryResultSelection();selection.Remember(task,task.Artifacts,0);
        var changed=task with{Artifacts=new[]{Glb,Image,Zip with{Error="Unavailable output"}}};
        Assert.Equal(2,selection.Choose(changed,changed.Artifacts,Exists));var access=Access(changed,changed.Artifacts[2]);
        Assert.Equal("Unavailable output",access.Status);Assert.False(access.CanPlace);Assert.False(access.CanDownload);
    }
    [Fact]
    public void DifferentTaskWithSameKeyDoesNotInheritAndReturningRestoresChoice()
    {
        var first=Model(Zip,Glb);var second=Model(Zip,Glb);var selection=new LibraryResultSelection();selection.Remember(first,first.Artifacts,0);
        Assert.Equal(1,selection.Choose(second,second.Artifacts,Exists));Assert.Equal(0,selection.Choose(first,first.Artifacts,Exists));
    }
    [Theory]
    [InlineData("account")][InlineData("region")][InlineData("remote")][InlineData("operation")][InlineData("service")]
    public void ChoiceDoesNotCrossTaskProvenanceChanges(string change)
    {
        var original=Model(Zip,Glb,Image);var selection=new LibraryResultSelection();selection.Remember(original,original.Artifacts,0);
        var changed=change switch{"account"=>original with{ConnectionId="other"},"region"=>original with{Region="china"},"remote"=>original with{LuxTaskId="456"},"operation"=>original with{ServiceOperation="lux.material-transfer"},_=>original with{Kind="world",WorldId="world"}};
        Assert.Equal(LibraryResultPolicy.DefaultIndex(changed,changed.Artifacts,Exists),selection.Choose(changed,changed.Artifacts,Exists));
        Assert.Equal(0,selection.Choose(original,original.Artifacts,Exists));
    }
    [Theory]
    [InlineData("missing")][InlineData("format")][InlineData("duplicate")]
    public void MissingChangedOrAmbiguousExplicitIdentityFallsBackAndForgets(string change)
    {
        var task=Model(Zip,Glb);var selection=new LibraryResultSelection();selection.Remember(task,task.Artifacts,0);
        var rows=change switch{"missing"=>new[]{Glb},"format"=>new[]{Zip with{Format="fbx"},Glb},_=>new[]{Zip,Zip with{Label="Duplicate"},Glb}};
        Assert.Equal(Array.IndexOf(rows,Glb),selection.Choose(task,rows,Exists));Assert.Equal(1,selection.Choose(task,task.Artifacts,Exists));
    }
    [Fact]
    public void InvalidUserIndicesDoNotReplaceAnExplicitChoice()
    {
        var task=Model(Zip,Glb);var selection=new LibraryResultSelection();selection.Remember(task,task.Artifacts,0);
        selection.Remember(task,task.Artifacts,-1);selection.Remember(task,task.Artifacts,4);
        Assert.Equal(0,selection.Choose(task,task.Artifacts,Exists));Assert.Equal(-1,selection.Choose(Model(),Array.Empty<TaskArtifact>(),Exists));
    }
    [Fact]
    public void RealCacheWinsWithinPlaceableResultsButNotOverResultKinds()
    {
        var rows=new[]{Zip with{LocalPath="present.zip"},Glb,new TaskArtifact("3","USD","usdz",LocalPath:"present.usdz"),Image with{LocalPath="present.png"}};
        Assert.Equal(2,LibraryResultPolicy.DefaultIndex(Model(rows),rows,Exists));
        rows[2]=rows[2] with{LocalPath="deleted.usdz"};Assert.Equal(1,LibraryResultPolicy.DefaultIndex(Model(rows),rows,Exists));
    }
    [Theory]
    [InlineData(false,"owner","international")][InlineData(true,"other","international")][InlineData(true,"owner","china")]
    public void MissingMeshCacheKeepsOfflineReceiptLookupButNeverPreviewOrDownload(bool connected,string account,string region)
    {
        var result=Glb with{LocalPath="deleted.glb"};var access=Access(Model(result),result,new(connected,account,false,Region:region));
        Assert.True(access.CanPlace);Assert.False(access.CanPreview);Assert.False(access.CanDownload);Assert.False(access.CanOpenFolder);
        Assert.Equal("Cached file missing",access.Status);Assert.Contains("earlier placement",access.NextStep);Assert.DoesNotContain("Saved",LibraryResultPolicy.Label(result,false,"en"));
    }
    [Fact]
    public void MatchingCompletedAccountEnablesLazyDownloadWithoutPersistedUrl()
    {
        Assert.Null(Glb.Url);var access=Access(Model(Glb),Glb);Assert.True(access.CanPlace);Assert.True(access.CanPreview);Assert.True(access.CanDownload);Assert.False(access.IsCached);
        Assert.Contains("does not generate",access.NextStep);
    }
    [Fact]
    public void RunningOutputCannotDownloadButCachedMeshCanBeInspectedOffline()
    {
        var task=Model(Glb) with{Status="RUNNING"};var pending=Access(task,Glb);Assert.False(pending.CanDownload);Assert.False(pending.CanPreview);Assert.True(pending.CanPlace);
        var local=Glb with{LocalPath="present.glb"};task=task with{Artifacts=new[]{local}};var cached=Access(task,local,new(false,null,false));
        Assert.True(cached.CanPreview);Assert.True(cached.CanOpenFolder);Assert.False(cached.CanDownload);
    }
    [Theory]
    [InlineData("zip")][InlineData("fbx")][InlineData("fbx_zip")][InlineData("metadata")]
    public void DownloadOnlyResultsHaveAnExplicitNextStep(string format)
    {
        var result=Zip with{Format=format};var task=Model(result);var access=Access(task,result);
        Assert.True(access.CanDownload);Assert.False(access.CanPlace);Assert.False(access.CanPreview);Assert.Contains("Download only",access.NextStep);
        result=result with{LocalPath="present.asset"};task=task with{Artifacts=new[]{result}};access=Access(task,result);
        Assert.True(access.CanOpenFolder);Assert.Contains("Open result folder",access.NextStep);
    }
    [Fact]
    public void SyntheticWorldOutputsPreferCachedGaussianWithoutUsingTransientUrls()
    {
        var task=new LocalTask{Kind="world",WorldId="world",Status="SUCCEEDED",ConnectionId="owner",PlyPath="deleted.ply",SpzPath="present.spz",ImagePath="present.png"};
        var rows=LibraryResultPolicy.Outputs(task);Assert.Equal(1,LibraryResultPolicy.DefaultIndex(task,rows,Exists));
        var local=Access(task,rows[1]);Assert.True(local.CanPlace);Assert.False(local.CanPreview);Assert.True(local.CanDownload);Assert.Contains("not automatically placed",local.NextStep);
        var missing=Access(task,rows[0]);Assert.False(missing.CanPlace);Assert.True(missing.CanDownload);Assert.Contains("Download this result",missing.NextStep);
    }
    [Fact]
    public void SpatialAndRenderResultsReceiveDifferentActions()
    {
        var scene=new TaskArtifact("result","Spatial scene","dsl",LocalPath:"present.dsl");var spatial=new LocalTask{Kind="spatial",SpatialTaskId="space",ConnectionId="owner",Status="SUCCEEDED",Artifacts=new[]{scene}};
        Assert.True(Access(spatial,scene).CanPlace);Assert.False(Access(spatial,scene).CanOpenImage);
        var render=new LocalTask{Kind="render",OperationId="render",ConnectionId="owner",Status="SUCCEEDED",ImagePath="present.png"};var image=Assert.Single(LibraryResultPolicy.Outputs(render));
        var access=Access(render,image);Assert.False(access.CanPlace);Assert.True(access.CanOpenImage);Assert.Contains("Open image",access.NextStep);
    }
    [Fact]
    public void DuplicateOrForeignResultCannotAcquireActions()
    {
        var task=Model(Glb,Glb with{Format="zip"});var access=Access(task,Glb);Assert.Contains("ambiguous",access.Status);Assert.False(access.CanPlace);Assert.False(access.CanDownload);
        access=Access(Model(Glb),Zip);Assert.Equal("No result selected.",access.Status);Assert.False(access.CanDownload);
    }
    [Theory]
    [InlineData("RecordError",1)][InlineData("UnsupportedSchema",1)][InlineData("SUCCEEDED",2)]
    public void InvalidTaskRecordsNeverEnableResultActions(string status,int schema)
    {
        var access=Access(Model(Glb) with{Status=status,SchemaVersion=schema},Glb);Assert.False(access.CanPlace);Assert.False(access.CanDownload);Assert.Contains("cannot be used",access.Status);
    }
    [Fact]
    public void LabelsUseActualCacheStateAndKeepUserTextUnchanged()
    {
        var result=new TaskArtifact("0","我的房间","glb",LocalPath:"deleted.glb");
        Assert.Contains("我的房间",LibraryResultPolicy.Label(result,false,"en"));Assert.Contains("Cached file missing",LibraryResultPolicy.Label(result,false,"en"));
        Assert.Contains("Saved locally",LibraryResultPolicy.Label(result,true,"en"));Assert.DoesNotContain("deleted.glb",LibraryResultPolicy.Label(result,false,"en"));
    }
    [Fact]
    public void BilingualGuidanceExplainsDownloadAndPlacementWithoutTranslatingPaths()
    {
        var task=Model(Glb);var en=LibraryResultPolicy.Access(task,task.Artifacts,Glb,Connected,Exists,"en");
        var zh=LibraryResultPolicy.Access(task,task.Artifacts,Glb,Connected,Exists,"zh");
        Assert.Equal("Not downloaded.",en.Status);Assert.Equal("尚未下载。",zh.Status);Assert.Contains("不会重新生成",zh.NextStep);
        Assert.Equal("我的房间 · GLB · 本地缓存文件已缺失",LibraryResultPolicy.Label(new("0","我的房间","glb",LocalPath:"deleted.glb"),false,"zh"));
    }
}
