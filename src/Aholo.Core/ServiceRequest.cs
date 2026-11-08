using System.Text.Json;
using System.Text.Json.Nodes;
namespace Aholo.Core;

public sealed record ServiceFileInput(string Field, string[] Paths);
public sealed record CreateServiceRequest(string Operation, string Name, JsonElement Parameters, ServiceFileInput[]? Files=null);
public sealed record TaskArtifact(string Key,string Label,string Format,string? Url=null,string? LocalPath=null,string? Sha256=null,string? Error=null)
{
    public override string ToString()=>UiText.TranslateKnownMessage(Label)+" · "+Format.ToUpperInvariant()+(LocalPath is null?"":" · "+UiText.Get("Downloaded"))+(Error is null?"":" · "+UiText.Get("Unavailable"));
}
/// <summary>Apply local-file inputs to known schema fields without accepting arbitrary JSON paths.</summary>
public static class ServiceRequest
{
    public static JsonElement Bind(CreateServiceRequest request,Func<ServiceFileInput,int,string> resolve)
    {
        var body=JsonNode.Parse(request.Parameters.GetRawText()) as JsonObject??throw new ArgumentException("Expected task parameters.");
        var used=new HashSet<string>(StringComparer.Ordinal);
        foreach(var input in request.Files??Array.Empty<ServiceFileInput>())
        {
            if(!used.Add(input.Field)||body.ContainsKey(input.Field))throw new ArgumentException("Choose either files or a URL for each input.");
            if(input.Paths.Length==0)throw new ArgumentException("Choose an input file.");
            if(input.Field=="imgs")body[input.Field]=new JsonArray(input.Paths.Select((_,i)=>JsonValue.Create(resolve(input,i))).ToArray<JsonNode?>());
            else if(input.Field=="resources")body[input.Field]=new JsonArray(input.Paths.Select((path,i)=>(JsonNode)new JsonObject{{"url",resolve(input,i)},{"type",ResourceType(path)}}).ToArray());
            else
            {
                if(input.Field is not ("img" or "modelUrl" or "rigModelUrl" or "meshUrl" or "glbUrl" or "inputFileUrl" or "cover")||input.Paths.Length!=1)throw new ArgumentException("Invalid file input field.");
                body[input.Field]=resolve(input,0);
            }
        }
        var result=JsonSerializer.SerializeToElement(body);ServiceOperations.Validate(request.Operation,result);return result;
    }
    public static string ResourceType(string path)=>Path.GetExtension(path).ToLowerInvariant() switch{ ".mp4" or ".mov"=>"video", ".insv"=>"insv", _=>"image"};
    public static string Placeholder(ServiceFileInput input,int i)=>"https://validation.example/input"+Path.GetExtension(input.Paths[i]).ToLowerInvariant();
}
