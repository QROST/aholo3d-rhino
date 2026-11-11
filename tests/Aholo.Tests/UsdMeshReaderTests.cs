using System.Text.Json;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class UsdMeshReaderTests
{
    [Fact]
    public void PreservesPhysicalTransformQuadsUvNormalsAndLinearMaterial()
    {
        using var f=new Fixture();f.Manifest();var scene=UsdMeshReader.ReadBridgeOutput(f.Directory);
        var part=Assert.Single(scene.Parts);Assert.Equal(new[]{4},part.FaceCounts);Assert.Equal(new[]{0,1,2,3},part.Indices);
        Assert.Equal(new float[]{0,0,1,0,1,1,0,1},part.TexCoords);Assert.Equal(.25,part.Material.R);Assert.True(part.LeftHanded);
        var mm=scene.ToMillimetres(part.Transform);
        Assert.Equal(new GaussianVector(10000,-30000,20000),mm.TransformPoint(new(0,0,0)));
        Assert.Equal(new GaussianVector(12000,-30000,20000),mm.TransformPoint(new(1,0,0)));
        Assert.Equal(2,scene.DependencyHashes.Count);
    }
    [Fact]
    public void PreservesImageBytesAndIgnoredAlphaWithPartialOpacity()
    {
        using var f=new Fixture();byte[] image=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=");
        var bitmap=new MeshTexture(image);string filename=bitmap.Sha256.ToLowerInvariant()+".png";File.WriteAllBytes(Path.Combine(f.Directory,filename),image);
        f.Part["texture"]=new{file=filename,useAlpha=false,wrapS=33071,wrapT=10497,minFilter=9728,magFilter=9729};f.Manifest();
        var actual=Assert.Single(UsdMeshReader.ReadBridgeOutput(f.Directory).Parts);Assert.False(actual.Texture!.UseAlpha);Assert.Equal(.4,actual.Material.Alpha);
        using var output=new MemoryStream();actual.Texture.CopyEncodedTo(output);Assert.Equal(image,output.ToArray());
        image[image.Length-1]^=1;File.WriteAllBytes(Path.Combine(f.Directory,filename),image);
        Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
    }
    [Theory]
    [InlineData("../mesh-000000.bin")][InlineData("a/mesh-000000.bin")][InlineData("mesh-000000.bin/../a")][InlineData("C:mesh-000000.bin")]
    public void RejectsEscapingPayloadPaths(string name)
    {using var f=new Fixture();f.Part["file"]=name;f.Manifest();Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));}
    [Theory]
    [InlineData("vertices",5)][InlineData("faces",2)][InlineData("indices",5)][InlineData("vertices",0)]
    public void RejectsManifestLengthMismatchBeforeAllocation(string field,int value)
    {using var f=new Fixture();f.Part[field]=value;f.Manifest();Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));}
    [Fact]
    public void RejectsCorruptTopologyNormalsAndCoordinates()
    {
        using var f=new Fixture();f.Manifest();
        f.Payload(index:9);Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
        f.Payload(normal:0);Assert.Throws<ArgumentException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
        f.Payload(position:float.NaN);Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
        f.Payload(uv:float.PositiveInfinity);Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
    }
    [Fact]
    public void RejectsSingularTransformAndBadMaterial()
    {
        using var f=new Fixture();f.Part["transform"]=new double[12];f.Manifest();Assert.Throws<ArgumentException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
        f.Part["transform"]=Fixture.Matrix;f.Part["roughness"]=2;f.Manifest();Assert.Throws<ArgumentOutOfRangeException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
    }
    [Fact]
    public void RejectsExtremeAnisotropyBeforeHostWindingCouldUnderflow()
    {
        using var f=new Fixture();f.Part["transform"]=new double[]{-1e110,0,0,0,1e-110,0,0,0,1,0,0,0};f.Manifest();
        Assert.Throws<ArgumentException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
    }
    [Theory]
    [InlineData("X",1)][InlineData("Z",0)][InlineData("Z",-1)]
    public void RejectsInvalidUnitsAndAxis(string axis,double units)
    {using var f=new Fixture();f.Manifest(axis,units);Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));}
    [Fact]
    public void RejectsMissingDependencyAuthenticationAndPayloadSymlinks()
    {
        using var f=new Fixture();f.Manifest(dependencies:new[]{new string('b',64)});Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
        f.Manifest();if(OperatingSystem.IsWindows())return;
        string file=Path.Combine(f.Directory,"mesh-000000.bin");File.Move(file,file+".source");File.CreateSymbolicLink(file,file+".source");
        Assert.Throws<InvalidDataException>(()=>UsdMeshReader.ReadBridgeOutput(f.Directory));
    }
    [Fact]
    public void PreCancellationAvoidsFilesystemAndRuntimeAccess()
    {
        using var ct=new CancellationTokenSource();ct.Cancel();
        Assert.Throws<OperationCanceledException>(()=>UsdMeshReader.Read("missing.usd",ct:ct.Token));
        Assert.Throws<OperationCanceledException>(()=>UsdMeshReader.ReadBridgeOutput("missing",ct.Token));
    }
    private sealed class Fixture:IDisposable
    {
        public static double[] Matrix=>new double[]{2,0,0,0,3,0,0,0,-4,10,20,30};
        public string Directory{get;}=Path.Combine(Path.GetTempPath(),"aholo-usd-mesh-tests-"+Guid.NewGuid().ToString("N"));
        public Dictionary<string,object?> Part{get;}=new(){["primPath"]="/World/Mesh",["name"]="mesh 用户",["file"]="mesh-000000.bin",["vertices"]=4,["faces"]=1,["indices"]=4,["normals"]=true,["uv"]=true,["transform"]=Matrix,["leftHanded"]=true,["doubleSided"]=false,["color"]=new[]{.25,.5,.75,.4},["metallic"]=.2,["roughness"]=.3,["texture"]=null};
        public Fixture(){System.IO.Directory.CreateDirectory(Directory);Payload();}
        public void Payload(int index=0,float normal=1,float position=0,float uv=0)
        {
            using var writer=new BinaryWriter(File.Create(Path.Combine(Directory,"mesh-000000.bin")));
            foreach(float x in new[]{position,0,0,1,0,0,1,1,0,0,1,0})writer.Write(x);
            writer.Write(4);foreach(int x in new[]{index,1,2,3})writer.Write(x);
            for(int i=0;i<4;i++){writer.Write(0f);writer.Write(0f);writer.Write(normal);}
            foreach(float x in new[]{uv,0,1,0,1,1,0,1})writer.Write(x);
        }
        public void Manifest(string axis="Y",double units=1,string[]? dependencies=null)
            =>File.WriteAllText(Path.Combine(Directory,"mesh-import.json"),JsonSerializer.Serialize(new{protocol=1,openUsdVersion="26.11",upAxis=axis,metersPerUnit=units,sourceSha256=new string('a',64),dependencyHashes=dependencies??new[]{new string('a',64),new string('b',64)},parts=new[]{Part},warnings=new[]{"fixture warning"}}));
        public void Dispose()=>System.IO.Directory.Delete(Directory,true);
    }
}
