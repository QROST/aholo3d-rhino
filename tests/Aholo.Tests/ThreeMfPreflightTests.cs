using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aholo.Core;
namespace Aholo.Tests;

public sealed class ThreeMfPreflightTests : IDisposable
{
    private const string Core="http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private const string Rel="http://schemas.openxmlformats.org/package/2006/relationships";
    private const string Start="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";
    private const string Types="""
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>
          <Default Extension="png" ContentType="image/png"/>
        </Types>
        """;
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-3mf-tests-"+Guid.NewGuid());
    private static string Relationships(string content)=>$"<Relationships xmlns=\"{Rel}\">{content}</Relationships>";
    private static string Link(string target,string id="main",string? mode=null,string? kind=null)=>
        $"<Relationship Id=\"{id}\" Type=\"{kind??Start}\" Target=\"{target}\"{(mode is null?"":$" TargetMode=\"{mode}\"")}/>";
    private static string Model(string attributes="",string extra="")=>$"""
        <model xmlns="{Core}" unit="millimeter" xml:lang="en-US" {attributes}>
          <resources><basematerials id="1"><base name="Blue" displaycolor="#0000FFFF"/></basematerials>
          <object id="2" type="model" pid="1" pindex="0"><mesh><vertices>
          <vertex x="0" y="0" z="0"/><vertex x="1" y="0" z="0"/><vertex x="0" y="1" z="1"/>
          </vertices><triangles><triangle v1="0" v2="1" v3="2"/></triangles></mesh></object>
          <object id="3" type="model"><components><component objectid="2" transform="1 0 0 0 1 0 0 0 1 2 3 4"/></components></object></resources>
          <build><item objectid="3"/></build>{extra}
        </model>
        """;
    private string Package(string? model=null,string? rels=null,string? types=null,params (string Name,string Content)[] extras)
    {
        Directory.CreateDirectory(root);string path=Path.Combine(root,Guid.NewGuid()+".3mf");
        using var archive=ZipFile.Open(path,ZipArchiveMode.Create);
        void Add(string name,string content){using var writer=new StreamWriter(archive.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(content);}
        Add("[Content_Types].xml",types??Types);Add("_rels/.rels",rels??Relationships(Link("/3D/scene.model")));Add("3D/scene.model",model??Model());
        foreach(var extra in extras)Add(extra.Name,extra.Content);
        return path;
    }
    [Fact]
    public void CoreComponentsAndColorsKeepOriginalHashBytesAndSuffix()
    {
        string source=Package();byte[] original=File.ReadAllBytes(source);
        using var snapshot=NativeMeshImportSnapshot.Create(source,Convert.ToHexString(SHA256.HashData(original)));
        Assert.EndsWith(".3mf",snapshot.Path);Assert.Equal(original,File.ReadAllBytes(snapshot.Path));Assert.Equal(original,File.ReadAllBytes(source));
        Assert.True(MeshPlacementPolicy.IsNativeMeshFormat("3mf"));
        Assert.Equal("3mf",MeshPlacementPolicy.SelectStaticMesh(new(){Artifacts=new[]{new TaskArtifact("model","model","3mf")}}).Format);
        string copy=snapshot.Path;snapshot.Dispose();Assert.False(File.Exists(copy));Assert.True(File.Exists(source));
    }
    [Theory]
    [InlineData("3D/scene.model")]
    [InlineData("./3D/scene.model")]
    [InlineData("/3D/scene.model")]
    public void LegalRootAndRelativeTargetsResolve(string target)=>ThreeMfPreflight.Validate(Package(rels:Relationships(Link(target))));
    [Fact]
    public void RelativeThumbnailAndSpaceEscapedPartsRemainInsidePackage()
    {
        string model=Model().Replace("<object id=\"2\"","<object thumbnail=\"../Metadata/blue%20preview.png\" id=\"2\"");
        ThreeMfPreflight.Validate(Package(model:model,extras:new[]{("3D/_rels/scene.model.rels",Relationships(Link("../Metadata/blue%20preview.png",kind:Rel+"/metadata/thumbnail"))),("Metadata/blue%20preview.png","synthetic bytes") }));
    }
    [Theory]
    [InlineData("../escape.png")]
    [InlineData("3D/../scene.model")]
    [InlineData("/absolute.png")]
    [InlineData("C:/absolute.png")]
    [InlineData("3D\\scene.model")]
    [InlineData("3D//scene.model")]
    [InlineData("3D/%73cene.model")]
    [InlineData("3D/%2escene.model")]
    [InlineData("3D/escape%2fpreview.png")]
    [InlineData("3D/%252e%252e.png")]
    [InlineData("3D/%.png")]
    [InlineData("3D/scene.model.")]
    [InlineData("3d/SCENE.MODEL")]
    public void AmbiguousAndUnsafePartNamesAreRejected(string name)=>
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(extras:new[]{(name,"duplicate or unsafe")})));
    [Theory]
    [InlineData("https://example.invalid/model.model",null)]
    [InlineData("/3D/scene.model","External")]
    [InlineData("//server/share/model.model",null)]
    [InlineData("file:///outside.model",null)]
    [InlineData("../outside.model",null)]
    [InlineData("/3D/missing.model",null)]
    [InlineData("/3D/scene.model#fragment",null)]
    [InlineData("/3D/scene.model?query",null)]
    public void ExternalEscapingOrMissingTargetsAreRejected(string target,string? mode)=>
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(rels:Relationships(Link(target,mode:mode)))));
    [Fact]
    public void AllRelationshipsAndModelPartsAreCheckedEvenWhenUnreachable()
    {
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(extras:new[]{("3D/_rels/scene.model.rels",Relationships(Link("https://example.invalid/texture.png",kind:Rel+"/metadata/thumbnail")))})));
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(extras:new[]{("3D/other.model",Model("xmlns:x=\"urn:unknown\" requiredextensions=\"x\""))})));
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(extras:new[]{("3D/_rels/missing.model.rels",Relationships(Link("scene.model")))})));
    }
    [Theory]
    [InlineData("xmlns:p=\"urn:production\" requiredextensions=\"p\"","")]
    [InlineData("xmlns:p=\"urn:production\" p:uuid=\"abc\"","")]
    [InlineData("xmlns:m=\"urn:material\"","<m:texture2d path=\"https://example.invalid/texture.png\"/>")]
    [InlineData("xmlns:p=\"urn:production\"","<p:component path=\"../x.model\"/>")]
    [InlineData("xml:base=\"https://example.invalid/\"","")]
    public void ExtensionDataIsRejectedInsteadOfSilentlyLosingIt(string attributes,string extra)=>
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(model:Model(attributes,extra))));
    [Fact]
    public void UnusedNamespaceDeclarationsAreNotMistakenForExtensionContent()=>
        ThreeMfPreflight.Validate(Package(model:Model("xmlns:vendor=\"urn:unused\"")));
    [Fact]
    public void XmlAndContainerFailuresNeverModifyTheSource()
    {
        foreach(string model in new[]{"",Model()+"<extra/>","<!DOCTYPE model [<!ENTITY external SYSTEM 'file:///private/never-read'>]>"+Model(extra:"&external;")})
        {
            string source=Package(model:model);byte[] original=File.ReadAllBytes(source);
            Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Create(source,null));Assert.Equal(original,File.ReadAllBytes(source));
        }
        string truncated=Package();File.WriteAllBytes(truncated,File.ReadAllBytes(truncated)[..100]);
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(truncated));
    }
    [Fact]
    public void DuplicateOrMissingPrimaryContentTypesAndIdentityAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(rels:Relationships(Link("3D/scene.model")+Link("3D/scene.model","second")))));
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(rels:Relationships(""))));
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(types:Types.Replace("application/vnd.ms-package.3dmanufacturing-3dmodel+xml","application/octet-stream"))));
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(types:Types.Replace("</Types>","<Default Extension=\"MODEL\" ContentType=\"another/type\"/></Types>"))));
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(extras:new[]{("[Content_Types].xml",Types)})));
    }
    [Fact]
    public void HashAndCancellationRemainAuthoritative()
    {
        string source=Package();Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Create(source,new string('0',64)));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>NativeMeshImportSnapshot.Create(source,null,ct:cancel.Token));
        Assert.Throws<OperationCanceledException>(()=>ThreeMfPreflight.Validate(source,cancel.Token));
        using var snapshot=NativeMeshImportSnapshot.Create(source,null);File.AppendAllText(snapshot.Path,"mutation");
        Assert.Throws<InvalidDataException>(()=>NativeMeshImportSnapshot.Verify(snapshot.Path,snapshot.Sha256));
    }
    [Theory]
    [InlineData("%E6%A8%A1%E5%9E%8B.model")]
    [InlineData("模型.model")]
    [InlineData("caf%C3%A9.model")]
    public void Utf8PartNamesAndRelationshipsRemainUsable(string name)
    {
        string path=Package(rels:Relationships(Link("3D/"+name)),extras:new[]{("3D/"+name,Model())});
        ThreeMfPreflight.Validate(path);
    }
    [Theory]
    [InlineData("%FF")]
    [InlineData("%C2%80")]
    [InlineData("%00")]
    public void InvalidUtf8OrEncodedControlsAreRejected(string name)=>
        Assert.Throws<InvalidDataException>(()=>ThreeMfPreflight.Validate(Package(extras:new[]{("3D/"+name+".model",Model())})));
    [Fact]
    public void NativeAcceptanceFixturePassesTheSamePreflight()
    {
        string? directory=AppContext.BaseDirectory;
        while(directory is not null&&!Directory.Exists(Path.Combine(directory,"fixtures")))directory=Directory.GetParent(directory)?.FullName;
        Assert.NotNull(directory);ThreeMfPreflight.Validate(Path.Combine(directory!,"fixtures","mesh-core.3mf"));
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
