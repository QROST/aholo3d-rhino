using System.Security.Cryptography;
using Aholo.Core;

namespace Aholo.Tests;

public sealed class GaussianReferenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aholo-reference-" + Guid.NewGuid().ToString("N"));
    public GaussianReferenceTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private static GaussianReferenceMetadata? Resolve(Dictionary<string, string?> values)
        => GaussianReferenceMetadata.Resolve(key => values.GetValueOrDefault(key), () => values.Keys);
    private static Dictionary<string, string?> Properties(string prefix, string hash, string? path = null)
        => new() { [prefix + ".Version"] = "1", [prefix + ".Sha256"] = hash, [prefix + ".AssetPath"] = path };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private string Source()
    {
        string path = Path.Combine(directory, "source.ply");
        GaussianPlyWriter.Write(path, new[] { new GaussianSplat(new(1, 2, 3), new(-2, -3, -4), new(1, .2, .3, .4), 2, 3,
            Enumerable.Range(0, 48).Select(i => (float)(i - 20) / 15).ToArray()) });
        return path;
    }

    [Fact]
    public void NativeReferencesDoNotEnumerateUnrelatedMetadata()
    {
        var values = Properties("Aholo.GS", new string('A', 64));
        var reference = GaussianReferenceMetadata.Resolve(key => values.GetValueOrDefault(key), () => throw new Exception("Must use native fast path"));
        Assert.NotNull(reference); Assert.True(reference.IsNative); Assert.Equal(new string('a',64),reference.Hash);
    }

    [Fact]
    public void CompatibleNamespaceIsResolvedWithoutChangingMetadataOrPlacementFields()
    {
        var values = Properties("LegacyViewer.GS", new string('B',64), Path.Combine(directory,"source.ply"));
        values["LegacyViewer.GS.InitialScale"] = "10"; values["LegacyViewer.GS.SourceUpAxis"] = "Y";
        var before = values.ToArray(); var reference = Resolve(values);
        Assert.NotNull(reference); Assert.False(reference.IsNative); Assert.Equal("LegacyViewer.GS",reference.Prefix);
        Assert.Equal(new string('b',64),reference.Hash); Assert.Equal(values["LegacyViewer.GS.AssetPath"],reference.AssetPath);
        Assert.Equal(before, values.ToArray());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("2")]
    [InlineData("01")]
    public void UnsupportedVersionsAreNotGuessed(string version)
    {
        var values = Properties("LegacyViewer.GS",new string('a',64)); values["LegacyViewer.GS.Version"]=version;
        Assert.Null(Resolve(values));
    }

    [Fact]
    public void HashAndPathCannotBeTakenFromAnotherNamespace()
    {
        var values = new Dictionary<string,string?> { ["ProducerA.GS.Version"]="1", ["ProducerB.GS.Sha256"]=new string('a',64), ["ProducerB.GS.AssetPath"]="/tmp/model.ply" };
        Assert.Null(Resolve(values));
        values["ProducerA.GS.Sha256"]="../bad"; Assert.Null(Resolve(values));
    }

    [Fact]
    public void AmbiguousForeignReferencesAreRejectedAndExplicitNativeMarkerOwnsSelection()
    {
        var values = Properties("ProducerA.GS",new string('a',64));
        foreach(var pair in Properties("ProducerB.GS",new string('b',64))) values.Add(pair.Key,pair.Value);
        Assert.Null(Resolve(values));
        foreach(var pair in Properties("Aholo.GS",new string('c',64))) values.Add(pair.Key,pair.Value);
        Assert.True(Resolve(values)!.IsNative); Assert.Equal(new string('c',64),Resolve(values)!.Hash);
        values["Aholo.GS.Version"]="2"; Assert.Null(Resolve(values));
    }

    [Fact]
    public void FullCompatibleAssetIsVerifiedCachedAndReopensWithoutTheOriginal()
    {
        string source=Source(), cache=Path.Combine(directory,"cache"), hash=Hash(source);
        byte[] original=File.ReadAllBytes(source);
        var reference=Resolve(Properties("LegacyViewer.GS",hash.ToUpperInvariant(),source))!;
        GaussianReferenceData loaded=GaussianReferenceAsset.Load(reference,cache);
        Assert.True(loaded.CopiedToCache); Assert.Equal(original,File.ReadAllBytes(loaded.Path)); Assert.Equal(original,File.ReadAllBytes(source));
        GaussianSplat splat=Assert.Single(loaded.Splats);
        Assert.Equal(new GaussianVector(1,2,3),splat.Center); Assert.Equal(3,splat.Degree); Assert.Equal(48,splat.Coefficients.Length);
        File.Delete(source);
        GaussianReferenceData reopened=GaussianReferenceAsset.Load(reference,cache);
        Assert.False(reopened.CopiedToCache); Assert.Equal(splat.Coefficients.ToArray(),Assert.Single(reopened.Splats).Coefficients.ToArray());
        Assert.Single(Directory.GetFiles(cache));
    }

    [Fact]
    public void WrongHashDoesNotPublishOrChangeSource()
    {
        string source=Source(), cache=Path.Combine(directory,"cache"); byte[] original=File.ReadAllBytes(source);
        Assert.Throws<IOException>(()=>GaussianReferenceAsset.Load(new("LegacyViewer.GS",new string('0',64),source),cache));
        Assert.Equal(original,File.ReadAllBytes(source)); Assert.Empty(Directory.GetFiles(cache));
    }

    [Fact]
    public void CorrectHashStillRequiresFullGaussianData()
    {
        string source=Path.Combine(directory,"points.ply"),cache=Path.Combine(directory,"cache");
        File.WriteAllText(source,"ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nproperty float y\nproperty float z\nend_header\n0 0 0\n");
        Assert.Throws<InvalidDataException>(()=>GaussianReferenceAsset.Load(new("LegacyViewer.GS",Hash(source),source),cache));
        Assert.Empty(Directory.GetFiles(cache));
    }

    [Theory]
    [InlineData("relative.ply")]
    [InlineData("https://example.test/model.ply")]
    [InlineData("\\\\server\\model.ply")]
    [InlineData("//server/model.ply")]
    public void NonlocalOrRelativeReferencesAreNotFollowed(string source)
        => Assert.Throws<IOException>(()=>GaussianReferenceAsset.Load(new("LegacyViewer.GS",new string('a',64),source),Path.Combine(directory,"cache")));

    [Fact]
    public void MissingAndCorruptCachesHaveExplicitErrorsAndRemainUnchanged()
    {
        string source=Source(),cache=Path.Combine(directory,"cache"),hash=Hash(source);
        var reference=new GaussianReferenceMetadata("LegacyViewer.GS",hash,source);
        string destination=GaussianReferenceAsset.Load(reference,cache).Path;
        File.WriteAllText(destination,"corrupt");
        Assert.Throws<IOException>(()=>GaussianReferenceAsset.Load(reference,cache)); Assert.Equal("corrupt",File.ReadAllText(destination));
        File.Delete(destination); File.Delete(source);
        Assert.Throws<FileNotFoundException>(()=>GaussianReferenceAsset.Load(reference,cache));
    }

    [Fact]
    public void CancellationDoesNotCreateCacheOrChangeSource()
    {
        string source=Source(),cache=Path.Combine(directory,"cache");
        Assert.Throws<OperationCanceledException>(()=>GaussianReferenceAsset.Load(new("LegacyViewer.GS",Hash(source),source),cache,new CancellationToken(true)));
        Assert.False(Directory.Exists(cache)); Assert.True(File.Exists(source));
    }
}
