using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Aholo.Core;
using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using OpenFileDialog=Eto.Forms.OpenFileDialog;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

[Guid("676AA7F9-6F1F-4F15-9CE1-DB3CB70CF397")]
public sealed class AholoPanel : Panel,IPanel
{
    private readonly uint serial;
    private readonly AholoSession session=AholoPlugin.Session;
    private readonly TabControl tabs=new();
    private readonly DropDown creation=new();
    private readonly Panel formHost=new();
    private CreationForm form=null!;
    private readonly Dictionary<string,CreationForm> drafts=new();
    private readonly ListBox tasks=new(){Height=190};
    private readonly DropDown artifacts=new();
    private readonly Label artifactDetail=Info("");
    private readonly TextArea detail=new(){ReadOnly=true,Wrap=true,Height=130};
    private readonly TextArea feedback=new(){ReadOnly=true,Wrap=true,Height=65};
    private readonly Label connection=Info("Not connected");
    private readonly Label operation=Info("Ready");
    private readonly TextBox search=new(){PlaceholderText=T("Search local history")};
    private readonly DropDown filter=new(){DataStore=new[]{T("All tasks"),T("Worlds"),T("Objects and images"),T("Renders"),T("Spatial understanding")},SelectedIndex=0};
    private readonly Label pageInfo=Info("");
    private readonly Button previous=new(){Text=T("Previous")},next=new(){Text=T("Next")},cancel=new(){Text=T("Cancel local operation"),Enabled=false};
    private readonly TextBox scale=new(){Text="1"};
    private readonly DropDown axis=new(){DataStore=new[]{T("Automatic up axis"),"Z-up","Y-up"},SelectedIndex=0};
    private readonly TextBox objectHeight=new(){Text="1"};
    private readonly DropDown exportScope=new(){DataStore=new[]{T("Selected top-level GS"),T("All top-level GS (including hidden and locked)")},SelectedIndex=0};
    private readonly DropDown resolution=new(){DataStore=new[]{"1280 × 720","1920 × 1080"},SelectedIndex=0};
    private readonly List<Button> actions=new();
    private readonly Button submit,render,refresh,download,place,continueRender;
    private readonly UITimer elapsed=new(){Interval=1};
    private readonly Stopwatch clock=new();
    private readonly CancellationTokenSource lifetime=new();
    private CancellationToken token;
    private bool busy,disposed;
    private int offset;
    private string workName="";
    private LocalTask? Selected=>tasks.SelectedValue as LocalTask;
    private TaskArtifact? SelectedArtifact=>artifacts.SelectedValue as TaskArtifact;
    private static string T(string value)=>UiText.Get(value);
    private static Label Info(string value)=>new(){Text=T(value),Wrap=WrapMode.Word};
    private RhinoDoc Document()=>RhinoDoc.FromRuntimeSerialNumber(serial)??throw new InvalidOperationException(T("The target document has closed."));
    private string Scope=>AholoPlugin.DocumentScope(serial);
    public AholoPanel(uint documentSerialNumber)
    {
        serial=documentSerialNumber;
        var document=RhinoDoc.FromRuntimeSerialNumber(serial);
        if(document is not null){double unitScale=global::Rhino.RhinoMath.UnitScale(global::Rhino.UnitSystem.Meters,document.ModelUnitSystem);if(double.IsFinite(unitScale)&&unitScale>0)objectHeight.Text=unitScale.ToString("R",CultureInfo.InvariantCulture);}
        creation.DataStore=ServiceOperations.All.Select(x=>T(x.Title)).ToArray();creation.SelectedIndex=0;
        creation.SelectedIndexChanged+=(_,_)=>ChangeForm();ChangeForm();
        submit=Action("Create · Uses service credits",CreateAsync);
        render=Action("Upload selection and render · Uses service credits",RenderAsync);
        refresh=Action("Refresh selected task",RefreshAsync);
        download=Action("Download selected result",DownloadAsync);
        place=Action("Place in current document",PlaceAsync);
        continueRender=Action("Continue original render · Uses service credits",ContinueAsync);
        var createPage=Column(creation,formHost,submit,Info("Each click creates one paid task. Follow its progress in Library."));
        tasks.SelectedIndexChanged+=(_,_)=>Describe();artifacts.SelectedIndexChanged+=(_,_)=>UpdateActions();
        filter.SelectedIndexChanged+=(_,_)=>{offset=0;_=ReloadAsync();};search.TextChanged+=(_,_)=>{offset=0;_=ReloadAsync();};
        previous.Click+=(_,_)=>{offset=Math.Max(0,offset-50);_=ReloadAsync();};next.Click+=(_,_)=>{offset+=50;_=ReloadAsync();};
        var worldId=new TextBox{PlaceholderText=T("Existing Aholo World ID")};
        var libraryPage=Column(search,filter,tasks,Row(previous,pageInfo,next),Row(Local("Load history",()=>_=ReloadAsync()),refresh),detail,artifacts,artifactDetail,
            Row(download,Local("Open result folder",OpenResultFolder)),Local("Open image",OpenImage),place,continueRender,
            Row(Local("Pause local polling",Pause),Local("Resume polling",Resume)),
            Info("Local files"),Row(Action("Import GS",ImportGsAsync),Action("Export GS",ExportGsAsync)),exportScope,
            Row(Local("GS viewport",()=>GaussianViewport.Activate(Document())),Local("Diagnostics",()=>Report(GaussianViewport.Diagnostics(Document())))),
            worldId,Action("Restore World ID",async()=>{var task=await session.Client.ImportWorldAsync(worldId.Text.Trim(),token);session.Observe(task);await RevealAsync(task);}));
        var renderPage=Column(Info("Select meshes, surfaces, extrusions or mesh blocks and use a perspective view. Geometry, base colors and camera will be uploaded."),resolution,render,
            Info("Offline rendering uses a captured scene, including nested block placements. Results appear in Library. Textures, lights and GS ingestion are not converted yet."),
            Info("Realtime cloud rendering is still under development. Native depth compositing is not available from the documented image stream."));
        tabs.Pages.Add(new(){Text=T("Create"),Content=createPage});tabs.Pages.Add(new(){Text=T("Library"),Content=libraryPage});tabs.Pages.Add(new(){Text=T("Render"),Content=renderPage});
        cancel.Click+=(_,_)=>session.Operations.CancelPrefix(Scope);
        elapsed.Elapsed+=(_,_)=>{if(!disposed&&busy)operation.Text=workName+" · "+(int)clock.Elapsed.TotalSeconds+" s";};
        Content=new StackLayout{Padding=10,Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={
            Row(new Label{Text="Aholo3D",Font=new Font(SystemFont.Bold,16)},Local("Settings",ShowSettings)),connection,new StackLayoutItem(tabs,true),Row(operation,cancel),feedback}};
        session.Changed+=SessionChanged;session.TaskChanged+=TaskChanged;UpdateActions();
        Report(T("Create assets, organize results and place them in this Rhino document."));
    }
    private static Control Column(params Control[] controls)
    {var stack=new StackLayout{Padding=6,Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch};foreach(var control in controls)stack.Items.Add(control);return new Scrollable{Content=stack};}
    private static Control Row(params Control[] controls)
    {var row=new StackLayout{Orientation=Orientation.Horizontal,Spacing=6};foreach(var control in controls)row.Items.Add(new StackLayoutItem(control,true));return row;}
    private Button Action(string title,Func<Task> work)
    {var button=new Button{Text=T(title)};button.Click+=(_,_)=>_=RunAsync(title,work);actions.Add(button);return button;}
    private Button Local(string title,System.Action work)
    {var button=new Button{Text=T(title)};button.Click+=(_,_)=>{if(disposed)return;try{work();}catch(Exception ex){Report(AholoHost.FriendlyError(ex));}};return button;}
    private void ChangeForm()
    {
        if(creation.SelectedIndex<0)return;string id=ServiceOperations.All[creation.SelectedIndex].Id;
        if(!drafts.TryGetValue(id,out var selected)){selected=new CreationForm(id);drafts[id]=selected;}
        form=selected;formHost.Content=form;
    }
    private void Report(string text){if(!disposed)feedback.Text=text;}
    private async Task RunAsync(string name,Func<Task> work)
    {
        if(disposed||busy)return;busy=true;workName=T(name);clock.Restart();elapsed.Start();UpdateActions();
        try
        {
            await session.Operations.RunAsync(Scope+"foreground",name,async ct=>{token=ct;try{await work();}finally{token=default;}},lifetime.Token);
        }
        catch(Exception ex){AholoHost.Ui(()=>Report(AholoHost.FriendlyError(ex)));}
        finally
        {
            AholoHost.Ui(()=>{busy=false;clock.Stop();if(disposed){lifetime.Dispose();return;}elapsed.Stop();operation.Text=T("Ready");UpdateActions();});
        }
    }
    private void UpdateActions()
    {
        if(disposed)return;
        bool connected=session.Connection.Connected;
        connection.Text=connected?T("International · Connected"):T("International · Not connected");
        foreach(var button in actions)button.Enabled=!busy;
        submit.Enabled=render.Enabled=!busy&&connected;
        var task=Selected;var capability=task is null?null:TaskCapabilities.For(task);var artifact=SelectedArtifact;
        artifactDetail.Text=artifact?.Error is not null?UiText.TranslateKnownMessage(artifact.Error):artifact?.LocalPath??"";
        bool sameAccount=task?.ConnectionId==session.Connection.ConnectionId;
        refresh.Enabled=!busy&&connected&&sameAccount&&capability?.CanQuery==true;
        download.Enabled=!busy&&connected&&sameAccount&&capability?.CanDownload==true&&artifact is not null&&artifact.Error is null;
        place.Enabled=!busy&&task is not null&&artifact is not null&&artifact.Format is "glb" or "ply"&&artifact.LocalPath is not null&&File.Exists(artifact.LocalPath);
        continueRender.Enabled=!busy&&connected&&sameAccount&&capability?.CanContinueRender==true;
        continueRender.Visible=task?.Kind=="render"&&capability?.CanContinueRender==true;
        cancel.Enabled=session.Operations.Snapshot().Any(x=>x.State=="Running"&&x.Scope.StartsWith(Scope,StringComparison.Ordinal));
    }
    private long historyRevision;
    private async Task ReloadAsync(Guid? select=null)
    {
        if(disposed)return;
        long revision=Interlocked.Increment(ref historyRevision);
        var request=new TaskPageRequest(offset,50,filter.SelectedIndex switch{1=>"world",2=>"object",3=>"render",4=>"spatial",_=>null},search.Text.Trim());
        Guid? selected=select??Selected?.Id;
        try
        {
            var page=await session.ReadHistoryAsync(request);
            AholoHost.Ui(()=>{if(disposed||revision!=historyRevision)return;tasks.DataStore=page.Items;int index=page.Items.ToList().FindIndex(t=>t.Id==selected);tasks.SelectedIndex=index>=0?index:page.Items.Count>0?0:-1;previous.Enabled=offset>0;next.Enabled=page.HasMore;pageInfo.Text=$"{Math.Min(offset+1,page.Total)}–{offset+page.Items.Count} / {page.Total}";Describe();});
        }
        catch(Exception ex){AholoHost.Ui(()=>Report(AholoHost.FriendlyError(ex)));}
    }
    private static TaskArtifact[] Outputs(LocalTask task)=>task.Kind switch
    {
        "world"=>new[]{new TaskArtifact("ply","Gaussian scene","ply",LocalPath:task.PlyPath),new TaskArtifact("spz","Compressed Gaussian scene","spz",LocalPath:task.SpzPath),new TaskArtifact("image","Panorama","image",LocalPath:task.ImagePath)},
        "render"=>new[]{new TaskArtifact("image","Rendered image","image",LocalPath:task.ImagePath)},_=>task.Artifacts
    };
    private void Describe()
    {
        var task=Selected;if(task is null){detail.Text=T("No task selected.");artifacts.DataStore=Array.Empty<TaskArtifact>();UpdateActions();return;}
        string? key=SelectedArtifact?.Key;
        string progress=task.Progress.HasValue?" · "+task.Progress.Value.ToString("0.##",CultureInfo.InvariantCulture):"";
        string queried=task.LastQueriedAt.HasValue?task.LastQueriedAt.Value.ToLocalTime().ToString("g"):T("Not queried yet");
        detail.Text=task.Name+"\n"+T(task.Kind)+" · "+UiText.Status(task.Status)+progress+"\nID: "+(TaskIdentity.From(task).RemoteId??"—")+"\n"+T("Last query")+": "+queried+(session.IsPaused(task.Id)?" · "+T("Polling paused"):"")+(task.Error is null?"":"\n"+UiText.TranslateKnownMessage(task.Error));
        var rows=Outputs(task);artifacts.DataStore=rows;int index=Array.FindIndex(rows,a=>a.Key==key);artifacts.SelectedIndex=index>=0?index:rows.Length>0?0:-1;UpdateActions();
    }
    private async Task CreateAsync()
    {var request=form.Capture();var task=await session.Client.CreateServiceAsync(request,token);session.Observe(task);await RevealAsync(task);}
    private async Task RevealAsync(LocalTask task)
    {
        if(disposed)return;
        var page=await Task.Run(()=>session.Store.PageContaining(task.Id));
        AholoHost.Ui(()=>{if(disposed)return;tabs.SelectedIndex=1;filter.SelectedIndex=0;search.Text="";offset=page.Offset;Report(task.Error is null?T("Task recorded. Library follows the original task; no automatic resubmission."):UiText.TranslateKnownMessage(task.Error));});
        await ReloadAsync(task.Id);
    }
    private async Task RefreshAsync()
    {var task=Selected??throw new InvalidOperationException(T("Select a task first."));var updated=await session.Client.RefreshAsync(task.Id,token);session.Observe(updated);await ReloadAsync(task.Id);}
    private async Task ContinueAsync()
    {var task=Selected??throw new InvalidOperationException(T("Select a task first."));var updated=await session.ContinueRenderAsync(task.Id,token);session.Observe(updated);await ReloadAsync(task.Id);}
    private async Task DownloadAsync()
    {
        var task=Selected??throw new InvalidOperationException(T("Select a task first."));var asset=SelectedArtifact??throw new InvalidOperationException(T("Select a result first."));
        var updated=await session.Client.DownloadAsync(new(task.Id,task.Kind is "world" or "render"?asset.Key:"artifact:"+asset.Key),token);session.Observe(updated);
        string? path=Outputs(updated).FirstOrDefault(a=>a.Key==asset.Key)?.LocalPath;AholoHost.Ui(()=>Report(T("Saved locally")+":\n"+path));await ReloadAsync(task.Id);
    }
    private void OpenImage()
    {
        var artifact=SelectedArtifact;if(artifact?.Format!="image"||artifact.LocalPath is null||!File.Exists(artifact.LocalPath))throw new InvalidOperationException(T("Download and select an image result first."));
        Process.Start(new ProcessStartInfo(artifact.LocalPath){UseShellExecute=true});
    }
    private void OpenResultFolder()
    {string? path=SelectedArtifact?.LocalPath;string folder=path is not null?Path.GetDirectoryName(path)!:Path.Combine(session.Store.Root,"assets");Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}
    private void Pause(){if(Selected is{} task){session.PauseTask(task.Id);Describe();Report(T("Local polling paused. The remote task continues."));}}
    private void Resume(){if(Selected is{} task){session.ResumeQueries(task.Id);Describe();}}
    private double Scale()
    {if(!double.TryParse(scale.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||!double.IsFinite(value)||value<=0)throw new ArgumentException(T("Enter an import scale greater than zero, such as 1 or 1000."));return value;}
    private string Axis()=>axis.SelectedIndex==1?"Z":axis.SelectedIndex==2?"Y":"Auto";
    private async Task PlaceAsync()
    {
        var task=Selected??throw new InvalidOperationException(T("Select a task first."));var artifact=SelectedArtifact??throw new InvalidOperationException(T("Select a result first."));
        if(artifact.LocalPath is null||!File.Exists(artifact.LocalPath))throw new InvalidOperationException(T("Download the selected result first."));
        var target=AholoHost.CaptureTarget(Document());
        if(artifact.Format=="ply")
        {string up=Axis()=="Auto"?task.UpAxis??"Z":Axis();string message=await GaussianDocument.ImportAsync(artifact.LocalPath,target,Scale(),up,task.Id,token);AholoHost.Ui(()=>Report(message));return;}
        if(artifact.Format!="glb")throw new InvalidOperationException(T("This result format cannot be placed directly."));
        if(!double.TryParse(objectHeight.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out double height)||!double.IsFinite(height)||height<=0)throw new ArgumentException(T("Enter a positive model height in document units."));
        var trace=new HostImportTrace(task.Id,serial,AholoPlugin.DisplayVersion,session.Store.Root);
        try
        {
            using var snapshot=await Task.Run(()=>GlbImportSnapshot.Create(artifact.LocalPath,artifact.Sha256,trace),token);
            await AholoHost.OnUi(()=>
            {
                token.ThrowIfCancellationRequested();var doc=Document();
                if(doc.ModelUnitSystem!=target.Units||RhinoDoc.ActiveDoc?.RuntimeSerialNumber!=serial||doc.IsReadOnly||doc.IsClosing||doc.IsCommandRunning||doc.UndoRecordingIsActive)throw new InvalidOperationException(T("The target document changed. Retry placement in the intended document."));
                Guid importId=new(SHA256.HashData(Encoding.UTF8.GetBytes(task.Id.ToString("N")+":"+artifact.Key)).AsSpan(0,16));
                var host=AholoPlugin.MeshHost;var prior=host.FindReceipt(doc,importId);
                if(prior is not null){Report(prior.State=="Imported"?T("This result is already in the document."):T("The earlier placement was removed. Use Rhino Redo to restore it."));return true;}
                using var prepared=host.Prepare(snapshot,doc,trace);
                int previousLayer=doc.Layers.CurrentLayerIndex;int visible=GaussianDocument.VisibleLayer(doc);
                if(!doc.Layers.SetCurrentLayerIndex(visible,true))throw new InvalidOperationException(T("Cannot select a visible layer."));
                ImportReceipt receipt;
                try{receipt=host.Commit(prepared,new(global::Rhino.Geometry.Plane.WorldXY,global::Rhino.Geometry.Point3d.Origin,height,0),importId,task.Id,trace);}
                finally{doc.Layers.SetCurrentLayerIndex(previousLayer,true);}
                var obj=doc.Objects.FindId(receipt.ObjectId);if(obj is not null){doc.Objects.UnselectAll();obj.Select(true);doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(obj.Geometry.GetBoundingBox(true));doc.Views.Redraw();}
                trace.Complete(receipt.State);Report(T("Model placed at the world origin. Rhino Undo removes this placement."));return true;
            });
        }
        catch(Exception ex){trace.Fail(ex);throw;}
    }
    private async Task ImportGsAsync()
    {
        using var dialog=new OpenFileDialog{Title=T("Import GS"),MultiSelect=false};dialog.Filters.Add(new FileFilter("Gaussian scene",".ply",".usd",".usda",".usdc",".usdz"));
        if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        var target=AholoHost.CaptureTarget(Document());string message=await GaussianDocument.ImportSceneAsync(dialog.FileName,target,Scale(),Axis(),true,token);AholoHost.Ui(()=>Report(message));
    }
    private async Task ExportGsAsync()
    {
        var snapshot=GaussianDocument.CaptureExport(Document(),selectedOnly:exportScope.SelectedIndex==0);using var dialog=GaussianExportDialogs.CreateSaveDialog();if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        string message=await GaussianDocument.ExportAsync(GaussianExportDialogs.NormalizePath(dialog),snapshot,token);AholoHost.Ui(()=>Report(message));
    }
    private async Task RenderAsync()
    {
        int width=resolution.SelectedIndex==1?1920:1280,height=resolution.SelectedIndex==1?1080:720;
        var snapshot=AholoHost.CaptureRender(Document(),width,height);string path=Path.Combine(session.Store.Root,"exports",Guid.NewGuid().ToString("N")+".glb");
        await Task.Run(()=>{Directory.CreateDirectory(Path.GetDirectoryName(path)!);GlbWriter.Write(path,snapshot.Meshes,token);},token);
        var task=await session.RenderAsync(new(snapshot.Name,path,snapshot.Camera,width,height),token);await RevealAsync(task);
    }
    private void ShowSettings()
    {
        var validation=new DropDown{DataStore=new[]{"World","Lux3D",T("Spatial understanding")},SelectedIndex=0};
        var key=new PasswordBox();var remember=new CheckBox{Text=T("Save in secure local storage")};
        var auto=new CheckBox{Text=T("Auto-refresh active tasks (every 8 seconds)"),Checked=session.AutoRefresh};auto.CheckedChanged+=(_,_)=>session.AutoRefresh=auto.Checked==true;
        var settingsScale=new TextBox{Text=scale.Text};var settingsAxis=new DropDown{DataStore=axis.DataStore,SelectedIndex=axis.SelectedIndex};var settingsHeight=new TextBox{Text=objectHeight.Text};
        var status=Info(session.Connection.Connected?"International · Connected":"International · Not connected");
        using var dialog=new Dialog{Title=T("Settings"),ClientSize=new Size(460,620),Resizable=true};
        var connect=Local("Validate and connect",()=>{if(busy){status.Text=T("Wait for the current operation or cancel it before changing credentials.");return;}string value=key.Text;bool save=remember.Checked==true;string service=validation.SelectedIndex switch{1=>"lux",2=>"spatial",_=>"world"};key.Text="";_=RunAsync("Connecting",async()=>{await session.ConnectAsync(new(value,save,service),token);AholoHost.Ui(()=>Report(T("Connected. Service permissions and credits are checked per operation.")));});});
        var disconnect=Local("Disconnect and clear credentials",()=>{if(busy){status.Text=T("Wait for the current operation or cancel it before changing credentials.");return;}_=RunAsync("Disconnecting",async()=>{await session.DisconnectAsync(token);AholoHost.Ui(()=>Report(T("Disconnected and cleared locally saved credentials. Remote tasks are not cancelled.")));});});
        var close=new Button{Text=T("Close")};close.Click+=(_,_)=>dialog.Close();
        // Dialog controls are snapshots; closing it preserves placement values in this panel.
        dialog.Content=Column(status,Info("International API Key"),key,remember,Info("Validate access with"),validation,connect,disconnect,
            Info("Validation queries the selected service history. Each service may need separate account permissions. OAuth login does not replace an API Key."),auto,
            Info("GS import scale (document units per source unit)"),settingsScale,settingsAxis,Info("Mesh height (document units)"),settingsHeight,
            Local("Open local asset folder",()=>Process.Start(new ProcessStartInfo(Path.Combine(session.Store.Root,"assets")){UseShellExecute=true})),
            Info("Closing the panel keeps tasks running. Reopen it to view progress. Cancel stops local waiting only; remote tasks remain on the service."),
            Info("Aholo3D for Rhino "+AholoPlugin.DisplayVersion+"\nCuriousArc · qrost"),close);
        bool closed=false;
        void UpdateSettings()=>Application.Instance.AsyncInvoke(()=>{if(!closed)status.Text=T(session.Connection.Connected?"International · Connected":"International · Not connected");});
        session.Changed+=UpdateSettings;
        try{dialog.ShowModal(this);}finally{closed=true;session.Changed-=UpdateSettings;}
        scale.Text=settingsScale.Text;axis.SelectedIndex=settingsAxis.SelectedIndex;objectHeight.Text=settingsHeight.Text;
    }
    private void SessionChanged()=>Application.Instance.AsyncInvoke(()=>{if(!disposed)UpdateActions();});
    private void TaskChanged(LocalTask task)=>Application.Instance.AsyncInvoke(()=>{if(!disposed)_=ReloadAsync();});
    public void PanelShown(uint documentSerialNumber,ShowPanelReason reason)
    {if(disposed)return;session.Start();UpdateActions();_=ReloadAsync();}
    public void PanelHidden(uint documentSerialNumber,ShowPanelReason reason){}
    public void PanelClosing(uint documentSerialNumber,bool onCloseDocument)
    {if(onCloseDocument){lifetime.Cancel();session.Operations.CancelPrefix(Scope);}}
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!disposed){disposed=true;session.Changed-=SessionChanged;session.TaskChanged-=TaskChanged;elapsed.Stop();elapsed.Dispose();foreach(var draft in drafts.Values)draft.Dispose();drafts.Clear();if(!busy)lifetime.Dispose();}
        base.Dispose(disposing);
    }
}
