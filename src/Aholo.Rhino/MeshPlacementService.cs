using Aholo.Core;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal sealed record MeshPlacementTarget(ImportTarget Document, string Session, PlacementIntent Placement);

internal sealed class MeshPlacementService
{
    private readonly AholoSession session;
    private readonly MeshHostService host;
    internal MeshPlacementService(AholoSession session,MeshHostService host){this.session=session;this.host=host;}
    internal MeshPlacementTarget Capture(RhinoDoc doc,PlacementIntent placement)
        =>new(AholoHost.CaptureTarget(doc),host.Session(doc),placement);
    internal static PlacementIntent DefaultPlacement(RhinoDoc doc)
    {
        double height=global::Rhino.RhinoMath.UnitScale(global::Rhino.UnitSystem.Meters,doc.ModelUnitSystem);
        if(!double.IsFinite(height)||height<=0)height=1;
        Plane plane=doc.Views.ActiveView?.ActiveViewport.ConstructionPlane()??Plane.WorldXY;
        return new(plane,plane.Origin,height,0);
    }
    internal void Validate(MeshPlacementTarget target)=>Resolve(target);
    private RhinoDoc Resolve(MeshPlacementTarget target)
    {
        var doc=RhinoDoc.FromRuntimeSerialNumber(target.Document.Serial)??throw new InvalidOperationException(UiText.Get("The original target document has closed."));
        _=AholoHost.CaptureTarget(doc);
        if(doc.ModelUnitSystem!=target.Document.Units||host.Session(doc)!=target.Session)throw new InvalidOperationException(UiText.Get("The target document changed. Retry placement in the intended document."));
        return doc;
    }
    internal async Task<PreparedAsset> PrepareAsync(string path,string? hash,MeshPlacementTarget target,CancellationToken ct,HostImportTrace? trace=null,Func<bool>? allowed=null,bool interactive=true)
    {
        void ValidateIntent(){ct.ThrowIfCancellationRequested();Resolve(target);if(allowed is not null&&!allowed())throw new LocalOperationCanceledException();}
        await AholoHost.OnUi(()=>{ValidateIntent();return true;}).ConfigureAwait(false);
        IMeshImportSnapshot snapshot;
        string extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension is ".usd" or ".usda" or ".usdc" or ".usdz" or ".obj" or ".zip" or ".3mf")
        {
            var settings=await AholoHost.OnUi(()=>
            {
                ValidateIntent();var doc=Resolve(target);
                if(doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(UiText.Get("Finish the current Rhino command before accepting a candidate."));
                return UsdMeshSnapshot.CaptureSettings(doc);
            }).ConfigureAwait(false);
            void ValidateSettings()
            {
                ValidateIntent();var doc=Resolve(target);
                if(doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(UiText.Get("Finish the current Rhino command before accepting a candidate."));
                if(UsdMeshSnapshot.CaptureSettings(doc)!=settings)throw new InvalidOperationException(UiText.Get("Document units or color preprocessing changed. Reload the mesh candidate before acceptance."));
            }
            UsdMeshScene scene;
            if(extension is ".obj" or ".zip")
            {
                if(!interactive)throw new InvalidOperationException(UiText.Get("OBJ placement requires explicit units, up axis and archive entry. Run AholoPlaceTask interactively or use Library to review the OBJ candidate."));
                using var source=await Task.Run(()=>ObjSourceSnapshot.OpenForSelection(path,hash,ct),ct).ConfigureAwait(false);
                var options=await AholoHost.OnUi(()=>
                {
                    ValidateSettings();var choice=ObjImportDialog.Choose(Resolve(target),source.CandidateEntries,settings,ct);
                    ValidateSettings();return choice;
                }).ConfigureAwait(false);
                scene=await Task.Run(()=>{source.SelectEntry(options.EntryName!,ct);return ObjMeshReader.Read(source,options,ct);},ct).ConfigureAwait(false);
            }
            else if(extension==".3mf")scene=await Task.Run(()=>ThreeMfReader.Read(path,hash,ct),ct).ConfigureAwait(false);
            else scene=await Task.Run(()=>UsdMeshReader.Read(path,hash,ct),ct).ConfigureAwait(false);
            await AholoHost.OnUi(()=>{ValidateSettings();return true;}).ConfigureAwait(false);
            snapshot=await Task.Run(()=>UsdMeshSnapshot.Create(scene,extension==".zip"?"obj_zip":extension.TrimStart('.'),settings,ct),ct).ConfigureAwait(false);
        }
        else snapshot=await Task.Run(()=>NativeMeshImportSnapshot.Create(path,hash,trace,ct),ct).ConfigureAwait(false);
        try
        {
            return await AholoHost.OnUi(()=>
            {
                ValidateIntent();var doc=Resolve(target);
                if(doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(UiText.Get("Finish the current Rhino command before accepting a candidate."));
                var prepared=host.Prepare(snapshot,doc,trace);
                try{ValidateIntent();return prepared;}catch{prepared.Dispose();throw;}
            }).ConfigureAwait(false);
        }
        catch{snapshot.Dispose();throw;}
    }
    internal async Task<MeshPlacementResult> PlaceTaskAsync(Guid id,string? artifactKey,MeshPlacementTarget target,CancellationToken ct,Func<bool>? allowed=null,bool interactive=true)
    {
        async Task Validate(CancellationToken token)=>await AholoHost.OnUi(()=>
        {
            token.ThrowIfCancellationRequested();Resolve(target);
            if(allowed is not null&&!allowed())throw new LocalOperationCanceledException();
            return true;
        }).ConfigureAwait(false);
        var task=await Task.Run(()=>session.Store.Get(id),ct).ConfigureAwait(false);
        task=await MeshTaskSource.DiscoverAsync(task,artifactKey,()=>session.Connection,session.Client.RefreshAsync,session.Observe,Validate,ct).ConfigureAwait(false);
        var selection=MeshTaskSelection.Capture(task,artifactKey);
        Task<MeshPlacementResult?> Check(CancellationToken token)=>AholoHost.OnUi(()=>
        {
            token.ThrowIfCancellationRequested();var doc=Resolve(target);
            if(allowed is not null&&!allowed())throw new LocalOperationCanceledException();
            if(doc.IsCommandRunning||doc.UndoRecordingIsActive)return new MeshPlacementResult("Deferred",UiText.Get("Finish the current Rhino command before accepting a candidate."));
            return Existing(doc,selection.ImportId);
        });
        var trace=new HostImportTrace(id,target.Document.Serial,AholoPlugin.DisplayVersion,session.Store.Root);
        try
        {
            var result=await MeshTaskPlacement.ExecuteAsync(Check,
                token=>MeshTaskSource.AcquireAsync(selection,task,()=>session.Connection,session.Client.DownloadAsync,session.Observe,Validate,token),
                (artifact,token)=>PrepareAsync(artifact.LocalPath!,artifact.Sha256,target,token,trace,allowed,interactive),
                (asset,token)=>AholoHost.OnUi(()=>Commit(asset,target,selection.ImportId,id,trace,token,allowed)),asset=>AholoHost.OnUi(()=>{asset.Dispose();return true;}),ct).ConfigureAwait(false);
            trace.Complete(result.State);return result;
        }
        catch(Exception error){trace.Fail(error);throw;}
    }
    internal MeshPlacementResult Commit(PreparedAsset asset,MeshPlacementTarget target,Guid importId,Guid? taskId=null,HostImportTrace? trace=null,CancellationToken ct=default,Func<bool>? allowed=null)
    {
        void ValidateCommit(){ct.ThrowIfCancellationRequested();Resolve(target);if(allowed is not null&&!allowed())throw new LocalOperationCanceledException();}
        ValidateCommit();var doc=Resolve(target);
        if(doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(UiText.Get("Finish the current Rhino command before accepting a candidate."));
        var prior=Existing(doc,importId);if(prior is not null)return prior;
        ImportReceipt receipt=host.Commit(asset,target.Placement,importId,taskId,trace,ValidateCommit);
        if(receipt.State=="Imported")Frame(doc,receipt.ObjectId);
        return new(receipt.State,UiText.Get("The model is placed as an independent block. One Undo reverses this placement.")+(asset.ImportWarnings.Count==0?"":"\n"+string.Join("\n",asset.ImportWarnings.Select(w=>UiText.Get(w)))));
    }
    private MeshPlacementResult? Existing(RhinoDoc doc,Guid importId)
    {
        var receipt=host.FindReceipt(doc,importId);if(receipt is null)return null;
        if(receipt.State=="Imported"){Frame(doc,receipt.ObjectId);return new("Existing",UiText.Get("This result is already in the document."));}
        return new(receipt.State,UiText.Get("The earlier placement was removed. Use Rhino Redo to restore it."));
    }
    private static void Frame(RhinoDoc doc,Guid id)
    {
        try{var obj=doc.Objects.FindId(id);if(obj is null)return;doc.Objects.UnselectAll();obj.Select(true);doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(obj.Geometry.GetBoundingBox(true));doc.Views.Redraw();}
        catch{/* View framing must not turn a completed import into a retry. */}
    }
}
