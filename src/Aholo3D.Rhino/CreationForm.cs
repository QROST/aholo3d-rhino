using System.Text.Json;
using Aholo3D.Core;
using Eto.Forms;
namespace Aholo3D.Rhino;

/// <summary>Schema-backed controls; service field keys never become executable paths or request URLs.</summary>
internal sealed class CreationForm : Panel
{
    private readonly string operation;
    public event EventHandler? Changed;
    private bool ready,restoring;
    private readonly Dictionary<string,Label> fieldErrors=new();
    private readonly Label validationSummary=Label("");
    private CreationValidation validation=new(null,new Dictionary<string,string>());
    public bool IsValid=>validation.IsValid;
    public string ValidationMessage=>string.Join("\n",validation.Errors.Values.Select(x=>UiText.TranslateKnownMessage(x)));
    private void Edited(){if(!ready||restoring)return;ValidateForm();Changed?.Invoke(this,EventArgs.Empty);}
    private void ValidateForm()
    {
        validation=CreationDraftValidation.Evaluate(operation,CaptureDraft());
        foreach(var entry in fieldErrors){entry.Value.Text=validation.Errors.TryGetValue(entry.Key,out var error)?UiText.TranslateKnownMessage(error):"";entry.Value.Visible=entry.Value.Text.Length>0;}
        validationSummary.Text=validation.Errors.TryGetValue("",out var general)?UiText.TranslateKnownMessage(general):UiText.Get("Review the highlighted fields, including Advanced options.");
        validationSummary.Visible=!validation.IsValid;
    }
    private readonly Dictionary<string,(Func<string[]> Capture,Action<IReadOnlyList<string>> Restore)> draftFields=new(StringComparer.Ordinal);
    private void DraftField(string key,Func<string[]> capture,Action<IReadOnlyList<string>> restore)=>draftFields.Add(key,(capture,restore));
    private void DraftText(string key,TextControl control)
    {DraftField(key,()=>new[]{control.Text},values=>control.Text=values.FirstOrDefault()??"");control.TextChanged+=(_,_)=>Edited();}
    private static string Toggle(bool? value)=>value is null?"":value==true?"true":"false";
    private static bool? Toggle(string? value)=>value=="true"?true:value=="false"?false:null;
    public CreationDraft CaptureDraft()=>new(draftFields.Select(x=>new KeyValuePair<string,string[]>(x.Key,x.Value.Capture())));
    public void RestoreDraft(CreationDraft draft)
    {restoring=true;try{foreach(var field in draftFields)if(draft.Values.TryGetValue(field.Key,out var values))field.Value.Restore(values);}finally{restoring=false;}ValidateForm();Changed?.Invoke(this,EventArgs.Empty);}
    private readonly TextBox name=new(){PlaceholderText=UiText.Get("Task name (optional)")};
    private static Label Label(string text)=>UiLayout.Text(UiText.Get(text));
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
        var basic=new StackLayout{Spacing=8,AlignLabels=false,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var advanced=new StackLayout{Spacing=8,AlignLabels=false,HorizontalContentAlignment=HorizontalAlignment.Stretch,Visible=false};
        basic.Items.Add(name);DraftText("$name",name);
        var schema=ServiceOperations.RequestSchema(operation);
        var required=schema.TryGetProperty("required",out var r)?r.EnumerateArray().Select(x=>x.GetString()!).ToHashSet():new HashSet<string>();
        foreach(var property in schema.GetProperty("properties").EnumerateObject())
        {
            string field=property.Name;if(field=="name")continue;
            var definition=ServiceOperations.ResolveSchema(operation,property.Value);
            var container=required.Contains(field)||field is "prompt" or "img" or "imgs" or "resources"?basic:advanced;
            container.Items.Add(Label(UiText.Get(Titles.GetValueOrDefault(field,field))+(required.Contains(field)?" *":"")));
            container.Items.Add(BuildField(field,definition,required.Contains(field)));
            string hint=CreationDraftValidation.Hint(operation,field);if(hint.Length>0)container.Items.Add(Label(hint));
            var error=Label("");error.TextColor=Eto.Drawing.Colors.IndianRed;error.Visible=false;fieldErrors[field]=error;container.Items.Add(error);
        }
        var more=new CheckBox{Text=UiText.Get("Advanced options")};more.CheckedChanged+=(_,_)=>{advanced.Visible=more.Checked==true;Edited();};
        DraftField("$advanced",()=>new[]{Toggle(more.Checked)},values=>{more.Checked=Toggle(values.FirstOrDefault());advanced.Visible=more.Checked==true;});
        basic.Items.Add(more);basic.Items.Add(advanced);basic.Items.Add(validationSummary);Content=basic;ready=true;ValidateForm();
    }
    private Control BuildField(string field,JsonElement schema,bool required)
    {
        if(field is "img" or "imgs" or "resources" or "cover" or "modelUrl" or "meshUrl" or "rigModelUrl" or "glbUrl" or "inputFileUrl")
        {
            string[] paths=Array.Empty<string>();
            var summary=Label("No files selected");
            var choose=new Button{Text=UiText.Get("Choose files")};var clear=new Button{Text=UiText.Get("Clear")};
            var url=new TextArea{Height=45,Wrap=true,ToolTip=UiText.Get("Public HTTPS URL; one per line for multiple images"),Visible=field!="resources"&&field!="inputFileUrl"};
            choose.Click+=(_,_)=>
            {
                using var dialog=new OpenFileDialog{Title=UiText.Get(Titles.GetValueOrDefault(field,field)),MultiSelect=field=="imgs"||field=="resources"&&operation=="world.reconstruct"};
                string[] extensions=field is "img" or "imgs" or "cover"?new[]{".png",".jpg",".jpeg",".webp"}:field=="resources"?operation=="world.generate"?new[]{".png",".jpg",".jpeg",".webp"}:new[]{".png",".jpg",".jpeg",".webp",".mp4",".mov",".insv"}:field=="inputFileUrl"?new[]{".ply"}:field=="modelUrl"&&operation=="lux.multi-format-export"?new[]{".glb",".zip"}:new[]{".glb"};
                dialog.Filters.Add(new FileFilter(UiText.Get("Supported inputs"),extensions));
                if(dialog.ShowDialog(this)!=DialogResult.Ok)return;paths=dialog.Filenames.ToArray();url.Text="";summary.Text=string.Join("\n",paths.Select(Path.GetFileName));Edited();
            };
            clear.Click+=(_,_)=>{paths=Array.Empty<string>();url.Text="";summary.Text=UiText.Get("No files selected");Edited();};
            DraftText(field+":url",url);
            DraftField(field+":files",()=>paths.ToArray(),values=>{paths=values.ToArray();summary.Text=paths.Length==0?UiText.Get("No files selected"):string.Join("\n",paths.Select(Path.GetFileName));});
            var row=UiLayout.Toolbar(choose,clear);
            return new StackLayout{Spacing=4,AlignLabels=false,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={summary,row,url}};
        }
        if(schema.TryGetProperty("enum",out var values))
        {
            var options=values.EnumerateArray().Select(x=>x.GetString()!).ToArray();
            var all=required?options:new[]{""}.Concat(options).ToArray();
            var control=new DropDown{DataStore=all.Select(x=>x.Length==0?UiText.Get("Service default"):UiText.Get(x)).ToArray(),SelectedIndex=0};
            string? initial=schema.TryGetProperty("default",out var d)?d.GetString():field=="version"?"G1-Turbo":null;
            if(initial is not null&&Array.IndexOf(all,initial) is int selected&&selected>=0)control.SelectedIndex=selected;
            DraftField(field+":choice",()=>control.SelectedIndex<0?Array.Empty<string>():new[]{all[control.SelectedIndex]},values=>control.SelectedIndex=values.Count==0?-1:Array.IndexOf(all,values[0]));
            control.SelectedIndexChanged+=(_,_)=>Edited();return control;
        }
        string type=schema.GetProperty("type").GetString()!;
        if(type=="boolean")
        {
            var box=new DropDown{DataStore=new[]{UiText.Get("Service default"),UiText.Get("Yes"),UiText.Get("No")},SelectedIndex=0};
            DraftField(field+":choice",()=>new[]{box.SelectedIndex==1?"true":box.SelectedIndex==2?"false":""},values=>box.SelectedIndex=values.FirstOrDefault() switch{"true"=>1,"false"=>2,_=>0});
            box.SelectedIndexChanged+=(_,_)=>Edited();return box;
        }
        if(field=="transformationMatrix")
        {
            var enabled=new CheckBox{Text=UiText.Get("Apply alignment transform")};var rows=UiLayout.Stack();var grid=new TableLayout{Spacing=new Eto.Drawing.Size(3,3)};var cells=new TextBox[4,4];
            for(int r=0;r<4;r++){var row=new TableRow();for(int c=0;c<4;c++){cells[r,c]=new(){Text=r==c?"1":"0",Width=24,ToolTip=$"[{r+1}, {c+1}]"};row.Cells.Add(new TableCell(cells[r,c],true));}grid.Rows.Add(row);}rows.Items.Add(grid);grid.Enabled=enabled.Checked==true;enabled.CheckedChanged+=(_,_)=>grid.Enabled=enabled.Checked==true;
            DraftField(field+":enabled",()=>new[]{Toggle(enabled.Checked)},values=>enabled.Checked=Toggle(values.FirstOrDefault()));
            DraftField(field+":matrix",()=>Enumerable.Range(0,16).Select(i=>cells[i/4,i%4].Text).ToArray(),values=>{if(values.Count==16)for(int i=0;i<16;i++)cells[i/4,i%4].Text=values[i];});
            rows.Items.Insert(0,enabled);enabled.CheckedChanged+=(_,_)=>Edited();foreach(var cell in cells)cell.TextChanged+=(_,_)=>Edited();return rows;
        }
        if(type=="array")
        {
            var entries=schema.GetProperty("items").GetProperty("enum").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            var boxes=entries.Select(x=>new CheckBox{Text=UiText.Get(x),Tag=x,Checked=field=="outputFormat"&&x=="glb"}).ToArray();
            var list=new StackLayout{Spacing=3,AlignLabels=false,HorizontalContentAlignment=HorizontalAlignment.Stretch};foreach(var box in boxes)list.Items.Add(box);
            DraftField(field+":selected",()=>boxes.Where(x=>x.Checked==true).Select(x=>(string)x.Tag).ToArray(),values=>{foreach(var box in boxes)box.Checked=values.Contains((string)box.Tag);});
            foreach(var box in boxes)box.CheckedChanged+=(_,_)=>Edited();
            return boxes.Length>10?new Scrollable{Height=160,Content=list}:list;
        }
        if(type is "number" or "integer")
        {
            var box=new TextBox{PlaceholderText=UiText.Get("Service default"),Text=field=="faceCount"?"300000":""};
            DraftText(field+":text",box);
            return box;
        }
        var input=new TextArea{Height=field=="prompt"?100:45,Wrap=true};DraftText(field+":text",input);return input;
    }
    public CreateServiceRequest Capture()
    {
        ValidateForm();return validation.Request??throw new ArgumentException(ValidationMessage);
    }
}
