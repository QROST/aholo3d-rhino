using System.Globalization;
using Aholo.Core;
using Eto.Drawing;
using Eto.Forms;
namespace Aholo.Rhino;

internal sealed partial class CloudPreviewWindow
{
    private sealed record SceneOptions(RenderModelPlacement Model,double Environment=1,double LightMultiplier=1,bool IncludeLights=false,double PointRadius=50);
    private SceneOptions sceneOptions=new(new());
    private (long Revision,SceneOptions Options)? pendingScene;
    private RenderLight[] baseLights=Array.Empty<RenderLight>();
    private readonly Button sceneControls=new(){Text=T("Scene settings")};
    private long sceneRevision,acceptedScene,acknowledgedScene;
    private bool sceneSending;
    private string? lastStage;
    private static RenderLight[] ScaleLights(RenderLight[] lights,double multiplier)=>lights.Select(l=>l with{Intensity=l.Intensity*multiplier}).ToArray();
    private async Task EditSceneAsync()
    {
        Guid? original=active;bool running=original is not null;
        TextBox Field(double value)=>new(){Text=value.ToString("R",CultureInfo.CurrentCulture)};
        var x=Field(sceneOptions.Model.X);var y=Field(sceneOptions.Model.Y);var z=Field(sceneOptions.Model.Z);
        var scale=Field(sceneOptions.Model.Scale);var rotation=Field(sceneOptions.Model.RotationDegrees);
        var environment=Field(sceneOptions.Environment);var lightMultiplier=Field(sceneOptions.LightMultiplier);var radius=Field(sceneOptions.PointRadius);
        var include=new CheckBox{Text=T("Capture selected rectangular / point lights at start"),Checked=sceneOptions.IncludeLights,Enabled=!running};
        radius.Enabled=!running&&include.Checked==true;include.CheckedChanged+=(_,_)=>radius.Enabled=!running&&include.Checked==true;
        var error=new Label{Wrap=WrapMode.Word};var ok=new Button{Text=running?T("Apply scene settings"):T("Save scene settings")};var cancel=new Button{Text=T("Cancel")};
        using var dialog=new Dialog<SceneOptions?>{Title=T("Scene settings"),ClientSize=new Size(440,610),Resizable=true,DefaultButton=ok,AbortButton=cancel};
        var content=new StackLayout{Padding=12,Spacing=7,HorizontalContentAlignment=HorizontalAlignment.Stretch};
        void Row(string label,Control field){content.Items.Add(new Label{Text=T(label)});content.Items.Add(field);}
        Row("Model X / Y / Z offset (mm)",new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={new StackLayoutItem(x,true),new StackLayoutItem(y,true),new StackLayoutItem(z,true)}});
        Row("Model scale",scale);Row("Model rotation around Z (degrees)",rotation);Row("Environment brightness multiplier",environment);
        content.Items.Add(include);Row("Point-light radius before block scaling (mm)",radius);Row("Captured light intensity multiplier",lightMultiplier);
        content.Items.Add(new Label{Text=running?UiText.Format("Captured lights: {0}. Positions and dimensions stay at the start snapshot; intensity can change.",baseLights.Length):T("Scene settings affect the cloud preview only. The Rhino document is unchanged."),Wrap=WrapMode.Word});
        content.Items.Add(error);content.Items.Add(new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={ok,cancel}});dialog.Content=new Scrollable{Content=content};
        cancel.Click+=(_,_)=>dialog.Close(null);
        ok.Click+=(_,_)=>
        {
            try
            {
                double Read(TextBox field,bool positive=false,bool nonnegative=false)
                {
                    if(!double.TryParse(field.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double value)||!double.IsFinite(value)||positive&&value<=0||nonnegative&&value<0)
                        throw new ArgumentException(T("Enter finite scene values; scale, environment and radius must be positive, and light intensity cannot be negative."));
                    return value;
                }
                var options=new SceneOptions(new(Read(x),Read(y),Read(z),Read(scale,true),Read(rotation)),Read(environment,true),Read(lightMultiplier,nonnegative:true),include.Checked==true,Read(radius,true));
                _=options.Model.Pose(coordinates.SelectedIndex==0);
                dialog.Close(options);
            }
            catch(Exception e){error.Text=AholoHost.FriendlyError(e);}
        };
        var next=dialog.ShowModal(this);
        if(next is null||disposed)return;
        if(active!=original||starting||stopping)throw new InvalidOperationException(T("The preview changed while editing. Open scene settings again."));
        if(original is not{} id){sceneOptions=next;return;}
        _=BoundCamera(id);sceneSending=true;UpdateButtons();
        try
        {
            var request=new RealtimeSceneRequest(id,++sceneRevision,next.Model,next.Environment,ScaleLights(baseLights,next.LightMultiplier));
            var packet=await client.RealtimeAsync("scene",request,lifetime.Token);
            if(disposed||active!=id||stopping)return;_=BoundCamera(id);
            if(packet.Reply.State is { } state&&state.AcceptedScene>=request.Revision){pendingScene=(request.Revision,next);ShowState(packet);}
        }
        catch(RealtimeControlException){throw;}
        catch{await StopAsync();throw;} // An uncertain acknowledgement is never automatically resubmitted.
        finally{sceneSending=false;UpdateButtons();}
    }
}
