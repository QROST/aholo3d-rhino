using System.Text.Json;
using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class MeshAttributesTests
{
    private static MeshData Triangle() => new(new[]{0f,0,0,1000,0,0,0,1000,0},new[]{0,1,2},255,255,255);
    [Fact]
    public void GlbNormalsUseRotationWithoutUnitScaleAndUvsRemainUnclamped()
    {
        string path=Path.GetTempFileName();
        try
        {
            var mesh=Triangle() with{Normals=new[]{0f,0,4,0,0,2,0,0,1},TexCoords=new[]{0f,1,2,-1,.2f,.3f}};
            GlbWriter.Write(path,new[]{mesh,Triangle()});
            using var input=new BinaryReader(File.OpenRead(path));input.BaseStream.Position=12;int n=input.ReadInt32();input.ReadUInt32();
            using var doc=JsonDocument.Parse(input.ReadBytes(n));var root=doc.RootElement;
            int bin=input.ReadInt32();input.ReadUInt32();long start=input.BaseStream.Position;
            var attributes=root.GetProperty("meshes")[0].GetProperty("primitives")[0].GetProperty("attributes");
            float[] ReadAttribute(string name,int count)
            {
                var a=root.GetProperty("accessors")[attributes.GetProperty(name).GetInt32()];
                var view=root.GetProperty("bufferViews")[a.GetProperty("bufferView").GetInt32()];
                input.BaseStream.Position=start+view.GetProperty("byteOffset").GetInt64();
                return Enumerable.Range(0,count).Select(_=>input.ReadSingle()).ToArray();
            }
            Assert.Equal(new[]{0f,1,0,0,1,0,0,1,0},ReadAttribute("NORMAL",9));
            Assert.Equal(mesh.TexCoords,ReadAttribute("TEXCOORD_0",6));
            Assert.Equal(start+bin,input.BaseStream.Length);
            var legacy=root.GetProperty("meshes")[1].GetProperty("primitives")[0].GetProperty("attributes");
            Assert.False(legacy.TryGetProperty("NORMAL",out _));Assert.False(legacy.TryGetProperty("TEXCOORD_0",out _));
            // Rotating an outward +Z triangle to glTF gives +Y, matching its normals without a winding flip.
            var p=ReadAttribute("POSITION",9);
            var a1=new GaussianVector(p[3]-p[0],p[4]-p[1],p[5]-p[2]);var b1=new GaussianVector(p[6]-p[0],p[7]-p[1],p[8]-p[2]);
            Assert.True(a1.Z*b1.X-a1.X*b1.Z>0);
        }
        finally{File.Delete(path);}
    }
    [Fact]
    public void InverseTransposePreservesSmoothNormalsUnderMirroredNonuniformShear()
    {
        var linear=new GaussianMatrix(-2,1,0,0,3,0,0,0,4);
        var n=new GaussianVector(1,1,1).Normalized();
        var values=MeshAttributes.TransformNormals(new[]{(float)n.X,(float)n.Y,(float)n.Z},linear);
        var actual=new GaussianVector(values[0],values[1],values[2]);
        // Independently constructed tangent plane; inverse transpose remains perpendicular to both tangents.
        Assert.InRange(Math.Abs(GaussianVector.Dot(actual,linear.Transform(new(1,-1,0)))),0,1e-6);
        Assert.InRange(Math.Abs(GaussianVector.Dot(actual,linear.Transform(new(1,0,-1)))),0,1e-6);
        Assert.InRange(actual.Length,.999999,1.000001);Assert.True(actual.X<0);
        Assert.Throws<ArgumentException>(()=>MeshAttributes.TransformNormals(new[]{0f,0,1},new(1,0,0,0,0,0,0,0,1)));
    }
    public static IEnumerable<object[]> InvalidAttributes()
    {
        yield return new object[]{Triangle() with{Normals=new float[6]}};
        yield return new object[]{Triangle() with{Normals=new float[9]}};
        yield return new object[]{Triangle() with{Normals=new[]{0f,0,1,0,0,1,float.NaN,0,1}}};
        yield return new object[]{Triangle() with{TexCoords=new float[4]}};
        yield return new object[]{Triangle() with{TexCoords=new[]{0f,0,0,1,float.PositiveInfinity,0}}};
    }
    [Theory][MemberData(nameof(InvalidAttributes))]
    public void InvalidAttributeDoesNotReplaceExistingOutput(MeshData mesh)
    {
        string path=Path.GetTempFileName();File.WriteAllText(path,"keep");
        try{Assert.Throws<ArgumentException>(()=>GlbWriter.Write(path,new[]{mesh}));Assert.Equal("keep",File.ReadAllText(path));}
        finally{File.Delete(path);}
    }
}
