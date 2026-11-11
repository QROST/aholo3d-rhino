using Aholo.Core;
namespace Aholo.Tests;

public sealed class OutputFileLeaseTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-output-lease-"+Guid.NewGuid().ToString("N"));
    public OutputFileLeaseTests(){Directory.CreateDirectory(root);}
    public void Dispose(){Directory.Delete(root,true);}
    private string Output=>Path.Combine(root,"scene.usdz");
    [Fact] public void ASecondWriterCannotQueueAReplacementButDifferentFilesRemainIndependent()
    {
        using var first=OutputFileLease.Acquire(Output);
        using var different=OutputFileLease.Acquire(Path.Combine(root,"other.usdz"));
        Assert.Throws<InvalidOperationException>(()=>OutputFileLease.Acquire(Path.Combine(root,".","scene.usdz")));
        first.Dispose();using var replacement=OutputFileLease.Acquire(Output);
        first.Dispose();Assert.Throws<InvalidOperationException>(()=>OutputFileLease.Acquire(Output));
    }
    [Theory][InlineData("mesh")][InlineData("gs")][InlineData("mixed")]
    public void EveryUsdWriterRejectsBusyDestinationBeforeValidationAndPreservesExistingFile(string kind)
    {
        File.WriteAllText(Output,"existing");using(var lease=OutputFileLease.Acquire(Output))
        {
            var error=Assert.Throws<InvalidOperationException>(()=>WriteInvalid(kind));
            Assert.Equal(UiText.Get("Another export is writing this destination. Wait for it to finish or choose a different filename."),error.Message);
            Assert.Equal("existing",File.ReadAllText(Output));Assert.Single(Directory.GetFiles(root));
        }
        Assert.ThrowsAny<ArgumentException>(()=>WriteInvalid(kind)); // Actual writer acquired and released its own lease on failure.
        using var afterFailure=OutputFileLease.Acquire(Output);Assert.Equal("existing",File.ReadAllText(Output));
    }
    [Fact] public void CancellationCannotLeaveAReservedDestination()
    {
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(()=>OutputFileLease.Acquire(Output,cancel.Token));
        using var lease=OutputFileLease.Acquire(Output);
    }
    [UnixRuntimeFact] public void IntermediateParentSymlinksShareAdmissionButFinalFileSymlinksDoNot()
    {
        string real=Path.Combine(root,"real"),nested=Path.Combine(real,"nested"),alias=Path.Combine(root,"alias");
        Directory.CreateDirectory(nested);Directory.CreateSymbolicLink(alias,real);
        string output=Path.Combine(nested,"scene.usdz");File.WriteAllText(output,"existing");
        using var lease=OutputFileLease.Acquire(output);
        Assert.Throws<InvalidOperationException>(()=>OutputFileLease.Acquire(Path.Combine(alias,"nested","scene.usdz")));
        string finalLink=Path.Combine(nested,"link.usdz");File.CreateSymbolicLink(finalLink,output);
        using var linkLease=OutputFileLease.Acquire(finalLink);
        Assert.Equal("existing",File.ReadAllText(output));
    }
    [Fact] public void SupportedDesktopPlatformsConservativelyReserveCaseAndUnicodeAliases()
    {
        if(!OperatingSystem.IsWindows()&&!OperatingSystem.IsMacOS())return;
        using var lease=OutputFileLease.Acquire(Path.Combine(root,"Café.usdz"));
        Assert.Throws<InvalidOperationException>(()=>OutputFileLease.Acquire(Path.Combine(root,"cafe\u0301.USDZ")));
    }
    [WindowsOutputFact] public void WindowsDevicePathAndOrdinaryDrivePathCannotBypassTheSameLease()
    {
        string normal=Path.GetFullPath(Output);File.WriteAllText(normal,"existing");
        using var lease=OutputFileLease.Acquire(normal);
        Assert.Throws<InvalidOperationException>(()=>OutputFileLease.Acquire(@"\\?\"+normal));
        Assert.Throws<InvalidOperationException>(()=>OutputFileLease.Acquire(@"\\.\"+normal));
        Assert.Equal("existing",File.ReadAllText(normal));
    }
    private void WriteInvalid(string kind)
    {
        switch(kind)
        {
            case "mesh":UsdMeshWriter.Write(Output,Array.Empty<UsdMeshPart>());break;
            case "gs":UsdGaussianWriter.Write(Output,null!);break;
            default:UsdSceneWriter.Write(Output,null,Array.Empty<UsdMeshPart>());break;
        }
    }
}

public sealed class WindowsOutputFactAttribute : FactAttribute
{
    public WindowsOutputFactAttribute(){if(!OperatingSystem.IsWindows())Skip="Requires Windows filesystem path semantics.";}
}
