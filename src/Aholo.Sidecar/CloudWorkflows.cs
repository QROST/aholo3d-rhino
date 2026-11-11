using Aholo.Core;
using Aholo.Provider;
namespace Aholo.Sidecar;

public sealed partial class Workflows
{
    public async Task<CloudHistoryPage> CloudHistoryAsync(ApiKeyCredential key, CloudHistoryRequest request, CancellationToken ct)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100) throw new ArgumentException("Invalid task history filter.");
        string[]? statuses = request.Status is null ? null : new[] { request.Status };
        switch (request.Service)
        {
            case TaskService.World:
                var worlds = await api.ListWorldPageAsync(key, request.Page - 1, request.PageSize, statuses, ct).ConfigureAwait(false);
                return new(request.Service, key.ConnectionId, request.Page, request.PageSize, worlds.TotalCount, worlds.HasMore,
                    worlds.Items.Select(w => new CloudTaskRow(request.Service, w.WorldId, TaskStore.DisplayText(w.Name, 128), w.Status, w.Created)).ToArray());
            case TaskService.SpatialUnderstanding:
                var spatial = await api.ListSpatialTasksAsync(key, request.Page, request.PageSize, statuses, ct).ConfigureAwait(false);
                return new(request.Service, key.ConnectionId, request.Page, request.PageSize, spatial.TotalCount, spatial.HasMore,
                    spatial.Items.Select(w => new CloudTaskRow(request.Service, w.TaskId, w.TaskId, w.Status, w.Created)).ToArray());
            case TaskService.Lux3D:
                int? status = request.Status switch { null => null, "PENDING" => 0, "RUNNING" => 1, "SUCCEEDED" => 3, "FAILED" => 4, _ => throw new ArgumentException("Invalid task history filter.") };
                var lux = await api.ListLuxTasksAsync(key, request.Page, request.PageSize, status, ct: ct).ConfigureAwait(false);
                return new(request.Service, key.ConnectionId, request.Page, request.PageSize, lux.Total, (long)request.Page * request.PageSize < lux.Total,
                    lux.Items.Select(w => new CloudTaskRow(request.Service, w.TaskId, w.TaskId, LuxStatus(w.Status), w.Created)).ToArray());
            default: throw new ArgumentException("Cloud history is available for World, Lux3D and Spatial. Restore a render using its operation ID.");
        }
    }

    public async Task<LocalTask> RestoreRemoteAsync(ApiKeyCredential key, RestoreRemoteRequest request, CancellationToken ct)
    {
        if (request.ExpectedConnectionId is not null && request.ExpectedConnectionId != key.ConnectionId)
            throw new InvalidOperationException("The connection changed. Reload cloud history before restoring this result.");
        if (string.IsNullOrWhiteSpace(request.RemoteId)) throw new ArgumentException("Enter an existing remote task ID.");
        if (request.Service == TaskService.Lux3D && (request.Operation is null || ServiceOperations.Get(request.Operation).Service != TaskService.Lux3D))
            throw new ArgumentException("Choose the original Lux3D operation. Its history API does not identify the operation type.");
        // Network queries run before local index/task locks and never create remote work.
        WorldResult? world = null; LuxTaskResult? lux = null; SpatialTaskResult? spatial = null; RenderOperation? render = null;
        switch (request.Service)
        {
            case TaskService.World: world = await api.GetWorldAsync(key, request.RemoteId, ct).ConfigureAwait(false); break;
            case TaskService.Lux3D: lux = await api.GetLuxTaskAsync(key, request.RemoteId, ct).ConfigureAwait(false); break;
            case TaskService.SpatialUnderstanding: spatial = await api.GetSpatialTaskAsync(key, request.RemoteId, ct).ConfigureAwait(false); break;
            case TaskService.RenderCloud: render = await api.GetRenderAsync(key, request.RemoteId, ct).ConfigureAwait(false); break;
            default: throw new ArgumentException("Choose a remote task service.");
        }
        string remote = world?.WorldId ?? lux?.TaskId ?? spatial?.TaskId ?? render!.OperationId;
        ct.ThrowIfCancellationRequested();
        using var index = store.LockMutation();
        var previous = store.FindRemote(request.Service, remote, key.ConnectionId, key.Region);
        var task = previous ?? new LocalTask { Region = key.Region, ConnectionId = key.ConnectionId, Name = remote,
            Kind = request.Service switch { TaskService.World => "world", TaskService.Lux3D => "object", TaskService.SpatialUnderstanding => "spatial", _ => "render" } };
        using var ownership = store.LockTask(task.Id);
        if (previous is not null) task = store.GetForUpdate(task.Id);
        if (lux is not null && task.ServiceOperation is not null && task.ServiceOperation != request.Operation)
            throw new InvalidOperationException("The original task operation differs from the selected operation. Its record was retained.");
        if (world is not null) task = ApplyWorld(task, world);
        if (lux is not null)
            task = task with { LuxTaskId = lux.TaskId, ServiceOperation = request.Operation, Status = LuxStatus(lux.Status),
                LastQueriedAt = DateTimeOffset.UtcNow, Artifacts = MergeArtifacts(task, LuxArtifacts.Decode(request.Operation!, lux.Outputs)),
                Error = lux.Status == 4 ? "The original service task failed. Check the service console for details." : null };
        if (spatial is not null)
            task = task with { SpatialTaskId = spatial.TaskId, ServiceOperation = "spatial.understand", Status = spatial.Status,
                LastQueriedAt = DateTimeOffset.UtcNow, Artifacts = MergeArtifacts(task, spatial.ResultUrl is null ? Array.Empty<TaskArtifact>() : new[] { new TaskArtifact("result", "Spatial scene", "dsl", spatial.ResultUrl) }),
                Error = spatial.Status is "FAILED" or "TIMEOUT" ? "Spatial understanding did not finish successfully." : null };
        if (render is not null) task = ApplyRender(task, render); // An operation ID can be polled, never continued as a fresh submission.
        ct.ThrowIfCancellationRequested(); return Save(task);
    }
    private static string LuxStatus(int status) => status switch { 0 => "PENDING", 1 => "RUNNING", 3 => "SUCCEEDED", 4 => "FAILED", 6 => "CANCELED", _ => "UnknownRemoteStatus" };
}
