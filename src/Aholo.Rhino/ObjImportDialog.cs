using Aholo.Core;
using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

/// <summary>UI choices only. The worker retains ownership of the frozen source and its dependencies.</summary>
internal sealed class ObjImportDialog : Dialog<ObjImportOptions?>
{
    private bool closed;
    private ObjImportDialog(IReadOnlyList<string> entries,UsdSnapshotSettings settings)
    {
        Title=UiText.Get("OBJ import options");ClientSize=new Size(520,380);Resizable=true;
        var models=new ListBox{DataStore=entries.ToArray(),Height=130,SelectedIndex=entries.Count==1?0:-1};
        double documentMeters=global::Rhino.RhinoMath.MetersPerUnit(settings.Units);
        if(!double.IsFinite(documentMeters)||documentMeters<=0)throw new InvalidOperationException(UiText.Get("Cannot convert document units."));
        var choices=new List<(string Label,double Meters)>{(UiText.Format("Document units ({0})",UiText.Get(settings.Units.ToString())),documentMeters)};
        foreach(var item in new[]{("Millimeters",.001),("Centimeters",.01),("Meters",1d),("Inches",.0254),("Feet",.3048)})
            if(!choices.Any(x=>x.Meters==item.Item2))choices.Add((UiText.Get(item.Item1),item.Item2));
        var units=new DropDown{DataStore=choices.Select(x=>x.Label).ToArray(),SelectedIndex=0};
        var axis=new DropDown{DataStore=new[]{UiText.Get("Z up"),UiText.Get("Y up")},SelectedIndex=0};
        var accept=new Button{Text=UiText.Get("Load candidate"),Enabled=models.SelectedIndex>=0};
        var cancel=new Button{Text=UiText.Get("Cancel")};DefaultButton=accept;AbortButton=cancel;
        models.SelectedIndexChanged+=(_,_)=>accept.Enabled=models.SelectedIndex>=0;
        accept.Click+=(_,_)=>
        {
            if(models.SelectedIndex<0||units.SelectedIndex<0||axis.SelectedIndex<0)return;
            Close(new(choices[units.SelectedIndex].Meters,axis.SelectedIndex==0?"Z":"Y",entries[models.SelectedIndex]));
        };
        cancel.Click+=(_,_)=>Close(null);Closed+=(_,_)=>closed=true;
        Content=new Scrollable{Content=new StackLayout{Padding=12,Spacing=8,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={
            new Label{Text=UiText.Get("Choose one model. OBJ files do not define physical units or an up axis."),Wrap=WrapMode.Word},models,
            new Label{Text=UiText.Get("Source length units")},units,new Label{Text=UiText.Get("Source up axis")},axis,
            new Label{Text=UiText.Get("These choices interpret the source model; they do not change your document settings."),Wrap=WrapMode.Word},
            new StackLayout{Orientation=Orientation.Horizontal,Spacing=8,Items={accept,cancel}}}}};
    }
    internal static ObjImportOptions Choose(RhinoDoc doc,IReadOnlyList<string> entries,UsdSnapshotSettings settings,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();using var dialog=new ObjImportDialog(entries,settings);
        // Never synchronously wait for the UI from a cancellation callback.
        using var registration=ct.Register(()=>Application.Instance.AsyncInvoke(()=>{if(!dialog.closed)dialog.Close(null);}));
        ct.ThrowIfCancellationRequested();var result=dialog.ShowModal(RhinoEtoApp.MainWindowForDocument(doc));
        ct.ThrowIfCancellationRequested();return result??throw new LocalOperationCanceledException();
    }
    protected override void Dispose(bool disposing){closed=true;base.Dispose(disposing);}
}
