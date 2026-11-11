using System.Runtime.InteropServices;
using Aholo.Core;
using Rhino.Commands;
namespace Aholo.Rhino;

[Guid("4E7AB6C0-5922-4E79-9F28-0897985E7F5D")]
public sealed class AholoCancelOperationCommand : Command
{
    public override string EnglishName=>"AholoCancelOperation";
    protected override Result RunCommand(global::Rhino.RhinoDoc doc,RunMode mode)
    {
        string scope=AholoPlugin.DocumentScope(doc.RuntimeSerialNumber);
        var registry=AholoPlugin.Session.Operations;
        var active=registry.Snapshot().Where(x=>x.State=="Running"&&x.Scope.StartsWith(scope,StringComparison.Ordinal)).ToArray();
        if(active.Length==0){global::Rhino.RhinoApp.WriteLine(UiText.Get("No local operation is running in this document."));return Result.Nothing;}
        foreach(var item in active)global::Rhino.RhinoApp.WriteLine(item.Id+" · "+UiText.Get(item.Name));
        string value="";
        var result=global::Rhino.Input.RhinoGet.GetString(UiText.Get("Local operation ID to cancel"),false,ref value);
        if(result!=Result.Success)return result;
        if(!Guid.TryParse(value,out var id)){global::Rhino.RhinoApp.WriteLine(UiText.Get("Enter a local operation ID from the list."));return Result.Failure;}
        bool cancelled=registry.Cancel(id,scope);
        global::Rhino.RhinoApp.WriteLine(UiText.Get(cancelled?"Local cancellation requested. Remote tasks are not cancelled.":"That operation has ended or belongs to another document. Nothing was cancelled."));
        return cancelled?Result.Success:Result.Nothing;
    }
}
