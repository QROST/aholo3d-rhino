using System.Security.Cryptography;
using Aholo3D.Core;

namespace Aholo3D.Tests;

public sealed class GaussianReferenceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aholo3d-reference-" + Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("Aholo.GS")]
    [InlineData("GSRhino.GS")]
    [InlineData("LegacyViewer.GS")]
    public void MarkedInvalidReferencesRemainDistinguishableFromOrdinaryObjects(string prefix)
    {
        var values = Properties(prefix, new string('a', 64));
        bool Marked() => GaussianReferenceMetadata.HasMarker(key => values.GetValueOrDefault(key), () => values.Keys);
        Assert.True(Marked()); Assert.NotNull(Resolve(values));
        values[prefix + ".Version"] = "2";
        Assert.True(Marked()); Assert.Null(Resolve(values));
        values[prefix + ".Version"] = "1"; values[prefix + ".Sha256"] = "invalid";
        Assert.True(Marked()); Assert.Null(Resolve(values));
        values.Clear(); values["ordinary.Version"] = "1";
        Assert.False(Marked()); Assert.Null(Resolve(values));
    }

    [Fact]
    public void IndependentProducerReferenceReusesVerifiedAssetWithoutRelabeling()
    {
        string source = Source(), hash = Hash(source), cache = Path.Combine(directory, "cache");
        var values = Properties("GSRhino.GS", hash.ToUpperInvariant(), source);
        values["GSRhino.GS.Antialiased"] = "true";
        values["GSRhino.GS.SourceUpAxis"] = "Y";
        var before = values.ToArray();
        var reference = Resolve(values)!;
        var loaded = GaussianReferenceAsset.Load(reference, cache);
        Assert.False(reference.IsNative); Assert.Equal(hash, Hash(loaded.Path));
        Assert.Equal("true", values[reference.Prefix + ".Antialiased"]);
        Assert.Equal(before, values.ToArray());
        Assert.DoesNotContain("Aholo.GS.Version", values.Keys);
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
    public void CorruptCacheRecoversFromVerifiedSourceAndKeepsBadBytes()
    {
        string source = Source(), cache = Path.Combine(directory, "cache"), hash = Hash(source);
        byte[] original = File.ReadAllBytes(source);
        var reference = new GaussianReferenceMetadata("LegacyViewer.GS", hash, source);
        string destination = GaussianReferenceAsset.Load(reference, cache).Path;
        File.WriteAllText(destination, "damaged cache");

        GaussianReferenceData recovered = GaussianReferenceAsset.Load(reference, cache);

        Assert.True(recovered.CopiedToCache);
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Equal("damaged cache", File.ReadAllText(Assert.Single(Directory.GetFiles(cache, "*.corrupt-*"))));
        Assert.Equal(3, Assert.Single(recovered.Splats).Degree);
        Assert.Empty(Directory.GetFiles(cache, "*.tmp"));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("same")]
    public void CorruptCacheWithoutValidSourceIsPreserved(string kind)
    {
        string source = Source(), cache = Path.Combine(directory, "cache"), hash = Hash(source);
        string destination = GaussianReferenceAsset.Load(new("Aholo.GS", hash, source), cache).Path;
        File.WriteAllText(destination, "bad target");
        if (kind == "missing") File.Delete(source);
        else if (kind == "invalid") File.WriteAllText(source, "bad source");
        else source = destination;
        var reference = new GaussianReferenceMetadata("Aholo.GS", hash, source);

        Assert.Throws<IOException>(() => GaussianReferenceAsset.Load(reference, cache));

        Assert.Equal("bad target", File.ReadAllText(destination));
        if (kind == "invalid") Assert.Equal("bad source", File.ReadAllText(source));
        Assert.Empty(Directory.GetFiles(cache, "*.corrupt-*"));
        Assert.Empty(Directory.GetFiles(cache, "*.tmp"));
    }

    [Fact]
    public void MissingSourceAndCacheHaveExplicitError()
    {
        string source = Source(), cache = Path.Combine(directory, "cache"), hash = Hash(source);
        File.Delete(source);
        Assert.Throws<FileNotFoundException>(() => GaussianReferenceAsset.Load(new("Aholo.GS", hash, source), cache));
    }

    [Fact]
    public void ValidCacheIsReusedWithoutReplacingItOrReadingMissingSource()
    {
        string source = Source(), cache = Path.Combine(directory, "cache"), hash = Hash(source);
        var reference = new GaussianReferenceMetadata("Aholo.GS", hash, source);
        string destination = GaussianReferenceAsset.Load(reference, cache).Path;
        File.SetLastWriteTimeUtc(destination, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        DateTime stamp = File.GetLastWriteTimeUtc(destination);
        File.Delete(source);

        Assert.False(GaussianReferenceAsset.Load(reference, cache).CopiedToCache);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(destination));
        Assert.Empty(Directory.GetFiles(cache, "*.corrupt-*"));
    }

    [Fact]
    public void PublishingFreshAndExistingContentHasExplicitOwnership()
    {
        string source = Source(), hash = Hash(source), destination = Path.Combine(directory, hash + ".ply");
        string first = Path.Combine(directory, "first.tmp"), second = Path.Combine(directory, "second.tmp");
        File.Copy(source, first); File.Copy(source, second);
        Assert.True(GaussianCachePublisher.Publish(first, destination, hash));
        Assert.False(File.Exists(first));
        File.SetLastWriteTimeUtc(destination, new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        DateTime stamp = File.GetLastWriteTimeUtc(destination);
        Assert.False(GaussianCachePublisher.Publish(second, destination, hash));
        Assert.True(File.Exists(second));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(destination));
        Assert.Equal(hash, Hash(destination)); Assert.Equal(hash, Hash(source));
        Assert.Empty(Directory.GetFiles(directory, "*.corrupt-*"));
    }

    [Fact]
    public void UnverifiedReplacementAndSamePathCannotDestroyExistingData()
    {
        string source = Source(), hash = Hash(source), destination = Path.Combine(directory, hash + ".ply");
        File.WriteAllText(destination, "keep corrupt target");
        string invalid = Path.Combine(directory, "invalid.tmp"); File.WriteAllText(invalid, "invalid replacement");
        Assert.Throws<IOException>(() => GaussianCachePublisher.Publish(invalid, destination, hash));
        Assert.Equal("keep corrupt target", File.ReadAllText(destination));
        Assert.Equal("invalid replacement", File.ReadAllText(invalid));
        Assert.False(GaussianCachePublisher.Publish(source, source, hash));
        Assert.Throws<IOException>(() => GaussianCachePublisher.Publish(destination, destination, hash));
        Assert.Equal(hash, Hash(source)); Assert.Equal("keep corrupt target", File.ReadAllText(destination));
        Assert.Empty(Directory.GetFiles(directory, "*.corrupt-*"));
    }

    [Fact]
    public async Task CancellationWhileWaitingForRepairLockPreservesFilesAndCleansSnapshot()
    {
        string source = Source(), cache = Path.Combine(directory, "cache"), hash = Hash(source);
        var reference = new GaussianReferenceMetadata("Aholo.GS", hash, source);
        string destination = GaussianReferenceAsset.Load(reference, cache).Path;
        File.WriteAllText(destination, "keep on cancellation");
        string lockPath = Path.Combine(cache, ".locks", Path.GetFileName(destination) + ".lock");
        using var held = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource();
        Task<GaussianReferenceData> repair = Task.Run(() => GaussianReferenceAsset.Load(reference, cache, cancellation.Token));
        Assert.True(SpinWait.SpinUntil(() => Directory.GetFiles(cache, "*.tmp").Length != 0, TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await repair.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("keep on cancellation", File.ReadAllText(destination)); Assert.Equal(hash, Hash(source));
        Assert.Empty(Directory.GetFiles(cache, "*.corrupt-*")); Assert.Empty(Directory.GetFiles(cache, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentPublishersRepairOnceAndPreserveTheSameCorruptBackup()
    {
        string source = Source(), hash = Hash(source), destination = Path.Combine(directory, hash + ".ply");
        File.WriteAllText(destination, "one corrupt version");
        string[] candidates = Enumerable.Range(0, 12).Select(i => Path.Combine(directory, $"candidate-{i}.tmp")).ToArray();
        foreach (string candidate in candidates) File.Copy(source, candidate);
        bool[] published = await Task.WhenAll(candidates.Select(candidate => Task.Run(() =>
        {
            try { return GaussianCachePublisher.Publish(candidate, destination, hash); }
            finally { if (File.Exists(candidate)) File.Delete(candidate); }
        })));
        Assert.Equal(1, published.Count(value => value));
        Assert.Equal(hash, Hash(destination)); Assert.Equal(hash, Hash(source));
        Assert.Equal("one corrupt version", File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.corrupt-*"))));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentReferencesRecoverAllSplatsWithoutTemporaryLeaks()
    {
        string source = Source(), cache = Path.Combine(directory, "cache"), hash = Hash(source);
        var reference = new GaussianReferenceMetadata("Aholo.GS", hash, source);
        string destination = GaussianReferenceAsset.Load(reference, cache).Path;
        File.WriteAllText(destination, "bad before concurrent loads");
        GaussianReferenceData[] loaded = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => GaussianReferenceAsset.Load(reference, cache))));
        Assert.All(loaded, item => { Assert.Equal(destination, item.Path); Assert.Equal(3, Assert.Single(item.Splats).Degree); });
        Assert.Equal(1, loaded.Count(item => item.CopiedToCache));
        Assert.Equal(hash, Hash(destination)); Assert.Equal(hash, Hash(source));
        Assert.Single(Directory.GetFiles(cache, "*.corrupt-*")); Assert.Empty(Directory.GetFiles(cache, "*.tmp"));
    }

    [Fact]
    public void CancellationDoesNotCreateCacheOrChangeSource()
    {
        string source=Source(),cache=Path.Combine(directory,"cache");
        Assert.Throws<OperationCanceledException>(()=>GaussianReferenceAsset.Load(new("LegacyViewer.GS",Hash(source),source),cache,new CancellationToken(true)));
        Assert.False(Directory.Exists(cache)); Assert.True(File.Exists(source));
    }
}
