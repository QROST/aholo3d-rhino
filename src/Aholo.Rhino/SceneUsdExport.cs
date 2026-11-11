using System.Runtime.InteropServices;
using Aholo.Core;
using Eto.Forms;
using Rhino.Commands;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal sealed record SceneExportSnapshot(GaussianDocument.ExportSnapshot Gaussians, GeometrySnapshot Geometry);

internal static class SceneUsdExport
{
    internal static SaveFileDialog SaveDialog()
    {
        var dialog=GaussianExportDialogs.CreateSaveDialog();
        dialog.Title=UiText.Get("Export selected scene to USD");dialog.FileName="scene.usdz";return dialog;
    }
    internal static Task<string> ExportAsync(string path,SceneExportSnapshot snapshot,CancellationToken ct,IProgress<string>? progress=null)
        =>GaussianDocument.RunExportAsync(snapshot.Gaussians.Serial,ct,token=>
        {
            GaussianScene? gaussians=snapshot.Gaussians.Parts.Length==0?null:GaussianDocument.ResolveExportScene(snapshot.Gaussians,token,progress);
            var meshes=MeshUsdExport.ResolveParts(snapshot.Geometry,token);
            progress?.Report(UiText.Get("Building and packaging the USD scene; large scenes may take several minutes…"));
            var result=UsdSceneWriter.Write(path,gaussians,meshes,token);
            string message=UiText.Format("Exported {0:N0} GS blocks, {1:N0} Gaussians, {2:N0} mesh parts and {3:N0} triangles.\n{4}",result.GaussianParts,result.Gaussians,result.Meshes,result.Triangles,result.Path);
            if(result.AssetDirectory is not null)message+="\n"+UiText.Format("Keep this texture folder beside the USD file: {0}",result.AssetDirectory);
            if(meshes.Any(p=>p.Mesh.BaseColorTexture is not null))message+="\n"+UiText.Get("USD texture filtering is viewer-defined; source filter settings are retained as metadata.");
            return message;
        });
}

[Guid("D2922BCD-E1AE-4B87-AD4B-C2EE4409C39B")]
public sealed class AholoExportSceneCommand : global::Rhino.Commands.Command
{
    public override string EnglishName=>"AholoExportScene";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        try
        {
            string path;
            if(mode==RunMode.Scripted)
            {
                string value="";var result=global::Rhino.Input.RhinoGet.GetString(UiText.Get("USD output file"),false,ref value);
                if(result!=Result.Success)return result;path=Path.GetFullPath(value);
            }
            else
            {
                using var dialog=SceneUsdExport.SaveDialog();
                if(dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc))!=DialogResult.Ok)return Result.Cancel;
                path=GaussianExportDialogs.NormalizePath(dialog);
            }
            var snapshot=AholoHost.CaptureSceneExport(doc);
            _=ExportAsync(path,snapshot);
            global::Rhino.RhinoApp.WriteLine(UiText.Get("Exporting the captured selection locally. Use AholoCancel to stop. No cloud request is made."));
            return Result.Success;
        }
        catch(Exception ex){global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(ex));return Result.Failure;}
    }
    private static async Task ExportAsync(string path,SceneExportSnapshot snapshot)
    {
        try
        {
            string result=await AholoPlugin.Session.Operations.RunAsync(AholoPlugin.DocumentScope(snapshot.Gaussians.Serial)+"scene-export","Export selected scene USD",ct=>SceneUsdExport.ExportAsync(path,snapshot,ct)).ConfigureAwait(false);
            AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(result));
        }
        catch(OperationCanceledException){AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(UiText.Get("Local export cancelled. The previous output was preserved.")));}
        catch(Exception ex){AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(ex)));}
    }
}
