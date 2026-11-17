using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class FirstRunGuardTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "aholo-firstrun-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public void MissingMarkerIsNotDismissed()
    {
        string root = NewRoot(); Directory.CreateDirectory(root);
        try { Assert.False(FirstRunGuard.IsDismissed(root)); }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void DismissPersistsAcrossReadsAndCreatesTheRoot()
    {
        string root = NewRoot();
        try
        {
            FirstRunGuard.Dismiss(root);
            Assert.True(FirstRunGuard.IsDismissed(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void CorruptMarkerReadsAsNotDismissed()
    {
        string root = NewRoot(); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "first-run.json"), "{not json");
            Assert.False(FirstRunGuard.IsDismissed(root));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void DismissOverwritesAnExistingMarker()
    {
        string root = NewRoot(); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "first-run.json"), "{\"Dismissed\":false}");
            FirstRunGuard.Dismiss(root);
            Assert.True(FirstRunGuard.IsDismissed(root));
        }
        finally { Directory.Delete(root, true); }
    }
}
