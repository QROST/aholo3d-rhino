using System.Collections.ObjectModel;
namespace Aholo3D.Core;

/// <summary>Unvalidated UI text only. Immutable, process-local and never submitted or persisted.</summary>
public sealed class CreationDraft
{
    public IReadOnlyDictionary<string,IReadOnlyList<string>> Values { get; }
    public CreationDraft(IEnumerable<KeyValuePair<string,string[]>> values)
        =>Values=new ReadOnlyDictionary<string,IReadOnlyList<string>>(values.ToDictionary(x=>x.Key,x=>(IReadOnlyList<string>)Array.AsReadOnly((string[])x.Value.Clone()),StringComparer.Ordinal));
}
public enum WorkspacePage { Create,Library,Render }
public enum WorkspaceDestination { World,Model,Tasks,Render }

/// <summary>One live document's UI workspace; contains no Eto/native resources or placement authority.</summary>
public sealed class PanelWorkspace
{
    private readonly Dictionary<string,CreationDraft> drafts=new(StringComparer.Ordinal);
    private string lastWorld="world.generate",lastModel="lux.text-to-3d";
    public long NavigationRevision { get; private set; }
    public void UserNavigated()=>NavigationRevision++;
    public WorkspacePage Page { get; private set; }
    public string Operation { get; private set; }="world.generate";
    public IReadOnlyDictionary<string,CreationDraft> Drafts=>new ReadOnlyDictionary<string,CreationDraft>(drafts);
    public void SelectPage(WorkspacePage page)
    {if(!Enum.IsDefined(typeof(WorkspacePage),page))throw new ArgumentOutOfRangeException(nameof(page));Page=page;}
    public void SelectOperation(string id)
    {
        var service=ServiceOperations.Get(id).Service;Operation=id;
        if(service==TaskService.World)lastWorld=id;
        if(service==TaskService.Lux3D)lastModel=id;
    }
    public void SaveDraft(string id,CreationDraft draft){_=ServiceOperations.Get(id);drafts[id]=draft;}
    public void Navigate(WorkspaceDestination destination)
    {
        switch(destination)
        {
            case WorkspaceDestination.World:SelectOperation(lastWorld);Page=WorkspacePage.Create;break;
            case WorkspaceDestination.Model:SelectOperation(lastModel);Page=WorkspacePage.Create;break;
            case WorkspaceDestination.Tasks:Page=WorkspacePage.Library;break;
            case WorkspaceDestination.Render:Page=WorkspacePage.Render;break;
            default:throw new ArgumentOutOfRangeException(nameof(destination));
        }
    }
}
public interface IPanelWorkspaceOwner { void SaveWorkspace(); }
public sealed record WorkspaceNavigation(long Revision,WorkspaceDestination Destination,long NavigationRevision);

/// <summary>UI-thread registry. Closing a document invalidates its old panel instances and queued navigation.</summary>
public sealed class PanelWorkspaces
{
    private readonly Dictionary<uint,PanelWorkspace> workspaces=new();
    private readonly Dictionary<uint,WorkspaceNavigation> pending=new();
    private sealed record Owner(long Lease,WeakReference<IPanelWorkspaceOwner> Panel);
    private readonly Dictionary<uint,Owner> owners=new();
    private long revision,leaseRevision;
    public PanelWorkspace Open(uint serial)
    {if(!workspaces.TryGetValue(serial,out var workspace))workspaces.Add(serial,workspace=new());return workspace;}
    public bool IsCurrent(uint serial,PanelWorkspace workspace)=>workspaces.TryGetValue(serial,out var current)&&ReferenceEquals(current,workspace);
    public long Claim(uint serial,PanelWorkspace workspace,IPanelWorkspaceOwner panel)
    {
        if(!IsCurrent(serial,workspace))return 0;
        var previous=owners.GetValueOrDefault(serial);
        if(previous is not null&&previous.Panel.TryGetTarget(out var old)&&!ReferenceEquals(old,panel))old.SaveWorkspace();
        // A close or reentrant transfer during capture must not recreate/steal ownership.
        if(!IsCurrent(serial,workspace)||!ReferenceEquals(previous,owners.GetValueOrDefault(serial)))return 0;
        long lease=++leaseRevision;owners[serial]=new(lease,new(panel));return lease;
    }
    public bool IsOwner(uint serial,PanelWorkspace workspace,long lease)=>lease!=0&&IsCurrent(serial,workspace)&&owners.GetValueOrDefault(serial)?.Lease==lease;
    public bool TryUpdate(uint serial,PanelWorkspace workspace,long lease,Action<PanelWorkspace> update)
    {if(!IsOwner(serial,workspace,lease))return false;update(workspace);return true;}
    public WorkspaceNavigation Request(uint serial,WorkspaceDestination destination)
    {
        if(!Enum.IsDefined(typeof(WorkspaceDestination),destination))throw new ArgumentOutOfRangeException(nameof(destination));
        var workspace=Open(serial);workspace.UserNavigated();return pending[serial]=new(++revision,destination,workspace.NavigationRevision);
    }
    public WorkspaceNavigation? Peek(uint serial)=>pending.GetValueOrDefault(serial);
    public WorkspaceNavigation? Take(uint serial,uint? activeSerial,PanelWorkspace workspace)
    {
        if(activeSerial!=serial||!IsCurrent(serial,workspace)||!pending.Remove(serial,out var request))return null;
        return request.NavigationRevision==workspace.NavigationRevision?request:null;
    }
    public void Cancel(uint serial,long requestedRevision)
    {if(pending.TryGetValue(serial,out var request)&&request.Revision==requestedRevision)pending.Remove(serial);}
    public void Close(uint serial){pending.Remove(serial);owners.Remove(serial);workspaces.Remove(serial);}
    public void Clear(){pending.Clear();owners.Clear();workspaces.Clear();}
}
