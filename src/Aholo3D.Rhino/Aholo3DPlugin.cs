using System.Reflection;
using System.Runtime.InteropServices;
using Aholo3D.Core;
using Rhino.Commands;
using Rhino.PlugIns;
namespace Aholo3D.Rhino;

[Guid("D1D17217-1169-41BB-B63A-FEE17DFA777F")]
public sealed class Aholo3DPlugin : PlugIn
{
    // Persisted instances are ordinary blocks, so opening a 3dm does not itself request this plug-in.
    // Register the conduit at startup to restore saved GS views without requiring a panel command.
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;
    internal static string DisplayVersion => typeof(Aholo3DPlugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";
    private static readonly Lazy<Aholo3DSession> SharedSession = new(() => new Aholo3DSession(
        new Aholo3DClient(Path.Combine(Path.GetDirectoryName(typeof(Aholo3DPlugin).Assembly.Location)!, "sidecar")),
        new TaskStore(ProductPaths.DataRoot)));
    internal static Aholo3DSession Session => SharedSession.Value;
    private static MeshHostService? meshHost;
    internal static MeshHostService MeshHost => meshHost ??= new MeshHostService(Session.Store.Root);
    private static MeshPlacementService? placement;
    internal static MeshPlacementService Placement => placement ??= new MeshPlacementService(Session,MeshHost);
    internal static IAholo3DServiceClient CreateClient() => Session.Client;
    internal static string DocumentScope(uint serial) => "document:" + serial.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
    internal static event Action<uint>? DocumentCancellationRequested;
    internal static void CancelDocumentOperations(uint serial)
    {
        // Revoke every live panel's pending placement authority before cancelling active work.
        // Hidden panels also subscribe; GetPanel alone cannot enumerate every live instance.
        try
        {
            foreach(Action<uint> observer in DocumentCancellationRequested?.GetInvocationList()??Array.Empty<Delegate>())
                try{observer(serial);}catch(Exception error){global::Rhino.RhinoApp.WriteLine(Aholo3DHost.FriendlyError(error));}
        }
        finally
        {
            Aholo3DImportMeshCommand.Cancel(serial);
            if(SharedSession.IsValueCreated)Session.Operations.CancelPrefix(DocumentScope(serial));
        }
    }
    private static void OnDocumentClosed(object? sender, global::Rhino.DocumentEventArgs e)
    { PanelNavigation.Close(e.Document.RuntimeSerialNumber);CancelDocumentOperations(e.Document.RuntimeSerialNumber); meshHost?.Forget(e.Document.RuntimeSerialNumber); }
    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        int? language = null;
        try { language = global::Rhino.UI.Localization.CurrentLanguageId; }
        catch (Exception) { /* Use the system UI language when Rhino cannot provide one during startup. */ }
        UiText.Initialize(language, System.Globalization.CultureInfo.CurrentUICulture.Name);
        global::Rhino.UI.Panels.RegisterPanel(this, typeof(Aholo3DPanel), "Aholo3D", null!, global::Rhino.UI.PanelType.PerDoc);
        global::Rhino.RhinoDoc.CloseDocument += OnDocumentClosed;
        GaussianViewport.Initialize(this); // Schedules non-fatal GS initialization on the UI idle event.
        global::Rhino.RhinoApp.WriteLine(global::Aholo3D.Core.UiText.Format("Aholo3D {0} · CuriousArc · qrost. Run Aholo3DPanel to open world generation and RenderCloud.", DisplayVersion));
        return LoadReturnCode.Success;
    }
    protected override void OnShutdown()
    {
        global::Rhino.RhinoDoc.CloseDocument -= OnDocumentClosed;
        PanelNavigation.Shutdown();Aholo3DImportMeshCommand.Cancel();
        if(SharedSession.IsValueCreated)_ = Session.DisposeAsync().AsTask();
        meshHost?.Dispose();meshHost=null;
        GaussianViewport.Shutdown();
        base.OnShutdown();
    }
}

[Guid("716AD812-E24C-4317-8566-C9D3A533F109")]
public sealed class Aholo3DPanelCommand : Command
{
    public override string EnglishName => "Aholo3DPanel";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Execute(doc,mode);
    internal static Result Execute(global::Rhino.RhinoDoc doc, RunMode mode)
    {
        global::Rhino.UI.Panels.OpenPanel(typeof(Aholo3DPanel));
        return Result.Success;
    }
}

[Guid("927DAA97-9449-4331-AD19-EB43788B0054")]
public sealed class Aholo3DImportPlyCommand : Command
{
    public override string EnglishName => "Aholo3DImportPly";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode) => Execute(doc,mode);
    internal static Result Execute(global::Rhino.RhinoDoc doc, RunMode mode)
    {
        try
        {
            string path;
            if(mode==RunMode.Scripted)
            {
                path="";var result=global::Rhino.Input.RhinoGet.GetString(UiText.Get("Local PLY file"),false,ref path);
                if(result!=Result.Success)return result;path=Path.GetFullPath(path);
            }
            else
            {
                using var dialog = new Eto.Forms.OpenFileDialog { Title = UiText.Get("Diagnostic: import PLY centers (Z-up, 1 PLY unit = 1 document unit)"), MultiSelect = false };
                dialog.Filters.Add(new Eto.Forms.FileFilter(UiText.Get("PLY point cloud"), ".ply"));
                if (dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc)) != Eto.Forms.DialogResult.Ok) return Result.Cancel;
                path=dialog.FileName;
            }
            var target = Aholo3DHost.CaptureTarget(doc);
            CommandOperation.Start(target.Serial,"ply-centers","Import PLY centers",ct=>Aholo3DHost.ImportAsync(path,target,1,"Z",null,ct));
            global::Rhino.RhinoApp.WriteLine(UiText.Get("Reading the PLY point cloud. This command previews centers and colors; use Aholo3DImportGs or Aholo3DPanel for complete GS."));
            return Result.Success;
        }
        catch(Exception error){global::Rhino.RhinoApp.WriteLine(Aholo3DHost.FriendlyError(error));return Result.Failure;}
    }
}
