using System.Text.Json;
using Aholo3D.Core;

namespace Aholo3D.Tests;

public sealed class UsdSceneWriterTests
{
    private const string Assets = "usd-assets-0123456789abcdef0123456789abcdef";
    private static GaussianScene Scene() => new(new[]{new GaussianPart("same / name",new[]{new GaussianSplat(new(1e9+.125,2,3),new(-1,-2,-3),new(1,0,0,0),.5,0,new float[]{.1f,.2f,.3f})},GaussianTransform.Identity)},"Y",.3048);
    private static UsdMeshPart Mesh() => new("same / name",new MeshData(new float[]{0,0,0,1,0,0,0,1,0},new[]{0,1,2},255,255,255),new(1000,2000,3000));

    [Theory]
    [InlineData(true,true)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public void TransportKeepsTypedGeometryAndExplicitGsUnits(bool gs,bool mesh)
    {
        using var temp=new TemporaryDirectory();
        UsdSceneWriter.WriteBridgeInput(temp.Path,gs?Scene():null,mesh?new[]{Mesh()}:Array.Empty<UsdMeshPart>(),Assets);
        using var manifest=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path,"scene-export.json")));
        Assert.Equal(gs,manifest.RootElement.GetProperty("gaussians").GetBoolean());
        Assert.Equal(mesh,manifest.RootElement.GetProperty("meshes").GetBoolean());
        Assert.Equal(gs,File.Exists(System.IO.Path.Combine(temp.Path,"export.json")));
        Assert.Equal(mesh,File.Exists(System.IO.Path.Combine(temp.Path,"mesh-export.json")));
        if(gs)
        {
            using var input=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path,"export.json")));
            Assert.Equal("Y",input.RootElement.GetProperty("upAxis").GetString());
            Assert.Equal(.3048,input.RootElement.GetProperty("metersPerUnit").GetDouble());
            Assert.Equal(Scene().Parts[0].Splats[0].Center,PlyReader.ReadGaussians(System.IO.Path.Combine(temp.Path,"part-000000.ply")).Single().Center);
        }
        if(mesh)
        {
            using var input=JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(temp.Path,"mesh-export.json")));
            Assert.Equal(new double[]{1000,2000,3000},input.RootElement.GetProperty("parts")[0].GetProperty("origin").EnumerateArray().Select(v=>v.GetDouble()));
        }
    }

    [Fact]
    public void InvalidSecondGeometryDoesNotReplaceExistingFileOrLeaveAssets()
    {
        using var temp=new TemporaryDirectory();string output=System.IO.Path.Combine(temp.Path,"existing.usda");File.WriteAllText(output,"old scene");
        var invalid=Mesh() with { Mesh=Mesh().Mesh with { Indices=new[]{0,1,99} } };
        Assert.Throws<ArgumentException>(()=>UsdSceneWriter.Write(output,Scene(),new[]{invalid}));
        Assert.Equal("old scene",File.ReadAllText(output));Assert.Single(Directory.GetFileSystemEntries(temp.Path));
    }

    [Fact]
    public void EmptySceneAndCancelledExportLeaveOldOutputUntouched()
    {
        using var temp=new TemporaryDirectory();string output=System.IO.Path.Combine(temp.Path,"existing.usdz");File.WriteAllText(output,"old scene");
        Assert.Throws<ArgumentException>(()=>UsdSceneWriter.Write(output,null,Array.Empty<UsdMeshPart>()));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>UsdSceneWriter.Write(output,Scene(),new[]{Mesh()},cancel.Token));
        Assert.Equal("old scene",File.ReadAllText(output));Assert.Single(Directory.GetFileSystemEntries(temp.Path));
    }

    private sealed class TemporaryDirectory:IDisposable
    {
        public string Path{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"aholo3d-scene-usd-tests-"+Guid.NewGuid().ToString("N"));
        public TemporaryDirectory()=>Directory.CreateDirectory(Path);
        public void Dispose()=>Directory.Delete(Path,true);
    }
}
