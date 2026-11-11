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
        meshEditor=new(serial,AholoPlugin.Placement,Report,RunAsync,()=>token);
        meshEditor.HeightText=objectHeight.Text;placementConnection=session.Connection;
        autoPlace.CheckedChanged+=(_,_)=>{if(autoPlace.Checked!=true){RevokePlacements();UpdateActions();}};
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
    private void DocumentCancellationRequested(uint documentSerial)
    {
        if(disposed||documentSerial!=serial)return;
        RevokePlacements();UpdateActions();
        Report(T("Local cancellation requested. Pending automatic placements were removed; remote tasks continue."));
    }
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
        var selection=MeshTaskSelection.Capture(task,artifact.Key);
        var target=meshEditor.Capture();long revision=meshEditor.Revision;
        Task Validate(CancellationToken ct)=>AholoHost.OnUi(()=>
        {
            ct.ThrowIfCancellationRequested();AholoPlugin.Placement.Validate(target);
            if(disposed||!shown||meshEditor.Revision!=revision)throw new LocalOperationCanceledException();
            return true;
        });
        artifact=await MeshTaskSource.AcquireAsync(selection,task,()=>session.Connection,session.Client.DownloadAsync,session.Observe,Validate,token);
        await meshEditor.AddAsync(artifact.LocalPath!,artifact.Sha256,task.Name,task.Id,token,selection.ImportId,revision);
    }
    private async Task ImportSceneAsync()
    {
        using var dialog=AholoImportSceneCommand.FileDialog();if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        var target=GaussianDocument.CaptureSceneImport(Document());
        var progress=StageProgress();
        string message=await GaussianDocument.ImportUsdSceneAsync(dialog.FileName,target,token,progress);
        AholoHost.Ui(()=>Report(message));
    }
    private async Task ExportSceneAsync()
    {
        using var dialog=SceneUsdExport.SaveDialog();if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        var snapshot=AholoHost.CaptureSceneExport(Document(),meshExportTextures.Checked==true);
        var progress=StageProgress();
        string message=await SceneUsdExport.ExportAsync(GaussianExportDialogs.NormalizePath(dialog),snapshot,token,progress);
        AholoHost.Ui(()=>Report(message));
    }
    private async Task ExportMeshAsync()
    {
        using var dialog=MeshUsdExport.SaveDialog();if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        var snapshot=AholoHost.CaptureMeshExport(Document(),meshExportTextures.Checked==true);
        string message=await MeshUsdExport.ExportAsync(GaussianExportDialogs.NormalizePath(dialog),snapshot,token);
        AholoHost.Ui(()=>Report(message));
    }
    internal Task ImportMeshCandidateAsync(string path)
    {
        if(disposed||!shown)throw new InvalidOperationException(T("Open Aholo3D Library before loading a mesh candidate."));
        _=AholoHost.CaptureTarget(Document());
        if(busy||autoBusy)throw new InvalidOperationException(T("The panel is busy. Wait for the current operation or cancel it, then load the mesh."));
        tabs.SelectedIndex=1;
        return RunAsync("Load local mesh",()=>meshEditor.AddAsync(path,null,Path.GetFileName(path),null,token));
    }
    private async Task LoadLocalMeshAsync()
    {
        using var dialog=AholoImportMeshCommand.FileDialog();
        if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        await meshEditor.AddAsync(dialog.FileName,null,Path.GetFileName(dialog.FileName),null,token);
    }
}
