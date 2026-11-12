using Aholo3D.Core;
namespace Aholo3D.Tests;
public sealed class LibraryPrimaryActionTests
{
    private static LibraryResultAccess Empty=>new(false,false,false,false,false,false,"","");
    [Fact] public void CachedModelPrioritizesPlaceAndMissingModelPrioritizesDownload()
    {
        var result=Empty with{IsCached=true,CanPlace=true,CanDownload=true,CanOpenFolder=true};
        Assert.Equal(LibraryPrimaryAction.Place,LibraryPrimaryActionPolicy.Choose(result,false,true));
        Assert.Equal(LibraryPrimaryAction.Download,LibraryPrimaryActionPolicy.Choose(result with{IsCached=false,CanOpenFolder=false},false,true));
        Assert.Equal(LibraryPrimaryAction.Place,LibraryPrimaryActionPolicy.Choose(Empty with{CanPlace=true},false,false));
    }
    [Fact] public void ImagesArchivesRenderRecoveryAndRunningTasksHaveDistinctActions()
    {
        Assert.Equal(LibraryPrimaryAction.OpenImage,LibraryPrimaryActionPolicy.Choose(Empty with{CanOpenImage=true,CanOpenFolder=true},false,false));
        Assert.Equal(LibraryPrimaryAction.OpenFolder,LibraryPrimaryActionPolicy.Choose(Empty with{CanOpenFolder=true},false,false));
        Assert.Equal(LibraryPrimaryAction.ContinueRender,LibraryPrimaryActionPolicy.Choose(Empty,true,true));
        Assert.Equal(LibraryPrimaryAction.Refresh,LibraryPrimaryActionPolicy.Choose(Empty,false,true));
        Assert.Equal(LibraryPrimaryAction.None,LibraryPrimaryActionPolicy.Choose(Empty,false,false));
    }
}
