using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class PanelWorkspaceTests
{
    private sealed class Owner(Action? save=null):IPanelWorkspaceOwner { public void SaveWorkspace()=>save?.Invoke(); }
    private static CreationDraft Draft(string value)=>new(new Dictionary<string,string[]>{{"prompt:text",new[]{value}}});
    [Fact] public void RawDraftPreservesUnfinishedInputsAndDoesNotExposeMutableBuffers()
    {
        var matrix=Enumerable.Range(0,16).Select(i=>i==2?"1e-":" "+i+" ").ToArray();
        var files=new[]{"relative source with spaces.ply","另一个文件.ply"};
        var input=new Dictionary<string,string[]>
        {
            ["$name"]=new[]{"  新草稿 \n"},["$advanced"]=new[]{"true"},
            ["prompt:text"]=new[]{"  description\nsecond line  "},["faceCount:text"]=new[]{"-"},
            ["resources:files"]=files,["img:url"]=new[]{"https://example.test/unfinished?"},
            ["outputFormat:selected"]=Array.Empty<string>(),["enablePbr:choice"]=new[]{""},
            ["version:choice"]=new[]{"G1-Turbo"},["transformationMatrix:enabled"]=new[]{"false"},["transformationMatrix:matrix"]=matrix
        };
        var draft=new CreationDraft(input);files[0]="changed";matrix[2]="0";input.Clear();
        Assert.Equal("relative source with spaces.ply",draft.Values["resources:files"][0]);Assert.Equal("1e-",draft.Values["transformationMatrix:matrix"][2]);
        Assert.Equal("-",Assert.Single(draft.Values["faceCount:text"]));Assert.Empty(draft.Values["outputFormat:selected"]);
        Assert.Equal("  新草稿 \n",Assert.Single(draft.Values["$name"]));
        Assert.Throws<NotSupportedException>(()=>((IList<string>)draft.Values["resources:files"])[0]="changed again");
        Assert.Throws<NotSupportedException>(()=>((IDictionary<string,IReadOnlyList<string>>)draft.Values).Clear());
    }
    [Fact] public void DocumentWorkspacesKeepIndependentOperationDraftsAndLastPages()
    {
        var registry=new PanelWorkspaces();var first=registry.Open(1);var second=registry.Open(2);
        first.SelectOperation("world.reconstruct");first.SaveDraft("world.reconstruct",Draft("first"));first.SelectPage(WorkspacePage.Library);
        second.SelectOperation("lux.text-to-3d");second.SaveDraft("lux.text-to-3d",Draft("second"));second.SelectPage(WorkspacePage.Render);
        Assert.Same(first,registry.Open(1));Assert.Equal(WorkspacePage.Library,first.Page);Assert.Equal(WorkspacePage.Render,second.Page);
        Assert.False(second.Drafts.ContainsKey("world.reconstruct"));Assert.Equal("first",Assert.Single(first.Drafts["world.reconstruct"].Values["prompt:text"]));
    }
    [Fact] public void WorldAndModelRestoreTheirOwnLastOperationWithoutReplacingDrafts()
    {
        var workspace=new PanelWorkspace();var draft=Draft(" unfinished ");
        workspace.SelectOperation("world.reconstruct");workspace.SaveDraft("world.reconstruct",draft);
        workspace.SelectOperation("lux.material-transfer");workspace.SelectOperation("spatial.understand");
        workspace.Navigate(WorkspaceDestination.World);Assert.Equal("world.reconstruct",workspace.Operation);
        workspace.Navigate(WorkspaceDestination.Model);Assert.Equal("lux.material-transfer",workspace.Operation);
        workspace.Navigate(WorkspaceDestination.Tasks);Assert.Equal(WorkspacePage.Library,workspace.Page);
        workspace.Navigate(WorkspaceDestination.Render);Assert.Equal(WorkspacePage.Render,workspace.Page);
        Assert.Same(draft,workspace.Drafts["world.reconstruct"]);
    }
    [Fact] public void LatestPendingCommandWinsAndOnlyOriginalActiveDocumentCanConsumeIt()
    {
        var registry=new PanelWorkspaces();var first=registry.Open(1);var second=registry.Open(2);
        var old=registry.Request(1,WorkspaceDestination.World);var latest=registry.Request(1,WorkspaceDestination.Render);
        registry.Cancel(1,old.Revision);
        Assert.Null(registry.Take(1,2,first));Assert.Null(registry.Take(1,1,second));Assert.NotNull(registry.Peek(1));
        Assert.Equal(latest,registry.Take(1,1,first));Assert.Null(registry.Take(1,1,first));
    }
    [Fact] public void ManualNavigationInvalidatesQueuedCommandAndLateOperationCompletionIntent()
    {
        var registry=new PanelWorkspaces();var workspace=registry.Open(1);long creation=workspace.NavigationRevision;
        registry.Request(1,WorkspaceDestination.Render);Assert.NotEqual(creation,workspace.NavigationRevision);
        workspace.UserNavigated();Assert.Null(registry.Take(1,1,workspace));Assert.Null(registry.Peek(1));
        long current=workspace.NavigationRevision;workspace.SaveDraft("world.generate",Draft("edited"));workspace.SelectPage(WorkspacePage.Create);
        Assert.Equal(current,workspace.NavigationRevision);
    }
    [Fact] public void ClosedDocumentCannotRestorePendingNavigationOrAcceptOldPanelWrites()
    {
        var registry=new PanelWorkspaces();var old=registry.Open(1);long lease=registry.Claim(1,old,new Owner());
        old.SaveDraft("world.generate",Draft("private draft"));registry.Request(1,WorkspaceDestination.World);registry.Close(1);
        Assert.False(registry.IsCurrent(1,old));Assert.False(registry.IsOwner(1,old,lease));Assert.Null(registry.Take(1,1,old));Assert.Equal(0,registry.Claim(1,old,new Owner()));
        var reopened=registry.Open(1);Assert.NotSame(old,reopened);Assert.Empty(reopened.Drafts);Assert.False(registry.IsCurrent(1,old));
    }
    [Fact] public void OldPanelLeaseCannotOverwriteNewOwnerEvenWhenWorkspaceIsShared()
    {
        var registry=new PanelWorkspaces();var workspace=registry.Open(1);long old=registry.Claim(1,workspace,new Owner());long current=registry.Claim(1,workspace,new Owner());
        Assert.False(registry.IsOwner(1,workspace,old));Assert.True(registry.IsOwner(1,workspace,current));
        registry.Clear();Assert.False(registry.IsOwner(1,workspace,current));Assert.Empty(registry.Open(1).Drafts);
    }
    [Fact] public void HandoffCapturesUnsavedDraftBeforeNewPanelRestoresAndOldEventsCannotOverwriteIt()
    {
        var registry=new PanelWorkspaces();var workspace=registry.Open(1);long firstLease=0;int captured=0;
        var first=new Owner(()=>
        {
            Assert.True(registry.IsOwner(1,workspace,firstLease));captured++;
            registry.TryUpdate(1,workspace,firstLease,w=>{w.SaveDraft("world.generate",Draft("latest unsaved text"));w.SelectPage(WorkspacePage.Render);});
        });
        firstLease=registry.Claim(1,workspace,first);var second=new Owner();long secondLease=registry.Claim(1,workspace,second);
        Assert.Equal(1,captured);Assert.Equal("latest unsaved text",Assert.Single(workspace.Drafts["world.generate"].Values["prompt:text"]));Assert.Equal(WorkspacePage.Render,workspace.Page);
        registry.TryUpdate(1,workspace,secondLease,w=>w.SaveDraft("world.generate",Draft("new owner input")));
        var request=registry.Request(1,WorkspaceDestination.Model);long navigation=workspace.NavigationRevision;string operation=workspace.Operation;
        Assert.False(registry.TryUpdate(1,workspace,firstLease,w=>{w.UserNavigated();w.SelectOperation("lux.rig");w.SaveDraft("world.generate",Draft("stale input"));}));
        Assert.Equal(navigation,workspace.NavigationRevision);Assert.Equal(operation,workspace.Operation);
        Assert.Equal("new owner input",Assert.Single(workspace.Drafts["world.generate"].Values["prompt:text"]));Assert.Equal(request,registry.Take(1,1,workspace));
        GC.KeepAlive(first);GC.KeepAlive(second);
    }
    [Fact] public void ClosingDuringOwnerCaptureDoesNotRecreateWorkspaceOrGrantNewLease()
    {
        var registry=new PanelWorkspaces();var workspace=registry.Open(1);var first=new Owner(()=>registry.Close(1));
        registry.Claim(1,workspace,first);
        Assert.Equal(0,registry.Claim(1,workspace,new Owner()));Assert.False(registry.IsCurrent(1,workspace));GC.KeepAlive(first);
    }
    [Fact] public void FailedOpenCancelsOnlyItsOwnPendingRevisionAndRetainsDraft()
    {
        var registry=new PanelWorkspaces();var workspace=registry.Open(1);workspace.SaveDraft("world.generate",Draft("kept"));
        var request=registry.Request(1,WorkspaceDestination.Model);registry.Cancel(1,request.Revision);
        Assert.Null(registry.Peek(1));Assert.Single(workspace.Drafts);Assert.Same(workspace,registry.Open(1));
    }
    [Fact] public void FlushUsesCurrentOwnerAndGuardsReentrantClose()
    {
        var registry=new PanelWorkspaces();var workspace=registry.Open(1);int firstCalls=0,secondCalls=0;
        var first=new Owner(()=>firstCalls++);registry.Claim(1,workspace,first);
        var second=new Owner(()=>{secondCalls++;registry.Flush(1);registry.Close(1);});registry.Claim(1,workspace,second);
        registry.FlushAll();Assert.Equal(1,firstCalls);Assert.Equal(1,secondCalls);Assert.False(registry.IsCurrent(1,workspace));
        registry.Flush(1);Assert.Equal(1,secondCalls);GC.KeepAlive(first);GC.KeepAlive(second);
    }

}
