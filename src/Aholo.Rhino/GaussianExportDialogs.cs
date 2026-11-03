using Eto.Forms;
namespace Aholo.Rhino;

internal static class GaussianExportDialogs
{
    internal static SaveFileDialog CreateSaveDialog()
    {
        var dialog = new SaveFileDialog { Title = "导出完整 Gaussian Splat 场景", FileName = "gaussian-scene.usdz" };
        dialog.Filters.Add(new FileFilter("USDZ · 单文件便携场景", ".usdz"));
        dialog.Filters.Add(new FileFilter("USD · 二进制场景", ".usd"));
        dialog.Filters.Add(new FileFilter("USDC · 二进制场景", ".usdc"));
        dialog.Filters.Add(new FileFilter("USDA · 文本场景", ".usda"));
        return dialog;
    }
    internal static string NormalizePath(SaveFileDialog dialog)
    {
        string path = Path.GetFullPath(dialog.FileName);
        if (Path.GetExtension(path).ToLowerInvariant() is not (".usd" or ".usda" or ".usdc" or ".usdz"))
            throw new ArgumentException("请使用 .usd、.usda、.usdc 或 .usdz 文件扩展名。");
        return path;
    }
}
