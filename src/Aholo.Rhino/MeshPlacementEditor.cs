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
    private readonly Func<CancellationToken> currentToken;
    private readonly CandidatePreview preview=new();
    private readonly List<Candidate> candidates=new();
    private readonly DropDown selected=new();
    private readonly DropDown mode=new(){DataStore=new[]{UiText.Get("Fit height on construction plane"),UiText.Get("Original size on construction plane"),UiText.Get("Preserve scene coordinates")},SelectedIndex=0};
    private readonly Button pick,constructionPlane;
    private readonly TextBox height=new(),rotation=new(){Text="0"};
    private readonly Label position=new(){Wrap=WrapMode.Word};
    private readonly UITimer timer=new(){Interval=.08};
    private Plane plane;
    private Point3d point;
    private bool disposed,visible=true;
    private long revision;
    internal long Revision=>revision;
    internal Control View{get;}
    internal string HeightText{get=>height.Text;set=>height.Text=value;}
    internal MeshPlacementEditor(uint serial,MeshPlacementService placement,Action<string> report,Func<string,Func<Task>,Task> run,Func<CancellationToken> currentToken)
    {
        this.serial=serial;this.placement=placement;this.report=report;this.currentToken=currentToken;
        var doc=Document();var initial=MeshPlacementService.DefaultPlacement(doc);plane=initial.Plane;point=initial.Point;height.Text=initial.Height.ToString("R",CultureInfo.InvariantCulture);
        Button Button(string title,Action work)
        {
            var button=new Button{Text=UiText.Get(title)};
            button.Click+=(_,_)=>_=run(title,()=>{work();return Task.CompletedTask;});return button;
        }
        pick=Button("Pick placement point",Pick);constructionPlane=Button("Use current construction plane",UsePlane);
        mode.SelectedIndexChanged+=(_,_)=>{UpdateMode();Schedule();};
        selected.SelectedIndexChanged+=(_,_)=>Schedule();height.TextChanged+=(_,_)=>Schedule();rotation.TextChanged+=(_,_)=>Schedule();
        timer.Elapsed+=(_,_)=>ShowPreview();
        View=new StackLayout{Spacing=6,HorizontalContentAlignment=HorizontalAlignment.Stretch,Items={
            new Label{Text=UiText.Get("Mesh placement and candidates")},mode,
            new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={new Label{Text=UiText.Get("Height (document units)")},height,new Label{Text=UiText.Get("Rotation (degrees)")},rotation}},
            new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={pick,constructionPlane}},position,
            selected,new StackLayout{Orientation=Orientation.Horizontal,Spacing=6,Items={Button("Accept candidate",Accept),Button("Remove candidate",Remove),Button("Clear candidates",Clear)}}}};
        UpdateMode();
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
        var selectedMode=Mode;
        if(selectedMode==MeshPlacementMode.SourceCoordinates)return new(Plane.WorldXY,Point3d.Origin,1,0,selectedMode);
        double size=selectedMode==MeshPlacementMode.FitHeight?Value(height.Text):1;
        if(size<=0)throw new ArgumentException(UiText.Get("Enter a positive model height in document units."));
        return new(plane,point,size,Math.IEEERemainder(Value(rotation.Text),360),selectedMode);
    }
    private MeshPlacementMode Mode=>mode.SelectedIndex switch{1=>MeshPlacementMode.SourceSizeOnPlane,2=>MeshPlacementMode.SourceCoordinates,_=>MeshPlacementMode.FitHeight};
    private void UpdateMode()
    {
        height.Enabled=Mode==MeshPlacementMode.FitHeight;
        rotation.Enabled=pick.Enabled=constructionPlane.Enabled=Mode!=MeshPlacementMode.SourceCoordinates;
        Describe();
    }
    internal MeshPlacementTarget Capture()=>placement.Capture(Document(),Intent());
    internal void SetBusy(bool busy){if(!disposed)View.Enabled=!busy;}
    internal void SetVisible(bool shown)
    {
        if(disposed)return;visible=shown;if(shown)Schedule();else{timer.Stop();preview.Clear();}
    }
    internal async Task AddAsync(string path,string? hash,string name,Guid? taskId,CancellationToken ct,Guid? importId=null,long? expectedRevision=null)
    {
        var request=await AholoHost.OnUi(()=>
        {
            ct.ThrowIfCancellationRequested();
            if(disposed||expectedRevision is not null&&expectedRevision!=revision)throw new OperationCanceledException(ct);
            int duplicate=importId.HasValue?candidates.FindIndex(c=>c.ImportId==importId.Value):-1;
            if(duplicate>=0){Select(duplicate);report(UiText.Get("This result is already loaded as a candidate."));return (revision,(MeshPlacementTarget?)null);}
            if(candidates.Count>=3)throw new InvalidOperationException(UiText.Get("Up to 3 candidates per group. Remove an unwanted candidate first."));
            return (revision,(MeshPlacementTarget?)Capture());
        }).ConfigureAwait(false);
        if(request.Item2 is null)return;
        PreparedAsset asset=await placement.PrepareAsync(path,hash,request.Item2,ct).ConfigureAwait(false);
        await AholoHost.OnUi(()=>
        {
            if(disposed||request.revision!=revision||ct.IsCancellationRequested){asset.Dispose();ct.ThrowIfCancellationRequested();return false;}
            candidates.Add(new(name,asset,request.Item2,importId??Guid.NewGuid(),taskId));Select(candidates.Count-1);
            report(UiText.Get("Candidate loaded. Adjust placement, compare candidates, then accept. The document is unchanged until acceptance.")+(asset.ImportWarnings.Count==0?"":"\n"+string.Join("\n",asset.ImportWarnings.Select(w=>UiText.Get(w)))));
            return true;
        }).ConfigureAwait(false);
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
    private void Describe()=>position.Text=Mode==MeshPlacementMode.SourceCoordinates?UiText.Get("Source units and axes are resolved into this document; scene positions are preserved."):UiText.Format("Position ({0:G5}, {1:G5}, {2:G5}) on the captured construction plane",point.X,point.Y,point.Z);
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
        var candidate=candidates[index];long expected=revision;
        var result=placement.Commit(candidate.Asset,candidate.Target with{Placement=Intent()},candidate.ImportId,candidate.TaskId,ct:currentToken(),allowed:()=>!disposed&&visible&&revision==expected);
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
