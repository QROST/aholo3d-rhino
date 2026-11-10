using System.Text.Json;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class MeshMaterialTests
{
    [Fact]
    public void GlbPreservesIndependentPbrConstantsAndLegacyDefaults()
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".glb");
        try
        {
            var mesh=new MeshData(new[]{0f,0,0,1000,0,0,0,1000,0},new[]{0,1,2},128,64,255);
            GlbWriter.Write(path,new[]{mesh,mesh with{Material=new(.5,.25,.75,.4,.9,.12)},mesh with{Material=new(.5,.25,.75,1,.1,.9,MeshColorEncoding.Srgb)}});
            using var reader=new BinaryReader(File.OpenRead(path));reader.BaseStream.Position=12;int length=reader.ReadInt32();reader.ReadUInt32();
            using var json=JsonDocument.Parse(reader.ReadBytes(length));var materials=json.RootElement.GetProperty("materials");
            var legacy=materials[0].GetProperty("pbrMetallicRoughness");Assert.Equal(0,legacy.GetProperty("metallicFactor").GetDouble());Assert.Equal(.75,legacy.GetProperty("roughnessFactor").GetDouble());
            Assert.InRange(legacy.GetProperty("baseColorFactor")[0].GetDouble(),.2158,.2159);Assert.False(materials[0].TryGetProperty("alphaMode",out _));
            var linear=materials[1].GetProperty("pbrMetallicRoughness");Assert.Equal(new[]{.5,.25,.75,.4},linear.GetProperty("baseColorFactor").EnumerateArray().Select(x=>x.GetDouble()));
            Assert.Equal(.9,linear.GetProperty("metallicFactor").GetDouble());Assert.Equal(.12,linear.GetProperty("roughnessFactor").GetDouble());Assert.Equal("BLEND",materials[1].GetProperty("alphaMode").GetString());
            Assert.InRange(materials[2].GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[0].GetDouble(),.214,.2141);Assert.Equal("OPAQUE",materials[2].GetProperty("alphaMode").GetString());
        }
        finally{File.Delete(path);}
    }
    public static IEnumerable<object[]> InvalidMaterials()
    {
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,-.01,1.01})
        {
            yield return new object[]{new MeshMaterial(bad,0,0)};yield return new object[]{new MeshMaterial(0,bad,0)};yield return new object[]{new MeshMaterial(0,0,bad)};
            yield return new object[]{new MeshMaterial(0,0,0,Alpha:bad)};yield return new object[]{new MeshMaterial(0,0,0,Metallic:bad)};yield return new object[]{new MeshMaterial(0,0,0,Roughness:bad)};
        }
        yield return new object[]{new MeshMaterial(0,0,0,ColorEncoding:(MeshColorEncoding)999)};
    }
    [Theory][MemberData(nameof(InvalidMaterials))]
    public void InvalidConstantsFailBeforeReplacingFile(MeshMaterial material)
    {
        string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".glb");File.WriteAllText(path,"existing");
        try
        {
            var mesh=new MeshData(new float[9],new[]{0,1,2},0,0,0){Material=material};
            Assert.Throws<ArgumentOutOfRangeException>(()=>GlbWriter.Write(path,new[]{mesh}));Assert.Equal("existing",File.ReadAllText(path));
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void SrgbConversionPreservesAlphaAndDarkBoundary()
    {
        var color=new MeshMaterial(0,.04045,1,.37,ColorEncoding:MeshColorEncoding.Srgb).LinearBaseColor();
        Assert.Equal(0,color[0]);Assert.Equal(.04045/12.92,color[1],12);Assert.Equal(1,color[2]);Assert.Equal(.37,color[3]);
        Assert.Equal(new[]{.1,.2,.3,.4},new MeshMaterial(.1,.2,.3,.4).LinearBaseColor());
    }
}
