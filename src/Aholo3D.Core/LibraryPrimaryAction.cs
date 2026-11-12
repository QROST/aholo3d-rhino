namespace Aholo3D.Core;

public enum LibraryPrimaryAction { None,Download,Place,OpenImage,OpenFolder,ContinueRender,Refresh }
public static class LibraryPrimaryActionPolicy
{
    public static LibraryPrimaryAction Choose(LibraryResultAccess result,bool canContinue,bool canRefresh)
    {
        if(result.IsCached&&result.CanPlace)return LibraryPrimaryAction.Place;
        if(result.CanOpenImage)return LibraryPrimaryAction.OpenImage;
        if(result.CanOpenFolder)return LibraryPrimaryAction.OpenFolder;
        if(result.CanDownload)return LibraryPrimaryAction.Download;
        // Preserve offline receipt lookup for meshes whose source is no longer cached.
        if(result.CanPlace)return LibraryPrimaryAction.Place;
        if(canContinue)return LibraryPrimaryAction.ContinueRender;
        return canRefresh?LibraryPrimaryAction.Refresh:LibraryPrimaryAction.None;
    }
}
