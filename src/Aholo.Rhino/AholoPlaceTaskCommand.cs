using System.Runtime.InteropServices;
using Aholo.Core;
using Rhino.Commands;
using Rhino.Input.Custom;
namespace Aholo.Rhino;

[Guid("74DB5755-9C7D-4910-91C3-8C01DCECA772")]
public sealed class AholoPlaceTaskCommand : Command
{
    public override string EnglishName=>"AholoPlaceTask";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc,RunMode mode)
    {
        using var input=new GetString();input.SetCommandPrompt(UiText.Get("Local task ID from Aholo3D Library (places the original static GLB; never regenerates)"));
        if(input.Get()!=global::Rhino.Input.GetResult.String)return Result.Cancel;
        if(!Guid.TryParse(input.StringResult(),out var id)||id==Guid.Empty){global::Rhino.RhinoApp.WriteLine(UiText.Get("Enter the full local task GUID from Library."));return Result.Failure;}
        MeshPlacementTarget target;
        try{target=AholoPlugin.Placement.Capture(doc,MeshPlacementService.DefaultPlacement(doc));}
        catch(Exception error){global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error));return Result.Failure;}
        EventHandler? idle=null;idle=(_,_)=>{global::Rhino.RhinoApp.Idle-=idle;_=PlaceAsync(id,target);};global::Rhino.RhinoApp.Idle+=idle;
        return Result.Success;
    }
    private static async Task PlaceAsync(Guid id,MeshPlacementTarget target)
    {
        try
        {
            var result=await AholoPlugin.Session.Operations.RunAsync(AholoPlugin.DocumentScope(target.Document.Serial)+"mesh-placement","Place original task",ct=>AholoPlugin.Placement.PlaceTaskAsync(id,null,target,ct)).ConfigureAwait(false);
            AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(result.Message));
        }
        catch(Exception error){AholoHost.Ui(()=>global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error)));}
    }
}
