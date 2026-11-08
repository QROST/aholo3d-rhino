using System.Text.Json;
namespace Aholo.Core;

public sealed record ServiceOperation(string Id, TaskService Service, string Title, string Path, string Schema, string SchemaService);

/// <summary>Verified public create operations. Registry keys are not URLs supplied by callers.</summary>
public static class ServiceOperations
{
    public static IReadOnlyList<ServiceOperation> All { get; } = Array.AsReadOnly(new[]
    {
        new ServiceOperation("world.generate",TaskService.World,"Generate world","world/v1/generations","GenerateWorldRequest","world"),
        new ServiceOperation("world.reconstruct",TaskService.World,"Reconstruct world","world/v1/reconstructions","CreateWorldRequest","world"),
        new ServiceOperation("lux.text-to-3d",TaskService.Lux3D,"Text to 3D","lux3d/v1/generate/text-to-3d/task/create","TextTo3dRequest","lux3d"),
        new ServiceOperation("lux.img-to-3d",TaskService.Lux3D,"Images to 3D","lux3d/v1/generate/img-to-3d/task/create","ImgTo3dRequest","lux3d"),
        new ServiceOperation("lux.multimodal-to-image",TaskService.Lux3D,"Create image","lux3d/v1/generate/multimodal-to-image/task/create","MultimodalToImageRequest","lux3d"),
        new ServiceOperation("lux.image-to-four-view",TaskService.Lux3D,"Create four views","lux3d/v1/generate/image-to-four-view/task/create","ImageToFourViewRequest","lux3d"),
        new ServiceOperation("lux.material-transfer",TaskService.Lux3D,"Transfer material","lux3d/v1/generate/material-transfer/task/create","MaterialTransferRequest","lux3d"),
        new ServiceOperation("lux.part-split",TaskService.Lux3D,"Split model parts","lux3d/v1/part-split/task/create","PartSplitRequest","lux3d"),
        new ServiceOperation("lux.multi-format-export",TaskService.Lux3D,"Convert model format","lux3d/v1/multi-format-export/task/create","MultiFormatExportRequest","lux3d"),
        new ServiceOperation("lux.articulation-animation",TaskService.Lux3D,"Articulate model","lux3d/v1/articulation-animation/task/create","ArticulationAnimationRequest","lux3d"),
        new ServiceOperation("lux.rig",TaskService.Lux3D,"Rig character","lux3d/v1/animations/rig/task/create","HumanoidAutoRigRequest","lux3d"),
        new ServiceOperation("lux.retarget",TaskService.Lux3D,"Animate character","lux3d/v1/animations/retarget/task/create","HumanoidAnimationRetargetRequest","lux3d"),
        new ServiceOperation("spatial.understand",TaskService.SpatialUnderstanding,"Understand space","spatial-understanding/v1/tasks","CreateSpatialUnderstandingTaskRequest","spatialunderstanding")
    });
    private static readonly IReadOnlyDictionary<string,JsonElement> Schemas = new[] {"world","lux3d","spatialunderstanding"}.ToDictionary(x=>x,Load);
    public static ServiceOperation Get(string id) => All.SingleOrDefault(x=>x.Id==id) ?? throw new ArgumentException("Unknown service operation.",nameof(id));
    public static JsonElement RequestSchema(string id) { var op=Get(id);return Schemas[op.SchemaService].GetProperty(op.Schema); }
    private static JsonElement Load(string service)
    {
        using var stream=typeof(ServiceOperations).Assembly.GetManifestResourceStream("Aholo.Core.ServiceSchemas."+service+".json") ?? throw new InvalidOperationException("Missing service contract.");
        using var doc=JsonDocument.Parse(stream);return doc.RootElement.GetProperty("components").GetProperty("schemas").Clone();
    }
    public static void Validate(string operation,JsonElement body)
    {
        var op=Get(operation);ValidateValue(body,RequestSchema(operation),Schemas[op.SchemaService],"request");
        bool Has(string key)=>body.TryGetProperty(key,out var v)&&v.ValueKind!=JsonValueKind.Null;
        string? Text(string key)=>Has(key)&&body.GetProperty(key).ValueKind==JsonValueKind.String?body.GetProperty(key).GetString():null;
        foreach(string key in new[]{"img","modelUrl","rigModelUrl","meshUrl","glbUrl","inputFileUrl","cover"})
            if(Has(key))RequireHttps(Text(key)!);
        if(Has("imgs"))foreach(var image in body.GetProperty("imgs").EnumerateArray())RequireHttps(image.GetString()!);
        if(operation is "lux.multimodal-to-image" or "lux.image-to-four-view" or "world.generate")
            if(!Has("prompt")&&!Has(operation=="world.generate"?"resources":"img"))throw new ArgumentException("Enter a description or choose reference images.");
        if(operation=="lux.img-to-3d")
        {
            if(Has("img")==Has("imgs"))throw new ArgumentException("Choose either one image or an image set.");
            if(Has("imgs")&&!Has("version"))throw new ArgumentException("Choose a generation model for multiple images.");
        }
        if(Has("resources"))
        {
            var resources=body.GetProperty("resources").EnumerateArray().ToArray();int images=0;
            foreach(var resource in resources)
            {
                string url=resource.GetProperty("url").GetString()!;RequireHttps(url);
                string type=resource.TryGetProperty("type",out var t)?t.GetString()!:"image";
                string ext=Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
                bool valid=type switch {"image"=>ext is ".png" or ".jpg" or ".jpeg" or ".webp","video"=>ext is ".mp4" or ".mov","insv"=>ext==".insv",_=>false};
                if(!valid)throw new ArgumentException("A resource format does not match its declared type.");
                if(type=="image")images++;
            }
            if(operation=="world.reconstruct"&&images is >0 and <20)throw new ArgumentException("Reconstruction requires at least 20 image resources.");
            if(operation=="world.generate"&&resources.Length!=1)throw new ArgumentException("World generation accepts one reference image.");
        }
        if(operation=="lux.multi-format-export"&&Path.GetExtension(new Uri(Text("modelUrl")!).AbsolutePath).Equals(".glb",StringComparison.OrdinalIgnoreCase))
            if(!Has("outputFormat")||body.GetProperty("outputFormat").GetArrayLength()==0)throw new ArgumentException("Choose at least one export format for a GLB model.");
        if(operation=="spatial.understand")
        {
            if(!new Uri(Text("inputFileUrl")!).AbsolutePath.EndsWith(".ply",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Spatial understanding requires an uploaded indoor PLY.");
            if(Has("transformationMatrix"))
            {
                var rows=body.GetProperty("transformationMatrix");
                if(rows.GetArrayLength()!=4||rows.EnumerateArray().Any(row=>row.GetArrayLength()!=4))throw new ArgumentException("Spatial transforms must be 4 by 4, in metres with column vectors.");
            }
        }
    }
    public static void RequireHttps(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||!uri.IsDefaultPort||uri.UserInfo.Length!=0||uri.Fragment.Length!=0||uri.IsLoopback||Uri.CheckHostName(uri.Host)!=UriHostNameType.Dns)
            throw new ArgumentException("Use a public HTTPS asset address.");
    }
    private static bool SameScalar(JsonElement left, JsonElement right)
    {
        if(left.ValueKind!=right.ValueKind)return false;
        if(left.ValueKind==JsonValueKind.String)return string.Equals(left.GetString(),right.GetString(),StringComparison.Ordinal);
        if(left.ValueKind==JsonValueKind.Number&&left.TryGetDecimal(out var a)&&right.TryGetDecimal(out var b))return a==b;
        return left.GetRawText()==right.GetRawText();
    }
    private static void ValidateValue(JsonElement value,JsonElement schema,JsonElement definitions,string field)
    {
        if(schema.TryGetProperty("$ref",out var reference))schema=definitions.GetProperty(reference.GetString()!.Split('/')[^1]);
        string? type=schema.TryGetProperty("type",out var typeValue)?typeValue.GetString():null;
        if(value.ValueKind==JsonValueKind.Null)throw new ArgumentException("Missing value: "+field);
        if(schema.TryGetProperty("enum",out var options)&&!options.EnumerateArray().Any(x=>SameScalar(x,value)))throw new ArgumentException("Unsupported choice: "+field);
        switch(type)
        {
            case "object":
                if(value.ValueKind!=JsonValueKind.Object)throw new ArgumentException("Expected fields: "+field);
                var properties=schema.GetProperty("properties");
                var seen=new HashSet<string>(StringComparer.Ordinal);
                foreach(var property in value.EnumerateObject())
                {
                    if(!seen.Add(property.Name)||!properties.TryGetProperty(property.Name,out var child))throw new ArgumentException("Unknown or duplicate field: "+property.Name);
                    ValidateValue(property.Value,child,definitions,field+"."+property.Name);
                }
                if(schema.TryGetProperty("required",out var required))foreach(var key in required.EnumerateArray())
                    if(!seen.Contains(key.GetString()!))throw new ArgumentException("Required field: "+key.GetString());
                break;
            case "array":
                if(value.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Expected a list: "+field);
                int count=value.GetArrayLength();
                if(schema.TryGetProperty("minItems",out var minItems)&&count<minItems.GetInt32()||schema.TryGetProperty("maxItems",out var maxItems)&&count>maxItems.GetInt32())throw new ArgumentException("Invalid number of items: "+field);
                var items=value.EnumerateArray().ToArray();
                if(schema.TryGetProperty("uniqueItems",out var unique)&&unique.GetBoolean()&&items.Where((item,index)=>items.Take(index).Any(previous=>SameScalar(previous,item))).Any())throw new ArgumentException("Duplicate choices: "+field);
                foreach(var item in items)ValidateValue(item,schema.GetProperty("items"),definitions,field+"[]");
                break;
            case "string":
                if(value.ValueKind!=JsonValueKind.String)throw new ArgumentException("Expected text: "+field);
                string text=value.GetString()!;
                if(string.IsNullOrWhiteSpace(text))throw new ArgumentException("Enter a value: "+field);
                if(schema.TryGetProperty("minLength",out var minLength)&&text.Length<minLength.GetInt32())throw new ArgumentException("Text is too short: "+field);
                break;
            case "integer": case "number":
                if(value.ValueKind!=JsonValueKind.Number||!value.TryGetDouble(out double number)||!double.IsFinite(number)||type=="integer"&&!value.TryGetInt64(out _))throw new ArgumentException("Expected a finite number: "+field);
                bool exclusive=schema.TryGetProperty("exclusiveMinimum",out var ex)&&ex.ValueKind==JsonValueKind.True;
                if(schema.TryGetProperty("minimum",out var min)&&(number<min.GetDouble()||exclusive&&number==min.GetDouble())||schema.TryGetProperty("maximum",out var max)&&number>max.GetDouble())throw new ArgumentException("Value is outside the service range: "+field);
                break;
            case "boolean":
                if(value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new ArgumentException("Expected a yes/no choice: "+field);break;
        }
    }
}
