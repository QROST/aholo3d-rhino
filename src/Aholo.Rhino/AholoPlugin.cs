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
        global::Rhino.UI.Panels.RegisterPanel(this, typeof(AholoPanel), "Aholo", null!, global::Rhino.UI.PanelType.PerDoc);
        GaussianViewport.Initialize(this); // Schedules non-fatal GS initialization on the UI idle event.
        global::Rhino.RhinoApp.WriteLine($"Aholo {DisplayVersion} · CuriousArc · qrost。输入 AholoPanel 打开世界生成与 RenderCloud 面板。");
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
        using var dialog = new Eto.Forms.OpenFileDialog { Title = "诊断：导入 PLY 中心点（Z 向上，1 PLY 单位 = 1 文档单位）", MultiSelect = false };
        dialog.Filters.Add(new Eto.Forms.FileFilter("PLY 点云", ".ply"));
        if (dialog.ShowDialog(global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc)) != Eto.Forms.DialogResult.Ok) return Result.Cancel;
        var target = AholoHost.CaptureTarget(doc);
        _ = ImportAsync(dialog.FileName, target);
        global::Rhino.RhinoApp.WriteLine("正在读取 PLY 点云。此命令预览中心点和颜色；完整 GS 请使用 AholoImportGs 或 AholoPanel。");
        return Result.Success;
    }
    private static async Task ImportAsync(string path, ImportTarget target)
    {
        try
        {
            var result = await AholoHost.ImportAsync(path, target, 1, "Z", null, CancellationToken.None);
            AholoHost.Ui(() => global::Rhino.RhinoApp.WriteLine(result));
        }
        catch (Exception ex) { AholoHost.Ui(() => global::Rhino.RhinoApp.WriteLine("Aholo 导入未完成：" + AholoHost.FriendlyError(ex))); }
    }
}
