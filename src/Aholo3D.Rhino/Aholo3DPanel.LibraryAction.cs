using Aholo3D.Core;
using Eto.Forms;
namespace Aholo3D.Rhino;

public sealed partial class Aholo3DPanel
{
    private readonly Button primaryResult=new(){Text=T("Select a task"),Enabled=false};
    private LibraryPrimaryAction primaryKind;
    private void UpdatePrimaryResult(LibraryResultAccess access,bool canContinue,bool canRefresh)
    {
        primaryKind=LibraryPrimaryActionPolicy.Choose(access,canContinue,canRefresh);
        var source=primaryKind switch
        {
            LibraryPrimaryAction.Download=>download,LibraryPrimaryAction.Place=>place,
            LibraryPrimaryAction.OpenImage=>openImage,LibraryPrimaryAction.OpenFolder=>openFolder,
            LibraryPrimaryAction.ContinueRender=>continueRender,LibraryPrimaryAction.Refresh=>refresh,_=>null
        };
        primaryResult.Text=source?.Text??T(Selected is null?"Select a task":"Result unavailable");
        primaryResult.Enabled=source?.Enabled==true;
        primaryResult.ToolTip=source?.ToolTip??access.Status;
        download.Visible=source!=download;place.Visible=source!=place;openImage.Visible=source!=openImage;
        openFolder.Visible=source!=openFolder;refresh.Visible=source!=refresh;
        continueRender.Visible=canContinue&&source!=continueRender;
    }
    private void PrimaryResultClicked()
    {
        if(disposed||!primaryResult.Enabled)return;
        // Use exactly the same invocation identities, leases and paid-action path as secondary actions.
        switch(primaryKind)
        {
            case LibraryPrimaryAction.Download:_=RunAsync("Download selected result",DownloadAsync,WorkKind.Task);break;
            case LibraryPrimaryAction.Place:_=RunAsync("Place in current document",PlaceAsync);break;
            case LibraryPrimaryAction.OpenImage:try{OpenImage();}catch(Exception ex){Report(Aholo3DHost.FriendlyError(ex));}break;
            case LibraryPrimaryAction.OpenFolder:try{OpenResultFolder();}catch(Exception ex){Report(Aholo3DHost.FriendlyError(ex));}break;
            case LibraryPrimaryAction.ContinueRender:_=RunAsync("Continue original render · Uses service credits",ContinueAsync,WorkKind.Task);break;
            case LibraryPrimaryAction.Refresh:_=RunAsync("Refresh selected task",RefreshAsync,WorkKind.Task);break;
        }
    }
}
