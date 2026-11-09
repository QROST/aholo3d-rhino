using System.Diagnostics;
using System.Text.Json;

namespace Aholo.Core;

public sealed record UsdRuntimeStatus(bool Ready, string PythonPath, string Detail);

/// <summary>Local readiness probe and setup command discovery. Never installs packages or makes network calls.</summary>
public static class UsdRuntime
{
    public static string PythonPath
    {
        get
        {
            string? configured = Environment.GetEnvironmentVariable(UsdGaussianReader.PythonEnvironmentVariable);
            return !string.IsNullOrWhiteSpace(configured) ? Path.GetFullPath(configured) : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aholo-Rhino", "usd-runtime",
                OperatingSystem.IsWindows() ? "Scripts" : "bin", OperatingSystem.IsWindows() ? "python.exe" : "python3");
        }
    }

    public static string SetupScript
    {
        get
        {
            string start = Path.GetDirectoryName(typeof(UsdRuntime).Assembly.Location) ?? AppContext.BaseDirectory;
            string installed = Path.Combine(start, "scripts", "setup-usd.py");
            if (File.Exists(installed)) return installed;
            for (DirectoryInfo? folder = new(start); folder is not null; folder = folder.Parent)
            {
                string script = Path.Combine(folder.FullName, "scripts", "setup-usd.py");
                if (File.Exists(Path.Combine(folder.FullName, "src", "Aholo.Core", "Aholo.Core.csproj")) && File.Exists(script)) return script;
            }
            throw new FileNotFoundException("The Aholo package is missing scripts/setup-usd.py. Reinstall the complete package.");
        }
    }

    public static string SetupCommand(string script, bool windows, bool checkOnly = false)
    {
        if (string.IsNullOrWhiteSpace(script)) throw new ArgumentException("A setup script path is required.", nameof(script));
        if (script.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
            throw new ArgumentException("The setup path cannot be represented safely in a terminal command.", nameof(script));
        string quoted = windows ? "'" + script.Replace("'", "''") + "'" : "'" + script.Replace("'", "'\"'\"'") + "'";
        return (windows ? "py -3.12 " : "python3.12 ") + quoted + (checkOnly ? " --check" : "");
    }

    public static async Task<UsdRuntimeStatus> CheckAsync(string? pythonPath = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string python = pythonPath ?? PythonPath;
        if (!File.Exists(python)) return new(false, python, "OpenUSD runtime is not installed.");
        var start = new ProcessStartInfo(python)
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("-I"); start.ArgumentList.Add("-c");
        start.ArgumentList.Add("from pxr import Usd,UsdVol; import json; print(json.dumps({'version':list(Usd.GetVersion()),'gaussianSchema':hasattr(UsdVol,'ParticleField3DGaussianSplat')}))");
        start.Environment.Remove("PXR_PLUGINPATH_NAME");
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) return new(false, python, "OpenUSD runtime could not start.");
        }
        catch (System.ComponentModel.Win32Exception) { return new(false, python, "OpenUSD runtime could not start. Check its executable or reinstall it."); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        Task<string> stdout = Capture(process.StandardOutput, linked.Token), stderr = Capture(process.StandardError, linked.Token);
        // A launcher can exit while descendants still hold its output pipes. Bound the entire
        // operation, including pipe completion, rather than only the launcher's lifetime.
        Task completion = Task.WhenAll(process.WaitForExitAsync(linked.Token), stdout, stderr);
        try
        {
            await completion.WaitAsync(linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { /* Cleanup must not hide cancellation. */ }
            // Cancellation can win the race with pipe shutdown. Observe any later I/O fault
            // without waiting for an orphaned child to close an inherited handle.
            _ = completion.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            return new(false, python, "OpenUSD readiness check timed out.");
        }
        string output = await stdout.ConfigureAwait(false);
        _ = await stderr.ConfigureAwait(false);
        if (process.ExitCode != 0) return new(false, python, "OpenUSD could not load. Run the setup command to repair the private runtime.");
        try
        {
            using JsonDocument result = JsonDocument.Parse(output);
            bool ready = result.RootElement.GetProperty("version").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(new[] { 0, 26, 11 }) &&
                result.RootElement.GetProperty("gaussianSchema").GetBoolean();
            ct.ThrowIfCancellationRequested();
            return new(ready, python, ready ? "OpenUSD 26.11 is ready for local import and export." : "OpenUSD 26.11 with the Gaussian schema is required.");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        { return new(false, python, "OpenUSD returned an invalid readiness response."); }
    }

    private static async Task<string> Capture(StreamReader reader, CancellationToken ct)
    {
        var output = new System.Text.StringBuilder(); char[] buffer = new char[2048]; int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
            if (output.Length < 4096) output.Append(buffer, 0, Math.Min(count, 4096 - output.Length));
        return output.ToString();
    }
}
