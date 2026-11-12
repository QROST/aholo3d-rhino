using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aholo3D.Core;
namespace Aholo3D.Tests;

public sealed class ThreeMfReaderTests : IDisposable
{
    private const string Core="http://schemas.microsoft.com/3dmanufacturing/core/2015/02",Mat="http://schemas.microsoft.com/3dmanufacturing/material/2015/02";
    private const string Rel="http://schemas.openxmlformats.org/package/2006/relationships",TextureRel="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dtexture";
    private const string Png="iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAF0lEQVR4nAXBAQEAAACCIKb33EAkQxUHQdUHepwfNGcAAAAASUVORK5CYII=";
    private const string Vertices="<vertices><vertex x='0' y='0' z='0'/><vertex x='2' y='0' z='0'/><vertex x='0' y='3' z='0'/></vertices>";
    private const string Colors="<basematerials id='1'><base name='red' displaycolor='#FF000080'/><base name='blue' displaycolor='#0000FF'/></basematerials>";
    private const string Texture="<m:texture2d id='1' path='/3D/Textures/checker.png' contenttype='image/png'/><m:texture2dgroup id='2' texid='1'><m:tex2coord u='0' v='0'/><m:tex2coord u='1' v='0'/><m:tex2coord u='0' v='1'/></m:texture2dgroup>";
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo3d-reader3mf-"+Guid.NewGuid());
    private static string Mesh(string triangles="<triangle v1='0' v2='1' v3='2'/>",string defaults="pid='1' pindex='0'",string vertices=Vertices)=>$"<object id='10' name='part' type='surface' {defaults}><mesh>{vertices}<triangles>{triangles}</triangles></mesh></object>";
    private static string Model(string resources,string build="<item objectid='10'/>",string unit="millimeter",string attributes="")=>$"<model xmlns='{Core}' xmlns:m='{Mat}' unit='{unit}' {attributes}><resources>{resources}</resources><build>{build}</build></model>";
    private string Package(string model,bool image=false,string? imageRelations=null,string? types=null,string entryName="3D/scene.model")
    {
        Directory.CreateDirectory(root);string path=Path.Combine(root,Guid.NewGuid()+".3mf");
        using var archive=ZipFile.Open(path,ZipArchiveMode.Create);
        void Text(string name,string value){using var writer=new StreamWriter(archive.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(value);}
        Text("[Content_Types].xml",types??"<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='model' ContentType='application/vnd.ms-package.3dmanufacturing-3dmodel+xml'/><Default Extension='png' ContentType='application/vnd.ms-package.3dmanufacturing-3dmodeltexture'/></Types>");
        Text("_rels/.rels",$"<Relationships xmlns='{Rel}'><Relationship Id='start' Type='http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel' Target='/{entryName}'/></Relationships>");
        Text(entryName,model);
        if(image)
        {
            using(var output=archive.CreateEntry("3D/Textures/checker.png").Open())output.Write(Convert.FromBase64String(Png));
            string directory=Path.GetDirectoryName(entryName)!.Replace('\\','/'),filename=Path.GetFileName(entryName);
            Text(directory+"/_rels/"+filename+".rels",imageRelations??$"<Relationships xmlns='{Rel}'><Relationship Id='image' Type='{TextureRel}' Target='/3D/Textures/checker.png'/></Relationships>");
        }
        return path;
    }
    [Fact]
    public void BaseColorAlphaSourceIdentityAndFacetNormalsAreRetained()
    {
        string path=Package(Model(Colors+Mesh()));byte[] original=File.ReadAllBytes(path);var scene=ThreeMfReader.Read(path);
        var part=Assert.Single(scene.Parts);Assert.Equal(new MeshMaterial(1,0,0,128/255d,ColorEncoding:MeshColorEncoding.Srgb),part.Material);
        Assert.Equal(new float[]{0,0,1,0,0,1,0,0,1},part.Normals);Assert.True(part.GeneratedFacetNormals);Assert.Equal("Z",scene.UpAxis);Assert.Equal(.001,scene.MetersPerUnit);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)),scene.SourceSha256);Assert.Equal(scene.SourceSha256,Assert.Single(scene.DependencyHashes));Assert.Equal("3D/scene.model",scene.SourceEntry);Assert.Equal(original,File.ReadAllBytes(path));
        Assert.Equal(new GaussianVector(1,1.5,0),part.Transform.Translation);
    }
    [Theory]
    [InlineData("p1='0' p2='1' p3='2'",0,0,1,0,0,1)]
    [InlineData("p1='1' p3='2'",1,0,1,0,0,1)]
    [InlineData("p1='1' p2='2'",1,0,0,1,1,0)]
    [InlineData("p1='2'",0,1,0,1,0,1)]
    [InlineData("",0,0,0,0,0,0)]
    [InlineData("pid='2' p2='1' p3='2'",0,0,0,0,0,0)]
    public void CornerDefaultsUseObjectPropertyThenIndependentP1Fallback(string properties,float u0,float v0,float u1,float v1,float u2,float v2)
    {
        var scene=ThreeMfReader.Read(Package(Model(Texture+Mesh($"<triangle v1='0' v2='1' v3='2' {properties}/>","pid='2' pindex='0'")),true));
        Assert.Equal(new[]{u0,v0,u1,v1,u2,v2},scene.Parts[0].TexCoords);Assert.False(scene.Parts[0].Texture!.UseAlpha);
    }
    [Fact]
    public void TextureBytesLowerLeftUvAndIgnoredAlphaSurviveTwoInstances()
    {
        string path=Package(Model(Texture+Mesh("<triangle v1='0' v2='1' v3='2' p1='0' p2='1' p3='2'/>","pid='2' pindex='0'"),"<item objectid='10'/><item objectid='10' transform='1 0 0 0 1 0 0 0 1 10 0 0'/>",attributes:"requiredextensions='m'"),true);
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(path)); // Legacy native route stays strict.
        var scene=ThreeMfReader.Read(path);Assert.Equal(2,scene.Parts.Count);Assert.NotEqual(scene.Parts[0].PrimPath,scene.Parts[1].PrimPath);
        Assert.Same(scene.Parts[0].Texture,scene.Parts[1].Texture);Assert.Same(scene.Parts[0].Positions,scene.Parts[1].Positions);
        var image=scene.Parts[0].Texture!;Assert.True(image.HasAlpha);Assert.False(image.UseAlpha);using var bytes=new MemoryStream();image.CopyEncodedTo(bytes);Assert.Equal(Convert.FromBase64String(Png),bytes.ToArray());
        Assert.Equal(10,scene.Parts[1].Transform.Translation.X-scene.Parts[0].Transform.Translation.X);
    }
    [Theory]
    [InlineData("wrap",MeshTextureWrap.Repeat)][InlineData("clamp",MeshTextureWrap.Clamp)][InlineData("none",MeshTextureWrap.Clamp)]
    public void DirectTextureTilingHonorsOpaqueLayerSemantics(string style,MeshTextureWrap expected)
    {
        string texture=Texture.Replace("contenttype='image/png'","contenttype='image/png' tilestyleu='"+style+"' filter='nearest'");
        var part=ThreeMfReader.Read(Package(Model(texture+Mesh(defaults:"pid='2' pindex='0'")),true)).Parts[0];
        Assert.Equal(expected,part.Texture!.WrapU);Assert.Equal(MeshTextureFilter.Nearest,part.Texture.MinFilter);
    }
    [Fact]
    public void ColorGroupsForceOpaqueButDoNotFlattenGradients()
    {
        const string colors="<m:colorgroup id='1'><m:color color='#80808000'/><m:color color='#FF000080'/></m:colorgroup>";
        var part=ThreeMfReader.Read(Package(Model(colors+Mesh()))).Parts[0];Assert.Equal(1,part.Material.Alpha);Assert.Equal(128/255d,part.Material.R);Assert.Equal(MeshColorEncoding.Srgb,part.Material.ColorEncoding);
        Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(Model(colors+Mesh("<triangle v1='0' v2='1' v3='2' p1='0' p2='1' p3='0'/>")))));
    }
    [Fact]
    public void FaceMaterialSplittingRetainsGeometryAssociationAndUvSeams()
    {
        var scene=ThreeMfReader.Read(Package(Model(Colors+Mesh("<triangle v1='0' v2='1' v3='2' p1='1'/><triangle v1='0' v2='2' v3='1' p1='0'/>"))));
        Assert.Equal(2,scene.Parts.Count);Assert.Equal(1,scene.Parts[0].Material.B);Assert.Equal(1,scene.Parts[1].Material.R);
        Assert.Equal(1,scene.Parts[0].Normals![2]);Assert.Equal(-1,scene.Parts[1].Normals![2]);
        var textured=ThreeMfReader.Read(Package(Model(Texture+Mesh("<triangle v1='0' v2='1' v3='2' p1='0' p2='1' p3='2'/><triangle v1='0' v2='2' v3='1' p1='2' p2='0' p3='1'/>","pid='2' pindex='0'")),true));
        Assert.Equal(6,textured.Parts[0].Positions.Length/3);Assert.Equal(new float[]{0,0,1,0,0,1,0,1,0,0,1,0},textured.Parts[0].TexCoords);
    }
    [Fact]
    public void NonCommutingNestedRowMatricesKeepUnitsAndReflectionExactlyOnce()
    {
        string children="<object id='20'><components><component objectid='10' transform='0 1 0 -1 0 0 0 0 1 5 7 0'/></components></object><object id='30'><components><component objectid='20' transform='2 0 0 0 3 0 0 0 1 1 2 0'/></components></object>";
        var scene=ThreeMfReader.Read(Package(Model(Colors+Mesh()+children,"<item objectid='30' transform='-1 0 0 0 1 0 0 0 1 100 200 10'/>","centimeter")));
        var part=Assert.Single(scene.Parts);var transform=scene.ToMillimetres(part.Transform);var actual=Enumerable.Range(0,3).Select(i=>transform.TransformPoint(new(part.Positions[i*3],part.Positions[i*3+1],part.Positions[i*3+2]))).ToArray();
        Assert.Equal(new[]{new GaussianVector(890,2230,100),new(890,2290,100),new(950,2230,100)},actual);Assert.False(part.LeftHanded);Assert.Equal(new[]{0,1,2},part.Indices);
    }
    [Fact]
    public void DeepRepeatedComponentsHaveNoFixedDepthLimitAndUnbuiltObjectsStayOut()
    {
        var resources=new StringBuilder(Colors+Mesh());int previous=10;
        for(int i=0;i<520;i++){int id=20+i;resources.Append($"<object id='{id}'><components><component objectid='{previous}'/></components></object>");previous=id;}
        var scene=ThreeMfReader.Read(Package(Model(resources.ToString(),$"<item objectid='{previous}'/><item objectid='{previous}'/>")));Assert.Equal(2,scene.Parts.Count);
    }
    [Fact]
    public void LargeOriginIsRetainedBeforeFloatPositions()
    {
        string vertices="<vertices><vertex x='1000000000000' y='0' z='0'/><vertex x='1000000000002' y='0' z='0'/><vertex x='1000000000000' y='3' z='0'/></vertices>";
        var part=ThreeMfReader.Read(Package(Model(Colors+Mesh(vertices:vertices)))).Parts[0];Assert.Equal(1000000000001,part.Transform.Translation.X);Assert.Equal(new float[]{-1,-1.5f,0,1,-1.5f,0,-1,1.5f,0},part.Positions);
    }
    [Theory]
    [InlineData("micron",.000001)][InlineData("millimeter",.001)][InlineData("centimeter",.01)][InlineData("inch",.0254)][InlineData("foot",.3048)][InlineData("meter",1)]
    public void AllCoreUnitsArePhysical(string unit,double expected)=>Assert.Equal(expected,ThreeMfReader.Read(Package(Model(Colors+Mesh(),unit:unit))).MetersPerUnit);
    [Fact]
    public void HashMismatchAndPreCancellationLeaveOriginalIntact()
    {
        string path=Package(Model(Colors+Mesh()));byte[] original=File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(path,new string('0',64)));
        Assert.Throws<OperationCanceledException>(()=>ThreeMfReader.Read(path,ct:new CancellationToken(true)));
        Assert.Equal(original,File.ReadAllBytes(path));
    }
    [Fact]
    public void RootRelationshipSelectsActualUtf8ModelEntry()
    {var scene=ThreeMfReader.Read(Package(Model(Colors+Mesh()),entryName:"Models/模型.model"));Assert.Equal("Models/模型.model",scene.SourceEntry);}
    [Theory]
    [InlineData("<object id='1'><components><component objectid='10'/></components></object>")]
    [InlineData("<object id='10'><components><component objectid='10'/></components></object>")]
    [InlineData("<object id='10'><components><component objectid='999'/></components></object>")]
    [InlineData("<object id='10' pid='1' pindex='0'><components><component objectid='1'/></components></object>")]
    public void DuplicateForwardCyclicAndWrongTypeReferencesAreRejected(string value)=>Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(Model(Colors+value))));
    [Theory]
    [InlineData("<triangle v1='0' v2='1' v3='3'/>")]
    [InlineData("<triangle v1='0' v2='1' v3='2' p1='7'/>")]
    [InlineData("<triangle v1='0' v2='1' v3='2' p1='0' p2='1'/>")]
    [InlineData("<triangle v1='0' v2='1' v3='2' pid='999' p1='0'/>")]
    [InlineData("<triangle v1='0' v2='0' v3='2'/>")]
    public void InvalidIndicesOrBaseMaterialGradientsDoNotYieldPartialScene(string triangle)=>Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(Model(Colors+Mesh(triangle)))));
    [Fact]
    public void DuplicateContainersUnknownElementsAndEmptyBuildFail()
    {
        string mesh=Mesh();
        foreach(string model in new[]{Model(Colors+mesh.Replace("</object>","<mesh/> </object>")),Model(Colors+mesh,build:""),Model(Colors+mesh+"<m:multiproperties id='90'/>"),Model(Colors+mesh,attributes:"requiredextensions='unknown'"),Model(Colors+mesh).Replace("</model>","<build/></model>")})
            Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(model)));
    }
    [Fact]
    public void MissingTextureRelationshipAndUnsupportedSamplerFail()
    {
        string model=Model(Texture+Mesh(defaults:"pid='2' pindex='0'"));
        Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(model,true,$"<Relationships xmlns='{Rel}'/>")));
        Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(model.Replace("contenttype='image/png'","contenttype='image/png' tilestyleu='mirror'"),true)));
        Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(model.Replace("/3D/Textures/checker.png","https://example.invalid/image.png"),true)));
    }
    [Fact]
    public void ExistingCoreFixtureHasExpectedWorldBoundsAndTriangleCount()
    {
        string? project=AppContext.BaseDirectory;while(project is not null&&!Directory.Exists(Path.Combine(project,"fixtures")))project=Directory.GetParent(project)?.FullName;
        Assert.NotNull(project);var scene=ThreeMfReader.Read(Path.Combine(project!,"fixtures","mesh-core.3mf"));Assert.Equal(8,scene.Parts.Sum(p=>p.FaceCounts.Length));
        var world=scene.Parts.SelectMany(p=>Enumerable.Range(0,p.Positions.Length/3).Select(i=>scene.ToMillimetres(p.Transform).TransformPoint(new(p.Positions[i*3],p.Positions[i*3+1],p.Positions[i*3+2])))).ToArray();
        Assert.Equal(new GaussianVector(0,20,0),new(world.Min(p=>p.X),world.Min(p=>p.Y),world.Min(p=>p.Z)));Assert.Equal(new GaussianVector(30,30,10),new(world.Max(p=>p.X),world.Max(p=>p.Y),world.Max(p=>p.Z)));
    }
    [Theory]
    [InlineData("application/vnd.ms-package.3dmanufacturing-3dmodeltexture",true)]
    [InlineData("image/png",true)]
    [InlineData("image/jpeg",false)]
    [InlineData("application/octet-stream",false)]
    public void TexturePackageMimeMatchesGenericOrDeclaredImage(string mime,bool supported)
    {
        string types="<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='model' ContentType='application/vnd.ms-package.3dmanufacturing-3dmodel+xml'/><Default Extension='png' ContentType='"+mime+"'/></Types>";
        string path=Package(Model(Texture+Mesh(defaults:"pid='2' pindex='0'")),true,types:types);
        if(supported)Assert.Equal("image/png",Assert.Single(ThreeMfReader.Read(path).Parts).Texture!.MimeType);
        else Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(path));
    }
    [Fact]
    public void UnreferencedDistantVerticesDoNotReduceUsedTrianglePrecision()
    {
        var baseline=ThreeMfReader.Read(Package(Model(Colors+Mesh()))).Parts[0];
        var distant=ThreeMfReader.Read(Package(Model(Colors+Mesh(vertices:Vertices.Replace("</vertices>","<vertex x='1e12' y='-1e15' z='1e20'/></vertices>"))))).Parts[0];
        Assert.Equal(baseline.Positions,distant.Positions);Assert.Equal(baseline.Transform,distant.Transform);Assert.Equal(baseline.Normals,distant.Normals);
    }
    [Fact]
    public void DuplicateComponentsCannotSilentlyDropInstances()
    {
        string components="<object id='20'><components><component objectid='10'/></components><components><component objectid='10' transform='1 0 0 0 1 0 0 0 1 10 0 0'/></components></object>";
        Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(Model(Colors+Mesh()+components,"<item objectid='20'/>"))));
    }
    [Fact]
    public void RecommendedSupportedExtensionWorksButUnsupportedOrDuplicateDoesNot()
    {
        Assert.Single(ThreeMfReader.Read(Package(Model(Texture+Mesh(defaults:"pid='2' pindex='0'"),attributes:"recommendedextensions='m'"),true)).Parts);
        foreach(string attributes in new[]{"recommendedextensions='unknown'","requiredextensions='m' recommendedextensions='m'"})
            Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(Model(Colors+Mesh(),attributes:attributes))));
    }
    [Fact]
    public void TextureForwardReferenceRemainsInvalidUnderCoreElementOrdering()
    {
        int split=Texture.IndexOf("<m:texture2dgroup",StringComparison.Ordinal);
        string reordered=Texture[split..]+Texture[..split];
        Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(Model(reordered+Mesh(defaults:"pid='2' pindex='0'")),true)));
    }
    [Fact]
    public void MetadataCannotHideDuplicateOrOutOfOrderContent()
    {
        foreach(string metadata in new[]{"<metadatagroup/>","<metadatagroup><metadata name='Title'>Test</metadata></metadatagroup>","<metadatagroup><metadata name='Title'>Test</metadata></metadatagroup><metadatagroup/>"})
            Assert.Throws<InvalidDataException>(()=>ThreeMfReader.Read(Package(Model(Colors+Mesh().Replace("</object>",metadata+"</object>")))));
        string valid=Mesh().Replace("<mesh>","<metadatagroup><metadata name='Title'>Test</metadata></metadatagroup><mesh>");
        Assert.Single(ThreeMfReader.Read(Package(Model(Colors+valid))).Parts);
    }
    [Fact]
    public void PackagedTexturedFixturePreservesPerFaceImagesUvsTransformsAndOpacity()
    {
        string? project=AppContext.BaseDirectory;while(project is not null&&!Directory.Exists(Path.Combine(project,"fixtures")))project=Directory.GetParent(project)?.FullName;
        Assert.NotNull(project);string path=Path.Combine(project!,"fixtures","mesh-textured.3mf");var scene=ThreeMfReader.Read(path);
        Assert.Equal(3,scene.Parts.Count);Assert.Equal(5,scene.Parts.Sum(p=>p.FaceCounts.Length));Assert.Equal(.01,scene.MetersPerUnit);
        using var archive=ZipFile.OpenRead(path);
        for(int index=0;index<2;index++)
        {
            var part=scene.Parts[index];Assert.NotNull(part.Texture);Assert.False(part.Texture!.UseAlpha);Assert.True(part.Texture.HasAlpha);
            Assert.Equal(MeshTextureFilter.Nearest,part.Texture.MinFilter);Assert.Equal(MeshTextureWrap.Clamp,part.Texture.WrapU);
            Assert.Equal(new float[]{0,0,1,0,1,1,0,0,1,1,0,1},part.TexCoords);
            using var image=archive.GetEntry("3D/Textures/"+(index==0?"checker":"alternate")+".png")!.Open();
            using var expected=new MemoryStream();image.CopyTo(expected);using var actual=new MemoryStream();part.Texture.CopyEncodedTo(actual);Assert.Equal(expected.ToArray(),actual.ToArray());
        }
        Assert.NotEqual(scene.Parts[0].Texture!.Sha256,scene.Parts[1].Texture!.Sha256);
        Assert.Null(scene.Parts[2].Texture);Assert.Equal(128/255d,scene.Parts[2].Material.Alpha);
        var world=scene.Parts.SelectMany(p=>Enumerable.Range(0,p.Positions.Length/3).Select(i=>scene.ToMillimetres(p.Transform).TransformPoint(new(p.Positions[i*3],p.Positions[i*3+1],p.Positions[i*3+2])))).ToArray();
        Assert.Equal(new GaussianVector(0,10,0),new(world.Min(p=>p.X),world.Min(p=>p.Y),world.Min(p=>p.Z)));Assert.Equal(new GaussianVector(40,50,0),new(world.Max(p=>p.X),world.Max(p=>p.Y),world.Max(p=>p.Z)));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
