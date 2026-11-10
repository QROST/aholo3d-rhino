using System.Globalization;
using Aholo.Core;
using Eto.Forms;
using Rhino.Geometry;
using Rhino.Input.Custom;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal sealed class MeshPlacementEditor : IDisposable
{
    private sealed record Candidate(string Name,PreparedAsset Asset,MeshPlacementTarget Target,Guid ImportId,Guid? TaskId);
    private readonly uint serial;
    private readonly MeshPlacementService placement;
    private readonly Action<string> report;
    private readonly CandidatePreview preview=new();
    private readonly List<Candidate> candidates=new();
    private readonly DropDown selected=new();
    private readonly TextBox height=new(),rotation=new(){Text="0"};
    private readonly Label position=new(){Wrap=WrapMode.Word};
    private readonly UITimer timer=new(){Interval=.08};
    private Plane plane;
    private Point3d point;
    private bool disposed,visible=true;
    private long revision;
    internal Control View{get;}
    internal string HeightText{get=>height.Text;set=>height.Text=value;}
    internal MeshPlacementEditor(uint serial,MeshPlacementService placement,Action<string> report,Func<string,Func<Task>,Task> run)
    {
        this.serial=serial;this.placement=placement;this.report=report;
        var doc=Document();var initial=MeshPlacementService.DefaultPlacement(doc);plane=initial.Plane;point=initial.Point;height.Text=initial.Height.ToString("R",CultureInfo.InvariantCulture);
        Button Button(string title,Action work)
        {
            var button=new Button{Text=UiText.Get(title)};
            button.Click+=(_,_)=>_=run(title,()=>{work();return Task.CompletedTask;});return button;
        }
        selected.SelectedIndexChanged+=(_,_)=>Schedule();height.TextChanged+=(_,_)=>Schedule();rotation.TextChanged+=(_,_)=>Schedule();
        timer.Elapsed+=(_,_)=>ShowPreview();
        View=new StackLayout{Spacing=6,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={
            new Label{Text=UiText.Get("Mesh placement and candidates")},
            new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={new Label{Text=UiText.Get("Height (document units)")},height,new Label{Text=UiText.Get("Rotation (degrees)")},rotation}},
            new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={Button("Pick placement point",Pick),Button("Use current construction plane",UsePlane)}},position,
            selected,new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={Button("Accept candidate",Accept),Button("Remove candidate",Remove),Button("Clear candidates",Clear)}}}};
        Describe();
    }
    private RhinoDoc Document()=>RhinoDoc.FromRuntimeSerialNumber(serial)??throw new InvalidOperationException(UiText.Get("The target document has closed."));
    internal PlacementIntent Intent()
    {
        static double Value(string text)
        {
            if(!(double.TryParse(text,NumberStyles.Float,CultureInfo.CurrentCulture,out double value)||double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value))||!double.IsFinite(value))
                throw new ArgumentException(UiText.Get("Enter a valid height and rotation."));
            return value;
        }
        double size=Value(height.Text);if(size<=0)throw new ArgumentException(UiText.Get("Enter a positive model height in document units."));
        return new(plane,point,size,Math.IEEERemainder(Value(rotation.Text),360));
    }
    internal MeshPlacementTarget Capture()=>placement.Capture(Document(),Intent());
    internal void SetBusy(bool busy){if(!disposed)View.Enabled=!busy;}
    internal void SetVisible(bool shown)
    {
        if(disposed)return;visible=shown;if(shown)Schedule();else{timer.Stop();preview.Clear();}
    }
    internal async Task AddAsync(string path,string? hash,string name,Guid? taskId,CancellationToken ct)
    {
        if(candidates.Count>=3)throw new InvalidOperationException(UiText.Get("Up to 3 candidates per group. Remove an unwanted candidate first."));
        long expected=revision;var target=Capture();PreparedAsset asset=await placement.PrepareAsync(path,hash,target,ct);
        if(disposed||expected!=revision||ct.IsCancellationRequested){asset.Dispose();ct.ThrowIfCancellationRequested();return;}
        candidates.Add(new(name,asset,target,Guid.NewGuid(),taskId));Select(candidates.Count-1);
        report(UiText.Get("Candidate loaded. Adjust placement, compare candidates, then accept. The document is unchanged until acceptance."));
    }
    private void Select(int index){selected.DataStore=candidates.Select((c,i)=>$"{(char)('A'+i)} · {c.Name}").ToArray();selected.SelectedIndex=index;Schedule();}
    private void Pick()
    {
        var doc=Document();_=AholoHost.CaptureTarget(doc);
        using var input=new GetPoint();input.SetCommandPrompt(UiText.Get("Choose Aholo3D placement point"));
        if(input.Get()!=global::Rhino.Input.GetResult.Point)return;
        _=AholoHost.CaptureTarget(doc);point=input.Point();plane=doc.Views.ActiveView?.ActiveViewport.ConstructionPlane()??Plane.WorldXY;Describe();Schedule();
    }
    private void UsePlane(){var doc=Document();_=AholoHost.CaptureTarget(doc);plane=doc.Views.ActiveView?.ActiveViewport.ConstructionPlane()??Plane.WorldXY;point=plane.Origin;Describe();Schedule();}
    private void Describe()=>position.Text=UiText.Format("Position ({0:G5}, {1:G5}, {2:G5}) on the captured construction plane",point.X,point.Y,point.Z);
    private void Schedule(){if(disposed||!visible)return;timer.Stop();timer.Start();}
    private void ShowPreview()
    {
        timer.Stop();if(disposed||!visible)return;
        int index=selected.SelectedIndex;
        if(index<0||index>=candidates.Count){preview.Clear();return;}
        try{placement.Validate(candidates[index].Target);preview.Show(candidates[index].Asset,Intent());}
        catch(Exception error){preview.Clear();report(AholoHost.FriendlyError(error));}
    }
    private void Accept()
    {
        int index=selected.SelectedIndex;if(index<0||index>=candidates.Count)throw new InvalidOperationException(UiText.Get("Load and select a candidate first."));
        var candidate=candidates[index];var result=placement.Commit(candidate.Asset,candidate.Target with{Placement=Intent()},candidate.ImportId,candidate.TaskId);
        try{Clear();}catch{report(result.Message+"\n"+UiText.Get("Preview cleanup did not finish. The imported block is retained."));return;}
        report(result.Message);
    }
    private void Remove()
    {
        int index=selected.SelectedIndex;if(index<0||index>=candidates.Count)return;
        timer.Stop();preview.Clear();var asset=candidates[index].Asset;candidates.RemoveAt(index);
        try{asset.Dispose();}finally{Select(Math.Min(index,candidates.Count-1));}
    }
    internal void Clear()
    {
        revision++;timer.Stop();Exception? failure=null;try{preview.Clear();}catch(Exception error){failure=error;}
        foreach(var candidate in candidates)try{candidate.Asset.Dispose();}catch(Exception error){failure??=error;}
        candidates.Clear();selected.DataStore=Array.Empty<string>();selected.SelectedIndex=-1;
        if(failure is not null)throw new InvalidOperationException(UiText.Get("Preview cleanup did not finish. The imported block is retained."),failure);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        try{Clear();}catch(Exception error){global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error));}
        finally{timer.Dispose();try{preview.Dispose();}catch(Exception error){global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error));}}
    }
}
