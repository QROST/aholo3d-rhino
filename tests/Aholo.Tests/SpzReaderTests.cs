using System.Buffers.Binary;
using Aholo.Core;

namespace Aholo.Tests;

public sealed class SpzReaderTests
{
    [Theory]
    [InlineData(1, 0)] [InlineData(1, 3)]
    [InlineData(2, 0)] [InlineData(2, 1)] [InlineData(2, 2)] [InlineData(2, 3)]
    [InlineData(3, 0)] [InlineData(3, 3)]
    [InlineData(4, 0)] [InlineData(4, 1)] [InlineData(4, 2)] [InlineData(4, 3)]
    public void ReadsAllAttributesAndEveryShCoefficient(int version, int degree)
    {
        using var file = new SpzTestFile(SpzFixtures.File(version, degree, count: 3, flags: 1));
        GaussianScene scene = SpzReader.Read(file.Path);
        Assert.Equal($"spz-v{version}", scene.Format); Assert.Equal("Y", scene.UpAxis); Assert.Null(scene.MetersPerUnit);
        GaussianPart part = Assert.Single(scene.Parts); Assert.True(part.Antialiased);
        Assert.Equal(GaussianTransform.Identity, part.Transform); Assert.Equal(3, part.Splats.Length);
        for (int i = 0; i < part.Splats.Length; ++i)
        {
            GaussianSplat splat = part.Splats[i];
            Assert.Equal(new GaussianVector(1.25, -2.5, 3.75), splat.Center);
            Assert.Equal(new GaussianVector(-2, 0, 2), splat.LogScale);
            Assert.Equal(degree, splat.Degree); Assert.Equal(3 * (degree + 1) * (degree + 1), splat.Coefficients.Length);
            Near(-.5 / .15, splat.Coefficients.Span[0]); Near((128 / 255.0 - .5) / .15, splat.Coefficients.Span[1]); Near(.5 / .15, splat.Coefficients.Span[2]);
            for (int j = 0; j < splat.Coefficients.Length - 3; ++j)
                Assert.Equal((((j * 7 + i) & 255) - 128) / 128f, splat.Coefficients.Span[j + 3]);
            Assert.Equal(i switch { 0 => 0, 1 => 128 / 255.0, _ => 1 }, GaussianMath.Sigmoid(splat.OpacityLogit), 12);
            if (version >= 3) Assert.Equal(new GaussianQuaternion(1, 0, 0, 0), splat.Rotation);
            else { Near(128 / 127.5 - 1, splat.Rotation.X); Near(splat.Rotation.X, splat.Rotation.Y); }
        }
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void SmallestThreeRecoversEveryOmittedComponentAndSigns(int largest)
    {
        byte[][] attributes = SpzFixtures.Attributes(3, 0, 1);
        // Three 10-bit signed magnitudes in ascending XYZW order (except omitted component).
        // Reverse extraction is required: positive .1-ish, negative .2-ish, positive .3-ish.
        uint packed = (uint)largest;
        foreach (uint value in new uint[] { 72, 512 + 145, 217 }) packed = (packed << 10) | value;
        BinaryPrimitives.WriteUInt32LittleEndian(attributes[4], packed);
        using var file = new SpzTestFile(SpzFixtures.Container(3, 0, 1, 0, Array.Empty<byte>(), attributes));
        GaussianQuaternion q = Assert.Single(Assert.Single(SpzReader.Read(file.Path).Parts).Splats).Rotation;
        double[] actual = { q.X, q.Y, q.Z, q.W }, expectedSmall = { 72 / (511.0 * Math.Sqrt(2)), -145 / (511.0 * Math.Sqrt(2)), 217 / (511.0 * Math.Sqrt(2)) };
        int index = 0;
        for (int i = 0; i < 4; ++i) if (i != largest) Near(expectedSmall[index++], actual[i]);
        Near(Math.Sqrt(1 - expectedSmall.Sum(v => v * v)), actual[largest]);
    }
    [Theory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CoordinateExtensionUsesPartTransformAndDoesNotRewriteLocalSh(int version)
    {
        using var file = new SpzTestFile(SpzFixtures.File(version, degree: 3, flags: 2, extensions: SpzFixtures.Coordinate(10))); // RFD
        GaussianPart part = Assert.Single(SpzReader.Read(file.Path).Parts);
        GaussianSplat splat = Assert.Single(part.Splats);
        Assert.Equal(new GaussianVector(1.25, -2.5, 3.75), splat.Center);
        Assert.Equal(new GaussianVector(1.25, 3.75, -2.5), part.Transform.TransformPoint(splat.Center));
        Assert.Equal(-1, splat.Coefficients.Span[3]); Assert.Equal((7 - 128) / 128f, splat.Coefficients.Span[4]);
    }
    [Theory]
    [InlineData(1, -1, -1, 1)] [InlineData(2, 1, -1, 1)]
    [InlineData(3, -1, 1, 1)] [InlineData(4, 1, 1, 1)]
    [InlineData(5, -1, -1, -1)] [InlineData(6, 1, -1, -1)]
    [InlineData(7, -1, 1, -1)] [InlineData(8, 1, 1, -1)]
    public void StandardCoordinateFamiliesHaveCorrectAxisSigns(int system, int x, int y, int z)
    {
        using var file = new SpzTestFile(SpzFixtures.File(flags: 2, extensions: SpzFixtures.Coordinate((uint)system)));
        GaussianPart part = Assert.Single(SpzReader.Read(file.Path).Parts);
        Assert.Equal(new GaussianVector(x, y, z), part.Transform.TransformPoint(new(1, 1, 1)));
    }
    [Fact]
    public void UnspecifiedCoordinateAndUnknownExtensionsAreExplicitWarnings()
    {
        byte[] extensions = SpzFixtures.Coordinate(0).Concat(SpzFixtures.Extension(0x12340001, new byte[] { 1, 2, 3 })).ToArray();
        using var file = new SpzTestFile(SpzFixtures.File(flags: 2, extensions: extensions));
        GaussianScene scene = SpzReader.Read(file.Path);
        Assert.Equal(GaussianTransform.Identity, Assert.Single(scene.Parts).Transform);
        Assert.Contains(scene.Warnings!, warning => warning.Contains("unspecified", StringComparison.Ordinal));
        Assert.Contains(scene.Warnings!, warning => warning.Contains("0x12340001", StringComparison.Ordinal));
    }
    [Fact]
    public void SafeOrbitMetadataIsValidatedAndItsUnenforcedStatusIsDisclosed()
    {
        using var file = new SpzTestFile(SpzFixtures.File(flags: 2, extensions: SpzFixtures.Extension(0xADBE0002, new byte[12])));
        Assert.Contains(SpzReader.Read(file.Path).Warnings!, warning => warning.Contains("not enforced", StringComparison.Ordinal));
        byte[] invalid = new byte[12]; BinaryPrimitives.WriteSingleLittleEndian(invalid, float.NaN);
        using var bad = new SpzTestFile(SpzFixtures.File(flags: 2, extensions: SpzFixtures.Extension(0xADBE0002, invalid)));
        Assert.Throws<InvalidDataException>(() => SpzReader.Read(bad.Path));
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void DegreeFourFailsExplicitlyWithoutDiscardingCoefficients(int version)
    {
        using var file = new SpzTestFile(SpzFixtures.File(version, degree: 4));
        NotSupportedException error = Assert.Throws<NotSupportedException>(() => SpzReader.Read(file.Path));
        Assert.Contains("not truncated", error.Message);
    }
    [Fact]
    public void KeepsEveryGaussianAcrossStorageBlocks()
    {
        const int count = 16384 + 23;
        using var file = new SpzTestFile(SpzFixtures.File(4, degree: 1, count: count));
        GaussianScene scene = SpzReader.Read(file.Path);
        Assert.True(scene.Parts.Count > 1); Assert.Equal(count, scene.Parts.Sum(part => part.Splats.Length));
        Assert.Equal((((count - 1) & 255) - 128) / 128f, scene.Parts[^1].Splats[^1].Coefficients.Span[3]);
    }
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void RejectsTruncatedStreamsAndHonorsCancellation(int version)
    {
        byte[] bytes = SpzFixtures.File(version, degree: 3);
        using var file = new SpzTestFile(bytes[..^1]);
        Assert.Throws<InvalidDataException>(() => SpzReader.Read(file.Path));
        Assert.Throws<OperationCanceledException>(() => SpzReader.Read(file.Path, new CancellationToken(true)));
    }
    [Theory]
    [InlineData(0)] [InlineData(4)] [InlineData(8)]
    public void GzipTrailerMustBeCompleteAndMatchChecksum(int removed)
    {
        byte[] bytes = SpzFixtures.File(2);
        if (removed == 0) bytes[^8] ^= 1; else bytes = bytes[..^removed];
        using var file = new SpzTestFile(bytes);
        Assert.Throws<InvalidDataException>(() => SpzReader.Read(file.Path));
    }
    [Fact]
    public void RejectsLegacyTrailingDataAndImpossibleCountsBeforeAllocatingGaussians()
    {
        byte[] payload = SpzFixtures.Gunzip(SpzFixtures.File(2));
        using var trailing = new SpzTestFile(SpzFixtures.Gzip(payload.Concat(new byte[] { 99 }).ToArray()));
        Assert.Throws<InvalidDataException>(() => SpzReader.Read(trailing.Path));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), uint.MaxValue);
        using var impossible = new SpzTestFile(SpzFixtures.Gzip(payload[..16]));
        Assert.Throws<InvalidDataException>(() => SpzReader.Read(impossible.Path));
    }
    [Theory]
    [InlineData("stream-count")] [InlineData("toc")] [InlineData("size")] [InlineData("zstd")]
    [InlineData("reserved")] [InlineData("trailing")] [InlineData("extensions")]
    public void RejectsMalformedV4Container(string mutation)
    {
        byte[] bytes = SpzFixtures.File(4);
        switch (mutation)
        {
            case "stream-count": bytes[15] = 6; break;
            case "toc": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), uint.MaxValue); break;
            case "size": BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(40), ulong.MaxValue); break;
            case "zstd": bytes[32 + 5 * 16] ^= 255; break;
            case "reserved": bytes[20] = 1; break;
            case "trailing": bytes = bytes.Concat(new byte[] { 0 }).ToArray(); break;
            case "extensions": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 33); break;
        }
        using var file = new SpzTestFile(bytes);
        Assert.Throws<InvalidDataException>(() => SpzReader.Read(file.Path));
    }
    [Theory]
    [InlineData("partial-header")] [InlineData("partial-payload")] [InlineData("duplicate-coordinates")] [InlineData("coordinate-length")]
    public void RejectsMalformedExtensions(string mutation)
    {
        byte[] extension = mutation switch
        {
            "partial-header" => new byte[] { 1, 2, 3 },
            "partial-payload" => SpzFixtures.Extension(0x12340001, new byte[] { 1, 2 })[..^1],
            "duplicate-coordinates" => SpzFixtures.Coordinate(4).Concat(SpzFixtures.Coordinate(6)).ToArray(),
            _ => SpzFixtures.Extension(0xADBE0003, new byte[3])
        };
        foreach (int version in new[] { 2, 3, 4 })
        {
            using var file = new SpzTestFile(SpzFixtures.File(version, flags: 2, extensions: extension));
            Assert.Throws<InvalidDataException>(() => SpzReader.Read(file.Path));
        }
    }
    [Fact]
    public void RejectsUnknownCoordinatesFlagsVersionsAndInvalidQuaternions()
    {
        using var coordinate = new SpzTestFile(SpzFixtures.File(flags: 2, extensions: SpzFixtures.Coordinate(17)));
        Assert.Throws<NotSupportedException>(() => SpzReader.Read(coordinate.Path));
        using var flags = new SpzTestFile(SpzFixtures.File(flags: 4));
        Assert.Throws<NotSupportedException>(() => SpzReader.Read(flags.Path));
        byte[] unknown = SpzFixtures.File(); BinaryPrimitives.WriteUInt32LittleEndian(unknown.AsSpan(4), 5);
        using var version = new SpzTestFile(unknown); Assert.Throws<NotSupportedException>(() => SpzReader.Read(version.Path));
        byte[][] attributes = SpzFixtures.Attributes(4, 0, 1); Array.Fill(attributes[4], (byte)255);
        using var rotation = new SpzTestFile(SpzFixtures.Container(4, 0, 1, 0, Array.Empty<byte>(), attributes));
        Assert.Throws<InvalidDataException>(() => SpzReader.Read(rotation.Path));
    }
    [Fact]
    public void RejectsNonfiniteLegacyHalfCoordinates()
    {
        byte[][] attributes = SpzFixtures.Attributes(1, 0, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(attributes[0], 0x7C00); // IEEE half infinity
        using var file = new SpzTestFile(SpzFixtures.Container(1, 0, 1, 0, Array.Empty<byte>(), attributes));
        Assert.Throws<InvalidDataException>(() => SpzReader.Read(file.Path));
    }
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - 1e-6, expected + 1e-6);
}
