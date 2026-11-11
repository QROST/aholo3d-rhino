using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Aholo.Core;

namespace Aholo.Tests;

public sealed class ObjInputTests
{
    private const string Triangle="v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
    [Fact]
    public void SyntaxRetainsNegativeCornerIndicesAtEachFaceAndIndependentAttributes()
    {
        using var files=new Files();string path=files.Text("mesh.obj","""
            v 1000000000000.125 0 0
            v 1000000000001.125 0 0
            v 1000000000000.125 1 0
            vt 0 0
            vt 1 0
            vt 0 1
            vn 0 0 2
            o same name
            g left right left
            s 7
            usemtl Blue
            f -3/-2/-1 -2/-1/-1 -1/-3/-1
            v 9 9 9
            vt .25 .75
            vn 1 0 0
            o same name
            g right
            s off
            f 1/4/2 3/3/1 4/1/2
            """);
        using var captured=ObjSourceSnapshot.Create(path);var data=ObjSyntax.Read(captured);
        Assert.Equal(1e12+.125,data.Positions[0].X);Assert.Equal(2,data.Faces.Length);
        var first=data.Faces[0];Assert.Equal(new[]{new ObjCorner(0,1,0),new(1,2,0),new(2,0,0)},first.Corners);
        Assert.Equal(new[]{"left","right"},first.Groups);Assert.Equal("same name",first.ObjectName);Assert.Equal("Blue",first.Material);Assert.Equal(7,first.SmoothingGroup);
        Assert.Equal(1,first.ObjectOrdinal);Assert.Equal(2,data.Faces[1].ObjectOrdinal);
        Assert.Equal(0,data.Faces[1].SmoothingGroup);Assert.Equal(new ObjCorner(3,0,1),data.Faces[1].Corners[2]);
        Assert.Equal(new GaussianVector(0,0,2),data.Normals[0]);Assert.Equal((.25,.75),data.TexCoords[3]);
    }
    [Fact]
    public void SupportsContinuationsLineEndingsQuotesApostrophesAndDefaultTextureComponents()
    {
        using var files=new Files();files.Text("Bob's material #1.mtl","newmtl paint\nKd 1 0 0\n");
        string text="mtllib \"Bob's material #1.mtl\" # comment\r\nv 0 0 0 1\rv 1 0 0\nv 0 1 0\nvt .25\nvt .5 .75 0\nvt 1 1\ng Bob's group\ns on\nf 1/1 \\\n 2/2 3/3\n";
        using var captured=ObjSourceSnapshot.Create(files.Text("mesh.obj",text));var data=ObjSyntax.Read(captured);
        Assert.Equal("Bob's material #1.mtl",Assert.Single(data.MaterialLibraries).Name);
        Assert.Equal((.25,0d),data.TexCoords[0]);Assert.Equal((.5,.75),data.TexCoords[1]);Assert.Equal(1,data.Faces[0].SmoothingGroup);
        Assert.Equal(new[]{"Bob's","group"},data.Faces[0].Groups);Assert.Equal(3,data.Faces[0].Corners.Length);
    }
    [Fact]
    public void FrozenRootAndExplicitDependenciesRetainExactBytesAfterOriginalRemoval()
    {
        using var files=new Files();string path=files.Text("mesh.obj",Triangle+"mtllib materials/a.mtl\n");
        files.Text("materials/a.mtl","newmtl a\nmap_Kd ../images/a.png\n");files.Bytes("images/a.png",new byte[]{1,2,3,4});
        string hash=Hash(File.ReadAllBytes(path));using var captured=ObjSourceSnapshot.Create(path,hash);
        var data=ObjSyntax.Read(captured);var mtl=Assert.Single(data.MaterialLibraries);var image=captured.Resolve(mtl.Name,"../images/a.png");
        File.WriteAllText(path,"corrupt");Directory.Delete(Path.Combine(files.Root,"materials"),true);Directory.Delete(Path.Combine(files.Root,"images"),true);
        Assert.Equal(hash,captured.SourceSha256);Assert.Single(ObjSyntax.Read(captured).Faces);Assert.Equal(new byte[]{1,2,3,4},File.ReadAllBytes(image.Path));
        Assert.Equal(3,captured.DependencyHashes.Count);Assert.All(captured.DependencyHashes,x=>Assert.Matches("^[A-F0-9]{64}$",x));
        string privateRoot=captured.Root().Path;captured.Dispose();Assert.False(File.Exists(privateRoot));Assert.Throws<ObjectDisposedException>(()=>captured.Root());
    }
    [Fact]
    public void ZipRootHasSeparateHashAndOnlyReferencedFilesAreCaptured()
    {
        using var files=new Files();string path=files.Zip(("model/scene.obj",Triangle+"mtllib mats/a.mtl\n"),("model/mats/a.mtl","newmtl a\n"),("unused.txt","retained only in archive"));
        string originalHash=Hash(File.ReadAllBytes(path));using var captured=ObjSourceSnapshot.Create(path);
        var data=ObjSyntax.Read(captured);Assert.True(captured.IsArchive);Assert.Equal("model/scene.obj",captured.EntryName);
        Assert.Equal(originalHash,captured.SourceSha256);Assert.NotEqual(originalHash,captured.Root().Sha256);Assert.Equal(3,captured.DependencyHashes.Count);
        File.Delete(path);Assert.Single(ObjSyntax.Read(captured).Faces);Assert.Equal("model/mats/a.mtl",Assert.Single(data.MaterialLibraries).Name);
    }
    [Fact]
    public void MultipleObjEntriesNeedExplicitSelection()
    {
        using var files=new Files();string path=files.Zip(("one.obj",Triangle),("nested/two.obj",Triangle));
        Assert.Throws<InvalidDataException>(()=>ObjSourceSnapshot.Create(path));
        using var captured=ObjSourceSnapshot.Create(path,entryName:"nested/two.obj");Assert.Single(ObjSyntax.Read(captured).Faces);
        Assert.Throws<InvalidDataException>(()=>ObjSourceSnapshot.Create(path,entryName:"absent.obj"));
    }
    [Fact]
    public void FrozenArchiveSelectionSurvivesOriginalReplacementAndCannotBeChanged()
    {
        using var files=new Files();string path=files.Zip(("one.obj",Triangle),("nested/two.obj",Triangle));
        string hash=Hash(File.ReadAllBytes(path));using var source=ObjSourceSnapshot.OpenForSelection(path,hash);
        Assert.Equal(new[]{"nested/two.obj","one.obj"},source.CandidateEntries);
        Assert.Throws<InvalidOperationException>(()=>source.Root());File.WriteAllText(path,"replaced while choosing");
        source.SelectEntry("nested/two.obj");Assert.Single(ObjSyntax.Read(source).Faces);Assert.Equal(hash,source.SourceSha256);
        source.SelectEntry("nested/two.obj");Assert.Throws<InvalidOperationException>(()=>source.SelectEntry("one.obj"));
        Assert.Throws<NotSupportedException>(()=>((IList<string>)source.CandidateEntries)[0]="changed.obj");
    }
    [Fact]
    public void SelectionHonorsCancellationAndMissingEntryWithoutPoisoningSource()
    {
        using var files=new Files();using var source=ObjSourceSnapshot.OpenForSelection(files.Zip(("one.obj",Triangle)));
        Assert.Throws<OperationCanceledException>(()=>source.SelectEntry("one.obj",new CancellationToken(true)));
        Assert.Throws<InvalidDataException>(()=>source.SelectEntry("missing.obj"));source.SelectEntry("one.obj");Assert.Single(ObjSyntax.Read(source).Faces);
        source.Dispose();Assert.Throws<ObjectDisposedException>(()=>source.SelectEntry("one.obj"));
    }
    [Fact]
    public void StandaloneSourceAlsoExposesOneImmutableCandidate()
    {
        using var files=new Files();using var source=ObjSourceSnapshot.OpenForSelection(files.Text("one.obj",Triangle));
        Assert.Equal("one.obj",Assert.Single(source.CandidateEntries));source.SelectEntry("one.obj");Assert.Single(ObjSyntax.Read(source).Faces);
    }
    [Theory]
    [InlineData("../escape.obj")][InlineData("/root.obj")][InlineData("C:/root.obj")][InlineData("a\\b.obj")]
    [InlineData("a/./b.obj")][InlineData("a//b.obj")][InlineData("CON.obj")][InlineData("a/file. ")]
    public void RejectsNonPortableZipPathsEvenForUnreferencedEntries(string entry)
    {
        using var files=new Files();string path=files.Zip(("mesh.obj",Triangle),(entry,"bad"));
        Assert.Throws<InvalidDataException>(()=>ObjSourceSnapshot.Create(path));
    }
    [Theory]
    [InlineData("same.txt","same.txt")][InlineData("Same.txt","same.txt")][InlineData("caf\u00e9.txt","cafe\u0301.txt")]
    public void RejectsAmbiguousZipNames(string first,string second)
    {using var files=new Files();Assert.Throws<InvalidDataException>(()=>ObjSourceSnapshot.Create(files.Zip(("mesh.obj",Triangle),(first,"a"),(second,"b"))));}
    [Fact]
    public void RejectsFileDirectoryCollisionsAndZipLinks()
    {
        using var files=new Files();Assert.Throws<InvalidDataException>(()=>ObjSourceSnapshot.Create(files.Zip(("mesh.obj",Triangle),("folder","a"),("folder/file.txt","b"))));
        string path=files.Zip(("mesh.obj",Triangle));using(var z=ZipFile.Open(path,ZipArchiveMode.Update)){var e=z.CreateEntry("link");e.ExternalAttributes=unchecked((int)0xA1FF0000);}
        Assert.Throws<InvalidDataException>(()=>ObjSourceSnapshot.Create(path));
    }
    [Theory]
    [InlineData("../outside.mtl")][InlineData("/root.mtl")][InlineData("C:\\root.mtl")][InlineData("https://example.invalid/a.mtl")][InlineData("//server/share.mtl")]
    public void RejectsDependencyEscapeOrRemoteReference(string reference)
    {
        using var files=new Files();using var captured=ObjSourceSnapshot.Create(files.Text("mesh.obj",Triangle+"mtllib "+reference+"\n"));
        Assert.Throws<InvalidDataException>(()=>ObjSyntax.Read(captured));
    }
    [Fact]
    public void RejectsLinkedLocalDependencies()
    {
        if(OperatingSystem.IsWindows())return;
        using var files=new Files();files.Text("real.mtl","newmtl a\n");File.CreateSymbolicLink(Path.Combine(files.Root,"link.mtl"),Path.Combine(files.Root,"real.mtl"));
        using var captured=ObjSourceSnapshot.Create(files.Text("mesh.obj",Triangle+"mtllib link.mtl\n"));Assert.Throws<InvalidDataException>(()=>ObjSyntax.Read(captured));
    }
    [Fact]
    public void HashMismatchPreCancellationAndDisposedSourcesCannotEnterParsing()
    {
        using var files=new Files();string path=files.Text("mesh.obj",Triangle);
        Assert.Throws<InvalidDataException>(()=>ObjSourceSnapshot.Create(path,new string('0',64)));
        using var token=new CancellationTokenSource();token.Cancel();Assert.Throws<OperationCanceledException>(()=>ObjSourceSnapshot.Create("absent.obj",ct:token.Token));
        using var captured=ObjSourceSnapshot.Create(path);Assert.Throws<OperationCanceledException>(()=>ObjSyntax.Read(captured,token.Token));
        Assert.Throws<OperationCanceledException>(()=>captured.Resolve(captured.EntryName,"absent.mtl",token.Token));
    }
    [Theory]
    [InlineData("f 0 2 3")][InlineData("f -4 2 3")][InlineData("f 1 2 4")][InlineData("f 1 2")]
    [InlineData("f 1/ 2/ 3/")][InlineData("f 1/1/ 2/1/ 3/1/")][InlineData("f 1//1 2 3")]
    [InlineData("v NaN 0 0")][InlineData("v 0 Infinity 0")][InlineData("v 0 0 0 2")][InlineData("v 0 0 0 1 0 0")]
    [InlineData("vn 0 0 0")][InlineData("vt 0 0 1")][InlineData("s -1")][InlineData("s smooth")]
    [InlineData("l 1 2")][InlineData("curv 0 1 1 2 3")][InlineData("usemap texture")][InlineData("call other.obj")][InlineData("csh command")]
    public void UnsupportedOrMalformedContentCannotSilentlyDisappear(string statement)
    {
        using var files=new Files();using var captured=ObjSourceSnapshot.Create(files.Text("mesh.obj",Triangle+"vt 0 0\nvn 0 0 1\n"+statement+"\n"));
        Assert.Throws<InvalidDataException>(()=>ObjSyntax.Read(captured));
    }
    [Fact]
    public void NoArtificialFaceCornerLimitAndMalformedUtf8Fails()
    {
        using var files=new Files();int count=10000;var text=new StringBuilder();
        for(int i=0;i<count;i++)text.Append("v ").Append(i).Append(" 0 0\n");
        text.Append("f ").AppendJoin(' ',Enumerable.Range(1,count));
        using(var captured=ObjSourceSnapshot.Create(files.Text("large.obj",text.ToString())))Assert.Equal(count,Assert.Single(ObjSyntax.Read(captured).Faces).Corners.Length);
        using var invalid=ObjSourceSnapshot.Create(files.Bytes("bad.obj",new byte[]{0x76,0x20,0xff,0x0a}));
        Assert.Throws<DecoderFallbackException>(()=>ObjSyntax.Read(invalid));
    }
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private sealed class Files:IDisposable
    {
        public string Root{get;}=Path.Combine(Path.GetTempPath(),"aholo-obj-tests-"+Guid.NewGuid().ToString("N"));
        public Files()=>Directory.CreateDirectory(Root);
        public string Text(string name,string data)=>Bytes(name,Encoding.UTF8.GetBytes(data));
        public string Bytes(string name,byte[] data){string path=Path.Combine(Root,name);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,data);return path;}
        public string Zip(params (string Name,string Content)[] entries)
        {
            string path=Path.Combine(Root,Guid.NewGuid()+".zip");using var z=ZipFile.Open(path,ZipArchiveMode.Create);
            foreach(var item in entries){var entry=z.CreateEntry(item.Name);using var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false));writer.Write(item.Content);}return path;
        }
        public void Dispose()=>Directory.Delete(Root,true);
    }
}
