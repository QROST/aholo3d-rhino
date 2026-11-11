namespace Aholo.Core;

/// <summary>Pins one result slot across refresh/download; never submits generation.</summary>
public sealed record MeshTaskSelection(TaskIdentity Task, string? Operation, string Key, string Format, string? Sha256)
{
    public Guid ImportId => MeshPlacementPolicy.ImportId(Task.LocalId, Key);
    public static MeshTaskSelection Capture(LocalTask task, string? key = null)
    {
        var artifact = MeshPlacementPolicy.SelectStaticMesh(task, key);
        return new(TaskIdentity.From(task), task.ServiceOperation, artifact.Key, artifact.Format, artifact.Sha256);
    }
    public TaskArtifact Validate(LocalTask task)
    {
        if (TaskIdentity.From(task) != Task || task.ServiceOperation != Operation)
            throw Changed();
        var artifact = MeshPlacementPolicy.SelectStaticMesh(task, Key);
        if (artifact.Format != Format || Sha256 is not null && !string.Equals(Sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            throw Changed();
        return artifact;
    }
    private static InvalidOperationException Changed() => new(UiText.Get("The selected task result changed. Select it again before placement."));
}

public static class MeshTaskSource
{
    // DownloadAsync already queries the original task. Avoid a redundant refresh for a known slot.
    public static async Task<TaskArtifact> AcquireAsync(MeshTaskSelection selection, LocalTask task,
        Func<ConnectionState> connection, Func<DownloadRequest, CancellationToken, Task<LocalTask>> download,
        Action<LocalTask> observe, Func<CancellationToken, Task> validateIntent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await validateIntent(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var artifact = selection.Validate(task);
        if (artifact.LocalPath is not null && File.Exists(artifact.LocalPath)) return ValidatePath(artifact);
        var expected = RequireConnection(task, connection());
        ct.ThrowIfCancellationRequested();
        var updated = await download(new(task.Id, "artifact:" + selection.Key), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await validateIntent(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        RequireEpoch(expected, connection());
        artifact = selection.Validate(updated);
        if (updated.Status != "SUCCEEDED" || artifact.Sha256 is null || artifact.LocalPath is null || !File.Exists(artifact.LocalPath))
            throw new InvalidOperationException(UiText.Get("The original task result is not available locally. No new generation was submitted."));
        artifact = ValidatePath(artifact);
        observe(updated);
        return artifact;
    }
    public static async Task<LocalTask> DiscoverAsync(LocalTask task, string? key, Func<ConnectionState> connection,
        Func<Guid, CancellationToken, Task<LocalTask>> refresh, Action<LocalTask> observe,
        Func<CancellationToken, Task> validateIntent, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await validateIntent(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        // An explicit choice is never replaced by a newly discovered first result.
        if (key is not null || task.Artifacts.Length != 0) { _ = MeshTaskSelection.Capture(task, key); return task; }
        MeshPlacementPolicy.ValidateTask(task);
        var expected = RequireConnection(task, connection());
        var identity = TaskIdentity.From(task);
        var updated = await refresh(task.Id, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        await validateIntent(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        RequireEpoch(expected, connection());
        if (TaskIdentity.From(updated) != identity || task.ServiceOperation != updated.ServiceOperation)
            throw new InvalidOperationException(UiText.Get("The selected task result changed. Select it again before placement."));
        _ = MeshTaskSelection.Capture(updated);
        observe(updated);
        return updated;
    }
    private static ConnectionState RequireConnection(LocalTask task, ConnectionState current)
    {
        if (!current.Connected || task.Region != current.Region || task.ConnectionId != current.ConnectionId)
            throw new InvalidOperationException(UiText.Get("Connect to the task's original account before recovering its download."));
        return current;
    }
    private static void RequireEpoch(ConnectionState expected, ConnectionState current)
    {
        if (!current.Connected || current.Region != expected.Region || current.ConnectionId != expected.ConnectionId || current.CredentialEpoch != expected.CredentialEpoch)
            throw new OperationCanceledException(UiText.Get("The connection changed. Retry with the task's original account."));
    }
    private static TaskArtifact ValidatePath(TaskArtifact artifact)
    {
        if (!string.Equals(Path.GetExtension(artifact.LocalPath), "." + (artifact.Format=="obj_zip"?"zip":artifact.Format), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.Get("The cached result does not match its recorded format. Download it again."));
        return artifact;
    }
}

public sealed record MeshPlacementResult(string State, string Message);

/// <summary>Receipt-first placement with one owned prepared asset and no automatic retry.</summary>
public static class MeshTaskPlacement
{
    public static async Task<MeshPlacementResult> ExecuteAsync<TAsset>(
        Func<CancellationToken, Task<MeshPlacementResult?>> check,
        Func<CancellationToken, Task<TaskArtifact>> acquire,
        Func<TaskArtifact, CancellationToken, Task<TAsset>> prepare,
        Func<TAsset, CancellationToken, Task<MeshPlacementResult>> commit,
        Func<TAsset, Task> release, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var prior = await check(ct).ConfigureAwait(false);
        if (prior is not null) return prior;
        var artifact = await acquire(ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        prior = await check(ct).ConfigureAwait(false);
        if (prior is not null) return prior;
        var asset = await prepare(artifact, ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            prior = await check(ct).ConfigureAwait(false);
            if (prior is not null) return prior;
            return await commit(asset, ct).ConfigureAwait(false);
        }
        finally { await release(asset).ConfigureAwait(false); }
    }
}
