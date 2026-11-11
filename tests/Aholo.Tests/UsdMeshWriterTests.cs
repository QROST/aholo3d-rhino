using System.Text.Json;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class UsdMeshWriterTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-usd-mesh-"+Guid.NewGuid().ToString("N"));
    private const string Assets="usd-assets-0123456789abcdef0123456789abcdef";
    private const string Png="iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=";
    public UsdMeshWriterTests()=>Directory.CreateDirectory(root);
    private static MeshData Triangle()=>new(new float[]{0,0,0,.125f,0,0,0,.25f,.5f},new[]{0,1,2},128,255,0)
    {Normals=new float[]{0,-2,1,0,-2,1,0,-2,1},TexCoords=new float[]{0,1,1,1,0,0}};
    [Fact]
    public void BinaryTransportKeepsLocalDetailDoubleOriginAndMaterialSemantics()
    {
        var bitmap=new MeshTexture(Convert.FromBase64String(Png),MeshTextureWrap.Clamp,MeshTextureWrap.Repeat,MeshTextureFilter.Nearest,useAlpha:false);
        var mesh=Triangle() with{BaseColorTexture=bitmap,Material=new(.5,.25,.125,.3,.2,.4)};
        var first=new UsdMeshPart("模型 / 重复",mesh,new(1e12+.125,-2e12+.25,3e12+.5));
        UsdMeshWriter.WriteBridgeInput(root,new[]{first,first with{Name=""}},Assets);
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"mesh-export.json")));var parts=json.RootElement.GetProperty("parts");
        Assert.Equal(2,parts.GetArrayLength());Assert.Equal(first.Name,parts[0].GetProperty("name").GetString());
        Assert.Equal(new[]{first.Origin.X,first.Origin.Y,first.Origin.Z},parts[0].GetProperty("origin").EnumerateArray().Select(x=>x.GetDouble()));
        Assert.Equal(.3,parts[0].GetProperty("color")[3].GetDouble());Assert.False(parts[0].GetProperty("texture").GetProperty("useAlpha").GetBoolean());
        Assert.Equal(9728,parts[0].GetProperty("texture").GetProperty("minFilter").GetInt32());
        Assert.Single(Directory.GetFiles(Path.Combine(root,Assets)));Assert.Equal(Convert.FromBase64String(Png),File.ReadAllBytes(Directory.GetFiles(Path.Combine(root,Assets))[0]));
        using var data=new BinaryReader(File.OpenRead(Path.Combine(root,"mesh-000000.bin")));
        Assert.Equal(mesh.Positions,Enumerable.Range(0,9).Select(_=>data.ReadSingle()));Assert.Equal(mesh.Indices,Enumerable.Range(0,3).Select(_=>data.ReadInt32()));
        for(int i=0;i<3;i++){Assert.Equal(0,data.ReadSingle());Assert.InRange(data.ReadSingle(),-.894428f,-.894426f);Assert.InRange(data.ReadSingle(),.447212f,.447215f);}
        Assert.Equal(mesh.TexCoords,Enumerable.Range(0,6).Select(_=>data.ReadSingle()));Assert.Equal(data.BaseStream.Length,data.BaseStream.Position);
    }
    [Fact]
    public void OptionalAttributesStayAbsentAndDisplayColorIsLinearizedOnce()
    {
        UsdMeshWriter.WriteBridgeInput(root,new[]{new UsdMeshPart("plain",Triangle() with{Normals=null,TexCoords=null},new())},Assets);
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"mesh-export.json")));var p=json.RootElement.GetProperty("parts")[0];
        Assert.False(p.GetProperty("normals").GetBoolean());Assert.False(p.GetProperty("uv").GetBoolean());Assert.Equal(JsonValueKind.Null,p.GetProperty("texture").ValueKind);
        Assert.InRange(p.GetProperty("color")[0].GetDouble(),.2158,.2159);Assert.False(Directory.Exists(Path.Combine(root,Assets)));
        Assert.Equal(48,new FileInfo(Path.Combine(root,"mesh-000000.bin")).Length);
    }
    [Theory]
    [InlineData("../outside")]
    [InlineData("/outside")]
    [InlineData("usd-assets-no-guid")]
    public void AssetDirectoryCannotEscapeCallerWorkspace(string directory)
    {Assert.Throws<ArgumentException>(()=>UsdMeshWriter.WriteBridgeInput(root,new[]{new UsdMeshPart("",Triangle(),new())},directory));Assert.Empty(Directory.GetFileSystemEntries(root));}
    [Fact]
    public void InvalidGeometryOrCancellationNeverReplacesExistingOutput()
    {
        string path=Path.Combine(root,"existing.usdz");File.WriteAllText(path,"previous scene");
        foreach(var mesh in new[]{Triangle() with{Indices=new[]{0,1,3}},Triangle() with{Normals=new float[9]},Triangle() with{TexCoords=new float[1]},Triangle() with{Positions=new[]{float.NaN,0,0}},Triangle() with{BaseColorTexture=new(Convert.FromBase64String(Png)),TexCoords=null}})
        {
            Assert.ThrowsAny<ArgumentException>(()=>UsdMeshWriter.Write(path,new[]{new UsdMeshPart("",mesh,new())}));
            Assert.Equal("previous scene",File.ReadAllText(path));Assert.Single(Directory.GetFileSystemEntries(root));
        }
        Assert.Throws<OperationCanceledException>(()=>UsdMeshWriter.Write(path,new[]{new UsdMeshPart("",Triangle(),new())},new(true)));
        Assert.Equal("previous scene",File.ReadAllText(path));Assert.Single(Directory.GetFileSystemEntries(root));
    }
    public void Dispose()=>Directory.Delete(root,true);
}
