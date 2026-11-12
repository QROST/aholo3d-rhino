using Aholo3D.Core;

namespace Aholo3D.Tests;

public sealed class GaussianSceneTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aholo3d-scene-tests-" + Guid.NewGuid().ToString("N"));
    public GaussianSceneTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    [Fact]
    public void CacheRoundTripPreservesDoublesCompleteShAndOpacityEndpoints()
    {
        var input = new[] {
            new GaussianSplat(new(123456789.12345679,-1e-20,3),new(-4.123456789,0,1),new(2,.1,.2,.3),double.MaxValue,3,Enumerable.Range(0,48).Select(i=>(float)(i-24)/13).ToArray()),
            new GaussianSplat(new(0,0,0),new(0,0,0),new(1,0,0,0),-double.MaxValue,0,new float[]{1,2,3})
        };
        string path = Path.Combine(directory,"roundtrip.ply");
        GaussianPlyWriter.Write(path,input);
        GaussianScene scene = GaussianSceneReader.Read(path);
        Assert.Null(scene.MetersPerUnit); Assert.Null(scene.UpAxis);
        GaussianPart part = Assert.Single(scene.Parts); Assert.Equal(GaussianTransform.Identity,part.Transform);
        Assert.Equal(2,part.Splats.Length);
        for (int i=0;i<input.Length;i++)
        {
            GaussianSplat output=part.Splats[i], original=input[i];
            Assert.Equal(original.Center,output.Center); Assert.Equal(original.LogScale,output.LogScale);
            Assert.Equal(original.Rotation,output.Rotation); Assert.Equal(original.OpacityLogit,output.OpacityLogit);
            Assert.Equal(original.Coefficients.ToArray(),output.Coefficients.Span[..original.Coefficients.Length].ToArray());
            Assert.All(output.Coefficients.Span[original.Coefficients.Length..].ToArray(),v=>Assert.Equal(0,v));
        }
    }

    [Fact]
    public void CancelledCacheWritePreservesExistingFile()
    {
        string path=Path.Combine(directory,"existing.ply"); File.WriteAllText(path,"keep this");
        Assert.Throws<OperationCanceledException>(()=>GaussianPlyWriter.Write(path,Array.Empty<GaussianSplat>(),new CancellationToken(true)));
        Assert.Equal("keep this",File.ReadAllText(path)); Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void EmptyAndNonGaussianFilesAreRejected()
    {
        string empty=Path.Combine(directory,"empty.ply"); GaussianPlyWriter.Write(empty,Array.Empty<GaussianSplat>());
        Assert.Throws<InvalidDataException>(()=>GaussianSceneReader.Read(empty));
        string points=Path.Combine(directory,"points.ply");
        File.WriteAllText(points,"ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n");
        Assert.Throws<InvalidDataException>(()=>GaussianSceneReader.Read(points));
        Assert.Throws<NotSupportedException>(()=>GaussianSceneReader.Read(Path.Combine(directory,"mesh.obj")));
    }
}
