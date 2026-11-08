using System.Globalization;
using System.Text.Json;
using Aholo.Core;
using Eto.Forms;
namespace Aholo.Rhino;

/// <summary>Schema-backed controls; service field keys never become executable paths or request URLs.</summary>
internal sealed class CreationForm : Panel
{
    private readonly string operation;
    private readonly List<Func<(string,object?)>> readers=new();
    private readonly List<Func<ServiceFileInput?>> files=new();
    private readonly TextBox name=new(){PlaceholderText=UiText.Get("Task name (optional)")};
    private static Label Label(string text)=>new(){Text=UiText.Get(text),Wrap=WrapMode.Word};
    private static readonly Dictionary<string,string> Titles=new()
    {
        ["prompt"]="Description",["img"]="Reference image",["imgs"]="Reference images",["resources"]="Photos or video",["cover"]="Cover image",
        ["version"]="Generation model",["faceCount"]="Target faces",["outputFormat"]="Output formats",["enablePbr"]="PBR materials",["aiPredictSize"]="Predict model size",
        ["style"]="Style",["meshUrl"]="Source GLB",["modelUrl"]="Source model",["rigModelUrl"]="Rigged GLB",["glbUrl"]="Source GLB",["inputFileUrl"]="Indoor PLY",
        ["customSize"]="Target height (mm)",["animationIds"]="Animations (choose 1–10)",["animationOutputMode"]="Animation files",["outFormat"]="Output format",["animateInPlace"]="Animate in place",
        ["scene"]="Scene type",["taskQuality"]="Quality",["useMask"]="Remove background",["transformationMatrix"]="Alignment transform (metres)"
    };
    public CreationForm(string operation)
    {
        this.operation=operation;
        var basic=new StackLayout{Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var advanced=new StackLayout{Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch,Visible=false};
        basic.Items.Add(name);
        var schema=ServiceOperations.RequestSchema(operation);
        var required=schema.TryGetProperty("required",out var r)?r.EnumerateArray().Select(x=>x.GetString()!).ToHashSet():new HashSet<string>();
        foreach(var property in schema.GetProperty("properties").EnumerateObject())
        {
            string field=property.Name;if(field=="name")continue;
            var definition=ServiceOperations.ResolveSchema(operation,property.Value);
            var container=required.Contains(field)||field is "prompt" or "img" or "imgs" or "resources"?basic:advanced;
            container.Items.Add(Label(UiText.Get(Titles.GetValueOrDefault(field,field))+(required.Contains(field)?" *":"")));
            container.Items.Add(BuildField(field,definition,required.Contains(field)));
        }
        var more=new CheckBox{Text=UiText.Get("Advanced options")};more.CheckedChanged+=(_,_)=>advanced.Visible=more.Checked==true;
        basic.Items.Add(more);basic.Items.Add(advanced);Content=basic;
    }
    private Control BuildField(string field,JsonElement schema,bool required)
    {
        if(field is "img" or "imgs" or "resources" or "cover" or "modelUrl" or "meshUrl" or "rigModelUrl" or "glbUrl" or "inputFileUrl")
        {
            string[] paths=Array.Empty<string>();
            var summary=Label("No files selected");
            var choose=new Button{Text=UiText.Get("Choose files")};var clear=new Button{Text=UiText.Get("Clear")};
            var url=new TextArea{Height=45,Wrap=false,ToolTip=UiText.Get("Public HTTPS URL; one per line for multiple images"),Visible=field!="resources"&&field!="inputFileUrl"};
            choose.Click+=(_,_)=>
            {
                using var dialog=new OpenFileDialog{Title=UiText.Get(Titles.GetValueOrDefault(field,field)),MultiSelect=field=="imgs"||field=="resources"&&operation=="world.reconstruct"};
                string[] extensions=field is "img" or "imgs" or "cover"?new[]{".png",".jpg",".jpeg",".webp"}:field=="resources"?operation=="world.generate"?new[]{".png",".jpg",".jpeg",".webp"}:new[]{".png",".jpg",".jpeg",".webp",".mp4",".mov",".insv"}:field=="inputFileUrl"?new[]{".ply"}:field=="modelUrl"&&operation=="lux.multi-format-export"?new[]{".glb",".zip"}:new[]{".glb"};
                dialog.Filters.Add(new FileFilter(UiText.Get("Supported inputs"),extensions));
                if(dialog.ShowDialog(this)!=DialogResult.Ok)return;paths=dialog.Filenames.ToArray();url.Text="";summary.Text=string.Join("\n",paths.Select(Path.GetFileName));
            };
            clear.Click+=(_,_)=>{paths=Array.Empty<string>();url.Text="";summary.Text=UiText.Get("No files selected");};
            files.Add(()=>paths.Length==0?null:new ServiceFileInput(field,paths.ToArray()));
            readers.Add(()=>{string text=url.Text.Trim();return(field,text.Length==0?null:field=="imgs"?text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries):text);});
            var row=new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={choose,clear}};
            return new StackLayout{Spacing=4,Items={summary,row,url}};
        }
        if(schema.TryGetProperty("enum",out var values))
        {
            var options=values.EnumerateArray().Select(x=>x.GetString()!).ToArray();
            var all=required?options:new[]{""}.Concat(options).ToArray();
            var control=new DropDown{DataStore=all.Select(x=>x.Length==0?UiText.Get("Service default"):UiText.Get(x)).ToArray(),SelectedIndex=0};
            string? initial=schema.TryGetProperty("default",out var d)?d.GetString():field=="version"?"G1-Turbo":null;
            if(initial is not null&&Array.IndexOf(all,initial) is int selected&&selected>=0)control.SelectedIndex=selected;
            readers.Add(()=>(field,control.SelectedIndex<0||all[control.SelectedIndex].Length==0?null:all[control.SelectedIndex]));return control;
        }
        string type=schema.GetProperty("type").GetString()!;
        if(type=="boolean")
        {
            var box=new DropDown{DataStore=new[]{UiText.Get("Service default"),UiText.Get("Yes"),UiText.Get("No")},SelectedIndex=0};
            readers.Add(()=>(field,box.SelectedIndex==0?null:box.SelectedIndex==1));return box;
        }
        if(field=="transformationMatrix")
        {
            var enabled=new CheckBox{Text=UiText.Get("Apply alignment transform")};var rows=new StackLayout{Spacing=3};var cells=new TextBox[4,4];
            for(int r=0;r<4;r++){var row=new StackLayout{Orientation=Orientation.Horizontal,Spacing=3};for(int c=0;c<4;c++){cells[r,c]=new(){Text=r==c?"1":"0",Width=60};row.Items.Add(cells[r,c]);}rows.Items.Add(row);}
            rows.Items.Insert(0,enabled);readers.Add(()=>(field,enabled.Checked==true?Enumerable.Range(0,4).Select(r=>Enumerable.Range(0,4).Select(c=>double.Parse(cells[r,c].Text,CultureInfo.InvariantCulture)).ToArray()).ToArray():null));return rows;
        }
        if(type=="array")
        {
            var entries=schema.GetProperty("items").GetProperty("enum").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            var boxes=entries.Select(x=>new CheckBox{Text=UiText.Get(x),Tag=x,Checked=field=="outputFormat"&&x=="glb"}).ToArray();
            var list=new StackLayout{Spacing=3};foreach(var box in boxes)list.Items.Add(box);
            readers.Add(()=>{var selected=boxes.Where(x=>x.Checked==true).Select(x=>(string)x.Tag).ToArray();return(field,selected.Length==0?null:selected);});
            return boxes.Length>10?new Scrollable{Height=160,Content=list}:list;
        }
        if(type is "number" or "integer")
        {
            var box=new TextBox{PlaceholderText=UiText.Get("Service default"),Text=field=="faceCount"?"300000":""};
            readers.Add(()=>(field,string.IsNullOrWhiteSpace(box.Text)?null:type=="integer"?(object)long.Parse(box.Text,CultureInfo.InvariantCulture):double.Parse(box.Text,CultureInfo.InvariantCulture)));return box;
        }
        var input=new TextArea{Height=field=="prompt"?100:45,Wrap=true};readers.Add(()=>(field,string.IsNullOrWhiteSpace(input.Text)?null:input.Text.Trim()));return input;
    }
    public CreateServiceRequest Capture()
    {
        var body=new Dictionary<string,object>();foreach(var read in readers){var(field,value)=read();if(value is not null)body[field]=value;}
        string title=name.Text.Trim();if(title.Length==0)title=body.TryGetValue("prompt",out var prompt)?TaskStore.DisplayText((string)prompt,60):ServiceOperations.Get(operation).Title;
        if(ServiceOperations.Get(operation).Service==TaskService.World)body["name"]=title;
        var request=new CreateServiceRequest(operation,title,AholoJson.Element(body),files.Select(f=>f()).Where(f=>f is not null).Cast<ServiceFileInput>().ToArray());
        _=ServiceRequest.Bind(request,ServiceRequest.Placeholder);return request;
    }
}
