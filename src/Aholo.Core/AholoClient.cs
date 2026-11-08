using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Aholo.Core;

/// <summary>One private child process per panel. Credentials stay in the child process after connection.</summary>
public sealed class AholoClient : IAsyncDisposable
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1);
    private Process? _process;
    private bool _disposed;
    public AholoClient(string sidecarDirectory) { _directory = Path.GetFullPath(sidecarDirectory); }

    public Task<ConnectionState> GetConnectionAsync(CancellationToken ct = default) => Call<ConnectionState>("connection", new { }, ct);
    public Task<ConnectionState> ConnectAsync(ConnectRequest request, CancellationToken ct = default) => Call<ConnectionState>("connect", request, ct);
    public Task<ConnectionState> DisconnectAsync(CancellationToken ct = default) => Call<ConnectionState>("disconnect", new { }, ct);
    public async Task<IReadOnlyList<LocalTask>> GetTasksAsync(CancellationToken ct = default) => await Call<LocalTask[]>("list", new { }, ct).ConfigureAwait(false);
    public Task<LocalTask> GenerateWorldAsync(GenerateWorldRequest request, CancellationToken ct = default) => Call<LocalTask>("generate", request, ct);
    public Task<LocalTask> RenderAsync(RenderRequest request, CancellationToken ct = default) => Call<LocalTask>("render", request, ct);
    public Task<LocalTask> RefreshAsync(Guid id, CancellationToken ct = default) => Call<LocalTask>("refresh", new { id }, ct);
    public Task<TaskPage> GetTaskPageAsync(TaskPageRequest request, CancellationToken ct = default) => Call<TaskPage>("page", request, ct);
    public Task<LocalTask> ContinueRenderAsync(Guid id, CancellationToken ct = default) => Call<LocalTask>("continueRender", new { id }, ct);
    public Task<LocalTask> DownloadAsync(DownloadRequest request, CancellationToken ct = default) => Call<LocalTask>("download", request, ct);
    public Task<LocalTask> ImportWorldAsync(string worldId, CancellationToken ct = default) => Call<LocalTask>("importWorld", new { worldId }, ct);

    private async Task<T> Call<T>(string method, object payload, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AholoClient));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(16));
            timeout.Token.ThrowIfCancellationRequested();
            EnsureProcess();
            var id = Guid.NewGuid();
            string request = JsonSerializer.Serialize(new RpcRequest(1, id, method, AholoJson.Element(payload)), AholoJson.Options);
            if (request.Length > 128 * 1024) throw new InvalidOperationException("The request is too long.");
            await _process!.StandardInput.WriteLineAsync(request.AsMemory(), timeout.Token).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
            string line = await BoundedLine.ReadAsync(_process.StandardOutput, timeout.Token).ConfigureAwait(false)
                ?? throw new IOException("The Aholo background process stopped. Recover the task from history; do not resubmit generations with unknown results.");
            var response = JsonSerializer.Deserialize<RpcResponse>(line, AholoJson.Options) ?? throw new IOException("Invalid background response.");
            if (response.Version != 1 || response.Id != id) throw new IOException("Background protocol mismatch.");
            if (response.Error is not null) throw new InvalidOperationException(response.Error);
            return response.Result!.Value.Deserialize<T>(AholoJson.Options) ?? throw new IOException("The background response is empty.");
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or JsonException)
        {
            StopProcess();
            throw;
        }
        finally { _gate.Release(); }
    }

    private void EnsureProcess()
    {
        if (_process is { HasExited: false }) return;
        _process?.Dispose();
        string executable = Path.Combine(_directory, OperatingSystem.IsWindows() ? "Aholo.Sidecar.exe" : "Aholo.Sidecar");
        string dll = Path.Combine(_directory, "Aholo.Sidecar.dll");
        bool standalone = File.Exists(executable);
        if (!standalone && !File.Exists(dll)) throw new IOException("Aholo background files are missing. Reinstall the complete plugin package.");
        var start = new ProcessStartInfo(standalone ? executable : SidecarRuntime.FindDotNet8())
        {
            WorkingDirectory = _directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false)
        };
        if (!standalone) start.ArgumentList.Add(dll);
        try { _process = Process.Start(start) ?? throw new IOException("Cannot start the Aholo background process."); }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new IOException("Cannot start the Aholo background process. Check file permissions and install the matching .NET 8 Runtime.");
        }
        // Drain diagnostics without copying provider responses or secrets into Rhino logs.
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();
    }
    private void StopProcess()
    {
        if (_process is null) return;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        _process.Dispose(); _process = null;
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { _disposed = true; StopProcess(); }
        finally { _gate.Release(); }
    }
}

public static class BoundedLine
{
    public static async Task<string?> ReadAsync(TextReader reader, CancellationToken ct, int maxLength = 2 * 1024 * 1024)
    {
        var text = new System.Text.StringBuilder();
        char[] one = new char[1];
        while (true)
        {
            int count = await reader.ReadAsync(one.AsMemory(), ct).ConfigureAwait(false);
            if (count == 0) return text.Length == 0 ? null : throw new IOException("Truncated protocol line.");
            if (one[0] == '\n') return text.ToString();
            if (text.Length >= maxLength) throw new IOException("Protocol line exceeds limit.");
            text.Append(one[0]);
        }
    }
}
