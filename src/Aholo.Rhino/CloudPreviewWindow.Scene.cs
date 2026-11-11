using Aholo.Core;
using Eto.Forms;
namespace Aholo.Rhino;

internal sealed partial class CloudPreviewWindow
{
    private RealtimeSceneLayout sceneDraft=new(Array.Empty<RealtimeAssetSlot>(),Array.Empty<RealtimeLightSlot>());
    private ConnectionState? sceneConnection;
    private (long Revision,RealtimeSceneLayout Scene)? pendingScene;
    private readonly Button sceneControls=new(){Text=T("Edit cloud scene")};
    private readonly Label sceneSummary=new(){Wrap=WrapMode.Word};
    private long sceneRevision,acceptedScene,acknowledgedScene;
    private bool sceneSending,sceneEditing;
    private string? lastStage;
    private void ShowSceneSummary()=>sceneSummary.Text=UiText.Format("Cloud scene: {0} assets · {1} lights",sceneDraft.Assets.Length,sceneDraft.Lights.Length)+
        (active is null&&sceneDraft.Assets.Length>0&&sceneConnection!=session.Connection?"\n"+T("Account changed. Review the cloud asset IDs in the scene editor before starting."):"");
    private async Task EditSceneAsync()
    {
        if(sceneEditing||sceneSending||starting||stopping)return;
        sceneEditing=true;UpdateButtons();Guid? original=active;long basis=acknowledgedScene;var editingConnection=session.Connection;
        try
        {
            if(original is{} running)
            {
                _=BoundCamera(running);
                var packet=await client.RealtimeAsync("get",new{id=running,afterFrame=long.MaxValue,includeScene=true},lifetime.Token);
                if(disposed||active!=original||stopping)return;_=BoundCamera(running);ShowState(packet);
                var state=packet.Reply.State;
                if(state?.Scene is null||state.Record.Stage!="Streaming"||state.AcceptedScene!=state.AcknowledgedScene)
                    throw new InvalidOperationException(T("Wait for the previous scene adjustment to be acknowledged before applying another."));
                sceneDraft=RealtimeLayout.Snapshot(state.Scene);basis=state.AcknowledgedScene;ShowSceneSummary();
            }
            using var dialog=new RealtimeSceneEditor(sceneDraft,original is not null,radius=>
            {
                if(original is{} id)_=BoundCamera(id);
                return AholoHost.CaptureSelectedLights(Document(),radius);
            });
            var next=dialog.ShowModal(this);
            if(next is null||disposed)return;
            if(active!=original||starting||stopping||session.Connection!=editingConnection)throw new InvalidOperationException(T("The preview changed while editing. Open scene settings again."));
            if(original is not{} preview){sceneDraft=next;sceneConnection=editingConnection;ShowSceneSummary();return;}
            if(RealtimeLayout.Fingerprint(next)==RealtimeLayout.Fingerprint(sceneDraft))return;
            _=BoundCamera(preview);sceneSending=true;UpdateButtons();
            long nextRevision=Math.Max(sceneRevision,acceptedScene)+1;sceneRevision=nextRevision;
            var request=new RealtimeCompositionRequest(preview,nextRevision,basis,next);
            var result=await client.RealtimeAsync("composition",request,lifetime.Token);
            if(disposed||active!=preview||stopping)return;_=BoundCamera(preview);
            if(result.Reply.State is{} accepted&&accepted.AcceptedScene==request.Revision)
            {pendingScene=(request.Revision,next);ShowState(result);}
            else throw new InvalidOperationException(T("Scene acknowledgement could not be matched. Inspect the original preview before editing again."));
        }
        catch(RealtimeControlException){throw;}
        catch{if(original is not null)await StopAsync();throw;}
        finally{sceneSending=false;sceneEditing=false;UpdateButtons();}
    }
}
