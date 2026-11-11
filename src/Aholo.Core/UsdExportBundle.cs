namespace Aholo.Core;

internal sealed record UsdExportBundleResult(string Path, string? AssetDirectory);

/// <summary>Publish immutable texture bundles before atomically replacing their USD root file.</summary>
internal static class UsdExportBundle
{
    internal static UsdExportBundleResult Write(string path, Action<string,string,string,CancellationToken> build, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string destination = Path.GetFullPath(path), extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not (".usd" or ".usda" or ".usdc" or ".usdz")) throw new ArgumentException(UiText.Get("Use a .usd, .usda, .usdc or .usdz file extension."));
        string parent = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(parent)) throw new DirectoryNotFoundException(parent);
        using var destinationLease=OutputFileLease.Acquire(destination,ct);
        string id = Guid.NewGuid().ToString("N"), assets = "usd-assets-" + id;
        string workspace = UsdBridgeProcess.CreateWorkspace();
        string pending = destination + "." + id + ".tmp", pendingAssets = Path.Combine(parent, assets + ".tmp"), finalAssets = Path.Combine(parent, assets);
        bool movedAssets = false, committed = false;
        try
        {
            build(workspace, assets, extension[1..], ct);
            string produced = Path.Combine(workspace, "result" + extension);
            if (!File.Exists(produced) || new FileInfo(produced).Length == 0) throw new InvalidDataException("OpenUSD export produced no complete scene.");
            bool hasAssets = extension != ".usdz" && Directory.Exists(Path.Combine(workspace, assets));
            if (hasAssets)
            {
                if (OperatingSystem.IsWindows()) Directory.CreateDirectory(pendingAssets);
                else Directory.CreateDirectory(pendingAssets, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                foreach (string source in Directory.EnumerateFiles(Path.Combine(workspace, assets)))
                    Copy(source, Path.Combine(pendingAssets, Path.GetFileName(source)), ct);
            }
            Copy(produced, pending, ct);
            ct.ThrowIfCancellationRequested();
            if (hasAssets) { Directory.Move(pendingAssets, finalAssets); movedAssets = true; }
            ct.ThrowIfCancellationRequested();
            // The rename is the commit point: cancellation after it is success, never a false rollback.
            File.Move(pending, destination, overwrite: true); committed = true;
            return new(destination, hasAssets ? finalAssets : null);
        }
        finally
        {
            try { if (File.Exists(pending)) File.Delete(pending); }
            finally
            {
                UsdBridgeProcess.RemoveWorkspace(pendingAssets);
                if (movedAssets && !committed) UsdBridgeProcess.RemoveWorkspace(finalAssets);
                UsdBridgeProcess.RemoveWorkspace(workspace);
            }
        }
    }

    private static void Copy(string source, string target, CancellationToken ct)
    {
        using var input = File.OpenRead(source);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var output = new FileStream(target, options);
        byte[] buffer = new byte[131072]; int count;
        while ((count = input.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
        ct.ThrowIfCancellationRequested(); output.Flush(true);
    }

}
