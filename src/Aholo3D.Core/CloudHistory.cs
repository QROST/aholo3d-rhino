namespace Aholo3D.Core;

public sealed record CloudHistoryRequest(TaskService Service, int Page = 1, int PageSize = 20, string? Status = null);
public sealed record CloudTaskRow(TaskService Service, string RemoteId, string Name, string Status, long? Created = null)
{
    public override string ToString() => Name + " · " + UiText.Status(Status) + " · " + RemoteId;
}
public sealed record CloudHistoryPage(TaskService Service, string ConnectionId, int Page, int PageSize, int Total, bool HasMore, CloudTaskRow[] Items);
public sealed record RestoreRemoteRequest(TaskService Service, string RemoteId, string? Operation = null, string? ExpectedConnectionId = null);
