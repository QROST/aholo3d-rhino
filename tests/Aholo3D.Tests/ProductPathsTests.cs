using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class ProductPathsTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo3d-rename-"+Guid.NewGuid().ToString("N"));
    private string Current=>Path.Combine(root,"Aholo3D-Rhino");
    private string Legacy=>Path.Combine(root,"Aholo-Rhino");
    public ProductPathsTests()=>Directory.CreateDirectory(root);
    [Fact] public void FreshInstallUsesNewNameWithoutCreatingData()=>Assert.Equal(Current,ProductPaths.ResolveDataRoot(root));
    [Fact] public void ExistingLegacyDirectoryRemainsTheOnlyAuthority()
    {Directory.CreateDirectory(Legacy);File.WriteAllText(Path.Combine(Legacy,"journal"),"original");Assert.Equal(Legacy,ProductPaths.ResolveDataRoot(root));Assert.False(Directory.Exists(Current));}
    [Fact] public void ExistingCurrentDirectoryUsesNewName()
    {Directory.CreateDirectory(Current);Assert.Equal(Current,ProductPaths.ResolveDataRoot(root));}
    [Fact] public void IndependentDirectoriesNeverSilentlyHideEitherJournal()
    {Directory.CreateDirectory(Current);Directory.CreateDirectory(Legacy);Assert.Throws<IOException>(()=>ProductPaths.ResolveDataRoot(root));Assert.True(Directory.Exists(Current));Assert.True(Directory.Exists(Legacy));}
    [Fact] public void FileAtNewPathIsNotTreatedAsAnEmptyStore()
    {File.WriteAllText(Current,"keep");Assert.Throws<IOException>(()=>ProductPaths.ResolveDataRoot(root));Assert.Equal("keep",File.ReadAllText(Current));}
    [UnixRuntimeFact] public void AliasSharesTaskRecordsAndExclusiveLocksWithOriginalDirectory()
    {
        var original=new TaskStore(Legacy);var task=new LocalTask{Id=Guid.NewGuid(),Name="saved task"};original.Save(task);
        Directory.CreateSymbolicLink(Current,Legacy);
        Assert.Equal(Current,ProductPaths.ResolveDataRoot(root));var current=new TaskStore(Current);
        Assert.Equal(task.Name,current.Get(task.Id).Name);
        using var lease=original.LockTask(task.Id);Assert.Throws<IOException>(()=>current.LockTask(task.Id));
        Assert.True(File.Exists(Path.Combine(Legacy,"tasks",task.Id.ToString("N")+".json")));
    }
    [UnixRuntimeFact] public void BrokenAliasDoesNotCreateANewJournal()
    {Directory.CreateSymbolicLink(Current,Path.Combine(root,"missing"));Assert.Throws<IOException>(()=>ProductPaths.ResolveDataRoot(root));Assert.False(Directory.Exists(Path.Combine(root,"missing")));}
    [Theory]
    [InlineData("new","old","new")][InlineData(null,"old","old")][InlineData(" ","old","old")][InlineData(null," ",null)]
    public void EnvironmentOverridesPreferCurrentAndRetainLegacy(string? current,string? legacy,string? expected)
    {Assert.Equal(expected,ProductPaths.EnvironmentValue("USD_PYTHON",key=>key=="AHOLO3D_USD_PYTHON"?current:legacy));}
    public void Dispose()=>Directory.Delete(root,true);
}
