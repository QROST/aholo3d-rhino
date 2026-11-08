using System.Text.Json;
namespace Aholo.Core;

/// <summary>Operation-specific, ordered result decoding. URLs are transient and never journaled.</summary>
public static class LuxArtifacts
{
    public static TaskArtifact[] Decode(string operation,IReadOnlyList<string?> outputs)
    {
        var result=new List<TaskArtifact>();
        for(int slot=0;slot<outputs.Count;slot++)
        {
            string? raw=outputs[slot];string key=slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(string.IsNullOrWhiteSpace(raw)||raw=="NOT_REQUESTED")continue;
            try
            {
                if(operation=="lux.image-to-four-view"&&slot==0)
                {
                    using var doc=JsonDocument.Parse(raw);var array=doc.RootElement;
                    if(array.ValueKind!=JsonValueKind.Array||array.GetArrayLength()!=4)throw new JsonException();
                    string[] labels={"Front three-quarter","Opposite three-quarter","Side","Back"};
                    for(int i=0;i<4;i++)result.Add(Link(key+"/"+i,labels[i],"image",Text(array[i])));
                }
                else if(operation=="lux.retarget"&&slot==0)
                {
                    using var doc=JsonDocument.Parse(raw);var root=doc.RootElement;
                    if(root.ValueKind!=JsonValueKind.Object||!root.TryGetProperty("actions",out var actions)||actions.ValueKind!=JsonValueKind.Array)throw new JsonException();
                    int index=0;
                    foreach(var action in actions.EnumerateArray())
                    {
                        string actionKey=key+"/"+index++,label="Animation "+index;
                        if(action.ValueKind!=JsonValueKind.Object||!action.TryGetProperty("status",out var status)||Text(status)!="success") {result.Add(Error(actionKey,label));continue;}
                        bool found=false;
                        foreach(var field in new[]{("animationGlbUrl","glb"),("fbxUrl","fbx")})if(action.TryGetProperty(field.Item1,out var url))
                        {found=true;result.Add(Link(actionKey+"/"+field.Item1,label,field.Item2,Text(url)));}
                        if(!found)result.Add(Error(actionKey,label));
                    }
                }
                else
                {
                    string[] formats=operation switch
                    {
                        "lux.material-transfer"=>new[]{"zip","glb","usdz","obj_zip","fbx_zip"},
                        "lux.multi-format-export"=>new[]{"zip","glb","usdz","obj_zip","fbx_zip","stl","3mf"},
                        "lux.part-split" or "lux.rig"=>new[]{"glb"},
                        "lux.multimodal-to-image"=>new[]{"image"},
                        _=>Array.Empty<string>()
                    };
                    string format=slot<formats.Length?formats[slot]:operation is "lux.text-to-3d" or "lux.img-to-3d" or "lux.articulation-animation"?FormatFromUrl(raw):"unknown";
                    result.Add(Link(key,"Result "+(slot+1),format,raw));
                }
            }
            catch(JsonException){result.Add(Error(key,"Result "+(slot+1)));}
        }
        return result.ToArray();
    }
    private static string? Text(JsonElement value)=>value.ValueKind==JsonValueKind.String?value.GetString():null;
    private static TaskArtifact Error(string key,string label)=>new(key,label,"unknown",Error:"The service returned an unavailable or unsupported result. Check the original task in the service console.");
    private static TaskArtifact Link(string key,string label,string format,string? raw)
    {
        try{if(format=="unknown"||raw is null)return Error(key,label);ServiceOperations.RequireHttps(raw);return new(key,label,format,raw);}
        catch(ArgumentException){return Error(key,label);}
    }
    private static string FormatFromUrl(string raw)
    {
        if(!Uri.TryCreate(raw,UriKind.Absolute,out var uri))return "unknown";
        return Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
        {".glb"=>"glb",".ply"=>"ply",".zip"=>"zip",".usdz"=>"usdz",".fbx"=>"fbx",".stl"=>"stl",".3mf"=>"3mf",_=>"unknown"};
    }
}
