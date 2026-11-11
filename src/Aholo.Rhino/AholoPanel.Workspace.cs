using Aholo.Core;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

public sealed partial class AholoPanel
{
    private readonly PanelWorkspace workspace;
    private bool workspaceReady,restoringWorkspace;
    private long workspaceLease,operationNavigationRevision;
    private void WorkspaceUserNavigated()
    {
        if(!workspaceReady||restoringWorkspace)return;
        if(PanelNavigation.Workspaces.TryUpdate(serial,workspace,workspaceLease,w=>w.UserNavigated()))history.UserNavigated();
    }
    private void SetPage(WorkspacePage page)
    {
        bool previous=restoringWorkspace;restoringWorkspace=true;
        try{tabs.SelectedIndex=(int)page;}
        finally{restoringWorkspace=previous;}
    }
    private void WorkspaceShown()
    {
        if(!PanelNavigation.Workspaces.IsCurrent(serial,workspace))return;
        if(!PanelNavigation.Workspaces.IsOwner(serial,workspace,workspaceLease))
        {
            workspaceLease=PanelNavigation.Workspaces.Claim(serial,workspace,this);
            if(workspaceLease!=0)RestoreWorkspace();
        }
    }
    private void RestoreWorkspace()
    {
        restoringWorkspace=true;
        try
        {
            foreach(var saved in workspace.Drafts)
            {
                if(!drafts.TryGetValue(saved.Key,out var draft))drafts.Add(saved.Key,draft=new CreationForm(saved.Key));
                draft.RestoreDraft(saved.Value);
            }
            creation.SelectedIndex=ServiceOperations.All.ToList().FindIndex(x=>x.Id==workspace.Operation);
            ChangeForm();SetPage(workspace.Page);
        }
        finally{restoringWorkspace=false;}
    }
    void IPanelWorkspaceOwner.SaveWorkspace()=>SaveWorkspace();
    private void SaveWorkspace()
    {
        var doc=RhinoDoc.FromRuntimeSerialNumber(serial);
        if(disposed||!workspaceReady||doc is null||doc.IsClosing||!PanelNavigation.Workspaces.IsOwner(serial,workspace,workspaceLease))return;
        if(tabs.SelectedIndex is >=0 and <=2)workspace.SelectPage((WorkspacePage)tabs.SelectedIndex);
        if(creation.SelectedIndex>=0)workspace.SelectOperation(ServiceOperations.All[creation.SelectedIndex].Id);
        foreach(var draft in drafts)workspace.SaveDraft(draft.Key,draft.Value.CaptureDraft());
    }
    internal bool CanNavigate=>!disposed&&workspaceReady&&shown&&PanelNavigation.Workspaces.IsOwner(serial,workspace,workspaceLease);
    internal void ApplyPendingNavigation()
    {
        if(!CanNavigate)return;
        var doc=RhinoDoc.FromRuntimeSerialNumber(serial);
        if(doc is null||doc.IsClosing)return;
        var request=PanelNavigation.Workspaces.Take(serial,RhinoDoc.ActiveDoc?.RuntimeSerialNumber,workspace);
        if(request is null)return;
        SaveWorkspace();workspace.Navigate(request.Destination);
        restoringWorkspace=true;
        try{creation.SelectedIndex=ServiceOperations.All.ToList().FindIndex(x=>x.Id==workspace.Operation);SetPage(workspace.Page);}
        finally{restoringWorkspace=false;}
        history.UserNavigated();PanelNavigation.Applied(serial,request.Revision);
    }
}
