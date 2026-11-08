using System.Text.Json;
using Aholo.Core;
namespace Aholo.Provider;

public sealed record CloudTaskPage<T>(int PageNumber,int PageSize,int Count,int TotalCount,bool HasMore,IReadOnlyList<T> Items);
public sealed record SpatialTaskResult(string TaskId,string Status,string? ResultUrl,string? ModelVersion,string? ErrorCode,long? Created,long? Updated);

public sealed partial class AholoApiClient
{
    public async Task<CloudTaskPage<WorldResult>> ListWorldPageAsync(ApiKeyCredential key,int page=0,int pageSize=20,IReadOnlyList<string>? statuses=null,CancellationToken ct=default)
    {
        ValidatePage(page,pageSize,statuses,new[]{"PENDING","PREPROCESSING","RUNNING","SUCCEEDED","FAILED","CANCELED","TIMEOUT","REJECTED"});
        using var request=Gateway(key,HttpMethod.Post,"world/v1/list",new{pageNum=page,pageSize,statusList=statuses??Array.Empty<string>()});
        return ParsePage(await SendAsync(_apiHttp,request,ct).ConfigureAwait(false),ParseWorld);
    }
    public async Task<SpatialTaskResult> GetSpatialTaskAsync(ApiKeyCredential key,string taskId,CancellationToken ct=default)
    {
        taskId=Identifier(taskId);
        using var request=Gateway(key,HttpMethod.Get,"spatial-understanding/v1/tasks/"+Uri.EscapeDataString(taskId));
        var task=ParseSpatial(await SendAsync(_apiHttp,request,ct).ConfigureAwait(false));
        if(task.TaskId!=taskId)throw InvalidResponse();return task;
    }
    public async Task<CloudTaskPage<SpatialTaskResult>> ListSpatialTasksAsync(ApiKeyCredential key,int page=1,int pageSize=20,IReadOnlyList<string>? statuses=null,CancellationToken ct=default)
    {
        ValidatePage(page,pageSize,statuses,new[]{"PENDING","RUNNING","SUCCEEDED","FAILED","TIMEOUT"});
        using var request=Gateway(key,HttpMethod.Post,"spatial-understanding/v1/tasks/list",new{pageNum=page,pageSize,statusList=statuses??Array.Empty<string>()});
        return ParsePage(await SendAsync(_apiHttp,request,ct).ConfigureAwait(false),ParseSpatial);
    }
    private static void ValidatePage(int page,int pageSize,IReadOnlyList<string>? statuses,IReadOnlyList<string> allowed)
    {
        if(page<0||pageSize is <0 or >100||statuses?.Any(s=>!allowed.Contains(s))==true)throw new ArgumentException("Invalid task history filter.");
    }
    private static CloudTaskPage<T> ParsePage<T>(JsonElement root,Func<JsonElement,T> parse)
    {
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("result",out var items)||items.ValueKind!=JsonValueKind.Array||!root.TryGetProperty("hasMore",out var more)||more.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw InvalidResponse();
        return new(RequiredInt(root,"pageNum"),RequiredInt(root,"pageSize"),RequiredInt(root,"count"),RequiredInt(root,"totalCount"),more.GetBoolean(),items.EnumerateArray().Select(parse).ToArray());
    }
    private static SpatialTaskResult ParseSpatial(JsonElement root)
    {
        string id=RequiredIdentifier(root,"taskId"),status=RequiredString(root,"status");
        string? url=root.TryGetProperty("result",out var result)?OptionalString(result,"resultSslUrl"):null;
        return new(id,status,url,OptionalString(root,"modelVersion"),OptionalString(root,"errorCode"),OptionalLong(root,"createTime"),OptionalLong(root,"updateTime"));
    }
}
