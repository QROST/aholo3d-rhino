namespace Aholo3D.Core;

public enum TaskService { World, Lux3D, RenderCloud, SpatialUnderstanding, Local }
public sealed record TaskIdentity(Guid LocalId, TaskService Service, string Region, string ConnectionId, string? RemoteId)
{
    public static TaskIdentity From(LocalTask task) => task.Kind switch
    {
        "world" => new(task.Id, TaskService.World, task.Region, task.ConnectionId, task.WorldId),
        "object" => new(task.Id, TaskService.Lux3D, task.Region, task.ConnectionId, task.LuxTaskId),
        "render" => new(task.Id, TaskService.RenderCloud, task.Region, task.ConnectionId, task.OperationId),
        "spatial" => new(task.Id, TaskService.SpatialUnderstanding, task.Region, task.ConnectionId, task.SpatialTaskId),
        _ => new(task.Id, TaskService.Local, task.Region, task.ConnectionId, null)
    };
}
public sealed record TaskCapabilities(bool CanQuery, bool CanDownload, bool CanContinueRender, bool IsRunning)
{
    public static TaskCapabilities For(LocalTask task)
    {
        if (task.SchemaVersion != LocalTask.CurrentSchemaVersion || task.Status is "RecordError" or "UnsupportedSchema") return new(false, false, false, false);
        bool known = TaskIdentity.From(task).RemoteId is not null || task.Kind == "render" && task.MeshTaskId is not null;
        bool uncertain = task.Status is "Submitting" or "RenderSubmitting" or "MeshSubmitting" or "SubmissionUnknown";
        return new(known, known && task.Status == "SUCCEEDED", !uncertain && task.Kind == "render" && task.OperationId is null && task.Status == "MeshReady" && task.Render is not null && task.MeshId is not null,
            task.Status is "MeshProcessing" or "Running" or "PENDING" or "PREPROCESSING" or "RUNNING");
    }
}
