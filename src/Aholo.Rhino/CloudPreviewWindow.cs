using System.Text.Json;
using Aholo.Core;
using Eto.Drawing;
using Eto.Forms;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal sealed partial class CloudPreviewWindow : Form
{
    private static readonly Dictionary<uint,CloudPreviewWindow> windows=new();
    private readonly uint serial;
    private readonly AholoSession session=AholoPlugin.Session;
    private readonly IRealtimePreviewClient client;
    private readonly Label status=new(){Wrap=WrapMode.Word};
    private readonly Label limits=new(){Wrap=WrapMode.Word,Text=T("Cloud JPEG preview. Images can lag behind navigation; depth, transparency and camera-frame matching are unavailable. Local Rhino geometry is not composited into these images.")};
    private readonly ImageView image=new();
    private readonly ListBox pending=new(){Height=85};
    private readonly Button start=new(){Text=T("Start cloud preview · Uses service credits")},stop=new(){Text=T("Stop cloud preview")};
    private readonly UITimer timer=new(){Interval=.35};
    private readonly CancellationTokenSource lifetime=new();
    private Guid? active;
    private Guid viewport;
    private ImportTarget? target;
    private ConnectionState? originalConnection;
    private long sequence,revision;
    private string? lastQueuedCamera;
    private bool disposed,polling,starting,stopping;
    private Bitmap? bitmap;
    private Task startWork=Task.CompletedTask;
    private const int RenderWidth=1280,RenderHeight=720;
    private static string T(string text)=>UiText.Get(text);
    internal static void Open(RhinoDoc doc,LocalTask? source)
    {
        if(windows.TryGetValue(doc.RuntimeSerialNumber,out var existing)){existing.BringToFront();return;}
        var window=new CloudPreviewWindow(doc,source);windows[doc.RuntimeSerialNumber]=window;window.Show();
    }
    private CloudPreviewWindow(RhinoDoc doc,LocalTask? source)
    {
        serial=doc.RuntimeSerialNumber;client=session.Client as IRealtimePreviewClient??throw new InvalidOperationException(T("Cloud preview control is unavailable."));
        Title=T("Aholo3D cloud preview");ClientSize=new Size(820,690);MinimumSize=new Size(560,620);Owner=global::Rhino.UI.RhinoEtoApp.MainWindowForDocument(doc);
        if(source?.MeshId is not null&&source.Region==session.Connection.Region&&source.ConnectionId==session.Connection.ConnectionId)
            sceneDraft=new(new[]{new RealtimeAssetSlot("Model",source.MeshId,Model:new())},Array.Empty<RealtimeLightSlot>());
        sceneConnection=session.Connection;ShowSceneSummary();
        Button Action(string label,Func<Task> work){var b=new Button{Text=T(label)};b.Click+=async(_,_)=>{try{await work();}catch(Exception error){Report(error);}};return b;}
        start.Click+=async(_,_)=>{try{await (startWork=StartAsync());}catch(Exception error){Report(error);}finally{starting=false;UpdateButtons();}};
        stop.Click+=async(_,_)=>await StopAsync();
        sceneControls.Click+=async(_,_)=>{try{await EditSceneAsync();}catch(Exception error){Report(error);}};
        Content=new StackLayout{Padding=12,Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={
            new Label{Text=T("Use an asset already prepared by RenderCloud. Selecting a prepared render in Library fills its ID; this window does not upload or ingest files."),Wrap=WrapMode.Word},
            sceneSummary,sceneControls,new StackLayout{Orientation=Orientation.Horizontal,Spacing=8,Items={start,stop}},limits,status,
            new StackLayoutItem(image,true),new Label{Text=T("Unresolved cloud sessions")},pending,
            new StackLayout{Spacing=5,Items={Action("Refresh cleanup list",PendingAsync),Action("Retry selected cleanup",CleanupSelectedAsync),Action("Confirm external resolution",ConfirmExternalAsync)}}}};
        timer.Elapsed+=async(_,_)=>await PollAsync();
        Shown+=async(_,_)=>{timer.Start();await PendingAsync();UpdateButtons();};
        Closed+=(_,_)=>
        {
            disposed=true;windows.Remove(serial);lifetime.Cancel();timer.Stop();timer.Dispose();image.Image=null;bitmap?.Dispose();bitmap=null;
            global::Rhino.RhinoDoc.CloseDocument-=DocumentClosed;session.Changed-=ConnectionChanged;_=CloseAfterStartAsync();
        };
        global::Rhino.RhinoDoc.CloseDocument+=DocumentClosed;session.Changed+=ConnectionChanged;
        UpdateButtons();
    }
    private void ConnectionChanged()=>Application.Instance.AsyncInvoke(()=>{if(!disposed){ShowSceneSummary();UpdateButtons();if(active is not null&&session.Connection!=originalConnection)_=StopAsync();}});
    private void DocumentClosed(object? sender,global::Rhino.DocumentEventArgs e){if(e.Document.RuntimeSerialNumber==serial&&!disposed)Close();}
    private RhinoDoc Document()=>RhinoDoc.FromRuntimeSerialNumber(serial)??throw new InvalidOperationException(T("The target document has closed."));
    private void UpdateButtons(){if(disposed)return;start.Enabled=!starting&&!stopping&&!sceneEditing&&active is null&&sceneDraft.Assets.Length>0&&sceneConnection==session.Connection&&session.Connection.Connected;stop.Enabled=active is not null&&!stopping;sceneControls.Enabled=!starting&&!stopping&&!sceneSending&&!sceneEditing&&(active is null||lastStage=="Streaming"&&acknowledgedScene>=acceptedScene);}
    private void Report(Exception error)
    {if(!disposed)status.Text=AholoHost.FriendlyError(error);else global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error));}
    private async Task CloseAfterStartAsync()
    {
        try{await startWork;}catch{}
        await StopAsync();
    }
    private void ClearImage(){var old=bitmap;image.Image=null;bitmap=null;old?.Dispose();}
    private RenderCamera BoundCamera(Guid id)
    {
        if(active!=id||stopping)throw new OperationCanceledException();
        var doc=Document();var current=AholoHost.CaptureTarget(doc);
        if(current!=target||doc.Views.ActiveView?.ActiveViewport.Id!=viewport||session.Connection.Connected!=true||session.Connection.ConnectionId!=originalConnection?.ConnectionId||session.Connection.CredentialEpoch!=originalConnection?.CredentialEpoch)
            throw new InvalidOperationException(T("The original preview document, viewport or connection changed. Stop this preview before restarting."));
        return AholoHost.CaptureCamera(doc,RenderWidth,RenderHeight);
    }
    private async Task StartAsync()
    {
        if(starting||active is not null)return;
        if(sceneConnection!=session.Connection)throw new InvalidOperationException(T("Account changed. Review the cloud asset IDs in the scene editor before starting."));
        var doc=Document();target=AholoHost.CaptureTarget(doc);viewport=doc.Views.ActiveView!.ActiveViewport.Id;
        var camera=AholoHost.CaptureCamera(doc,RenderWidth,RenderHeight);originalConnection=session.Connection;
        var scene=RealtimeLayout.Snapshot(sceneDraft);
        if(scene.Assets.Length==0)throw new InvalidOperationException(T("Add a prepared cloud asset before starting a preview."));
        var request=new RealtimeStartRequest(Guid.NewGuid(),scene.Assets[0].MeshId,camera,RenderWidth,RenderHeight,Scene:scene);
        _=RealtimeScene.Build(request);ClearImage();starting=true;active=request.Id;sequence=revision=sceneRevision=acceptedScene=acknowledgedScene=0;lastStage=null;pendingScene=null;lastQueuedCamera=JsonSerializer.Serialize(camera,AholoJson.Options);UpdateButtons();
        try{var packet=await client.RealtimeAsync("start",request,lifetime.Token);if(!disposed)ShowState(packet);else await StopAsync();}
        catch(RealtimeControlException){active=null;throw;}
        catch{if(!disposed)status.Text=T("Start outcome is uncertain. Inspect this preview and the cleanup list before starting another.");throw;}
    }
    private async Task PollAsync()
    {
        if(disposed||polling||starting||stopping||active is not{} id)return;
        polling=true;
        try
        {
            _=BoundCamera(id);
            var packet=await client.RealtimeAsync("get",new{id,afterFrame=sequence},lifetime.Token);
            if(disposed||active!=id||stopping)return;_=BoundCamera(id);ShowState(packet);
            if(packet.Reply.State?.Record.Stage is "Closed" or "NotSubmitted" or "ExternallyResolved" or "CleanupRequired" or "SubmissionUnknown")
            {active=null;UpdateButtons();await PendingAsync();return;}
            var camera=BoundCamera(id);string serialized=JsonSerializer.Serialize(camera,AholoJson.Options);
            if(serialized!=lastQueuedCamera)
            {
                await client.RealtimeAsync("camera",new RealtimeCameraRequest(id,++revision,camera),lifetime.Token);
                // This is only a queue watermark. AcknowledgedCamera remains the server-confirmed watermark.
                if(!disposed&&active==id&&!stopping)lastQueuedCamera=serialized;
            }
        }
        catch(Exception error){if(!disposed&&active==id){Report(error);await StopAsync();}}
        finally{polling=false;}
    }
    private static bool IsTerminalStage(string? stage)=>stage is "Closed" or "NotSubmitted" or "ExternallyResolved" or "CleanupRequired" or "SubmissionUnknown";
    private void ShowState(RealtimePreviewPacket packet)
    {
        if(disposed||packet.Reply.State is not{} state||state.Record.Id!=active)return;
        if(IsTerminalStage(lastStage)&&!IsTerminalStage(state.Record.Stage))return;
        lastStage=state.Record.Stage;acceptedScene=Math.Max(acceptedScene,state.AcceptedScene);acknowledgedScene=Math.Max(acknowledgedScene,state.AcknowledgedScene);
        if(pendingScene is { } change&&acknowledgedScene>=change.Revision){sceneDraft=RealtimeLayout.Snapshot(change.Scene);pendingScene=null;ShowSceneSummary();}
        if(lastStage is "Closed" or "NotSubmitted" or "ExternallyResolved" or "CleanupRequired" or "SubmissionUnknown")pendingScene=null;
        UpdateButtons();
        status.Text=UiText.Status(state.Record.Stage)+" · "+UiText.Format("Local preview {0}",state.Record.Id)+
            (state.Record.SessionId is null?"":"\n"+UiText.Format("Cloud session {0}",state.Record.SessionId))+
            (state.ReceivedAt is null?"":"\n"+UiText.Format("Latest image received {0}; camera updates acknowledged {1} (not a frame match)",state.ReceivedAt.Value.ToLocalTime().ToString("T"),state.AcknowledgedCamera))+
            (acceptedScene==0?"":"\n"+UiText.Format("Scene adjustments acknowledged {0} / {1}; image arrival is not an acknowledgement.",acknowledgedScene,acceptedScene))+
            (state.Record.Error is null?"":"\n"+UiText.TranslateKnownMessage(state.Record.Error));
        if(packet.Jpeg is null)return;
        try
        {
            JpegFrameSize.Validate(packet.Jpeg,4500L*4500); // Image-decoding capacity, unrelated to geometry counts.
            using var input=new MemoryStream(packet.Jpeg,false);var next=new Bitmap(input);var previous=bitmap;
            try{image.Image=next;}
            catch{next.Dispose();try{image.Image=previous;}catch{}throw;}
            bitmap=next;sequence=state.FrameSequence;previous?.Dispose();
        }
        catch(Exception error){sequence=state.FrameSequence;Report(error);} // Keep the last decoded image.
    }
    private async Task StopAsync()
    {
        if(stopping||active is not{} id)return;stopping=true;if(!disposed)ClearImage();UpdateButtons();
        try
        {
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var packet=await client.RealtimeAsync("stop",new{id},timeout.Token);
            if(!disposed)ShowState(packet);
            if(packet.Reply.State?.Record.NeedsCleanup==false)active=null;
            else if(!disposed)status.Text=T("Server cleanup is pending. Retry the original session from the cleanup list.");
        }
        catch(Exception error){Report(error);}
        finally{stopping=false;UpdateButtons();if(!disposed)await PendingAsync();}
    }
    private async Task PendingAsync()
    {
        try
        {
            var packet=await client.RealtimeAsync("pending",new{},lifetime.Token);
            if(!disposed)pending.DataStore=(packet.Reply.Records??Array.Empty<RealtimePreviewRecord>()).Select(r=>new PendingRow(r)).ToArray();
        }
        catch(Exception error){if(!disposed)Report(error);}
    }
    private async Task CleanupSelectedAsync()
    {
        if(pending.SelectedValue is not PendingRow row)return;
        var result=await client.RealtimeAsync("stop",new{id=row.Record.Id},lifetime.Token);
        if(result.Reply.State?.Record.NeedsCleanup==false&&active==row.Record.Id)active=null;UpdateButtons();await PendingAsync();
    }
    private async Task ConfirmExternalAsync()
    {
        if(pending.SelectedValue is not PendingRow row)return;
        if(MessageBox.Show(this,T("Confirm only after checking the official platform and ending the unknown session. This records your confirmation; it cannot verify server cleanup."),T("Confirm external resolution"),MessageBoxButtons.YesNo,MessageBoxType.Question)!=DialogResult.Yes)return;
        await client.RealtimeAsync("confirmExternalResolution",new{id=row.Record.Id},lifetime.Token);
        if(active==row.Record.Id)active=null;UpdateButtons();await PendingAsync();
    }
    private sealed record PendingRow(RealtimePreviewRecord Record)
    {public override string ToString()=>UiText.Status(Record.Stage)+" · "+Record.Id;}
}
