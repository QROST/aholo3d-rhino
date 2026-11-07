using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Aholo.Core;
using Rhino.Commands;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
namespace Aholo.Rhino;

[Guid("3D9FD212-DCBF-4CB1-A0C2-1536C857B43E")]
public sealed class AholoImportGsCommand : Command
{
    public override string EnglishName => "AholoImportGs";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        try
        {
            using var dialog=new Eto.Forms.OpenFileDialog { Title=global::Aholo.Core.UiText.Get("Import complete GS · PLY / USD / USDZ (automatic units and up axis)"),MultiSelect=false };
            dialog.Filters.Add(new Eto.Forms.FileFilter(global::Aholo.Core.UiText.Get("Gaussian Splat scene"),".ply",".usd",".usda",".usdc",".usdz"));
            if(dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc))!=Eto.Forms.DialogResult.Ok)return Result.Cancel;
            ImportTarget target=AholoHost.CaptureTarget(doc);
            global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("The experimental GS viewport prepares all Gaussians in the background and shows bounds while loading. Keep the target document units unchanged."));
            _=ImportAsync(dialog.FileName,target); return Result.Success;
        }
        catch (Exception ex) { global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(ex)); return Result.Failure; }
    }
    private static async Task ImportAsync(string path,ImportTarget target)
    {
        try
        {
            string result=await GaussianDocument.ImportSceneAsync(path,target,1,"Auto",true,CancellationToken.None).ConfigureAwait(false);
            AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(result));
        }
        catch(Exception ex){AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("GS import did not finish: ")+AholoHost.FriendlyError(ex)));}
    }
}

[Guid("36DD3D78-8407-4A81-9588-26D1302FB23E")]
public sealed class AholoGsModeCommand : Command
{
    public override string EnglishName=>"AholoGsMode";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        try
        {
            GaussianViewport.Activate(doc);
            global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("Aholo GS (Experimental) enabled. Opaque-geometry occlusion still requires native acceptance; transparency interleaving is unsupported. Missing source assets appear as bounds."));
            return Result.Success;
        }
        catch(Exception ex){global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(ex));return Result.Failure;}
    }
}

[Guid("C9D00C1E-DFCC-4623-B927-BC45EC976670")]
public sealed class AholoGsDiagnosticsCommand : Command
{
    public override string EnglishName=>"AholoGsDiagnostics";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        global::Rhino.RhinoApp.WriteLine($"Aholo {AholoPlugin.DisplayVersion}; Rhino {(global::Rhino.RhinoApp.Version)}; {RuntimeInformation.OSDescription}; {RuntimeInformation.ProcessArchitecture}");
        global::Rhino.RhinoApp.WriteLine(GaussianViewport.Diagnostics(doc));
        return Result.Success;
    }
}

[Guid("3B6E174F-0671-43D7-8B32-C41DD68E02DE")]
public sealed class AholoGsProbeCommand : Command
{
    public override string EnglishName=>"AholoGsProbe";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)
    {
        ImportTarget target=AholoHost.CaptureTarget(doc);
        global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("Creating an offline GS occlusion probe: two complete Gaussian groups, one opaque Brep cube and one Mesh. This adds local test objects without cloud access and supports Undo. Orbit the camera to check occlusion and soft transparent edges."));
        _=CreateAsync(target); return Result.Success;
    }
    private static async Task CreateAsync(ImportTarget target)
    {
        try
        {
            string path=await Task.Run(WriteFixture).ConfigureAwait(false);
            string result=await GaussianDocument.ImportAsync(path,target,1,"Z",null,CancellationToken.None).ConfigureAwait(false);
            await AholoHost.OnUi(()=>
            {
                RhinoDoc doc=RhinoDoc.FromRuntimeSerialNumber(target.Serial)??throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The probe target document has closed."));
                if(RhinoDoc.ActiveDoc?.RuntimeSerialNumber!=target.Serial||doc.ModelUnitSystem!=target.Units||doc.IsClosing||doc.IsReadOnly||doc.IsCommandRunning||doc.UndoRecordingIsActive)
                    throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The target document changed. GS was imported, but occlusion test geometry was not added."));
                uint undo=doc.BeginUndoRecord(global::Aholo.Core.UiText.Get("Add GS occlusion test geometry"));
                if(undo==0)throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot create the test geometry Undo record."));
                try
                {
                    using Brep cube=new BoundingBox(new Point3d(-.35,-.8,-1.5),new Point3d(.35,.8,1.5)).ToBrep();
                    using var attrs=new ObjectAttributes{LayerIndex=GaussianDocument.VisibleLayer(doc),Name="Aholo GS probe · opaque Brep",ObjectColor=Color.FromArgb(65,95,160),ColorSource=ObjectColorSource.ColorFromObject};
                    if(doc.Objects.AddBrep(cube,attrs)==Guid.Empty) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot add the Brep occlusion test geometry."));
                    using var mesh=Mesh.CreateFromBox(new BoundingBox(new Point3d(1.2,-1.0,-.25),new Point3d(2.3,1.0,.25)),1,1,1);
                    attrs.Name="Aholo GS probe · opaque Mesh";attrs.ObjectColor=Color.FromArgb(170,140,65);
                    if(doc.Objects.AddMesh(mesh,attrs)==Guid.Empty) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot add the Mesh occlusion test geometry. Use Undo to remove geometry already added."));
                }
                finally{doc.EndUndoRecord(undo);}
                var viewport=doc.Views.ActiveView?.ActiveViewport;
                if(viewport is not null)
                {
                    viewport.ChangeToPerspectiveProjection(true,50);
                    viewport.SetCameraLocations(Point3d.Origin,new Point3d(5,-12,4));
                    viewport.ZoomBoundingBox(new BoundingBox(new Point3d(-3,-2,-2.5),new Point3d(3,2,2.5)));
                }
                doc.Objects.UnselectAll();doc.Views.Redraw();global::Rhino.RhinoApp.WriteLine(result);
                global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("Use Undo twice: first remove the occlusion geometry, then the GS blocks. Visual acceptance is not automatic; run AholoGsDiagnostics for status."));
                return true;
            }).ConfigureAwait(false);
        }
        catch(Exception ex){AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("GS probe did not finish: ")+AholoHost.FriendlyError(ex)));}
    }
    private static string WriteFixture()
    {
        const double sh0=.28209479177387814;
        var data=new StringBuilder("ply\nformat ascii 1.0\ncomment Aholo offline GS occlusion probe by CuriousArc and qrost\nelement vertex 18\nproperty float x\nproperty float y\nproperty float z\nproperty float f_dc_0\nproperty float f_dc_1\nproperty float f_dc_2\nproperty float opacity\nproperty float scale_0\nproperty float scale_1\nproperty float scale_2\nproperty float rot_0\nproperty float rot_1\nproperty float rot_2\nproperty float rot_3\nend_header\n");
        for(int layer=0;layer<2;layer++)for(int row=0;row<3;row++)for(int column=0;column<3;column++)
        {
            double angle=(column-1)*.3;
            double[] values={ (column-1)*1.6,layer==0?-.45:.45,(row-1)*1.15,
                ((layer==0?.95:.15)-.5)/sh0,((layer==0?.25:.85)-.5)/sh0,((layer==0?.15:.75)-.5)/sh0,
                1.2,Math.Log(.55),Math.Log(.12),Math.Log(.28),Math.Cos(angle/2),0,Math.Sin(angle/2),0 };
            data.AppendLine(string.Join(" ",values.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))));
        }
        string path=Path.Combine(TaskStore.DefaultRoot,"inputs","aholo-gs-occlusion-probe.ply");
        TaskStore.WriteAtomic(path,Encoding.ASCII.GetBytes(data.ToString()));return path;
    }
}

[Guid("675D674B-5A08-4D85-86C0-57099AB2F528")]
public sealed class AholoExportGsCommand : Command
{
    public override string EnglishName => "AholoExportGs";
    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        try
        {
            GaussianDocument.ExportSnapshot snapshot = GaussianDocument.CaptureExport(doc, selectedOnly: true);
            using var dialog = GaussianExportDialogs.CreateSaveDialog();
            if (dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc)) != Eto.Forms.DialogResult.Ok) return Result.Cancel;
            global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("Exporting selected GS in the background using current document coordinates and units. Use AholoPanel to export all GS or to cancel."));
            _ = ExportAsync(GaussianExportDialogs.NormalizePath(dialog), snapshot);
            return Result.Success;
        }
        catch (Exception ex) { global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(ex)); return Result.Failure; }
    }
    private static async Task ExportAsync(string path, GaussianDocument.ExportSnapshot snapshot)
    {
        try
        {
            string result = await GaussianDocument.ExportAsync(path, snapshot, CancellationToken.None).ConfigureAwait(false);
            AholoHost.Ui(() => global::Rhino.RhinoApp.WriteLine(result));
        }
        catch (Exception ex) { AholoHost.Ui(() => global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("GS export did not finish: ") + AholoHost.FriendlyError(ex))); }
    }
}
