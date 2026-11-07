using System.Reflection;
using System.Runtime.InteropServices;
using Aholo.Core;
using Rhino.Commands;
using Rhino.PlugIns;
namespace Aholo.Rhino;

[Guid("D1D17217-1169-41BB-B63A-FEE17DFA777F")]
public sealed class AholoPlugin : PlugIn
{
    // Persisted instances are ordinary blocks, so opening a 3dm does not itself request this plug-in.
    // Register the conduit at startup to restore saved GS views without requiring a panel command.
    public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;
    internal static string DisplayVersion => typeof(AholoPlugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";
    internal static AholoClient CreateClient() => new(Path.Combine(Path.GetDirectoryName(typeof(AholoPlugin).Assembly.Location)!, "sidecar"));
    protected override LoadReturnCode OnLoad(ref string errorMessage)
    {
        int? language = null;
        try { language = global::Rhino.UI.Localization.CurrentLanguageId; }
        catch (Exception) { /* Use the system UI language when Rhino cannot provide one during startup. */ }
        UiText.Initialize(language, System.Globalization.CultureInfo.CurrentUICulture.Name);
        global::Rhino.UI.Panels.RegisterPanel(this, typeof(AholoPanel), "Aholo", null!, global::Rhino.UI.PanelType.PerDoc);
        GaussianViewport.Initialize(this); // Schedules non-fatal GS initialization on the UI idle event.
        global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Format("Aholo {0} · CuriousArc · qrost. Run AholoPanel to open world generation and RenderCloud.", DisplayVersion));
        return LoadReturnCode.Success;
    }
    protected override void OnShutdown()
    {
        GaussianViewport.Shutdown();
        base.OnShutdown();
    }
}

[Guid("716AD812-E24C-4317-8566-C9D3A533F109")]
public sealed class AholoPanelCommand : Command
{
    public override string EnglishName => "AholoPanel";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode)
    {
        global::Rhino.UI.Panels.OpenPanel(typeof(AholoPanel));
        return Result.Success;
    }
}

[Guid("927DAA97-9449-4331-AD19-EB43788B0054")]
public sealed class AholoImportPlyCommand : Command
{
    public override string EnglishName => "AholoImportPly";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc, RunMode mode)
    {
        using var dialog = new Eto.Forms.OpenFileDialog { Title = global::Aholo.Core.UiText.Get("Diagnostic: import PLY centers (Z-up, 1 PLY unit = 1 document unit)"), MultiSelect = false };
        dialog.Filters.Add(new Eto.Forms.FileFilter(global::Aholo.Core.UiText.Get("PLY point cloud"), ".ply"));
        if (dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc)) != Eto.Forms.DialogResult.Ok) return Result.Cancel;
        var target = AholoHost.CaptureTarget(doc);
        _ = ImportAsync(dialog.FileName, target);
        global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("Reading the PLY point cloud. This command previews centers and colors; use AholoImportGs or AholoPanel for complete GS."));
        return Result.Success;
    }
    private static async Task ImportAsync(string path, ImportTarget target)
    {
        try
        {
            var result = await AholoHost.ImportAsync(path, target, 1, "Z", null, CancellationToken.None);
            AholoHost.Ui(() => global::Rhino.RhinoApp.WriteLine(result));
        }
        catch (Exception ex) { AholoHost.Ui(() => global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("Aholo import did not finish: ") + AholoHost.FriendlyError(ex))); }
    }
}
