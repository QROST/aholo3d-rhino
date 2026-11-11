using System.Diagnostics;
using System.Runtime.InteropServices;
using Aholo.Core;
using Rhino.Commands;
using Rhino.UI;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal static class PanelNavigation
{
    private sealed record Opening(long Revision,Stopwatch Clock);
    internal static readonly PanelWorkspaces Workspaces=new();
    private static readonly Dictionary<uint,Opening> openings=new();
    private static bool listening;
    internal static Result Open(RhinoDoc doc,WorkspaceDestination destination)
    {
        uint serial=doc.RuntimeSerialNumber;WorkspaceNavigation? request=null;
        try
        {
            if(doc.IsClosing||RhinoDoc.ActiveDoc?.RuntimeSerialNumber!=serial)
                throw new InvalidOperationException(UiText.Get("Activate the document associated with this panel first."));
            request=Workspaces.Request(serial,destination);
            openings[serial]=new(request.Revision,Stopwatch.StartNew());Listen();
            Panels.OpenPanel(typeof(AholoPanel),true);
            Panels.GetPanel<AholoPanel>(serial)?.ApplyPendingNavigation();
            return Result.Success;
        }
        catch(Exception error)
        {
            if(request is not null)Cancel(serial,request.Revision);
            global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error));return Result.Failure;
        }
    }
    private static void Listen()
    {if(!listening){listening=true;global::Rhino.RhinoApp.Idle+=OnIdle;}}
    private static void StopIfEmpty()
    {if(openings.Count==0&&listening){listening=false;global::Rhino.RhinoApp.Idle-=OnIdle;}}
    internal static void Applied(uint serial,long revision)
    {if(openings.TryGetValue(serial,out var opening)&&opening.Revision==revision)openings.Remove(serial);StopIfEmpty();}
    private static void Cancel(uint serial,long revision)
    {Workspaces.Cancel(serial,revision);Applied(serial,revision);}
    private static void OnIdle(object? sender,EventArgs args)
    {
        foreach(var pair in openings.ToArray())
        {
            uint serial=pair.Key;var opening=pair.Value;
            try
            {
                if(Workspaces.Peek(serial) is null){Applied(serial,opening.Revision);continue;}
                var doc=RhinoDoc.FromRuntimeSerialNumber(serial);
                if(doc is null||doc.IsClosing){Close(serial);continue;}
                if(RhinoDoc.ActiveDoc?.RuntimeSerialNumber!=serial)
                {Cancel(serial,opening.Revision);continue;}
                if(opening.Clock.Elapsed>TimeSpan.FromSeconds(30))
                    throw new InvalidOperationException(UiText.Get("The workspace could not open. Finish the current command and try again."));
                Panels.GetPanel<AholoPanel>(serial)?.ApplyPendingNavigation();
            }
            catch(Exception error){Cancel(serial,opening.Revision);global::Rhino.RhinoApp.WriteLine(AholoHost.FriendlyError(error));}
        }
    }
    internal static void Close(uint serial){Workspaces.Close(serial);openings.Remove(serial);StopIfEmpty();}
    internal static void Shutdown(){Workspaces.Clear();openings.Clear();StopIfEmpty();}
}

[Guid("30BE962C-4333-419C-BFB1-58D4E7C04DE7")]
public sealed class AholoWorldCommand : Command
{
    public override string EnglishName=>"AholoWorld";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)=>PanelNavigation.Open(doc,WorkspaceDestination.World);
}
[Guid("2CFAB6F4-2A45-41E5-BD0B-CB48BFA42067")]
public sealed class AholoModelCommand : Command
{
    public override string EnglishName=>"AholoModel";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)=>PanelNavigation.Open(doc,WorkspaceDestination.Model);
}
[Guid("4A2AD212-2088-4236-869F-EDC4F9EC44BD")]
public sealed class AholoTasksCommand : Command
{
    public override string EnglishName=>"AholoTasks";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)=>PanelNavigation.Open(doc,WorkspaceDestination.Tasks);
}
[Guid("3E5B6073-3659-40D4-82D2-E1A03E7B1CF4")]
public sealed class AholoRenderCommand : Command
{
    public override string EnglishName=>"AholoRender";
    protected override Result RunCommand(RhinoDoc doc,RunMode mode)=>PanelNavigation.Open(doc,WorkspaceDestination.Render);
}
