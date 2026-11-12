using System.Globalization;
using System.Text.Json;
using Aholo3D.Core;
namespace Aholo3D.Provider;

public sealed record ServiceTaskIdentity(TaskService Service,string Operation,string Id);
public sealed record LuxTaskResult(string TaskId,int Status,IReadOnlyList<string?> Outputs);
public sealed record LuxListItem(string TaskId,int Status,long? Created,long? LastModified);
public sealed record LuxTaskPage(IReadOnlyList<LuxListItem> Items,int Total,int Page,int PageSize);

public sealed partial class Aholo3DApiClient
{
    public async Task<ServiceTaskIdentity> CreateServiceTaskAsync(ApiKeyCredential key,string operation,JsonElement parameters,CancellationToken ct=default)
    {
        var contract=ServiceOperations.Get(operation);
        ServiceOperations.Validate(operation,parameters);
        using var request=Gateway(key,HttpMethod.Post,contract.Path,parameters);
        string id=await CreateAsync(request,root=>contract.Service switch
        {
            TaskService.Lux3D=>NumericId(LuxData(root)),
            TaskService.World=>RequiredIdentifier(root,"worldId"),
            TaskService.SpatialUnderstanding=>RequiredIdentifier(root,"taskId"),
            _=>throw new ArgumentException("Unknown service operation.")
        },ct).ConfigureAwait(false);
        return new(contract.Service,operation,id);
    }
    public async Task<LuxTaskResult> GetLuxTaskAsync(ApiKeyCredential key,string taskId,CancellationToken ct=default)
    {
        taskId=NumericId(taskId);
        using var request=Gateway(key,HttpMethod.Get,"lux3d/v1/generate/task/get?taskid="+taskId);
        var data=LuxData(await SendAsync(_apiHttp,request,ct).ConfigureAwait(false));
        if(data.ValueKind!=JsonValueKind.Object||!data.TryGetProperty("taskId",out var id)||NumericId(id)!=taskId)throw InvalidResponse();
        int status=RequiredInt(data,"status");
        var outputs=new List<string?>();
        if(data.TryGetProperty("outputs",out var items)&&items.ValueKind!=JsonValueKind.Null)
        {
            if(items.ValueKind!=JsonValueKind.Array)throw InvalidResponse();
            foreach(var item in items.EnumerateArray())
            {
                if(item.ValueKind!=JsonValueKind.Object)throw InvalidResponse();
                if(item.TryGetProperty("content",out var content)&&content.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))throw InvalidResponse();
                outputs.Add(OptionalString(item,"content"));
            }
        }
        // Outputs are ordered, operation-specific strings (URL, JSON or NOT_REQUESTED).
        // Neither partial outputs nor an HTTP 200 response imply remote task success.
        return new(taskId,status,outputs);
    }
    public async Task<LuxTaskPage> ListLuxTasksAsync(ApiKeyCredential key,int page=1,int pageSize=20,int? status=null,long? startTime=null,long? endTime=null,CancellationToken ct=default)
    {
        if(page<1||pageSize is <1 or >100||status is not (null or 0 or 1 or 3 or 4)||startTime<0||endTime<0||startTime.HasValue&&endTime.HasValue&&startTime>=endTime)throw new ArgumentException("Invalid task history filter.");
        string path="lux3d/v1/generate/task/list?page="+page.ToString(CultureInfo.InvariantCulture)+"&pagesize="+pageSize.ToString(CultureInfo.InvariantCulture);
        if(status.HasValue)path+="&status="+status.Value.ToString(CultureInfo.InvariantCulture);
        if(startTime.HasValue)path+="&starttime="+startTime.Value.ToString(CultureInfo.InvariantCulture);
        if(endTime.HasValue)path+="&endtime="+endTime.Value.ToString(CultureInfo.InvariantCulture);
        using var request=Gateway(key,HttpMethod.Get,path);var data=LuxData(await SendAsync(_apiHttp,request,ct).ConfigureAwait(false));
        if(data.ValueKind!=JsonValueKind.Object||!data.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)throw InvalidResponse();
        var rows=items.EnumerateArray().Select(item=>new LuxListItem(RequiredNumericId(item,"taskId"),RequiredInt(item,"status"),OptionalLong(item,"created"),OptionalLong(item,"lastModified"))).ToArray();
        return new(rows,RequiredInt(data,"total"),RequiredInt(data,"page"),RequiredInt(data,"pageSize"));
    }
    private static JsonElement LuxData(JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("c",out var code)||Scalar(code) is not string value)throw InvalidResponse();
        if(value!="0")throw new ProviderException("business_rejected","The service rejected the task. Check account permissions, balance and parameters.");
        if(!root.TryGetProperty("d",out var data)||data.ValueKind==JsonValueKind.Null)throw InvalidResponse();
        return data;
    }
    private static string RequiredNumericId(JsonElement root,string name)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(name,out var value)?NumericId(value):throw InvalidResponse();
    private static int RequiredInt(JsonElement root,string name)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out int number)?number:throw InvalidResponse();
    private static long? OptionalLong(JsonElement root,string name)=>root.ValueKind==JsonValueKind.Object&&root.TryGetProperty(name,out var value)&&value.ValueKind!=JsonValueKind.Null?value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out long number)?number:throw InvalidResponse():null;
}
