using System.Runtime.InteropServices;
using Aholo3D.Core;
using Eto.Forms;
using Rhino.Commands;
using Rhino.Geometry;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo3D.Rhino;

internal sealed record SceneImportTarget(ImportTarget Document,UsdSnapshotSettings Settings,string Session);

internal static partial class GaussianDocument
{
    internal static SceneImportTarget CaptureSceneImport(RhinoDoc doc)
        =>new(Aholo3DHost.CaptureTarget(doc),UsdMeshSnapshot.CaptureSettings(doc),Aholo3DPlugin.MeshHost.Session(doc));
    private static RhinoDoc ValidateSceneTarget(SceneImportTarget target,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var doc=RequireTarget(target.Document.Serial);
        if(doc.ModelUnitSystem!=target.Document.Units||Aholo3DPlugin.MeshHost.Session(doc)!=target.Session||UsdMeshSnapshot.CaptureSettings(doc)!=target.Settings)
            throw new InvalidOperationException(UiText.Get("Document units or color preprocessing changed. Reload the USD candidate before acceptance."));
        if(doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(UiText.Get("Finish the current Rhino command before importing GS."));
        return doc;
    }
    internal static async Task<string> ImportUsdSceneAsync(string path,SceneImportTarget target,CancellationToken ct,IProgress<string>? progress=null)
    {
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(ct);var token=cancellation.Token;
        IMeshImportSnapshot? snapshot=null;
        try
        {
            await Aholo3DHost.OnUi(()=>
            {
                // A command may still be returning during registration; the final commit requires an idle host.
                var doc=RequireTarget(target.Document.Serial);token.ThrowIfCancellationRequested();
                if(UsdMeshSnapshot.CaptureSettings(doc)!=target.Settings)throw new InvalidOperationException(UiText.Get("Document units or color preprocessing changed. Reload the USD candidate before acceptance."));
                Initialize();if(!Imports.TryGetValue(target.Document.Serial,out var pending))Imports.Add(target.Document.Serial,pending=new());pending.Add(cancellation);return true;
            }).ConfigureAwait(false);
            var prepared=await Task.Run(()=>
            {
                progress?.Report(UiText.Get("Archiving the original file…"));var source=ArchiveSource(path,token);
                progress?.Report(UiText.Get("Reading the complete USD scene and its dependencies…"));
                var scene=UsdSceneReader.Read(path,source.Hash,token);
                var gs=scene.Gaussians is null?null:PrepareFromScene(source,scene.Gaussians,token,progress);
                if(scene.Meshes is not null)snapshot=UsdMeshSnapshot.Create(scene.Meshes,System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant(),target.Settings,token);
                return (Scene:scene,Gaussians:gs);
            },token).ConfigureAwait(false);
            progress?.Report(UiText.Get("Complete assets are ready; writing to the target document…"));
            return await Aholo3DHost.OnUi(()=>
            {
                var doc=ValidateSceneTarget(target,token);var gs=prepared.Gaussians;
                if(snapshot is null)
                    return CommitImport(gs??throw new InvalidDataException("Empty USD scene."),target.Document,1,"Auto",true,null,token);
                var placement=gs is null?null:ValidateGsPlacement(gs,doc,1,"Auto",true);
                var host=Aholo3DPlugin.MeshHost;
                using var asset=host.Prepare(snapshot,doc);
                ValidateSceneTarget(target,token);
                var added=new List<Guid>();Guid importId=Guid.NewGuid();
                var receipt=host.CommitScene(asset,importId,(destination,meshId,layer)=>
                {
                    added.Add(meshId);
                    if(gs is null)return;
                    for(int i=0;i<gs.Parts.Length;i++)
                    {
                        token.ThrowIfCancellationRequested();
                        var part=AddBlock(destination,gs.Parts[i],placement!.Transforms[i],gs.Source,gs.Scene,placement.Scale,placement.UpAxis,layer,null,importId);
                        added.Add(part.Id);
                    }
                },()=>ValidateSceneTargetDuringCommit(target,token));
                // CommitScene owns the one Undo and full native rollback. Cache/view updates follow its success.
                var messages=new List<string>{UiText.Format("Imported a USD scene with {0:N0} GS parts and {1:N0} mesh parts. One Undo reverses the complete import.",gs?.Parts.Length??0,prepared.Scene.Meshes!.Parts.Count)};
                try
                {
                    if(gs is not null)
                    {
                        if(!Documents.TryGetValue(target.Document.Serial,out var assets))Documents.Add(target.Document.Serial,assets=new());
                        foreach(var part in gs.Parts)
                        {
                            if(assets.TryGetValue(part.Asset.Hash,out var prior))prior.Cancellation?.Cancel();
                            assets[part.Asset.Hash]=new AssetEntry{Data=part.Asset};
                        }
                        GaussianViewport.ForgetDocument(target.Document.Serial);
                    }
                    var bounds=asset.Audit.Bounds;if(placement is not null)bounds.Union(placement.Bounds);
                    doc.Objects.UnselectAll();foreach(var id in added)doc.Objects.Select(id);
                    doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(bounds);
                    if(gs is not null)GaussianViewport.Activate(doc);doc.Views.Redraw();
                }
                catch(Exception ex){messages.Add(UiText.Get("The model is in the document, but the display mode is not ready. Bounds are retained. ")+Aholo3DHost.FriendlyError(ex));}
                if(gs is not null&&gs.Parts.Any(p=>p.Antialiased))messages.Add(UiText.Get("Antialiased GS parts use view-dependent opacity compensation. Original opacity is preserved; native display still uses 256 opacity levels."));
                messages.AddRange(prepared.Scene.Warnings.Select(w=>UiText.TranslateKnownMessage(w)));return string.Join(Environment.NewLine,messages);
            }).ConfigureAwait(false);
        }
        finally
        {
            snapshot?.Dispose();
            await Aholo3DHost.OnUi(()=>
            {
                if(Imports.TryGetValue(target.Document.Serial,out var pending)){pending.Remove(cancellation);if(pending.Count==0)Imports.Remove(target.Document.Serial);}
                return true;
            }).ConfigureAwait(false);
        }
    }
    private static void ValidateSceneTargetDuringCommit(SceneImportTarget target,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();var doc=RequireTarget(target.Document.Serial);
        if(doc.ModelUnitSystem!=target.Document.Units||Aholo3DPlugin.MeshHost.Session(doc)!=target.Session||UsdMeshSnapshot.CaptureSettings(doc)!=target.Settings)
            throw new InvalidOperationException(UiText.Get("Document units or color preprocessing changed. Reload the USD candidate before acceptance."));
    }
}

[Guid("DCD46CAC-88D0-4F7C-84D2-37B8BD83F75B")]
public sealed class Aholo3DImportSceneCommand:global::Rhino.Commands.Command
{
    public override string EnglishName=>"Aholo3DImportScene";
    internal static OpenFileDialog FileDialog()
    {
        var dialog=new OpenFileDialog{Title=UiText.Get("Import USD scene at its original position and size"),MultiSelect=false};
        dialog.Filters.Add(new FileFilter("USD / USDA / USDC / USDZ",".usd",".usda",".usdc",".usdz"));return dialog;
    }
    protected override Result RunCommand(RhinoDoc doc,RunMode mode) => Execute(doc,mode);
    internal static Result Execute(RhinoDoc doc,RunMode mode)
    {
        try
        {
            string path;
            if(mode==RunMode.Scripted)
            {
                string value="";var result=global::Rhino.Input.RhinoGet.GetString(UiText.Get("USD scene file"),false,ref value);
                if(result!=Result.Success)return result;path=System.IO.Path.GetFullPath(value);
            }
            else
            {
                using var dialog=FileDialog();if(dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc))!=DialogResult.Ok)return Result.Cancel;path=dialog.FileName;
            }
            var target=GaussianDocument.CaptureSceneImport(doc);
            CommandOperation.Start(target.Document.Serial,"scene-import","Import USD scene",ct=>GaussianDocument.ImportUsdSceneAsync(path,target,ct));
            global::Rhino.RhinoApp.WriteLine(UiText.Get("Importing the complete USD scene locally at its source position and size. Use Aholo3DCancel to stop."));return Result.Success;
        }
        catch(Exception ex){global::Rhino.RhinoApp.WriteLine(Aholo3DHost.FriendlyError(ex));return Result.Failure;}
    }
}
