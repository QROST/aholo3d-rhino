using System.IO.Compression;
using System.Security.Cryptography;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class ObjMeshReaderTests
{
    private const string Triangle="v 0 0 0\nv 1 0 0\nv 0 1 0\n";
    private const string Uv="vt 0 0\nvt 1 0\nvt 0 1\n";
    private const string Png="iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=";
    [Fact]
    public void TexturePreservesRgbModulationAndSeparateDissolveWithoutImageAlpha()
    {
        using var files=new Files();files.Image();files.Text("paint.mtl","newmtl blue\nKd .2 .4 .8\nd .4\nmap_Kd paint.png\n");
        var scene=files.Read("mtllib paint.mtl\n"+Triangle+Uv+"usemtl blue\nf 1/1 2/2 3/3\n");var part=Assert.Single(scene.Parts);
        Assert.Equal(new MeshMaterial(.2,.4,.8,.4),part.Material);Assert.Equal(new float[]{0,0,1,0,0,1},part.TexCoords);
        Assert.True(part.Texture!.HasAlpha);Assert.False(part.Texture.UseAlpha);Assert.Equal(MeshTextureWrap.Repeat,part.Texture.WrapU);
        using var image=new MemoryStream();part.Texture.CopyEncodedTo(image);Assert.Equal(Convert.FromBase64String(Png),image.ToArray());
        Assert.Equal(3,scene.DependencyHashes.Count);Assert.Equal("model.obj",scene.SourceEntry);
    }
    [Theory]
    [InlineData("d .4\nTr .6")][InlineData("Tr .6\nd .4")][InlineData("Tr .6")]
    public void DissolveHasOrderIndependentAuthoredPrecedence(string statements)
    {using var files=new Files();var m=files.Material("Kd .5\n"+statements);Assert.Equal(.4,m.Surface.Alpha,12);Assert.Equal(.5,m.Surface.G);}
    [Theory]
    [InlineData("Pr .3\nNs 998")][InlineData("Ns 998\nPr .3")]
    public void PbrRoughnessOverridesLegacyExponent(string statements)
    {using var files=new Files();var m=files.Material(statements);Assert.Equal(.3,m.Surface.Roughness);Assert.True(m.LegacyPreview);}
    [Fact]
    public void LegacyConversionIsExplicitAndOnlyUsedMaterialsAreDecoded()
    {
        using var files=new Files();files.Text("paint.mtl","newmtl unused\nmap_Kd missing.png\nunsupported value\nnewmtl used\nKd 1 0 0\nKa 0\nKs .2\nNs 98\nNi 1.5\nillum 2\n");
        var scene=files.Read("mtllib paint.mtl\n"+Triangle+"usemtl used\nf 1 2 3\n");
        Assert.Equal(Math.Sqrt(.02),scene.Parts[0].Material.Roughness,12);Assert.Contains(scene.Warnings,x=>x.Contains("legacy"));Assert.Equal(2,scene.DependencyHashes.Count);
    }
    [Theory]
    [InlineData("d .4\nTr .2")][InlineData("d -halo .5")][InlineData("map_d paint.png")][InlineData("bump paint.png")]
    [InlineData("Ke .1 0 0")][InlineData("Tf .9 1 1")][InlineData("illum 4")][InlineData("Kd 2 0 0")]
    [InlineData("Kd spectral a.rfl")][InlineData("Kd .2\nKd .3")][InlineData("Ni 0")][InlineData("Ns -1")]
    public void RejectsUnsupportedOrConflictingUsedMaterialStatements(string statements)
    {using var files=new Files();Assert.Throws<InvalidDataException>(()=>files.Material(statements));}
    [Theory]
    [InlineData("-s -2",-2,1,0,0)] [InlineData("-s 0",0,1,0,0)]
    [InlineData("-o -.5",1,1,-.5,0)] [InlineData("-s 2 3 1 -o .1 .2 0",2,3,.1,.2)]
    public void TextureTransformDefaultsAreComponentWise(string options,double su,double sv,double ou,double ov)
    {using var files=new Files();files.Image();var m=files.Material("map_Kd "+options+" paint.png");Assert.Equal(su,m.ScaleU);Assert.Equal(sv,m.ScaleV);Assert.Equal(ou,m.OffsetU);Assert.Equal(ov,m.OffsetV);}
    [Theory]
    [InlineData("-s 1 1 2")][InlineData("-o 0 0 1")][InlineData("-t 0 1")][InlineData("-mm 1 1")][InlineData("-blendu off")]
    [InlineData("-cc on")][InlineData("-clamp bad")][InlineData("-s")][InlineData("-unknown 2")][InlineData("-s 1 -s 1")]
    public void UnsupportedTextureOptionsFailExplicitly(string options)
    {using var files=new Files();files.Image();Assert.Throws<InvalidDataException>(()=>files.Material("map_Kd "+options+" paint.png"));}
    [Fact]
    public void SameImageInDifferentMaterialsKeepsIndependentUvAndSampler()
    {
        using var files=new Files();files.Image();files.Text("paint.mtl","newmtl a\nmap_Kd -s .5 -o .25 -clamp on paint.png\nnewmtl b\nmap_Kd -s -2 -o .1 .2 paint.png\n");
        var scene=files.Read("mtllib paint.mtl\n"+Triangle+Uv+"usemtl a\nf 1/1 2/2 3/3\nusemtl b\nf 1/1 2/2 3/3\n");
        Assert.Equal(2,scene.Parts.Count);var a=scene.Parts[0];var b=scene.Parts[1];Assert.Equal(a.Texture!.Sha256,b.Texture!.Sha256);
        Assert.Equal(MeshTextureWrap.Clamp,a.Texture.WrapU);Assert.Equal(MeshTextureWrap.Repeat,b.Texture.WrapU);
        Assert.Equal(new float[]{.25f,0,.75f,0,.25f,1},a.TexCoords);Assert.Equal(new float[]{.1f,.2f,-1.9f,.2f,.1f,1.2f},b.TexCoords);
    }
    [Theory]
    [InlineData("map_Kd -clamp on -o .1 paint.png",true)]
    [InlineData("map_Kd paint.png",false)]
    public void RejectsOutOfRangeDecalOrBitmapWithoutUv(string map,bool useUv)
    {
        using var files=new Files();files.Image();files.Text("paint.mtl","newmtl a\n"+map);
        Assert.Throws<InvalidDataException>(()=>files.Read("mtllib paint.mtl\n"+Triangle+(useUv?Uv:"")+"usemtl a\n"+(useUv?"f 1/1 2/2 3/3":"f 1 2 3")));
    }
    [Fact]
    public void NamesAreCaseSensitiveAndUsedDuplicateOrUndefinedMaterialsFail()
    {
        using var files=new Files();files.Text("paint.mtl","newmtl A\nKd .2\nnewmtl a\nKd .5\n");
        var scene=files.Read("mtllib paint.mtl\n"+Triangle+"usemtl A\nf 1 2 3\nusemtl a\nf 1 2 3");Assert.Equal(2,scene.Parts.Count);
        Assert.Throws<InvalidDataException>(()=>files.Read("mtllib paint.mtl\n"+Triangle+"usemtl missing\nf 1 2 3"));
        files.Text("paint.mtl","newmtl A\nKd .2\nnewmtl A\nKd .5");Assert.Throws<InvalidDataException>(()=>files.Read("mtllib paint.mtl\n"+Triangle+"usemtl A\nf 1 2 3"));
    }
    [Fact]
    public void SmoothNormalsCrossMaterialAndGroupPartitionsWithoutDuplicatingFaces()
    {
        using var files=new Files();files.Text("paint.mtl","newmtl a\nKd .2\nnewmtl b\nKd .8");
        var scene=files.Read("mtllib paint.mtl\n"+Triangle+"v 0 0 1\ns 2\nusemtl a\ng left right\nf 1 2 3\nusemtl b\ng bottom\nf 1 4 2");
        Assert.Equal(2,scene.Parts.Count);Assert.Equal(2,scene.Parts.Sum(p=>p.FaceCounts.Length));Assert.Equal(new[]{"left","right"},scene.Parts[0].SourceGroups);
        foreach(var part in scene.Parts){var n=NormalAt(part,GaussianVector.Zero);Assert.Equal(0,n.X);Assert.Equal(Math.Sqrt(.5),n.Y,6);Assert.Equal(Math.Sqrt(.5),n.Z,6);}
    }
    [Fact]
    public void HardEdgesDuplicateVerticesAndAuthoredNormalsWin()
    {
        using var files=new Files();var hard=Assert.Single(files.Read(Triangle+"v 0 0 1\ns off\nf 1 2 3\nf 1 4 2").Parts);
        Assert.Equal(6,hard.Positions.Length/3);Assert.Equal(new float[]{0,0,1,0,0,1,0,0,1,0,1,0,0,1,0,0,1,0},hard.Normals);
        var authored=Assert.Single(files.Read(Triangle+"vn 2 0 0\ns 5\nf 1//1 2//1 3//1").Parts);
        Assert.Equal(new float[]{1,0,0,1,0,0,1,0,0},authored.Normals);
    }
    [Fact]
    public void RepeatedObjectNamesKeepDistinctOriginsAndLargeCoordinatesKeepDetail()
    {
        using var files=new Files();var scene=files.Read("v 1000000000000.125 0 0\nv 1000000000001.125 0 0\nv 1000000000000.125 1 0\no same\nf 1 2 3\no same\nf 1 2 3");
        Assert.Equal(2,scene.Parts.Count);Assert.Equal(scene.Parts[0].Name,scene.Parts[1].Name);Assert.NotEqual(scene.Parts[0].SourcePrimPath,scene.Parts[1].SourcePrimPath);
        var p=scene.Parts[0];Assert.Equal(1e12+.125,Point(p,0).X);Assert.Equal(1d,Point(p,1).X-Point(p,0).X);Assert.All(p.Positions,x=>Assert.InRange(x,-.5f,.5f));
    }
    [Fact]
    public void ConcavePolygonTriangulationHasIndependentAreaAndUnitsRotateProperly()
    {
        using var files=new Files();var scene=files.Read("v 0 0 0\nv 2 0 0\nv 2 2 0\nv 1 1 0\nv 0 2 0\nf 1 2 3 4 5",new(.01,"Y"));
        var part=Assert.Single(scene.Parts);Assert.Equal(3,part.FaceCounts.Length);double area=0;
        for(int i=0;i<part.Indices.Length;i+=3){var a=Point(part,part.Indices[i]);var b=Point(part,part.Indices[i+1]);var c=Point(part,part.Indices[i+2]);double cross=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);Assert.True(cross>0);area+=cross/2;}
        Assert.Equal(3,area);var mm=scene.ToMillimetres(GaussianTransform.Identity).TransformPoint(new(1,2,3));Assert.Equal(new GaussianVector(10,-30,20),mm);
    }
    [Fact]
    public void NonplanarQuadFailsWithoutSilentFlattening()
    {using var files=new Files();Assert.Throws<InvalidDataException>(()=>files.Read(Triangle+"v 1 1 .5\nf 1 2 4 3"));}
    [Theory]
    [InlineData(0,"Z")][InlineData(-1,"Y")][InlineData(double.NaN,"Z")][InlineData(1,"X")]
    public void ExplicitUnitsAndAxisAreRequired(double units,string axis)
    {using var files=new Files();Assert.Throws<ArgumentException>(()=>files.Read(Triangle+"f 1 2 3",new(units,axis)));}
    [Fact]
    public void ArchiveIsParsedWithoutPythonAndRecordsTheRootHashAndEntry()
    {
        using var files=new Files();string zip=Path.Combine(files.Root,"asset.zip");
        using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("mesh/model.obj").Open());writer.Write(Triangle+"f 1 2 3");}
        string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip)));var scene=ObjMeshReader.Read(zip,new(1,"Z"),hash);
        Assert.Equal(hash,scene.SourceSha256);Assert.Equal("mesh/model.obj",scene.SourceEntry);Assert.Equal(2,scene.DependencyHashes.Count);
        Assert.Throws<OperationCanceledException>(()=>ObjMeshReader.Read(zip,new(1,"Z"),ct:new CancellationToken(true)));
    }
    [Fact]
    public void ReusedAtlasSharesAnImmutableTextureOnlyWhenSamplersMatch()
    {
        using var files=new Files();files.Image();files.Text("paint.mtl","newmtl a\nmap_Kd paint.png\nnewmtl b\nKd .5\nmap_Kd -s 2 paint.png\n");
        var scene=files.Read("mtllib paint.mtl\n"+Triangle+Uv+"usemtl a\nf 1/1 2/2 3/3\nusemtl b\nf 1/1 2/2 3/3");
        Assert.Same(scene.Parts[0].Texture,scene.Parts[1].Texture);Assert.NotEqual(scene.Parts[0].Material,scene.Parts[1].Material);
        Assert.Equal(1f,scene.Parts[0].TexCoords![2]);Assert.Equal(2f,scene.Parts[1].TexCoords![2]);
    }
    [Fact]
    public void ExplicitUnicodeEntryUsesTheSameNormalizationAtEveryStage()
    {
        using var files=new Files();string path=Path.Combine(files.Root,"unicode.zip");
        using(var archive=ZipFile.Open(path,ZipArchiveMode.Create)){using var writer=new StreamWriter(archive.CreateEntry("caf\u00e9/model.obj").Open());writer.Write(Triangle+"f 1 2 3");}
        var scene=ObjMeshReader.Read(path,new(1,"Z","cafe\u0301/model.obj"));Assert.Equal("caf\u00e9/model.obj",scene.SourceEntry);
    }
    [Fact]
    public void PackagedSampleHasKnownGeometryMaterialsAndBothArchiveEntries()
    {
        string? root=AppContext.BaseDirectory;
        while(root is not null&&!File.Exists(Path.Combine(root,"Aholo.sln")))root=Path.GetDirectoryName(root);
        Assert.NotNull(root);
        string path=Path.Combine(root!,"fixtures","mesh-obj","scene.obj");var scene=ObjMeshReader.Read(path,new(1,"Z"));
        Assert.Equal(3,scene.Parts.Count);Assert.Equal(5,scene.Parts.Sum(x=>x.FaceCounts.Length));
        var points=scene.Parts.SelectMany(p=>Enumerable.Range(0,p.Positions.Length/3).Select(i=>Point(p,i))).ToArray();
        Assert.Equal(0d,points.Min(p=>p.X));Assert.Equal(2d,points.Max(p=>p.X));Assert.Equal(2d,points.Max(p=>p.Y));Assert.Equal(1d,points.Max(p=>p.Z));
        Assert.False(scene.Parts[0].Texture!.UseAlpha);Assert.Equal(.6,scene.Parts[1].Material.Alpha);
        string archive=Path.Combine(root!,"fixtures","mesh-obj-multiple.zip");using var frozen=ObjSourceSnapshot.OpenForSelection(archive);
        Assert.Equal(new[]{"alternate.obj","mesh-obj/scene.obj"},frozen.CandidateEntries);frozen.SelectEntry("mesh-obj/scene.obj");
        var packaged=ObjMeshReader.Read(frozen,new(1,"Z"));Assert.Equal(scene.Parts[0].Texture!.Sha256,packaged.Parts[0].Texture!.Sha256);
        Assert.Equal(5,packaged.Parts.Sum(x=>x.FaceCounts.Length));Assert.Single(ObjMeshReader.Read(archive,new(1,"Z","alternate.obj")).Parts[0].FaceCounts);
    }
    private static GaussianVector Point(UsdMeshPrimitive part,int i)=>part.Transform.TransformPoint(new(part.Positions[i*3],part.Positions[i*3+1],part.Positions[i*3+2]));
    private static GaussianVector NormalAt(UsdMeshPrimitive part,GaussianVector point)
    {int i=Enumerable.Range(0,part.Positions.Length/3).First(i=>Point(part,i)==point);return new(part.Normals![i*3],part.Normals[i*3+1],part.Normals[i*3+2]);}
    private sealed class Files:IDisposable
    {
        internal string Root{get;}=Path.Combine(Path.GetTempPath(),"aholo-obj-compile-test-"+Guid.NewGuid().ToString("N"));
        internal Files()=>Directory.CreateDirectory(Root);
        internal string Text(string name,string value){string path=Path.Combine(Root,name);File.WriteAllText(path,value);return path;}
        internal void Image()=>File.WriteAllBytes(Path.Combine(Root,"paint.png"),Convert.FromBase64String(Png));
        internal UsdMeshScene Read(string obj,ObjImportOptions? options=null)=>ObjMeshReader.Read(Text("model.obj",obj),options??new(1,"Z"));
        internal ObjMaterial Material(string statements)
        {Text("paint.mtl","newmtl a\n"+statements);using var source=ObjSourceSnapshot.Create(Text("model.obj","mtllib paint.mtl\n"+Triangle+"usemtl a\nf 1 2 3\n"));return ObjMaterials.Read(source,ObjSyntax.Read(source))["a"];}
        public void Dispose()=>Directory.Delete(Root,true);
    }
}
