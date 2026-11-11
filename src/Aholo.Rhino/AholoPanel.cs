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
public sealed partial class AholoPanel : Panel,IPanel,IPanelWorkspaceOwner
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
    private readonly LibraryResultSelection resultSelection=new();
    private TaskArtifact[] resultRows=Array.Empty<TaskArtifact>();
    private LibraryResultRow[] displayedResultRows=Array.Empty<LibraryResultRow>();
    private bool rebindingArtifacts;
    private LocalTask? displayedResultTask;
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
    private readonly DropDown sourceUnits=new(){DataStore=new[]{T("Auto · Source units; 1:1 if unspecified"),T("1 file unit = 1 document unit")},SelectedIndex=0};
    private AssetUploadOptions uploadOptions=new();
    private readonly TextBox objectHeight=new(){Text="1"};
    private readonly DropDown exportScope=new(){DataStore=new[]{T("Selected top-level GS"),T("All top-level GS (including hidden and locked)")},SelectedIndex=0};
    private readonly DropDown resolution=new(){DataStore=new[]{"1280 × 720","1920 × 1080"},SelectedIndex=0};
    private readonly CheckBox renderTextures=new(){Text=T("Include direct base-color bitmaps (mesh UVs / sRGB PNG or JPEG)"),Checked=true};
    private readonly CheckBox renderLights=new(){Text=T("Include selected rectangular / point lights")};
    private readonly TextBox lightRadius=new(){Text="50"},lightMultiplier=new(){Text="1"};
    private readonly List<Button> actions=new();
    private readonly Button submit,render,refresh,download,place,continueRender,openImage,openFolder;
    private readonly UITimer elapsed=new(){Interval=1};
    private readonly UITimer taskElapsed=new(){Interval=1};
    private readonly Stopwatch clock=new();
    private readonly CancellationTokenSource lifetime=new();
    private CancellationToken token;
    private bool busy,disposed;
    private long operationGeneration;
    private int offset;
    private string workName="";
    private LocalTask? Selected=>tasks.SelectedValue as LocalTask;
    private TaskArtifact? SelectedArtifact=>(artifacts.SelectedValue as LibraryResultRow)?.Artifact;
    private static string T(string value)=>UiText.Get(value);
    private static Label Info(string value)=>new(){Text=T(value),Wrap=WrapMode.Word};
    private RhinoDoc Document()=>RhinoDoc.FromRuntimeSerialNumber(serial)??throw new InvalidOperationException(T("The target document has closed."));
    private string Scope=>AholoPlugin.DocumentScope(serial);
    public AholoPanel(uint documentSerialNumber)
    {
        serial=documentSerialNumber;
        var document=RhinoDoc.FromRuntimeSerialNumber(serial);
        workspace=document is not null&&!document.IsClosing?PanelNavigation.Workspaces.Open(serial):new PanelWorkspace();
        workspaceLease=PanelNavigation.Workspaces.Claim(serial,workspace,this);
        if(document is not null){double unitScale=global::Rhino.RhinoMath.UnitScale(global::Rhino.UnitSystem.Meters,document.ModelUnitSystem);if(double.IsFinite(unitScale)&&unitScale>0)objectHeight.Text=unitScale.ToString("R",CultureInfo.InvariantCulture);}
        InitializePlacement();
        creation.DataStore=ServiceOperations.All.Select(x=>T(x.Title)).ToArray();creation.SelectedIndex=ServiceOperations.All.ToList().FindIndex(x=>x.Id==workspace.Operation);
        creation.SelectedIndexChanged+=(_,_)=>{WorkspaceUserNavigated();ChangeForm();};ChangeForm();
        submit=Action("Create · Uses service credits",CreateAsync);
        render=Action("Upload selection and render · Uses service credits",RenderAsync);
        refresh=Action("Refresh selected task",RefreshAsync);
        download=Action("Download selected result",DownloadAsync);
        place=Action("Place in current document",PlaceAsync);
        previewResult=Action("Preview mesh candidate",PreviewSelectedAsync);
        continueRender=Action("Continue original render · Uses service credits",ContinueAsync);
        var createPage=Column(creation,formHost,autoPlace,submit,Info("Each click creates one paid task. Follow its progress in Library."));
        tasks.SelectedIndexChanged+=(_,_)=>{if(!reloadingHistory){WorkspaceUserNavigated();Describe();}};
        artifacts.SelectedIndexChanged+=(_,_)=>
        {
            if(rebindingArtifacts)return;
            if(Selected is {} selected&&displayedResultTask is {} displayed&&TaskIdentity.From(selected)==TaskIdentity.From(displayed)&&selected.ServiceOperation==displayed.ServiceOperation)
                resultSelection.Remember(selected,resultRows,artifacts.SelectedIndex);
            UpdateActions();
        };
        filter.SelectedIndexChanged+=(_,_)=>HistoryQueryChanged();search.TextChanged+=(_,_)=>HistoryQueryChanged();
        previous.Click+=(_,_)=>{WorkspaceUserNavigated();offset=Math.Max(0,offset-50);_=ReloadAsync();};next.Click+=(_,_)=>{WorkspaceUserNavigated();offset+=50;_=ReloadAsync();};
        openImage=Local("Open image",OpenImage);openFolder=Local("Open result folder",OpenResultFolder);
        var worldId=new TextBox{PlaceholderText=T("Existing Aholo World ID")};
        meshSection=Section("Placement and mesh candidates",meshEditor.View);meshSection.Expanded=true;
        var localFiles=Section("Local file exchange",
            Row(Action("Import USD scene",ImportSceneAsync),Action("Export selected scene USD",ExportSceneAsync)),
            Action("Export selected mesh USD",ExportMeshAsync),meshExportTextures,
            Row(Action("Import GS",ImportGsAsync),Action("Export GS",ExportGsAsync)),exportScope);
        var taskTools=Section("Task tools and recovery",Row(Local("Pause local polling",Pause),Local("Resume polling",Resume)),
            Action("Cloud history and recovery",async()=>{using var dialog=new CloudLibraryDialog(session,Scope,token);var restored=dialog.ShowModal(this);if(restored is not null)await RevealAsync(restored);}),
            worldId,Action("Restore World ID",async()=>{var task=await session.Client.ImportWorldAsync(worldId.Text.Trim(),token);session.Observe(task);await RevealAsync(task);}));
        var gsTools=Section("GS display and diagnostics",Row(Local("GS viewport",()=>GaussianViewport.Activate(Document())),Local("Diagnostics",()=>Report(GaussianViewport.Diagnostics(Document())))));
        var libraryPage=Column(search,filter,tasks,Row(previous,pageInfo,next),
            Row(Local("Load history",()=>_=ReloadAsync()),refresh),Action("Load local mesh",LoadLocalMeshAsync),detail,artifacts,artifactDetail,
            Row(download,openFolder),openImage,place,previewResult,meshSection,continueRender,localFiles,taskTools,gsTools);
        var lightFields=Column(Info("Point-light radius before block scaling (mm)"),lightRadius,Info("Relative light intensity multiplier"),lightMultiplier,Info("Point lights use the chosen sphere radius. Light intensity is relative, not calibrated to Rhino lumens. Unsupported selected lights fail before upload."));
        lightFields.Visible=false;renderLights.CheckedChanged+=(_,_)=>lightFields.Visible=renderLights.Checked==true;
        var renderPage=Column(Info("Select meshes, surfaces, extrusions or mesh blocks and use a perspective view. Geometry, supported material constants, optional selected lights and camera will be uploaded."),resolution,renderTextures,renderLights,lightFields,render,
            Info("Offline rendering preserves smooth normals, supported PBR constants and direct mesh UV bitmaps. Complex texture mapping, material graphs, glass transmission and GS ingestion are not converted. Disable bitmap export to use material constants only."),
            Local("Open cloud preview",()=>CloudPreviewWindow.Open(Document(),Selected)),Info("Cloud preview uses an existing ingested asset. Its JPEG stream does not provide native depth compositing."));
        tabs.Pages.Add(new(){Text=T("Create"),Content=createPage});tabs.Pages.Add(new(){Text=T("Library"),Content=libraryPage});tabs.Pages.Add(new(){Text=T("Render"),Content=renderPage});
        cancel.Click+=(_,_)=>AholoPlugin.CancelDocumentOperations(serial);
        taskElapsed.Elapsed+=(_,_)=>{if(!disposed&&shown&&Selected is{} task&&TaskCapabilities.For(task).IsRunning)UpdateTaskDetail();};
        elapsed.Elapsed+=(_,_)=>{if(!disposed&&busy)operation.Text=workName+" · "+(int)clock.Elapsed.TotalSeconds+" s";};
        Content=new StackLayout{Padding=10,Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={
            Row(new Label{Text="Aholo3D",Font=new Font(SystemFont.Bold,16)},Local("Settings",ShowSettings)),connection,new StackLayoutItem(tabs,true),Row(operation,cancel),feedback}};
        RestoreWorkspace();workspaceReady=true;tabs.SelectedIndexChanged+=(_,_)=>WorkspaceUserNavigated();
        session.Changed+=SessionChanged;session.TaskChanged+=TaskChanged;AholoPlugin.DocumentCancellationRequested+=DocumentCancellationRequested;UpdateActions();
        Report(T("Create assets, organize results and place them in this Rhino document."));
    }
    private static StackLayout Stack(params Control[] controls)
    {var stack=new StackLayout{Padding=6,Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch};foreach(var control in controls)stack.Items.Add(control);return stack;}
    private static Control Column(params Control[] controls)=>new Scrollable{Content=Stack(controls)};
    private static Expander Section(string title,params Control[] controls)=>new(){Header=Info(title),Content=Stack(controls),Expanded=false};
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
        form=selected;formHost.Content=form;autoPlace.Visible=MeshPlacementPolicy.SupportsAutomaticPlacement(id);
        if(workspaceReady&&!restoringWorkspace)PanelNavigation.Workspaces.TryUpdate(serial,workspace,workspaceLease,w=>w.SelectOperation(id));
    }
    private void Report(string text){if(!disposed)feedback.Text=text;}
    private async Task RunAsync(string name,Func<Task> work)
    {
        if(disposed||busy||autoBusy)return;busy=true;operationGeneration++;operationNavigationRevision=workspace.NavigationRevision;workName=T(name);clock.Restart();elapsed.Start();UpdateActions();
        try
        {
            await session.Operations.RunAsync(Scope+"foreground",name,async ct=>{token=ct;try{await work();}finally{token=default;}},lifetime.Token);
        }
        catch(OperationCanceledException) when(name is "Export selected mesh USD" or "Export selected scene USD" or "Export GS"){AholoHost.Ui(()=>Report(T("Local export cancelled. The previous output was preserved.")));}
        catch(OperationCanceledException) when(name=="Import USD scene"){AholoHost.Ui(()=>Report(T("Local scene import cancelled before completion.")));}
        catch(Exception ex){AholoHost.Ui(()=>Report(AholoHost.FriendlyError(ex)));}
        finally
        {
            AholoHost.Ui(()=>{busy=false;clock.Stop();if(disposed){lifetime.Dispose();return;}elapsed.Stop();operation.Text=T("Ready");UpdateActions();});
        }
    }
    private void UpdateActions()
    {
        if(disposed)return;
        RefreshResultLabels();
        bool connected=session.Connection.Connected;
        bool working=busy||autoBusy;meshEditor.SetBusy(working);
        connection.ToolTip=UiText.TranslateKnownMessage(session.LastError??"");
        connection.Text=ConnectionLabel();
        foreach(var button in actions)button.Enabled=!working;
        submit.Enabled=render.Enabled=!working&&connected;
        var task=Selected;var capability=task is null?null:TaskCapabilities.For(task);var artifact=SelectedArtifact;
        var access=LibraryResultPolicy.Access(task,resultRows,artifact,session.Connection,File.Exists);
        artifactDetail.Text=access.Status+(artifact?.LocalPath is {Length:>0} path?"\n"+path:"")+(access.NextStep.Length>0?"\n"+access.NextStep:"");
        bool sameAccount=task?.Region==session.Connection.Region&&task?.ConnectionId==session.Connection.ConnectionId;
        refresh.Enabled=!working&&connected&&sameAccount&&capability?.CanQuery==true;
        download.Enabled=!working&&access.CanDownload;place.Enabled=!working&&access.CanPlace;previewResult.Enabled=!working&&access.CanPreview;
        openImage.Enabled=access.CanOpenImage;openFolder.Enabled=access.CanOpenFolder;
        download.ToolTip=place.ToolTip=previewResult.ToolTip=access.NextStep;
        continueRender.Enabled=!working&&connected&&sameAccount&&capability?.CanContinueRender==true;
        continueRender.Visible=task?.Kind=="render"&&capability?.CanContinueRender==true;
        var active=session.Operations.Snapshot().Where(x=>x.State=="Running"&&x.Scope.StartsWith(Scope,StringComparison.Ordinal)).ToArray();
        cancel.Enabled=active.Length>0||placementIntents.Count>0;
        if(!busy)operation.Text=active.Length>0?string.Join(" · ",active.Select(x=>T(x.Name))):placementIntents.Count>0?UiText.Format("Waiting to place {0} generated meshes",placementIntents.Count):T("Ready");
    }
    private readonly LibraryNavigation history=new();
    private bool reloadingHistory,rebindingHistoryQuery;
    private void HistoryQueryChanged()
    {
        if(rebindingHistoryQuery)return;
        WorkspaceUserNavigated();offset=0;_=ReloadAsync();
    }
    private async Task ReloadAsync()
    {
        if(disposed)return;
        var read=history.BeginRead(new(offset,50,filter.SelectedIndex switch{1=>"world",2=>"object",3=>"render",4=>"spatial",_=>null},search.Text.Trim()),Selected?.Id);
        try
        {
            // PageContaining returns the page and its items from one journal snapshot.
            var page=read.RevealTaskId is{} id?await Task.Run(()=>session.Store.PageContaining(id,read.Page.Limit)):await session.ReadHistoryAsync(read.Page);
            AholoHost.Ui(()=>
            {
                if(disposed||!history.TryApply(read,page,out var selected))return;
                reloadingHistory=true;
                try
                {
                    if(read.RevealTaskId is not null)
                    {
                        rebindingHistoryQuery=true;
                        try{filter.SelectedIndex=0;search.Text="";}
                        finally{rebindingHistoryQuery=false;}
                    }
                    offset=page.Offset;tasks.DataStore=page.Items;
                    tasks.SelectedIndex=page.Items.ToList().FindIndex(t=>t.Id==selected);
                    previous.Enabled=offset>0;next.Enabled=page.HasMore;pageInfo.Text=$"{Math.Min(offset+1,page.Total)}–{offset+page.Items.Count} / {page.Total}";
                    Describe();
                }
                finally{reloadingHistory=false;}
            });
        }
        catch(Exception ex){AholoHost.Ui(()=>{if(!disposed&&history.IsCurrent(read))Report(AholoHost.FriendlyError(ex));});}
    }
    private static TaskArtifact[] Outputs(LocalTask task)=>LibraryResultPolicy.Outputs(task);
    private void Describe()
    {
        var task=Selected;
        rebindingArtifacts=true;
        try
        {
            displayedResultTask=task;
            resultRows=task is null?Array.Empty<TaskArtifact>():Outputs(task);
            displayedResultRows=resultRows.Select(a=>new LibraryResultRow(a,LibraryResultPolicy.Cached(a,File.Exists))).ToArray();
            artifacts.DataStore=displayedResultRows;
            artifacts.SelectedIndex=task is null?-1:resultSelection.Choose(task,resultRows,File.Exists);
        }
        finally{rebindingArtifacts=false;}
        UpdateTaskDetail();UpdateActions();
    }
    private void RefreshResultLabels()
    {
        if(rebindingArtifacts)return;
        var rows=resultRows.Select(a=>new LibraryResultRow(a,LibraryResultPolicy.Cached(a,File.Exists))).ToArray();
        if(rows.SequenceEqual(displayedResultRows))return;
        int selected=artifacts.SelectedIndex;rebindingArtifacts=true;
        try{displayedResultRows=rows;artifacts.DataStore=rows;artifacts.SelectedIndex=selected;}
        finally{rebindingArtifacts=false;}
    }
    private void UpdateTaskDetail()
    {
        var task=Selected;if(task is null){detail.Text=T("No task selected.");return;}
        string progress=TaskProgressPresentation.Progress(task);if(progress.Length>0)progress=" · "+progress;
        string age=TaskProgressPresentation.RecordAge(task,DateTimeOffset.UtcNow);if(age.Length>0)age="\n"+age;
        string queried=task.LastQueriedAt.HasValue?task.LastQueriedAt.Value.ToLocalTime().ToString("g"):T("Not queried yet");
        detail.Text=task.Name+"\n"+T(task.Kind)+" · "+UiText.Status(task.Status)+progress+age+"\nID: "+(TaskIdentity.From(task).RemoteId??"—")+"\n"+T("Local task ID")+": "+task.Id+"\n"+T("Last query")+": "+queried+(session.IsPaused(task.Id)?" · "+T("Polling paused"):"")+(task.Error is null?"":"\n"+UiText.TranslateKnownMessage(task.Error));
    }
    private async Task CreateAsync()
    {
        var request=form.Capture() with {Upload=uploadOptions};var intent=CaptureAutomatic(request);var creationToken=token;
        LocalTask? task=null;
        try
        {
            task=await session.Client.CreateServiceAsync(request,creationToken);
            // Keep a late provider result in history, but never restore cancelled insertion authority.
            session.Observe(task);creationToken.ThrowIfCancellationRequested();
            RememberAutomatic(task,intent);await RevealAsync(task);
        }
        finally
        {
            // A precise cancellation may arrive after attachment while Library is still loading.
            if(task is not null&&creationToken.IsCancellationRequested)AholoHost.Ui(()=>ForgetPlacement(task.Id));
        }
    }
    private async Task RevealAsync(LocalTask task)
    {
        Task reload=Task.CompletedTask;
        AholoHost.Ui(()=>
        {
            if(disposed)return;
            // A later explicit navigation wins over this operation's completion.
            if(operationNavigationRevision!=workspace.NavigationRevision){reload=ReloadAsync();return;}
            // Keep the visible page/query intact until the target snapshot is accepted.
            history.Reveal(task.Id);SetPage(WorkspacePage.Library);
            Report(task.Error is null?T("Task recorded. Library follows the original task; no automatic resubmission."):UiText.TranslateKnownMessage(task.Error));
            reload=ReloadAsync();
        });
        await reload;
    }
    private async Task RefreshAsync()
    {var task=Selected??throw new InvalidOperationException(T("Select a task first."));var updated=await session.Client.RefreshAsync(task.Id,token);session.Observe(updated);await ReloadAsync();}
    private async Task ContinueAsync()
    {var task=Selected??throw new InvalidOperationException(T("Select a task first."));var updated=await session.ContinueRenderAsync(task.Id,token);session.Observe(updated);await ReloadAsync();}
    private async Task DownloadAsync()
    {
        var task=Selected??throw new InvalidOperationException(T("Select a task first."));var asset=SelectedArtifact??throw new InvalidOperationException(T("Select a result first."));
        var updated=await session.Client.DownloadAsync(new(task.Id,(task.Kind is "world" or "render")&&(asset.Key is "ply" or "spz" or "image")?asset.Key:"artifact:"+asset.Key),token);session.Observe(updated);
        string? path=Outputs(updated).FirstOrDefault(a=>a.Key==asset.Key)?.LocalPath;AholoHost.Ui(()=>Report(T("Saved locally")+" · "+task.Name+" · "+asset.Label+":\n"+path+"\n"+T("A download saves the file locally; it does not place it in the document.")));await ReloadAsync();
    }
    private void OpenImage()
    {
        var artifact=SelectedArtifact;if(artifact?.Format!="image"||artifact.LocalPath is null||!File.Exists(artifact.LocalPath))throw new InvalidOperationException(T("Download and select an image result first."));
        Process.Start(new ProcessStartInfo(artifact.LocalPath){UseShellExecute=true});
    }
    private void OpenResultFolder()
    {string? path=SelectedArtifact?.LocalPath;string folder=path is not null?Path.GetDirectoryName(path)!:Path.Combine(session.Store.Root,"assets");Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}
    private void Pause(){if(Selected is{} task){session.PauseTask(task.Id);ForgetPlacement(task.Id);Describe();Report(T("Local polling paused. The remote task continues."));}}
    private void Resume(){if(Selected is{} task){session.ResumeQueries(task.Id);Describe();}}
    private IProgress<string> StageProgress()
    {
        long generation=operationGeneration;
        return new Progress<string>(message=>AholoHost.Ui(()=>
        {
            if(!disposed&&busy&&generation==operationGeneration){workName=message;operation.Text=message;}
        }));
    }
    private double Scale()
    {if(!(double.TryParse(scale.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double value)||double.TryParse(scale.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value))||!double.IsFinite(value)||value<=0)throw new ArgumentException(T("Enter an import scale greater than zero, such as 1 or 1000."));return value;}
    private string Axis()=>axis.SelectedIndex==1?"Z":axis.SelectedIndex==2?"Y":"Auto";
    private async Task PlaceAsync()
    {
        var task=Selected??throw new InvalidOperationException(T("Select a task first."));var artifact=SelectedArtifact??throw new InvalidOperationException(T("Select a result first."));
        if(MeshPlacementPolicy.CanPlaceMesh(task,artifact))
        {
            var result=await AholoPlugin.Placement.PlaceTaskAsync(task.Id,artifact.Key,meshEditor.Capture(),token);
            Report(result.Message);return;
        }
        if(artifact.LocalPath is null||!File.Exists(artifact.LocalPath))throw new InvalidOperationException(T("Download the selected result first."));
        var target=AholoHost.CaptureTarget(Document());
        if(artifact.Format=="dsl")
        {string message=await SpatialDocument.PlaceAsync(artifact.LocalPath,artifact.Sha256,target,task.Id,artifact.Key,token);AholoHost.Ui(()=>Report(message));return;}
        if(artifact.Format=="ply")
        {
            var content=await Task.Run(()=>PlyReader.Inspect(artifact.LocalPath,token),token);
            if(content.Kind==PlyContentKind.Gaussian)
            {string up=Axis()=="Auto"?task.UpAxis??"Z":Axis();string message=await GaussianDocument.ImportAsync(artifact.LocalPath,target,Scale(),up,task.Id,token);AholoHost.Ui(()=>Report(message));return;}
            if(content.Kind!=PlyContentKind.Mesh)throw new InvalidDataException(T("This PLY is neither a complete Gaussian scene nor an ordinary mesh with faces."));
        }
        if(artifact.Format=="spz")
        {string message=await GaussianDocument.ImportSceneAsync(artifact.LocalPath,target,Scale(),Axis(),sourceUnits.SelectedIndex==0,token,StageProgress(),taskId:task.Id);AholoHost.Ui(()=>Report(message));return;}
        throw new InvalidOperationException(T("This result format cannot be placed directly."));
    }
    private async Task ImportGsAsync()
    {
        using var dialog=new OpenFileDialog{Title=T("Import GS"),MultiSelect=false};dialog.Filters.Add(new FileFilter("Gaussian scene",".ply",".splat",".spz",".usd",".usda",".usdc",".usdz"));
        if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        var target=AholoHost.CaptureTarget(Document());string message=await GaussianDocument.ImportSceneAsync(dialog.FileName,target,Scale(),Axis(),sourceUnits.SelectedIndex==0,token,StageProgress());AholoHost.Ui(()=>Report(message));
    }
    private async Task ExportGsAsync()
    {
        var snapshot=GaussianDocument.CaptureExport(Document(),selectedOnly:exportScope.SelectedIndex==0);using var dialog=GaussianExportDialogs.CreateSaveDialog();if(dialog.ShowDialog(this)!=DialogResult.Ok)return;
        string message=await GaussianDocument.ExportAsync(GaussianExportDialogs.NormalizePath(dialog),snapshot,token,StageProgress());AholoHost.Ui(()=>Report(message));
    }
    private async Task RenderAsync()
    {
        int width=resolution.SelectedIndex==1?1920:1280,height=resolution.SelectedIndex==1?1080:720;
        double ParseLightValue(string text)
        {if(!double.TryParse(text,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.CurrentCulture,out double value)||!double.IsFinite(value)||value<=0)throw new ArgumentException(T("Light radius and intensity multiplier must be positive numbers."));return value;}
        var snapshot=AholoHost.CaptureRender(Document(),width,height,renderLights.Checked==true,renderLights.Checked==true?ParseLightValue(lightRadius.Text):50,renderLights.Checked==true?ParseLightValue(lightMultiplier.Text):1,renderTextures.Checked==true);string path=Path.Combine(session.Store.Root,"exports",Guid.NewGuid().ToString("N")+".glb");
        await Task.Run(()=>
        {
            var meshes=snapshot.Meshes.ToArray();var loaded=new Dictionary<MeshTextureSource,MeshTexture>();
            foreach(var pair in snapshot.Textures)
            {
                token.ThrowIfCancellationRequested();
                if(!loaded.TryGetValue(pair.Value,out var bitmap)){bitmap=pair.Value.Load(token);loaded.Add(pair.Value,bitmap);}
                meshes[pair.Key]=meshes[pair.Key] with{BaseColorTexture=bitmap};
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);GlbWriter.Write(path,meshes,token);
        },token);
        var task=await session.RenderAsync(new(snapshot.Name,path,snapshot.Camera,width,height,Upload:uploadOptions,Lights:snapshot.Lights),token);await RevealAsync(task);
    }
    private string ConnectionLabel()=>AholoRegions.Label(session.Connection.Region)+" · "+T(session.Connection.Connected?"Connected":"Not connected");
    private void ShowSettings()
    {
        bool closed=false;
        objectHeight.Text=meshEditor.HeightText;
        var validation=new DropDown{DataStore=new[]{"World","Lux3D",T("Spatial understanding")},SelectedIndex=0};
        var key=new PasswordBox();var remember=new CheckBox{Text=T("Save in secure local storage")};
        var auto=new CheckBox{Text=T("Auto-refresh active tasks (every 8 seconds)"),Checked=session.AutoRefresh};auto.CheckedChanged+=(_,_)=>session.AutoRefresh=auto.Checked==true;
        var settingsScale=new TextBox{Text=scale.Text};var settingsAxis=new DropDown{DataStore=axis.DataStore,SelectedIndex=axis.SelectedIndex};var settingsHeight=new TextBox{Text=objectHeight.Text};var settingsUnits=new DropDown{DataStore=sourceUnits.DataStore,SelectedIndex=sourceUnits.SelectedIndex};
        var status=Info(ConnectionLabel());
        string displayedRegion=session.Connection.Region;
        var regions=new DropDown{DataStore=new[]{T("International"),T("China")},SelectedIndex=session.Connection.Region==AholoRegions.China?1:0};
        string SelectedRegion()=>regions.SelectedIndex==1?AholoRegions.China:AholoRegions.International;
        var switchRegion=Local("Apply service region",()=>
        {
            if(busy)return;string selected=SelectedRegion();if(selected==session.Connection.Region)return;key.Text="";
            _=RunAsync("Switching service region",async()=>{await session.SwitchRegionAsync(selected,token);});
        });
        var connectionError=Info(session.LastError??"");
        using var dialog=new Dialog{Title=T("Settings"),ClientSize=new Size(460,620),Resizable=true};
        var connect=Local("Validate and connect",()=>{if(busy){status.Text=T("Wait for the current operation or cancel it before changing credentials.");return;}string selected=SelectedRegion();string value=key.Text;bool save=remember.Checked==true;string service=validation.SelectedIndex switch{1=>"lux",2=>"spatial",_=>"world"};key.Text="";_=RunAsync("Connecting",async()=>{await session.ConnectAsync(new(value,save,service,selected),token);AholoHost.Ui(()=>Report(T("Connected. Service permissions and credits are checked per operation.")));});});
        var disconnect=Local("Disconnect and clear credentials",()=>{if(busy){status.Text=T("Wait for the current operation or cancel it before changing credentials.");return;}_=RunAsync("Disconnecting",async()=>{await session.DisconnectAsync(token);AholoHost.Ui(()=>Report(T("Disconnected and cleared locally saved credentials. Remote tasks are not cancelled.")));});});
        var restore=Local("Restore saved connection",()=>{if(busy)return;_=RunAsync("Restoring connection",async()=>{await session.ReadConnectionAsync(token);});});
        var uploadMetadata=new TextBox{Text=uploadOptions.Metadata??""};var uploadPrefix=new TextBox{Text=uploadOptions.CustomPrefix??""};var uploadFilename=new TextBox{Text=uploadOptions.CustomFilename??""};
        var uploadFields=new StackLayout{Spacing=6,Visible=false,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={Info("Multipart upload options apply to every local input. Leave blank for automatic storage. Small files do not support these fields."),Info("Upload metadata"),uploadMetadata,Info("Storage prefix"),uploadPrefix,Info("Stored filename"),uploadFilename}};
        var uploadAdvanced=new CheckBox{Text=T("Advanced upload settings")};uploadAdvanced.CheckedChanged+=(_,_)=>uploadFields.Visible=uploadAdvanced.Checked==true;
        var usdStatus=Info("USD readiness has not been checked.");
        string setup;
        try{setup=UsdRuntime.SetupCommand(UsdRuntime.SetupScript,OperatingSystem.IsWindows());}
        catch(FileNotFoundException){setup=T("The USD setup script is missing. Reinstall the complete plugin package.");}
        var usdSetup=new TextArea{ReadOnly=true,Height=65,Text=setup};
        var usdCheck=Local("Check USD readiness",()=>{if(busy)return;_=RunAsync("Checking USD runtime",async()=>
        {
            var result=await UsdRuntime.CheckAsync(ct:token);
            AholoHost.Ui(()=>{if(!closed)usdStatus.Text=UiText.TranslateKnownMessage(result.Detail)+"\n"+result.PythonPath;});
        });});
        var settingsCancel=Local("Cancel local operation",()=>AholoPlugin.CancelDocumentOperations(serial));
        settingsCancel.Enabled=placementIntents.Count>0||session.Operations.Snapshot().Any(x=>x.State=="Running"&&x.Scope.StartsWith(Scope,StringComparison.Ordinal));
        var close=new Button{Text=T("Close")};close.Click+=(_,_)=>dialog.Close();
        // Dialog controls are snapshots; closing it preserves placement values in this panel.
        dialog.Content=Column(Info("Service region"),regions,switchRegion,Info("Regions have separate API Keys and account sessions. Switching stops task polling and live previews; remote tasks and saved credentials in the other region are retained."),Local("Account sign-in (optional)",()=>{if(busy)return;if(session.Client is IAccountSessionClient accountClient){using var login=new AccountLoginDialog(accountClient,session.Connection.Region);login.ShowModal(dialog);}}),status,connectionError,Row(restore,settingsCancel),Info("API Key for the selected region"),key,remember,Info("Validate access with"),validation,connect,disconnect,
            Info("Validation queries the selected service history. Each service may need separate account permissions. OAuth login does not replace an API Key."),auto,
            Info("GS import scale (document units per source unit)"),settingsScale,settingsAxis,Info("Source units"),settingsUnits,Info("Mesh height (document units)"),settingsHeight,
            Local("Open local asset folder",()=>Process.Start(new ProcessStartInfo(Path.Combine(session.Store.Root,"assets")){UseShellExecute=true})),
            Info("Closing the panel keeps tasks running. Reopen it to view progress. Cancel stops local waiting only; remote tasks remain on the service."),
            uploadAdvanced,uploadFields,Info("Local USD import and export"),usdStatus,usdCheck,Info("Optional runtime setup command (runs only when you execute it):"),usdSetup,
            Info("Aholo3D for Rhino "+AholoPlugin.DisplayVersion+"\nCuriousArc · qrost"),close);
        void UpdateSettings()=>Application.Instance.AsyncInvoke(()=>{if(!closed){if(displayedRegion!=session.Connection.Region){displayedRegion=session.Connection.Region;regions.SelectedIndex=displayedRegion==AholoRegions.China?1:0;key.Text="";}status.Text=ConnectionLabel();connectionError.Text=UiText.TranslateKnownMessage(session.LastError??"");settingsCancel.Enabled=placementIntents.Count>0||session.Operations.Snapshot().Any(x=>x.State=="Running"&&x.Scope.StartsWith(Scope,StringComparison.Ordinal));}});
        void SettingsDocumentCancelled(uint documentSerial){if(documentSerial==serial)UpdateSettings();}
        session.Changed+=UpdateSettings;AholoPlugin.DocumentCancellationRequested+=SettingsDocumentCancelled;
        try{dialog.ShowModal(this);}finally{closed=true;session.Changed-=UpdateSettings;AholoPlugin.DocumentCancellationRequested-=SettingsDocumentCancelled;}
        static string? Optional(string value)=>string.IsNullOrEmpty(value)?null:value;
        uploadOptions=new(Optional(uploadMetadata.Text),Optional(uploadPrefix.Text),Optional(uploadFilename.Text));
        scale.Text=settingsScale.Text;axis.SelectedIndex=settingsAxis.SelectedIndex;sourceUnits.SelectedIndex=settingsUnits.SelectedIndex;objectHeight.Text=settingsHeight.Text;meshEditor.HeightText=settingsHeight.Text;
    }
    private void SessionChanged()=>Application.Instance.AsyncInvoke(()=>{if(!disposed){PlacementConnectionChanged();UpdateActions();}});
    private void TaskChanged(LocalTask task)=>Application.Instance.AsyncInvoke(()=>{if(!disposed){PlacementTaskChanged(task);_=ReloadAsync();}});
    public void PanelShown(uint documentSerialNumber,ShowPanelReason reason)
    {if(disposed)return;shown=true;WorkspaceShown();ApplyPendingNavigation();meshEditor.SetVisible(true);placementTimer.Start();taskElapsed.Start();session.Start();UpdateActions();_=ReloadAsync();}
    public void PanelHidden(uint documentSerialNumber,ShowPanelReason reason){SaveWorkspace();shown=false;placementTimer.Stop();taskElapsed.Stop();meshEditor.SetVisible(false);}
    public void PanelClosing(uint documentSerialNumber,bool onCloseDocument)
    {if(onCloseDocument)PanelNavigation.Close(serial);else SaveWorkspace();shown=false;RevokePlacements();placementTimer.Stop();taskElapsed.Stop();meshEditor.Clear();if(onCloseDocument){lifetime.Cancel();session.Operations.CancelPrefix(Scope);}}
    protected override void Dispose(bool disposing)
    {
        try
        {
            if(disposing&&!disposed)
            {
                SaveWorkspace();disposed=true;lifetime.Cancel();RevokePlacements();session.Changed-=SessionChanged;session.TaskChanged-=TaskChanged;AholoPlugin.DocumentCancellationRequested-=DocumentCancellationRequested;
                placementTimer.Stop();placementTimer.Dispose();elapsed.Stop();elapsed.Dispose();taskElapsed.Stop();taskElapsed.Dispose();
                try{meshEditor.Dispose();}
                finally{foreach(var draft in drafts.Values)draft.Dispose();drafts.Clear();if(!busy&&!autoBusy)lifetime.Dispose();}
            }
        }
        finally{base.Dispose(disposing);}
    }
}
