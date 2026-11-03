using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Aholo.Core;

/// <summary>Trusted local OpenUSD process and private workspace lifecycle shared by import and export.</summary>
internal static class UsdBridgeProcess
{
    private static readonly TimeSpan BridgeTimeout = TimeSpan.FromMinutes(15);

    internal static string CreateWorkspace()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Aholo", "usd-" + Guid.NewGuid().ToString("N"));
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
        string? configured = Environment.GetEnvironmentVariable(UsdGaussianReader.PythonEnvironmentVariable);
        string python = !string.IsNullOrWhiteSpace(configured) ? Path.GetFullPath(configured) : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aholo-Rhino", "usd-runtime",
            OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python3");
        if (!File.Exists(python)) throw new InvalidOperationException("OpenUSD runtime is not installed. Run the Aholo USD setup or set AHOLO_USD_PYTHON to a Python executable containing usd-core==26.11.");
        return python;
    }

    private static string ResolveBridge()
    {
        string assemblyDirectory = Path.GetDirectoryName(typeof(UsdGaussianReader).Assembly.Location) ?? AppContext.BaseDirectory;
        foreach (string start in new[] { assemblyDirectory, AppContext.BaseDirectory })
        {
            string packaged = Path.Combine(start, "usd", "usd_bridge.py");
            if (File.Exists(packaged)) return packaged;
        }
        // Source-tree fallback is for development; never load scripts next to an imported asset.
        foreach (string start in new[] { assemblyDirectory })
            for (DirectoryInfo? current = new(start); current is not null; current = current.Parent)
            {
                string project = Path.Combine(current.FullName, "src", "Aholo.Core", "Aholo.Core.csproj");
                string script = Path.Combine(current.FullName, "scripts", "usd_bridge.py");
                if (File.Exists(project) && File.Exists(script)) return script;
            }
        throw new FileNotFoundException("The installed Aholo package is missing usd/usd_bridge.py.");
    }

    internal static void Run(string input, string output, CancellationToken ct, string? exportFormat = null)
    {
        ct.ThrowIfCancellationRequested();
        string python = ResolvePython(), script = ResolveBridge();
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
        catch (System.ComponentModel.Win32Exception ex) { throw new InvalidOperationException("OpenUSD runtime could not start. Check AHOLO_USD_PYTHON and the USD runtime installation.", ex); }
        Task<string> stdout = Capture(process.StandardOutput), stderr = Capture(process.StandardError);
        using var timeout = new CancellationTokenSource(BridgeTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try { process.WaitForExitAsync(linked.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            Kill(process);
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new TimeoutException("OpenUSD conversion exceeded the 15-minute conversion timeout. The conversion process was stopped.");
        }
        finally { if (!process.HasExited) Kill(process); }
        string diagnostics = stderr.GetAwaiter().GetResult();
        string standardOutput = stdout.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            string message = ReadError(output) ?? diagnostics.Trim();
            if (message.Length == 0) message = standardOutput.Trim();
            throw new InvalidDataException("OpenUSD conversion failed: " + (message.Length == 0 ? "the decoder returned no diagnostic." : message));
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

    private static async Task<string> Capture(StreamReader reader)
    {
        var text = new StringBuilder(); char[] buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
            if (text.Length < 16384) text.Append(buffer, 0, Math.Min(count, 16384 - text.Length));
        return text.ToString();
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        try { process.WaitForExit(5000); }
        catch (InvalidOperationException) { }
    }
}
