using System.Globalization;
using System.Text.Json;
namespace Aholo3D.Core;

public sealed record CreationValidation(CreateServiceRequest? Request,IReadOnlyDictionary<string,string> Errors)
{
    public bool IsValid=>Request is not null&&Errors.Count==0;
}

/// <summary>Pure form validation. No I/O, uploads, credentials or paid calls.</summary>
public static class CreationDraftValidation
{
    public static bool IsFileField(string field)=>field is "img" or "imgs" or "resources" or "cover" or "modelUrl" or "meshUrl" or "rigModelUrl" or "glbUrl" or "inputFileUrl";
    public static object? Number(string text,bool integer)
    {
        if(string.IsNullOrWhiteSpace(text))return null;
        if(integer)
        {
            if(long.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out var n))return n;
            throw new ArgumentException("Enter a whole number without separators.");
        }
        if(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out var d)&&double.IsFinite(d))return d;
        throw new ArgumentException("Enter a finite number using a decimal point.");
    }
    public static string Hint(string operation,string field)
    {
        var schema=ServiceOperations.ResolveSchema(operation,ServiceOperations.RequestSchema(operation).GetProperty("properties").GetProperty(field));
        var parts=new List<string>();
        if(schema.TryGetProperty("minimum",out var min))parts.Add((schema.TryGetProperty("exclusiveMinimum",out var ex)&&ex.ValueKind==JsonValueKind.True?"> ":">= ")+min.GetRawText());
        if(schema.TryGetProperty("maximum",out var max))parts.Add("<= "+max.GetRawText());
        if(schema.TryGetProperty("minItems",out var minItems))parts.Add(UiText.Format("Minimum items: {0}",minItems.GetInt32()));
        if(schema.TryGetProperty("maxItems",out var maxItems))parts.Add(UiText.Format("Maximum items: {0}",maxItems.GetInt32()));
        return string.Join(" · ",parts);
    }
    public static CreationValidation Evaluate(string operation,CreationDraft draft)
    {
        var errors=new Dictionary<string,string>(StringComparer.Ordinal);
        var body=new Dictionary<string,object>();var files=new List<ServiceFileInput>();
        string[] Values(string key)=>draft.Values.TryGetValue(key,out var values)?values.ToArray():Array.Empty<string>();
        string Text(string key)=>Values(key).FirstOrDefault()?.Trim()??"";
        foreach(var property in ServiceOperations.RequestSchema(operation).GetProperty("properties").EnumerateObject())
        {
            string field=property.Name;if(field=="name")continue;
            try
            {
                var schema=ServiceOperations.ResolveSchema(operation,property.Value);object? value=null;
                if(IsFileField(field))
                {
                    var paths=Values(field+":files");string url=Text(field+":url");
                    if(paths.Length>0&&url.Length>0)throw new ArgumentException("Choose either files or a URL for each input.");
                    if(paths.Length>0)files.Add(new(field,paths));
                    if(url.Length>0)value=field=="imgs"?url.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries):url;
                }
                else if(schema.TryGetProperty("enum",out _)){string choice=Text(field+":choice");if(choice.Length>0)value=choice;}
                else
                {
                    switch(schema.GetProperty("type").GetString())
                    {
                        case "boolean":
                            string choice=Text(field+":choice");if(choice.Length>0)value=choice switch{"true"=>true,"false"=>false,_=>throw new ArgumentException("Choose Yes or No.")};break;
                        case "integer":value=Number(Text(field+":text"),true);break;
                        case "number":value=Number(Text(field+":text"),false);break;
                        case "array" when field=="transformationMatrix":
                            if(Text(field+":enabled")=="true")
                            {
                                var cells=Values(field+":matrix");if(cells.Length!=16)throw new ArgumentException("Spatial transforms must be 4 by 4, in metres with column vectors.");
                                value=Enumerable.Range(0,4).Select(r=>Enumerable.Range(0,4).Select(c=>(double)(Number(cells[r*4+c],false)??throw new ArgumentException("Enter all matrix values."))).ToArray()).ToArray();
                            }break;
                        case "array":var selected=Values(field+":selected");if(selected.Length>0)value=selected;break;
                        default:string text=Text(field+":text");if(text.Length>0)value=text;break;
                    }
                }
                if(value is not null){ServiceOperations.ValidateField(operation,field,Aholo3DJson.Element(value));body[field]=value;}
            }
            catch(ArgumentException ex){errors[field]=ex.Message;}
        }
        string title=Text("$name");if(title.Length==0)title=body.TryGetValue("prompt",out var prompt)?TaskStore.DisplayText((string)prompt,60):ServiceOperations.Get(operation).Title;
        if(ServiceOperations.Get(operation).Service==TaskService.World)body["name"]=title;
        var request=new CreateServiceRequest(operation,title,Aholo3DJson.Element(body),files.ToArray());
        if(errors.Count==0)
        {
            try{_=ServiceRequest.Bind(request,ServiceRequest.Placeholder);}
            catch(ArgumentException ex)
            {
                const string prefix="Required field: ";string field=ex.Message.StartsWith(prefix,StringComparison.Ordinal)?ex.Message[prefix.Length..]:"";
                errors[field]=ex.Message;
            }
        }
        return new(errors.Count==0?request:null,errors);
    }
}
