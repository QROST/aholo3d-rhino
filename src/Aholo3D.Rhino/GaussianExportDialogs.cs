using Eto.Forms;
namespace Aholo3D.Rhino;

internal static class GaussianExportDialogs
{
    internal static SaveFileDialog CreateSaveDialog()
    {
        var dialog = new SaveFileDialog { Title = global::Aholo3D.Core.UiText.Get("Export complete Gaussian Splat scene"), FileName = "gaussian-scene.usdz" };
        dialog.Filters.Add(new FileFilter(global::Aholo3D.Core.UiText.Get("USDZ · Portable single-file scene"), ".usdz"));
        dialog.Filters.Add(new FileFilter(global::Aholo3D.Core.UiText.Get("USD · Binary scene"), ".usd"));
        dialog.Filters.Add(new FileFilter(global::Aholo3D.Core.UiText.Get("USDC · Binary scene"), ".usdc"));
        dialog.Filters.Add(new FileFilter(global::Aholo3D.Core.UiText.Get("USDA · Text scene"), ".usda"));
        return dialog;
    }
    internal static string NormalizePath(SaveFileDialog dialog)
    {
        string path = Path.GetFullPath(dialog.FileName);
        if (Path.GetExtension(path).ToLowerInvariant() is not (".usd" or ".usda" or ".usdc" or ".usdz"))
            throw new ArgumentException(global::Aholo3D.Core.UiText.Get("Use a .usd, .usda, .usdc or .usdz file extension."));
        return path;
    }
}
