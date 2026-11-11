using Aholo.Core;
using Eto.Forms;
namespace Aholo.Rhino;

public sealed partial class AholoPanel
{
    private MeshPlacementEditor meshEditor=null!;
    private readonly CheckBox autoPlace=new(){Text=T("Place generated mesh when ready"),Checked=true};
    private readonly AutomaticPlacementIntents<MeshPlacementTarget> placementIntents=new();
    private readonly Dictionary<Guid,LocalTask> readyPlacements=new();
    private readonly UITimer placementTimer=new(){Interval=1};
    private ConnectionState placementConnection=new(false,null,false);
    private bool autoBusy,shown=true;
    private Button previewResult=null!;
    private readonly CheckBox meshExportTextures=new(){Text=T("Include direct base-color bitmaps in mesh USD"),Checked=true};
    private void InitializePlacement()
    {
        meshEditor=new(serial,AholoPlugin.Placement,Report,RunAsync);
        meshEditor.HeightText=objectHeight.Text;placementConnection=session.Connection;
        autoPlace.CheckedChanged+=(_,_)=>{if(autoPlace.Checked!=true)RevokePlacements();};
        placementTimer.Elapsed+=(_,_)=>_=PlaceReadyAsync();
    }
    private AutomaticPlacementIntent<MeshPlacementTarget>? CaptureAutomatic(CreateServiceRequest request)
        =>autoPlace.Checked==true&&MeshPlacementPolicy.SupportsAutomaticPlacement(request.Operation)
            ?placementIntents.Capture(meshEditor.Capture(),session.Connection):null;
    private void RememberAutomatic(LocalTask task,AutomaticPlacementIntent<MeshPlacementTarget>? intent)
    {
        if(intent is null||!session.Connection.Connected||intent.ConnectionId!=session.Connection.ConnectionId||intent.Epoch!=session.Connection.CredentialEpoch)return;
        placementIntents.Attach(task,intent);PlacementTaskChanged(task);
    }
    private void PlacementTaskChanged(LocalTask task)
    {
        if(!placementIntents.TryGet(task.Id,session.Connection,out _)||session.IsPaused(task.Id))return;
        if(task.Status=="SUCCEEDED")readyPlacements[task.Id]=task;
        else if(task.Status is "FAILED" or "CANCELED" or "SubmissionUnknown")ForgetPlacement(task.Id);
    }
    private void ForgetPlacement(Guid id){placementIntents.Remove(id);readyPlacements.Remove(id);}
    private void RevokePlacements(){placementIntents.RevokeAll();readyPlacements.Clear();}
    private void PlacementConnectionChanged()
    {
        var current=session.Connection;
        if(current.Connected!=placementConnection.Connected||current.ConnectionId!=placementConnection.ConnectionId||current.CredentialEpoch!=placementConnection.CredentialEpoch)
        {RevokePlacements();placementConnection=current;}
    }
    private async Task PlaceReadyAsync()
    {
        if(disposed||!shown||busy||autoBusy||readyPlacements.Count==0)return;
        var task=readyPlacements.Values.First();
        if(!placementIntents.TryGet(task.Id,session.Connection,out var intent)||session.IsPaused(task.Id)){ForgetPlacement(task.Id);return;}
        autoBusy=true;UpdateActions();
        try
        {
            var result=await session.Operations.RunAsync(Scope+"mesh-placement","Place generated mesh",ct=>
                AholoPlugin.Placement.PlaceTaskAsync(task.Id,null,intent!.Target,ct,()=>!disposed&&shown&&!session.IsPaused(task.Id)&&placementIntents.IsCurrent(task.Id,intent,session.Connection)),lifetime.Token);
            if(result.State!="Deferred")ForgetPlacement(task.Id);
            if(!disposed)Report(result.Message);
        }
        catch(Exception error){ForgetPlacement(task.Id);if(!disposed)Report(AholoHost.FriendlyError(error));}
        finally{autoBusy=false;if(!disposed)UpdateActions();else if(!busy)lifetime.Dispose();}
    }
    private async Task PreviewSelectedAsync()
    {
        var task=Selected??throw new InvalidOperationException(T("Select a task first."));
        var artifact=SelectedArtifact??throw new InvalidOperationException(T("Select a result first."));
        if(!MeshPlacementPolicy.IsNativeMeshFormat(artifact.Format))throw new InvalidOperationException(T("Choose an available static GLB, mesh PLY, STL or core 3MF result. Use Import GS for Gaussian files."));
        if(artifact.LocalPath is null||!File.Exists(artifact.LocalPath))
        {
            if(!session.Connection.Connected||task.ConnectionId!=session.Connection.ConnectionId)throw new InvalidOperationException(T("Connect to the task's original account before recovering its download."));
            task=await session.Client.DownloadAsync(new(task.Id,"artifact:"+artifact.Key),token);session.Observe(task);
            artifact=MeshPlacementPolicy.SelectStaticMesh(task,artifact.Key);
        }
        await meshEditor.AddAsync(artifact.LocalPath!,artifact.Sha256,task.Name,task.Id,token);
    }
    private async Task ExportMeshAsync()
    {
        using var dialog=MeshUsdExport.SaveDialog();if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        var snapshot=AholoHost.CaptureMeshExport(Document(),meshExportTextures.Checked==true);
        string message=await MeshUsdExport.ExportAsync(GaussianExportDialogs.NormalizePath(dialog),snapshot,token);
        AholoHost.Ui(()=>Report(message));
    }
    private async Task LoadLocalMeshAsync()
    {
        using var dialog=new OpenFileDialog{Title=T("Load local mesh"),MultiSelect=false};dialog.Filters.Add(new FileFilter(T("Static mesh"),".glb",".ply",".stl",".3mf",".usd",".usda",".usdc",".usdz"));
        if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        await meshEditor.AddAsync(dialog.FileName,null,Path.GetFileName(dialog.FileName),null,token);
    }
}
