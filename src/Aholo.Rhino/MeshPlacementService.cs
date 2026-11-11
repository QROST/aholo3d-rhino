using Aholo.Core;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal sealed record MeshPlacementTarget(ImportTarget Document, string Session, PlacementIntent Placement);
internal sealed record MeshPlacementResult(string State,string Message);

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
    internal async Task<PreparedAsset> PrepareAsync(string path,string? hash,MeshPlacementTarget target,CancellationToken ct)
    {
        IMeshImportSnapshot snapshot;
        if(Path.GetExtension(path).ToLowerInvariant() is ".usd" or ".usda" or ".usdc" or ".usdz")
        {
            var scene=await Task.Run(()=>UsdMeshReader.Read(path,hash,ct),ct).ConfigureAwait(false);
            var settings=await AholoHost.OnUi(()=>
            {
                ct.ThrowIfCancellationRequested();var doc=Resolve(target);
                if(doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(UiText.Get("Finish the current Rhino command before accepting a candidate."));
                return UsdMeshSnapshot.CaptureSettings(doc);
            }).ConfigureAwait(false);
            snapshot=await Task.Run(()=>UsdMeshSnapshot.Create(scene,Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),settings,ct),ct).ConfigureAwait(false);
        }
        else snapshot=await Task.Run(()=>NativeMeshImportSnapshot.Create(path,hash,ct:ct),ct).ConfigureAwait(false);
        try
        {
            return await AholoHost.OnUi(()=>
            {
                ct.ThrowIfCancellationRequested();var doc=Resolve(target);
                if(doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(UiText.Get("Finish the current Rhino command before accepting a candidate."));
                return host.Prepare(snapshot,doc);
            }).ConfigureAwait(false);
        }
        catch{snapshot.Dispose();throw;}
    }
    internal async Task<MeshPlacementResult> PlaceTaskAsync(Guid id,string? artifactKey,MeshPlacementTarget target,CancellationToken ct,Func<bool>? allowed=null)
    {
        var task=await Task.Run(()=>session.Store.Get(id),ct).ConfigureAwait(false);
        TaskArtifact? artifact=task.Artifacts.FirstOrDefault(a=>(artifactKey is null||a.Key==artifactKey)&&MeshPlacementPolicy.IsNativeMeshFormat(a.Format)&&a.Error is null);
        if(artifact?.LocalPath is null||!File.Exists(artifact.LocalPath))
        {
            if(!session.Connection.Connected||task.ConnectionId!=session.Connection.ConnectionId)throw new InvalidOperationException(UiText.Get("Connect to the task's original account before recovering its download."));
            task=await session.Client.RefreshAsync(id,ct).ConfigureAwait(false);session.Observe(task);
            if(task.Status!="SUCCEEDED")throw new InvalidOperationException(UiText.Get("The original task is not ready. No new generation was submitted."));
            artifact=MeshPlacementPolicy.SelectStaticMesh(task,artifactKey);
            task=await session.Client.DownloadAsync(new(id,"artifact:"+artifact.Key),ct).ConfigureAwait(false);session.Observe(task);
        }
        artifact=MeshPlacementPolicy.SelectStaticMesh(task,artifactKey);
        Guid importId=MeshPlacementPolicy.ImportId(task.Id,artifact.Key);
        var existing=await AholoHost.OnUi(()=>
        {
            ct.ThrowIfCancellationRequested();var doc=Resolve(target);
            if(allowed is not null&&!allowed())throw new OperationCanceledException(ct);
            if(doc.IsCommandRunning||doc.UndoRecordingIsActive)return new MeshPlacementResult("Deferred",UiText.Get("Finish the current Rhino command before accepting a candidate."));
            return Existing(doc,importId);
        }).ConfigureAwait(false);
        if(existing is not null)return existing;
        var trace=new HostImportTrace(id,target.Document.Serial,AholoPlugin.DisplayVersion,session.Store.Root);
        try
        {
            using var snapshot=await Task.Run(()=>NativeMeshImportSnapshot.Create(artifact.LocalPath!,artifact.Sha256,trace,ct),ct).ConfigureAwait(false);
            return await AholoHost.OnUi(()=>
            {
                ct.ThrowIfCancellationRequested();var doc=Resolve(target);
                if(allowed is not null&&!allowed())throw new OperationCanceledException(ct);
                var prior=Existing(doc,importId);if(prior is not null)return prior;
                if(doc.IsCommandRunning||doc.UndoRecordingIsActive)return new MeshPlacementResult("Deferred",UiText.Get("Finish the current Rhino command before accepting a candidate."));
                using var prepared=host.Prepare(snapshot,doc,trace);
                // Native preflight may pump host events; check the original intent again.
                ct.ThrowIfCancellationRequested();doc=Resolve(target);
                if(allowed is not null&&!allowed())throw new OperationCanceledException(ct);
                if(doc.IsCommandRunning||doc.UndoRecordingIsActive)return new MeshPlacementResult("Deferred",UiText.Get("Finish the current Rhino command before accepting a candidate."));
                var result=Commit(prepared,target,importId,task.Id,trace,ct,allowed);trace.Complete(result.State);return result;
            }).ConfigureAwait(false);
        }
        catch(Exception error){trace.Fail(error);throw;}
    }
    internal MeshPlacementResult Commit(PreparedAsset asset,MeshPlacementTarget target,Guid importId,Guid? taskId=null,HostImportTrace? trace=null,CancellationToken ct=default,Func<bool>? allowed=null)
    {
        var doc=Resolve(target);
        if(doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(UiText.Get("Finish the current Rhino command before accepting a candidate."));
        var prior=Existing(doc,importId);if(prior is not null)return prior;
        void ValidateCommit(){ct.ThrowIfCancellationRequested();Resolve(target);if(allowed is not null&&!allowed())throw new OperationCanceledException(ct);}
        ImportReceipt receipt=host.Commit(asset,target.Placement,importId,taskId,trace,ValidateCommit);
        if(receipt.State=="Imported")Frame(doc,receipt.ObjectId);
        return new(receipt.State,UiText.Get("The model is placed as an independent block. One Undo reverses this placement."));
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
