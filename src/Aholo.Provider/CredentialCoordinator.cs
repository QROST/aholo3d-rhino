using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aholo.Core;

namespace Aholo.Provider;

public sealed record CredentialSnapshot(ApiKeyCredential? Key, bool Remembered, string Epoch);

/// <summary>Coordinates the fixed OS credential scope across processes. State files contain no secrets.</summary>
public sealed class CredentialCoordinator
{
    public const string Account = "international-api-key";
    private sealed record State(int SchemaVersion, string Epoch, bool Ready);
    private readonly ICredentialStore store;
    private readonly string statePath;
    private CredentialSnapshot? cached;
    public static string DefaultRoot => Path.Combine(TaskStore.DefaultRoot, "credential-state");
    public CredentialCoordinator(ICredentialStore store, string? root = null)
    {
        this.store = store;
        string scope = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CredentialStoreFactory.DefaultService + "\0" + Account))).ToLowerInvariant();
        statePath = Path.Combine(root ?? DefaultRoot, scope + ".json");
    }
    public async Task<CredentialSnapshot> ReadAsync(CancellationToken ct = default)
    {
        using var gate = await LockAsync(ct).ConfigureAwait(false);
        State state = ReadState();
        if (!state.Ready) { cached = null; throw Changed(); }
        if (cached?.Epoch == state.Epoch) return cached;
        cached = null;
        string? secret = await store.GetAsync(Account, ct).ConfigureAwait(false);
        var key = string.IsNullOrWhiteSpace(secret) ? null : new ApiKeyCredential(secret);
        return cached = new(key, key is not null, state.Epoch);
    }
    public async Task<CredentialSnapshot> ConnectAsync(ApiKeyCredential key, bool remember, CancellationToken ct = default)
        => await ChangeAsync(key, remember, ct).ConfigureAwait(false);
    public async Task<CredentialSnapshot> DisconnectAsync(CancellationToken ct = default)
        => await ChangeAsync(null, false, ct).ConfigureAwait(false);
    private async Task<CredentialSnapshot> ChangeAsync(ApiKeyCredential? key, bool remember, CancellationToken ct)
    {
        using var gate = await LockAsync(ct).ConfigureAwait(false);
        cached = null;
        var state = new State(1, Guid.NewGuid().ToString("N"), false);
        WriteState(state); // Crash/failure after this point requires an explicit reconnect; never inherit the old key.
        if (remember && key is not null) await store.SetAsync(Account, key.Value, ct).ConfigureAwait(false);
        else await store.DeleteAsync(Account, ct).ConfigureAwait(false);
        WriteState(state with { Ready = true });
        return cached = new(key, remember && key is not null, state.Epoch);
    }
    public async Task<ApiKeyCredential> RequireAsync(string? expectedEpoch, CancellationToken ct = default)
    {
        using var gate = await AcquireRequestAsync(expectedEpoch, ct).ConfigureAwait(false);
        return cached?.Key ?? throw new InvalidOperationException("Connect an international API Key first.");
    }
    /// <summary>Held through HTTP admission/response headers. A credential change cannot pass a request waiting in this gate.</summary>
    public async Task<IDisposable> AcquireRequestAsync(string? expectedEpoch, CancellationToken ct = default)
    {
        var gate = await LockAsync(ct).ConfigureAwait(false);
        try
        {
            State state = ReadState();
            if (!state.Ready || cached?.Epoch != state.Epoch)
            { cached = null; throw Changed(); }
            // A stale caller must be rejected without erasing a newer session-only key.
            if (string.IsNullOrEmpty(expectedEpoch) || state.Epoch != expectedEpoch) throw Changed();
            return gate;
        }
        catch { gate.Dispose(); throw; }
    }
    private State ReadState()
    {
        if (!File.Exists(statePath)) { var state = new State(1, Guid.NewGuid().ToString("N"), true); WriteState(state); return state; }
        try
        {
            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(statePath));
            if (state?.SchemaVersion != 1 || !Guid.TryParseExact(state.Epoch, "N", out _)) throw Changed();
            return state;
        }
        catch (JsonException) { throw Changed(); }
    }
    private void WriteState(State state)
    {
        string temporary = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var output = new FileStream(temporary, options))
            { JsonSerializer.Serialize(output, state); output.Flush(true); }
            File.Move(temporary, statePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<FileStream> LockAsync(CancellationToken ct)
    {
        string directory = Path.GetDirectoryName(statePath)!;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(statePath + ".lock", options); }
            catch (IOException) when (File.Exists(statePath + ".lock"))
            {
                if (elapsed.Elapsed >= TimeSpan.FromSeconds(30)) throw new IOException("Another Aholo credential operation is still active. Retry after it finishes.");
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }
        // The lock file is permanent; deleting it would allow different inode owners.
    }
    private static ProviderException Changed() => new("credential_changed", "The saved connection changed or is incomplete. Restore the connection or explicitly reconnect. Existing tasks are retained.");
}
