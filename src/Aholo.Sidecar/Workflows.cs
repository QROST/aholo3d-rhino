using Aholo.Core;
using Aholo.Provider;
namespace Aholo.Sidecar;

/// <summary>Persist submit intent before network writes; never automatically replay an uncertain paid request.</summary>
public sealed class Workflows(AholoApiClient api, TaskStore store, AssetDownloader downloader)
{
    public async Task<LocalTask> GenerateAsync(ApiKeyCredential key, GenerateWorldRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Prompt) && string.IsNullOrWhiteSpace(request.ImagePath)) throw new ArgumentException("Enter a world description or choose a reference image.");
        using var gate = store.LockMutation();
        var task = new LocalTask { Name = Name(request.Name, "Aholo World"), ConnectionId = key.ConnectionId };
        store.Save(task);
        try
        {
            string? url = null;
            if (!string.IsNullOrWhiteSpace(request.ImagePath))
            {
                string snapshot = store.SnapshotInput(task.Id, request.ImagePath, false);
                task = Save(task with { Status = "Uploading" });
                url = await api.UploadAsync(key, snapshot, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            task = Save(task with { Status = "Submitting" });
            string id = await api.GenerateWorldAsync(key, task.Name, request.Prompt, url, ct).ConfigureAwait(false);
            return Save(task with { WorldId = id, Status = "PENDING", Error = null });
        }
        catch (Exception error) when (Expected(error)) { return Save(Failure(task, error)); }
    }
    public async Task<LocalTask> RenderAsync(ApiKeyCredential key, RenderRequest request, CancellationToken ct)
    {
        // Validate optics, resolution, and scene serialization before any paid or upload action.
        _ = UsdaSceneBuilder.Build("validation", request.Camera, request.Width, request.Height, request.EnvironmentIntensity);
        using var gate = store.LockMutation();
        var task = new LocalTask { Kind = "render", Name = Name(request.Name, "Aholo Render"), ConnectionId = key.ConnectionId };
        store.Save(task);
        try
        {
            string snapshot = store.SnapshotInput(task.Id, request.GlbPath, true);
            task = Save(task with { Render = request with { GlbPath = snapshot }, Status = "Uploading" });
            string url = await api.UploadAsync(key, snapshot, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            task = Save(task with { Status = "MeshSubmitting" });
            string meshTaskId = await api.CreateMeshAsync(key, url, ct).ConfigureAwait(false);
            return Save(task with { MeshTaskId = meshTaskId, Status = "MeshProcessing", Error = null });
        }
        catch (Exception error) when (Expected(error)) { return Save(Failure(task, error)); }
    }
    public async Task<LocalTask> RefreshAsync(ApiKeyCredential key, Guid id, CancellationToken ct)
    {
        using var gate = store.LockMutation();
        return await RefreshCoreAsync(key, store.Get(id), ct).ConfigureAwait(false);
    }
    private async Task<LocalTask> RefreshCoreAsync(ApiKeyCredential key, LocalTask task, CancellationToken ct)
    {
        CheckConnection(key, task);
        if (task.Kind == "world" && task.WorldId is not null)
            return Save(ApplyWorld(task, await api.GetWorldAsync(key, task.WorldId, ct).ConfigureAwait(false)));
        if (task.Kind != "render") return task;
        if (task.OperationId is not null)
            return Save(ApplyRender(task, await api.GetRenderAsync(key, task.OperationId, ct).ConfigureAwait(false)));
        if (task.Status != "MeshProcessing" || task.MeshTaskId is null) return task;
        var mesh = await api.GetMeshAsync(key, task.MeshTaskId, ct).ConfigureAwait(false);
        if (mesh.Status == 4) return Save(task with { Status = "FAILED", Error = "Cloud model processing failed. The original task is retained." });
        if (mesh.Status != 3) return Save(task with { Error = null });
        if (string.IsNullOrWhiteSpace(mesh.MeshId)) return Save(task with { Status = "FAILED", Error = "Cloud model processing finished without returning an asset identifier." });
        if (task.Render is null) throw new IOException("The render camera record is missing. Submission stopped.");
        string usda = UsdaSceneBuilder.Build(mesh.MeshId, task.Render.Camera, task.Render.Width, task.Render.Height, task.Render.EnvironmentIntensity);
        TaskStore.WriteAtomic(Path.Combine(store.Root, "inputs", task.Id.ToString("N") + ".usda"), System.Text.Encoding.UTF8.GetBytes(usda));
        ct.ThrowIfCancellationRequested();
        task = Save(task with { MeshId = mesh.MeshId, Status = "RenderSubmitting", Error = null });
        try
        {
            var operation = await api.SubmitRenderAsync(key, task.Id.ToString("N"), usda, ct).ConfigureAwait(false);
            return Save(ApplyRender(task, operation));
        }
        catch (Exception error) when (Expected(error)) { return Save(Failure(task, error)); }
    }
    public async Task<LocalTask> ImportWorldAsync(ApiKeyCredential key, string worldId, CancellationToken ct)
    {
        using var gate = store.LockMutation();
        var world = await api.GetWorldAsync(key, worldId, ct).ConfigureAwait(false);
        var existing = store.List().FirstOrDefault(x => x.WorldId == world.WorldId && x.ConnectionId == key.ConnectionId);
        return Save(ApplyWorld(existing ?? new LocalTask { ConnectionId = key.ConnectionId }, world));
    }
    public async Task<LocalTask> DownloadAsync(ApiKeyCredential key, DownloadRequest request, CancellationToken ct)
    {
        if (request.Kind is not ("ply" or "spz" or "image")) throw new ArgumentException("This download type is not supported.");
        using var gate = store.LockMutation();
        // Fetch fresh signed URLs rather than persisting expiring capabilities in task JSON.
        var task = await RefreshCoreAsync(key, store.Get(request.Id), ct).ConfigureAwait(false);
        if (task.Status != "SUCCEEDED") throw new InvalidOperationException("The task has not succeeded yet; no result is available to download.");
        string? url = request.Kind switch { "ply" => task.PlyUrl, "spz" => task.SpzUrl, _ => task.ImageUrl };
        if (string.IsNullOrWhiteSpace(url)) throw new IOException("The service has not provided this result format yet. Refresh the task later.");
        string path = await downloader.DownloadAsync(task.Id, url, request.Kind, ct).ConfigureAwait(false);
        return Save(request.Kind switch { "ply" => task with { PlyPath = path }, "spz" => task with { SpzPath = path }, _ => task with { ImagePath = path } });
    }
    private LocalTask Save(LocalTask task) { task = task with { UpdatedAt = DateTimeOffset.UtcNow, Name = TaskStore.DisplayText(task.Name) }; store.Save(task); return task; }
    private static LocalTask ApplyWorld(LocalTask t, WorldResult world) => t with
    {
        WorldId = world.WorldId, Name = string.IsNullOrWhiteSpace(world.Name) ? t.Name : world.Name,
        Status = world.Status, Progress = world.Progress, UpAxis = world.UpAxis,
        PlyUrl = world.PlyUrl, SpzUrl = world.SpzUrl, ImageUrl = world.ImageUrl,
        Error = world.Status is "FAILED" or "CANCELED" or "TIMEOUT" or "REJECTED" ? "World generation did not succeed. Check the Aholo console for details; generation will not restart automatically." : null
    };
    private static LocalTask ApplyRender(LocalTask t, RenderOperation op) => t with
    {
        OperationId = op.OperationId, Status = op.Done ? op.Error is null && !string.IsNullOrWhiteSpace(op.ResultUrl) ? "SUCCEEDED" : "FAILED" : "Running",
        ImageUrl = op.ResultUrl, Error = op.Done && (op.Error is not null || string.IsNullOrWhiteSpace(op.ResultUrl)) ? op.Error ?? "Rendering finished without returning an image." : null
    };
    private static bool Expected(Exception error) => error is ProviderException or IOException or OperationCanceledException or ArgumentException or HttpRequestException;
    private static LocalTask Failure(LocalTask task, Exception error)
    {
        bool submitting = task.Status is "Submitting" or "MeshSubmitting" or "RenderSubmitting";
        bool unknown = submitting && (error is not ProviderException p || p.SubmissionMayHaveSucceeded);
        return task with { Status = unknown ? "SubmissionUnknown" : "FAILED", Error = unknown ? "The request was sent but its result is unknown. Check Aholo history; automatic resubmission is prohibited." : error is ProviderException or ArgumentException ? error.Message : "Preparation or network operation failed. The original task is retained." };
    }
    private static void CheckConnection(ApiKeyCredential key, LocalTask task)
    { if (task.ConnectionId != key.ConnectionId) throw new InvalidOperationException("This task belongs to another API Key. Connect the original key before refreshing or downloading."); }
    private static string Name(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
