using System.Diagnostics;
using System.Runtime.InteropServices;
using Aholo.Core;
using Eto.Forms;
using Rhino.Commands;
using Rhino.UI;
using Command=global::Rhino.Commands.Command;
using OpenFileDialog=Eto.Forms.OpenFileDialog;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

[Guid("B8B2C276-45CE-4721-8D9C-3EBBC03FA780")]
public sealed class AholoImportMeshCommand : Command
{
    public override string EnglishName=>"AholoImportMesh";
    private sealed record Pending(uint Serial,string Path,Stopwatch Clock);
    private static Pending? pending;
    internal static OpenFileDialog FileDialog()
    {
        var dialog=new OpenFileDialog{Title=UiText.Get("Load local mesh"),MultiSelect=false};
        dialog.Filters.Add(new FileFilter(UiText.Get("Static mesh"),".glb",".ply",".stl",".3mf",".usd",".usda",".usdc",".usdz",".obj",".zip"));return dialog;
    }
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        try
        {
            if(pending is not null)throw new InvalidOperationException(UiText.Get("A mesh import is already waiting. Use AholoCancel to stop it."));
            _=AholoHost.CaptureTarget(doc);
            string path;
            if(mode==RunMode.Scripted)
            {
                string value="";var result=global::Rhino.Input.RhinoGet.GetString(UiText.Get("Local mesh file"),false,ref value);
                if(result!=Result.Success)return result;path=Path.GetFullPath(value);
            }
            else
            {
                using var dialog=FileDialog();
                if(dialog.ShowDialog(RhinoEtoApp.MainWindowForDocument(doc))!=DialogResult.Ok)return Result.Cancel;
                path=dialog.FileName;
            }
            if(!File.Exists(path)||!MeshPlacementPolicy.IsLocalMeshExtension(System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant()))
                throw new InvalidOperationException(UiText.Get("Select an existing supported static mesh file."));
            _=AholoHost.CaptureTarget(doc);
            Panels.OpenPanel(typeof(AholoPanel),true);
            pending=new(doc.RuntimeSerialNumber,path,Stopwatch.StartNew());
            global::Rhino.RhinoApp.Idle+=OnIdle;
            global::Rhino.RhinoApp.WriteLine(UiText.Get("Opening a local mesh candidate in Library. Review placement and accept it there. No cloud request is made."));
            return Result.Success;
        }
        catch(Exception error){global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error));return Result.Failure;}
    }
    internal static void Cancel(uint? serial=null)
    {
        if(pending is null||serial.HasValue&&pending.Serial!=serial)return;
        pending=null;global::Rhino.RhinoApp.Idle-=OnIdle;
    }
    private static void OnIdle(object? sender,EventArgs args)
    {
        var request=pending;if(request is null)return;
        try
        {
            var doc=RhinoDoc.FromRuntimeSerialNumber(request.Serial);
            if(doc is null||RhinoDoc.ActiveDoc?.RuntimeSerialNumber!=request.Serial)
                throw new InvalidOperationException(UiText.Get("The target document changed. Retry placement in the intended document."));
            if(request.Clock.Elapsed>TimeSpan.FromSeconds(30))
                throw new InvalidOperationException(UiText.Get("The mesh import could not start. Finish the current command and run AholoImportMesh again."));
            if(doc.IsCommandRunning||doc.UndoRecordingIsActive)return;
            var panel=Panels.GetPanel<AholoPanel>(request.Serial);if(panel is null)return;
            Cancel();_=StartAsync(panel,request.Path);
        }
        catch(Exception error){Cancel();global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error));}
    }
    private static async Task StartAsync(AholoPanel panel,string path)
    {
        try{await panel.ImportMeshCandidateAsync(path);}
        catch(Exception error){AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error)));}
    }
}
