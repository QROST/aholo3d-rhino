using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Aholo3D.Core;

/// <summary>Trusted local OpenUSD process and private workspace lifecycle shared by import and export.</summary>
internal static class UsdBridgeProcess
{
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromMinutes(15);

    internal static string CreateWorkspace()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Aholo3D", "usd-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return directory;
    }

    internal static void RemoveWorkspace(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { /* Do not hide a conversion result with a cleanup failure. */ }
        catch (UnauthorizedAccessException) { }
    }

    internal static string ResolvePython()
    {
        string python = UsdRuntime.PythonPath;
        if (!File.Exists(python)) throw new InvalidOperationException("OpenUSD runtime is not installed. Run the Aholo3D USD setup or set AHOLO3D_USD_PYTHON to a Python executable containing usd-core==26.11.");
        return python;
    }

    private static string ResolveBridge(bool mesh, bool scene)
    {
        string filename = scene ? "usd_scene_bridge.py" : mesh ? "usd_mesh_bridge.py" : "usd_bridge.py";
        string assemblyDirectory = Path.GetDirectoryName(typeof(UsdGaussianReader).Assembly.Location) ?? AppContext.BaseDirectory;
        foreach (string start in new[] { assemblyDirectory, AppContext.BaseDirectory })
        {
            string packaged = Path.Combine(start, "usd", filename);
            if (File.Exists(packaged)) return packaged;
        }
        // Source-tree fallback is for development; never load scripts next to an imported asset.
        foreach (string start in new[] { assemblyDirectory })
            for (DirectoryInfo? current = new(start); current is not null; current = current.Parent)
            {
                string project = Path.Combine(current.FullName, "src", "Aholo3D.Core", "Aholo3D.Core.csproj");
                string script = Path.Combine(current.FullName, "scripts", filename);
                if (File.Exists(project) && File.Exists(script)) return script;
            }
        throw new FileNotFoundException("The installed Aholo3D package is missing usd/" + filename + ".");
    }

    internal static void Run(string input, string output, CancellationToken ct, string? exportFormat = null, bool mesh = false, bool scene = false)
    {
        ct.ThrowIfCancellationRequested();
        string python = ResolvePython(), script = ResolveBridge(mesh, scene);
        var start = new ProcessStartInfo(python)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        };
        foreach (string argument in new[] { "-I", script, "--input", input, "--output", output }) start.ArgumentList.Add(argument);
        if (exportFormat is not null)
            foreach (string argument in new[] { "--export", "--format", exportFormat }) start.ArgumentList.Add(argument);
        start.Environment.Remove("PXR_PLUGINPATH_NAME");
        using var process = new Process { StartInfo = start };
        try { if (!process.Start()) throw new InvalidOperationException("OpenUSD process could not start."); }
        catch (System.ComponentModel.Win32Exception ex) { throw new InvalidOperationException("OpenUSD runtime could not start. Check AHOLO3D_USD_PYTHON and the USD runtime installation.", ex); }
        using var timeout = new CancellationTokenSource(BridgeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        Task<string> stdout = Capture(process.StandardOutput, linked.Token), stderr = Capture(process.StandardError, linked.Token);
        // A launcher may exit while a child still holds its output pipes. Bound the whole collection, not only exit.
        Task completion = Task.WhenAll(stdout, stderr, process.WaitForExitAsync(linked.Token));
        try
        {
            completion.WaitAsync(linked.Token).GetAwaiter().GetResult();
            linked.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            _ = completion.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new TimeoutException("OpenUSD conversion exceeded the 15-minute conversion timeout. The local wait ended; check any custom launcher before retrying.");
        }
        finally { if (!process.HasExited) Kill(process); }
        string diagnostics = stderr.GetAwaiter().GetResult();
        string standardOutput = stdout.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            string message = ReadError(output) ?? diagnostics.Trim();
            if (message.Length == 0) message = standardOutput.Trim();
            throw new InvalidDataException(UiText.Get("OpenUSD conversion failed: ") + UiText.TranslateKnownMessage(message.Length == 0 ? "the decoder returned no diagnostic." : message));
        }
    }

    private static string? ReadError(string directory)
    {
        string path = Path.Combine(directory, "error.json");
        if (!File.Exists(path)) return null;
        try { using JsonDocument error = JsonDocument.Parse(File.ReadAllText(path)); return error.RootElement.GetProperty("message").GetString(); }
        catch (JsonException) { return null; }
        catch (KeyNotFoundException) { return null; }
    }

    private static async Task<string> Capture(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder(); char[] buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            if (text.Length < 16384) text.Append(buffer, 0, Math.Min(count, 16384 - text.Length));
        return text.ToString();
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { /* Cleanup must not hide the original cancellation or failure. */ }
        try { process.WaitForExit(5000); }
        catch (InvalidOperationException) { }
    }
}
