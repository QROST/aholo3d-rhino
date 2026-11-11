using System.Runtime.InteropServices;
using Aholo.Core;
using Eto.Forms;
using Rhino.Commands;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal static class MeshUsdExport
{
    internal static SaveFileDialog SaveDialog()
    {
        var dialog=GaussianExportDialogs.CreateSaveDialog();
        dialog.Title=UiText.Get("Export selected meshes to USD");dialog.FileName="mesh-scene.usdz";return dialog;
    }
    internal static Task<string> ExportAsync(string path,GeometrySnapshot snapshot,CancellationToken ct)=>Task.Run(()=>
    {
        var parts=ResolveParts(snapshot,ct);
        var result=UsdMeshWriter.Write(path,parts,ct);
        string message=UiText.Format("Exported {0:N0} mesh parts and {1:N0} triangles to USD.\n{2}",result.Meshes,result.Triangles,result.Path);
        if(result.AssetDirectory is not null)message+="\n"+UiText.Format("Keep this texture folder beside the USD file: {0}",result.AssetDirectory);
        if(parts.Any(p=>p.Mesh.BaseColorTexture is not null))message+="\n"+UiText.Get("USD texture filtering is viewer-defined; source filter settings are retained as metadata.");
        return message;
    },ct);
    internal static UsdMeshPart[] ResolveParts(GeometrySnapshot snapshot,CancellationToken ct)
    {
        var loaded=new Dictionary<MeshTextureSource,MeshTexture>();var parts=new UsdMeshPart[snapshot.Meshes.Count];
        for(int i=0;i<parts.Length;i++)
        {
            ct.ThrowIfCancellationRequested();var mesh=snapshot.Meshes[i];
            if(snapshot.Textures.TryGetValue(i,out var source))
            {
                if(!loaded.TryGetValue(source,out var bitmap)){bitmap=source.Load(ct);loaded.Add(source,bitmap);}
                mesh=mesh with{BaseColorTexture=bitmap};
            }
            parts[i]=new(snapshot.Names[i],mesh,snapshot.Origins[i]);
        }
        return parts;
    }

}

[Guid("C703943F-511B-4C3C-A664-36054723F8CC")]
public sealed class AholoExportMeshCommand : global::Rhino.Commands.Command
{
    public override string EnglishName=>"AholoExportMesh";
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
                using var dialog=MeshUsdExport.SaveDialog();
                if(dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc))!=DialogResult.Ok)return Result.Cancel;
                path=GaussianExportDialogs.NormalizePath(dialog);
            }
            var snapshot=AholoHost.CaptureMeshExport(doc);
            CommandOperation.Start(doc.RuntimeSerialNumber,"mesh-export","Export selected mesh USD",ct=>MeshUsdExport.ExportAsync(path,snapshot,ct));
            global::Rhino.RhinoApp.WriteLine(UiText.Get("Exporting the captured selection locally. Use AholoCancel to stop. No cloud request is made."));
            return Result.Success;
        }
        catch(Exception ex){global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(ex));return Result.Failure;}
    }
}
