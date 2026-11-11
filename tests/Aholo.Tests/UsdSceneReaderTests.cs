using System.Text.Json;
using System.Text.Json.Nodes;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class UsdSceneReaderTests
{
    private const string Hash="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    [Theory]
    [InlineData(true,true)] [InlineData(true,false)] [InlineData(false,true)]
    public void AcceptsCompleteTypedComponentsFromOneSnapshot(bool gs,bool mesh)
    {
        using var d=new Fixture(gs,mesh);var scene=UsdSceneReader.ReadBridgeOutput(d.Path);
        Assert.Equal(gs,scene.Gaussians is not null);Assert.Equal(mesh,scene.Meshes is not null);
        Assert.Equal(Hash,scene.SourceSha256);Assert.Equal(new[]{Hash},scene.DependencyHashes);
        if(gs){Assert.Single(scene.Gaussians!.Parts.Single().Splats);Assert.Equal("Y",scene.Gaussians.UpAxis);Assert.Equal(.3048,scene.Gaussians.MetersPerUnit);}
        if(mesh)Assert.Equal(new[]{0,1,2},scene.Meshes!.Parts.Single().Indices);
    }
    [Theory]
    [InlineData("axis")] [InlineData("units")] [InlineData("hash")] [InlineData("dependencies")]
    [InlineData("missing-gs")] [InlineData("missing-mesh")] [InlineData("undeclared-mesh")]
    [InlineData("no-geometry")] [InlineData("bad-provenance")] [InlineData("bad-mesh")]
    public void RejectsPartialOrInconsistentScenes(string defect)
    {
        using var d=new Fixture(true,true);
        if(defect=="missing-gs")File.Delete(System.IO.Path.Combine(d.Path,"manifest.json"));
        else if(defect=="missing-mesh")File.Delete(System.IO.Path.Combine(d.Path,"mesh-import.json"));
        else if(defect=="bad-mesh")
        {
            using var file=new FileStream(System.IO.Path.Combine(d.Path,"mesh-000000.bin"),FileMode.Open,FileAccess.Write);
            file.Position=48;using var writer=new BinaryWriter(file);writer.Write(99);
        }
        else
        {
            bool root=defect is "undeclared-mesh" or "no-geometry" or "bad-provenance";
            string path=System.IO.Path.Combine(d.Path,root?"scene-import.json":"manifest.json");
            var data=JsonNode.Parse(File.ReadAllText(path))!;
            switch(defect)
            {
                case "axis":data["upAxis"]="Z";break;
                case "units":data["metersPerUnit"]=1;break;
                case "hash":data["sourceSha256"]=new string('b',64);break;
                case "dependencies":data["dependencyHashes"]=new JsonArray(JsonValue.Create(new string('b',64)));break;
                case "undeclared-mesh":data["meshes"]=false;break;
                case "no-geometry":data["meshes"]=false;data["gaussians"]=false;break;
                case "bad-provenance":data["sourceSha256"]="not a hash";break;
            }
            File.WriteAllText(path,data.ToJsonString());
        }
        Assert.Throws<InvalidDataException>(()=>UsdSceneReader.ReadBridgeOutput(d.Path));
    }
    [Fact]
    public void CancellationDoesNotReadPathsOrStartProcesses()
    {
        using var ct=new CancellationTokenSource();ct.Cancel();
        Assert.Throws<OperationCanceledException>(()=>UsdSceneReader.Read("missing.usdz",ct:ct.Token));
        Assert.Throws<OperationCanceledException>(()=>UsdSceneReader.ReadBridgeOutput("missing",ct.Token));
    }
    private sealed class Fixture:IDisposable
    {
        public string Path{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"aholo-scene-read-"+Guid.NewGuid().ToString("N"));
        public Fixture(bool gs,bool mesh)
        {
            Directory.CreateDirectory(Path);
            object Meta(object[] parts)=>new{protocol=1,openUsdVersion="26.11",upAxis="Y",metersPerUnit=.3048,sourceSha256=Hash,dependencyHashes=new[]{Hash},warnings=Array.Empty<string>(),parts};
            void Write(string name,object value)=>File.WriteAllText(System.IO.Path.Combine(Path,name),JsonSerializer.Serialize(value));
            double[] transform={1,0,0,0,1,0,0,0,1,10,20,30};
            if(gs)
            {
                GaussianPlyWriter.Write(System.IO.Path.Combine(Path,"part-000000.ply"),new[]{new GaussianSplat(new(1,2,3),new(-1,-1,-1),new(1,0,0,0),0,0,new float[]{0,0,0})});
                Write("manifest.json",Meta(new object[]{new{name="/Scene/GS",sourceName="GS",file="part-000000.ply",count=1,degree=0,transform,antialiased=false}}));
            }
            if(mesh)
            {
                using(var writer=new BinaryWriter(File.Create(System.IO.Path.Combine(Path,"mesh-000000.bin")) ))
                {foreach(float x in new float[]{0,0,0,1,0,0,0,1,0})writer.Write(x);foreach(int x in new[]{3,0,1,2})writer.Write(x);}
                Write("mesh-import.json",Meta(new object[]{new{primPath="/Scene/Mesh",name="Mesh",file="mesh-000000.bin",vertices=3,faces=1,indices=3,normals=false,uv=false,transform,leftHanded=false,doubleSided=true,color=new[]{.5,.25,.125,1},metallic=.2,roughness=.4,texture=(object?)null}}));
            }
            Write("scene-import.json",new{protocol=1,openUsdVersion="26.11",upAxis="Y",metersPerUnit=.3048,sourceSha256=Hash,dependencyHashes=new[]{Hash},warnings=Array.Empty<string>(),gaussians=gs,meshes=mesh});
        }
        public void Dispose()=>Directory.Delete(Path,true);
    }
}
