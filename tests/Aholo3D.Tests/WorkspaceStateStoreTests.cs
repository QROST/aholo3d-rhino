using Aholo3D.Core;
namespace Aholo3D.Tests;
public sealed class WorkspaceStateStoreTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo3d-workspace-"+Guid.NewGuid().ToString("N"));
    private readonly WorkspaceStateStore store;
    private string Doc(string name="one")=>Path.Combine(root,name+".3dm");
    private string Key(string name="one")=>WorkspaceStateStore.DocumentKey(Doc(name))!;
    public WorkspaceStateStoreTests()=>store=new(root);
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    private static WorkspaceState Draft(bool remember=true)=>new()
    {
        RememberDrafts=remember,Page=WorkspacePage.Render,Operation="lux.text-to-3d",
        Drafts=new(){{"lux.text-to-3d",new(){{"prompt:text",new[]{"chair\nraw text"}},{"faceCount:text",new[]{"1e-"}},{"$name",new[]{"draft"}}}}},
        Preferences=new(){{"scale","2.5"},{"resolution","1"},{"renderLights","true"}}
    };
    [Fact] public void OptInRoundTripsRawDraftAndPreferencesWithPrivateFiles()
    {
        var saved=store.Save(Key(),null,Draft());var restored=store.Read(Key())!;
        Assert.Equal(saved.Revision,restored.Revision);Assert.Equal("1e-",restored.Drafts["lux.text-to-3d"]["faceCount:text"][0]);
        Assert.Equal("2.5",restored.Preferences["scale"]);Assert.Equal(WorkspacePage.Render,restored.Page);
        if(!OperatingSystem.IsWindows())Assert.Equal(UnixFileMode.UserRead|UnixFileMode.UserWrite,File.GetUnixFileMode(Path.Combine(root,"ui","workspaces",Key()+".json")));
    }
    [Fact] public void DefaultAndForgetSaveNoDraftButKeepSafeSettings()
    {
        var first=store.Save(Key(),null,Draft());var removed=store.Save(Key(),first.Revision,Draft(false));
        Assert.Empty(removed.Drafts);Assert.False(store.Read(Key())!.RememberDrafts);Assert.Equal("1",removed.Preferences["resolution"]);
        string json=File.ReadAllText(Path.Combine(root,"ui","workspaces",Key()+".json"));Assert.DoesNotContain("chair",json);
    }
    [Fact] public void CredentialsUrlsFilesAndUnknownPreferencesAreNeverWritten()
    {
        var state=Draft();state.Drafts["lux.text-to-3d"]["apiKey:text"]=new[]{"secret-marker"};
        state.Drafts["lux.img-to-3d"]=new(){{"img:url",new[]{"https://example.invalid/image?token=secret-marker"}},{"img:files",new[]{"private-file-marker"}},{"version:choice",new[]{"G1-Turbo"}}};
        state.Preferences["apiKey"]="secret-marker";state.Preferences["objectHeight"]="12";state.Preferences["autoPlace"]="true";
        state.Preferences["scale"]="NaN";state.Preferences["resolution"]="100";
        var saved=store.Save(Key(),null,state);
        string json=File.ReadAllText(Path.Combine(root,"ui","workspaces",Key()+".json"));
        Assert.DoesNotContain("secret-marker",json);Assert.DoesNotContain("private-file-marker",json);Assert.DoesNotContain("objectHeight",json);Assert.DoesNotContain("autoPlace",json);
        Assert.False(saved.Preferences.ContainsKey("scale"));Assert.False(saved.Preferences.ContainsKey("resolution"));
        Assert.Equal("G1-Turbo",saved.Drafts["lux.img-to-3d"]["version:choice"][0]);
    }
    [Fact] public void StaleAndConcurrentWritersCannotOverwriteExistingState()
    {
        var first=store.Save(Key(),null,Draft());var newer=store.Save(Key(),first.Revision,Draft() with{Page=WorkspacePage.Library});
        Assert.Throws<IOException>(()=>store.Save(Key(),first.Revision,Draft(false)));
        Assert.Equal(newer.Revision,store.Read(Key())!.Revision);Assert.True(store.Read(Key())!.RememberDrafts);
    }
    [Theory][InlineData("{broken")][InlineData("{\"version\":99}")][InlineData("{\"version\":1,\"revision\":\"bad\"}")]
    public void CorruptionIsRetainedAndCannotBeSilentlyReplaced(string content)
    {
        string path=Path.Combine(root,"ui","workspaces",Key()+".json");TaskStore.WriteAtomic(path,System.Text.Encoding.UTF8.GetBytes(content));
        var handle=new WorkspaceStateHandle(store,Doc());Assert.NotNull(handle.Error);
        Assert.Throws<IOException>(()=>handle.Save(Doc(),Draft()));Assert.Equal(content,File.ReadAllText(path));
    }
    [Fact] public void UntitledDocumentsAreMemoryOnlyAndSeparate()
    {
        var a=new WorkspaceStateHandle(store,null);var b=new WorkspaceStateHandle(store,"");
        a.Save(null,Draft());b.Save(null,new());Assert.True(a.State.RememberDrafts);Assert.False(b.State.RememberDrafts);Assert.False(Directory.Exists(root));
        a.Save(Doc(),Draft());Assert.NotNull(store.Read(Key()));Assert.False(b.State.RememberDrafts);
    }
    [Fact] public void SaveAsCreatesSeparateStateAndNeverOverwritesExistingTarget()
    {
        var handle=new WorkspaceStateHandle(store,Doc());handle.Save(Doc(),Draft());
        handle.Save(Doc("new"),Draft() with{Page=WorkspacePage.Library});Assert.Equal(WorkspacePage.Render,store.Read(Key())!.Page);Assert.Equal(WorkspacePage.Library,store.Read(Key("new"))!.Page);
        var previous=store.Read(Key())!;
        Assert.Throws<IOException>(()=>handle.Save(Doc(),Draft(false)));Assert.Equal(previous.Revision,store.Read(Key())!.Revision);
    }
    [Fact] public void DifferentDocumentsDoNotShareDraftsAndCanonicalRelativeSegmentsAgree()
    {
        store.Save(Key(),null,Draft());Assert.Null(store.Read(Key("two")));
        Assert.Equal(Key(),WorkspaceStateStore.DocumentKey(Path.Combine(root,"sub","..","one.3dm")));
        Assert.Null(WorkspaceStateStore.DocumentKey(null));
    }
    [Fact] public void OversizedUiTextKeepsPreviousFileAndHasNoGeometryImplications()
    {
        var first=store.Save(Key(),null,Draft());var huge=Draft();huge.Drafts["lux.text-to-3d"]["prompt:text"]=new[]{new string('x',3*1024*1024)};
        Assert.Throws<IOException>(()=>store.Save(Key(),first.Revision,huge));Assert.Equal(first.Revision,store.Read(Key())!.Revision);
    }
    [Fact] public void ReadAndSaveDoNotShareMutableCallerArrays()
    {
        var original=Draft();store.Save(Key(),null,original);original.Drafts["lux.text-to-3d"]["prompt:text"][0]="changed";
        var read=store.Read(Key())!;read.Drafts["lux.text-to-3d"]["prompt:text"][0]="changed";
        Assert.Equal("chair\nraw text",store.Read(Key())!.Drafts["lux.text-to-3d"]["prompt:text"][0]);
    }
    [Fact] public void CorrectingOversizedDraftCanSaveWithoutRestartAndDoesNotBypassRevision()
    {
        var handle=new WorkspaceStateHandle(store,Doc());handle.Save(Doc(),Draft());
        var huge=Draft();huge.Drafts["lux.text-to-3d"]["prompt:text"]=new[]{new string('x',3*1024*1024)};
        Assert.Throws<IOException>(()=>handle.Save(Doc(),huge));Assert.NotNull(handle.Error);
        handle.Save(Doc(),Draft());Assert.Null(handle.Error);Assert.True(handle.CanPersist);
        var current=store.Read(Key())!;store.Save(Key(),current.Revision,Draft(false));
        Assert.Throws<IOException>(()=>handle.Save(Doc(),Draft()));Assert.False(store.Read(Key())!.RememberDrafts);
    }

}
