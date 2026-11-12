using Aholo3D.Core;
using Eto.Forms;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo3D.Rhino;

public sealed partial class Aholo3DPanel
{
    private readonly UITimer saveDelay=new(){Interval=0.75};
    private bool rememberDrafts;
    private bool persistenceReported;
    private void InitializePersistence(RhinoDoc? doc)
    {
        if(workspace.Storage is null)
        {
            workspace.Storage=new(new WorkspaceStateStore(session.Store.Root),doc?.Path);
            var state=workspace.Storage.State;
            // A command's pending navigation is applied later and retains precedence.
            workspace.SelectPage(state.Page);workspace.SelectOperation(state.Operation);
            foreach(var draft in state.Drafts)workspace.SaveDraft(draft.Key,new(draft.Value));
        }
        RestorePreferences();
        saveDelay.Elapsed+=(_,_)=>{saveDelay.Stop();SaveWorkspace();};
        scale.TextChanged+=(_,_)=>QueueWorkspaceSave();axis.SelectedIndexChanged+=(_,_)=>QueueWorkspaceSave();sourceUnits.SelectedIndexChanged+=(_,_)=>QueueWorkspaceSave();
        resolution.SelectedIndexChanged+=(_,_)=>QueueWorkspaceSave();renderTextures.CheckedChanged+=(_,_)=>QueueWorkspaceSave();renderLights.CheckedChanged+=(_,_)=>QueueWorkspaceSave();
        lightRadius.TextChanged+=(_,_)=>QueueWorkspaceSave();lightMultiplier.TextChanged+=(_,_)=>QueueWorkspaceSave();
        RhinoDoc.EndSaveDocument+=DocumentSaved;
    }
    private void RestorePreferences()
    {
        if(workspace.Storage is null)return;
        var saved=workspace.Storage.State;rememberDrafts=saved.RememberDrafts;
        string Value(string key,string fallback)=>saved.Preferences.GetValueOrDefault(key,fallback);
        scale.Text=Value("scale",scale.Text);axis.SelectedIndex=int.Parse(Value("axis","0"));sourceUnits.SelectedIndex=int.Parse(Value("sourceUnits","0"));
        resolution.SelectedIndex=int.Parse(Value("resolution","0"));renderTextures.Checked=Value("renderTextures","true")=="true";renderLights.Checked=Value("renderLights","false")=="true";
        lightRadius.Text=Value("lightRadius","50");lightMultiplier.Text=Value("lightMultiplier","1");
    }
    private void DocumentSaved(object? sender,global::Rhino.DocumentSaveEventArgs e)
    {
        if(!e.ExportSelected&&e.Document?.RuntimeSerialNumber==serial)QueueWorkspaceSave();
    }
    private void QueueWorkspaceSave()
    {
        if(disposed||!workspaceReady||restoringWorkspace)return;
        saveDelay.Stop();saveDelay.Start();
    }
    private void PersistWorkspace(RhinoDoc doc)
    {
        if(workspace.Storage is null)return;
        var state=new WorkspaceState
        {
            RememberDrafts=rememberDrafts,Page=workspace.Page,Operation=workspace.Operation,
            Drafts=workspace.Drafts.ToDictionary(x=>x.Key,x=>x.Value.Values.ToDictionary(f=>f.Key,f=>f.Value.ToArray())),
            Preferences=new()
            {
                ["scale"]=scale.Text,["axis"]=axis.SelectedIndex.ToString(),["sourceUnits"]=sourceUnits.SelectedIndex.ToString(),
                ["resolution"]=resolution.SelectedIndex.ToString(),["renderTextures"]=renderTextures.Checked==true?"true":"false",
                ["renderLights"]=renderLights.Checked==true?"true":"false",["lightRadius"]=lightRadius.Text,["lightMultiplier"]=lightMultiplier.Text
            }
        };
        try{workspace.Storage.Save(doc.Path,state);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException)
        {if(!persistenceReported){persistenceReported=true;Report(UiText.TranslateKnownMessage(ex.Message));}}
    }
    private string DraftStorageStatus()=>workspace.Storage?.Error is{} error?UiText.TranslateKnownMessage(error):
        string.IsNullOrWhiteSpace(RhinoDoc.FromRuntimeSerialNumber(serial)?.Path)?
        T("Save this Rhino document first to restore its workspace after restart."):
        T("Drafts stay on this Mac or PC. URLs and file selections must be entered again; credentials and upload overrides are never saved here.");
}
