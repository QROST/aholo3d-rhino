using System.Globalization;
using Aholo3D.Core;
using Eto.Drawing;
using Eto.Forms;
namespace Aholo3D.Rhino;

/// <summary>A private draft; closing the editor never changes a cloud session or the Rhino document.</summary>
internal sealed class RealtimeSceneEditor:Dialog<RealtimeSceneLayout?>
{
    private readonly List<RealtimeAssetSlot> assets;
    private readonly List<RealtimeLightSlot> lights;
    private readonly HashSet<string> existingAssets,existingLights;
    private readonly ListBox assetList=new(){Height=135},lightList=new(){Height=135};
    private readonly TextBox environment,radius=Field(50);
    private readonly Label error=new(){Wrap=WrapMode.Word};
    private readonly bool running;
    private static string T(string text)=>UiText.Get(text);
    private static TextBox Field(double value)=>new(){Text=value.ToString("R",CultureInfo.CurrentCulture)};
    private static double Number(TextBox field,bool positive=false,bool nonnegative=false)
    {
        if(!double.TryParse(field.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out double value)||!double.IsFinite(value)||positive&&value<=0||nonnegative&&value<0)
            throw new ArgumentException(T("Enter finite scene values; dimensions and scale must be positive, and color and intensity cannot be negative."));
        return value;
    }
    private static StackLayout Column(params Control[] controls)
    {var layout=new StackLayout{Spacing=6,HorizontalContentAlignment=HorizontalAlignment.Stretch};foreach(var control in controls)layout.Items.Add(control);return layout;}
    private static StackLayout Row(params Control[] controls)
    {var layout=new StackLayout{Orientation=Orientation.Horizontal,Spacing=6};foreach(var control in controls)layout.Items.Add(new StackLayoutItem(control,true));return layout;}
    private static Label Label(string text)=>new(){Text=T(text),Wrap=WrapMode.Word};
    private Button Action(string text,Action action)
    {var button=new Button{Text=T(text)};button.Click+=(_,_)=>{try{action();error.Text="";}catch(Exception ex){error.Text=Aholo3DHost.FriendlyError(ex);}};return button;}
    internal RealtimeSceneEditor(RealtimeSceneLayout source,bool running,Func<double,RenderLight[]> capture)
    {
        source=RealtimeLayout.Snapshot(source);this.running=running;assets=source.Assets.ToList();lights=source.Lights.ToList();
        existingAssets=running?assets.Select(a=>a.Id).ToHashSet(StringComparer.Ordinal):new();existingLights=running?lights.Select(l=>l.Id).ToHashSet(StringComparer.Ordinal):new();
        environment=Field(source.EnvironmentIntensity);Title=T("Cloud scene editor");ClientSize=new Size(590,680);MinimumSize=new Size(450,540);Resizable=true;
        var addAsset=Action("Add cloud asset",()=>{if(EditAsset(null) is {} asset){assets.Add(asset);Refresh();}});
        var editAsset=Action("Edit selected asset",()=>{if(assetList.SelectedValue is AssetRow row&&EditAsset(row.Slot) is{} asset){assets[assets.FindIndex(a=>a.Id==asset.Id)]=asset;Refresh();}});
        var removeAsset=Action("Remove selected asset",()=>{if(assetList.SelectedValue is AssetRow row){assets.RemoveAll(a=>a.Id==row.Slot.Id);Refresh();}});
        var addLight=Action("Add light",()=>{if(EditLight(null) is{} light){lights.Add(light);Refresh();}});
        var editLight=Action("Edit selected light",()=>{if(lightList.SelectedValue is LightRow row&&EditLight(row.Slot) is{} light){lights[lights.FindIndex(l=>l.Id==light.Id)]=light;Refresh();}});
        var removeLight=Action("Remove selected light",()=>{if(lightList.SelectedValue is LightRow row){lights.RemoveAll(l=>l.Id==row.Slot.Id);Refresh();}});
        var captureLights=Action("Add selected Rhino lights",()=>
        {
            var selected=capture(Number(radius,true));if(selected.Length==0)throw new InvalidOperationException(T("Select rectangular or point lights in Rhino before opening this editor."));
            var additions=selected.Select(light=>new RealtimeLightSlot("Light_"+Guid.NewGuid().ToString("N"),light)).ToArray();
            _=RealtimeLayout.Snapshot(new(assets.ToArray(),lights.Concat(additions).ToArray(),Number(environment,true)));
            lights.AddRange(additions);Refresh();
        });
        var apply=new Button{Text=running?T("Apply to cloud preview"):T("Save scene")};var cancel=new Button{Text=T("Cancel")};DefaultButton=apply;AbortButton=cancel;
        apply.Click+=(_,_)=>
        {
            try
            {
                var next=RealtimeLayout.Snapshot(new(assets.ToArray(),lights.ToArray(),Number(environment,true)));
                if(!running&&next.Assets.Length==0)throw new InvalidOperationException(T("Add a prepared cloud asset before starting a preview."));
                Close(next);
            }
            catch(Exception ex){error.Text=Aholo3DHost.FriendlyError(ex);}
        };
        cancel.Click+=(_,_)=>Close(null);
        var body=Column(Label("Cloud assets"),assetList,Row(addAsset,editAsset,removeAsset),Label("Lights"),lightList,Row(addLight,editLight,removeLight),
            Label("Point-light radius for Rhino capture (mm)"),Row(radius,captureLights),Label("Environment brightness multiplier"),environment,
            Label(running?"Changes apply only after you confirm. Cloud updates can affect several objects; failure does not guarantee an atomic rollback.":"Use already-ingested cloud asset IDs. This editor does not upload files or modify the Rhino document."),error,Row(apply,cancel));
        body.Padding=12;Content=new Scrollable{Content=body};Refresh();
        assetList.SelectedIndexChanged+=(_,_)=>{editAsset.Enabled=removeAsset.Enabled=assetList.SelectedValue is AssetRow;};
        lightList.SelectedIndexChanged+=(_,_)=>{editLight.Enabled=removeLight.Enabled=lightList.SelectedValue is LightRow;};
        editAsset.Enabled=removeAsset.Enabled=editLight.Enabled=removeLight.Enabled=false;
    }
    private void Refresh()
    {assetList.DataStore=assets.Select(a=>new AssetRow(a)).ToArray();lightList.DataStore=lights.Select(l=>new LightRow(l)).ToArray();}
    private RealtimeAssetSlot? EditAsset(RealtimeAssetSlot? original)
    {
        var seed=original??new("Model_"+Guid.NewGuid().ToString("N"),"",Model:new());var model=seed.Model??new();
        var id=new TextBox{Text=seed.MeshId,PlaceholderText=T("Existing ingested cloud asset ID")};
        var kind=new DropDown{DataStore=new[]{T("Cloud mesh"),T("Cloud Gaussian asset")},SelectedIndex=seed.Gaussian?1:0};
        var units=new DropDown{DataStore=new[]{T("Imported glTF · metres / Y-up"),T("Cloud native · millimetres / Z-up")},SelectedIndex=seed.GltfCoordinates?0:1};
        var x=Field(model.X);var y=Field(model.Y);var z=Field(model.Z);var scale=Field(model.Scale);var rotation=Field(model.RotationDegrees);var rotationX=Field(model.RotationXDegrees);var rotationY=Field(model.RotationYDegrees);
        var axisX=Field(model.AxisScaleX);var axisY=Field(model.AxisScaleY);var axisZ=Field(model.AxisScaleZ);
        var shearXY=Field(model.ShearXY);var shearXZ=Field(model.ShearXZ);var shearYZ=Field(model.ShearYZ);
        var advanced=new Expander{Header=Label("Advanced model shape"),Expanded=model.AxisScaleX!=1||model.AxisScaleY!=1||model.AxisScaleZ!=1||model.ShearXY!=0||model.ShearXZ!=0||model.ShearYZ!=0,
            Content=Column(Label("Axis scale X / Y / Z (negative mirrors, zero is invalid)"),Row(axisX,axisY,axisZ),Label("Shear: X from Y / X from Z / Y from Z"),Row(shearXY,shearXZ,shearYZ),
                Label("Shear acts after scale and before rotation. Mirrored and sheared cloud appearance still needs service validation."))};
        bool fixedIdentity=existingAssets.Contains(seed.Id);id.Enabled=kind.Enabled=!fixedIdentity;
        kind.SelectedIndexChanged+=(_,_)=>{if(kind.SelectedIndex==1)units.SelectedIndex=1;};
        var fields=Column(Label("Prepared cloud asset ID"),id,kind,units,Label("Model X / Y / Z offset (mm)"),Row(x,y,z),Label("Model scale"),scale,Label("Model rotation X / Y / Z (degrees)"),Row(rotationX,rotationY,rotation),
            Label("Rotate around world X, then Y, then Z, about the asset origin before its position offset."),advanced,
            Label(fixedIdentity?"To replace this asset or its type, add a new asset and remove this one.":"The selected coordinate convention is applied once, before the model placement."));
        return Edit(T("Cloud asset"),fields,()=>
        {
            var result=new RealtimeAssetSlot(seed.Id,id.Text.Trim(),kind.SelectedIndex==1,units.SelectedIndex==0,new(Number(x),Number(y),Number(z),Number(scale,true),Number(rotation))
            {RotationXDegrees=Number(rotationX),RotationYDegrees=Number(rotationY),AxisScaleX=Number(axisX),AxisScaleY=Number(axisY),AxisScaleZ=Number(axisZ),ShearXY=Number(shearXY),ShearXZ=Number(shearXZ),ShearYZ=Number(shearYZ)});
            return RealtimeLayout.Snapshot(new(new[]{result},Array.Empty<RealtimeLightSlot>())).Assets[0];
        });
    }
    private RealtimeLightSlot? EditLight(RealtimeLightSlot? original)
    {
        var seed=original??new("Light_"+Guid.NewGuid().ToString("N"),new("Sphere",new[]{1d,0,0,0,0,1,0,0,0,0,1,0,0,0,2000,1},new[]{1d,1d,1d},1,Width:1000,Height:1000,Radius:50));
        var light=seed.Light;var kind=new DropDown{DataStore=new[]{T("Point light (sphere)"),T("Rectangular light")},SelectedIndex=light.Kind=="Rect"?1:0,Enabled=!existingLights.Contains(seed.Id)};
        var x=Field(light.Transform[12]);var y=Field(light.Transform[13]);var z=Field(light.Transform[14]);
        var r=Field(light.Color[0]);var g=Field(light.Color[1]);var b=Field(light.Color[2]);var intensity=Field(light.Intensity);
        var rotationX=Field(0);var rotationY=Field(0);var rotationZ=Field(0);var exposure=Field(light.Exposure);var specular=Field(light.Specular);
        var reflections=new CheckBox{Text=T("Affect reflections"),Checked=light.AffectReflections};
        var doubleSided=new CheckBox{Text=T("Emit from both rectangle sides"),Checked=light.DoubleSided};
        var advanced=new Expander{Header=Label("Advanced light settings"),Expanded=light.Exposure!=0||light.Specular!=1||!light.AffectReflections||light.DoubleSided,
            Content=Column(Label("Exposure (stops; +1 doubles intensity)"),exposure,Label("Specular contribution multiplier"),specular,reflections,doubleSided)};
        var width=Field(light.Width>0?light.Width:1000);var height=Field(light.Height>0?light.Height:1000);var lightRadius=Field(light.Radius>0?light.Radius:50);
        void Dimensions(){width.Enabled=height.Enabled=doubleSided.Enabled=kind.SelectedIndex==1;lightRadius.Enabled=kind.SelectedIndex==0;}
        kind.SelectedIndexChanged+=(_,_)=>Dimensions();Dimensions();
        var fields=Column(kind,Label("Light X / Y / Z position (mm)"),Row(x,y,z),Label("Linear RGB color"),Row(r,g,b),Label("Relative light intensity multiplier"),intensity,
            Label("Rectangle width / height (mm)"),Row(width,height),Label("Sphere radius (mm)"),lightRadius,
            Label("Rotate current light around world X / Y / Z (degrees)"),Row(rotationX,rotationY,rotationZ),
            Label("Rotation is applied X, then Y, then Z without moving the light. Zero keeps its captured orientation; new rectangles face downward along Z."),advanced);
        return Edit(T("Cloud light"),fields,()=>
        {
            var pose=(double[])light.Transform.Clone();pose[12]=Number(x);pose[13]=Number(y);pose[14]=Number(z);
            var changed=new RenderLight(kind.SelectedIndex==1?"Rect":"Sphere",pose,new[]{Number(r,nonnegative:true),Number(g,nonnegative:true),Number(b,nonnegative:true)},Number(intensity,nonnegative:true),
                Width:kind.SelectedIndex==1?Number(width,true):0,Height:kind.SelectedIndex==1?Number(height,true):0,Radius:kind.SelectedIndex==0?Number(lightRadius,true):0)
            {Exposure=Number(exposure),Specular=Number(specular,nonnegative:true),AffectReflections=reflections.Checked==true,DoubleSided=kind.SelectedIndex==1&&doubleSided.Checked==true};
            changed=changed.RotateWorld(Number(rotationX),Number(rotationY),Number(rotationZ));return new RealtimeLightSlot(seed.Id,changed);
        });
    }
    private TResult? Edit<TResult>(string title,Control fields,Func<TResult> read) where TResult:class
    {
        using var dialog=new Dialog<TResult?>{Title=title,ClientSize=new Size(440,580),Resizable=true};
        var message=new Label{Wrap=WrapMode.Word};var ok=new Button{Text=T("Save")};var cancel=new Button{Text=T("Cancel")};
        var body=Column(fields,message,Row(ok,cancel));body.Padding=12;dialog.Content=new Scrollable{Content=body};dialog.DefaultButton=ok;dialog.AbortButton=cancel;
        ok.Click+=(_,_)=>{try{dialog.Close(read());}catch(Exception ex){message.Text=Aholo3DHost.FriendlyError(ex);}};
        cancel.Click+=(_,_)=>dialog.Close(null);return dialog.ShowModal(this);
    }
    private sealed record AssetRow(RealtimeAssetSlot Slot)
    {public override string ToString()=>T(Slot.Gaussian?"Cloud Gaussian asset":"Cloud mesh")+" · "+Slot.MeshId;}
    private sealed record LightRow(RealtimeLightSlot Slot)
    {public override string ToString()=>T(Slot.Light.Kind=="Rect"?"Rectangular light":"Point light (sphere)")+" · "+string.Join(", ",Slot.Light.Transform.Skip(12).Take(3).Select(x=>x.ToString("G5",CultureInfo.CurrentCulture)))+" mm";}
}
