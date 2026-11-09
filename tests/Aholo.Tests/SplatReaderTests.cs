using System.Buffers.Binary;
using Aholo.Core;

namespace Aholo.Tests;

public sealed class SplatReaderTests
{
    private static byte[] Record(byte alpha = 128)
    {
        byte[] bytes = new byte[32];
        float[] values = { 1.25f, -2.5f, 3.75f, .5f, 2, 4 };
        for (int i = 0; i < values.Length; ++i) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), values[i]);
        bytes[24] = 255; bytes[25] = 0; bytes[26] = 128; bytes[27] = alpha;
        bytes[28] = 128; bytes[29] = 128; bytes[30] = 128; bytes[31] = 255; // WXYZ: Z rotation
        return bytes;
    }
    [Fact]
    public void DecodesOriginalSplatConventionAndPreservesActivatedParameters()
    {
        using var file = new SpzTestFile(Record());
        GaussianScene scene = SplatReader.Read(file.Path);
        GaussianPart part = Assert.Single(scene.Parts); GaussianSplat splat = Assert.Single(part.Splats);
        Assert.Equal("splat", scene.Format); Assert.Null(scene.UpAxis); Assert.Null(scene.MetersPerUnit);
        Assert.Equal(GaussianTransform.Identity, part.Transform);
        Assert.Equal(new GaussianVector(1.25, -2.5, 3.75), splat.Center);
        Assert.Equal(new GaussianVector(Math.Log(.5), Math.Log(2), Math.Log(4)), splat.LogScale);
        Assert.Equal(new GaussianQuaternion(0, 0, 0, 1), splat.Rotation);
        Assert.Equal(0, splat.Degree); Assert.Equal(3, splat.Coefficients.Length);
        GaussianColor color = GaussianHarmonics.Evaluate(splat, new(0, 0, 1));
        Near(1, color.R); Near(0, color.G); Near(128 / 255.0, color.B);
        Near(128 / 255.0, GaussianMath.Sigmoid(splat.OpacityLogit));
    }
    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 1)]
    public void OpacityEndpointsStayExactlyTransparentOrOpaque(int encoded, double expected)
    {
        using var file = new SpzTestFile(Record((byte)encoded));
        GaussianSplat splat = Assert.Single(Assert.Single(SplatReader.Read(file.Path).Parts).Splats);
        Assert.True(double.IsFinite(splat.OpacityLogit));
        Assert.Equal(expected, GaussianMath.Sigmoid(splat.OpacityLogit));
    }
    [Fact]
    public void ReadsEveryRecordAcrossStorageBlocks()
    {
        const int count = 16384 + 19;
        byte[] bytes = new byte[count * 32]; byte[] record = Record();
        for (int i = 0; i < count; ++i) record.CopyTo(bytes, i * 32);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan((count - 1) * 32), -123.5f);
        using var file = new SpzTestFile(bytes);
        GaussianScene scene = SplatReader.Read(file.Path);
        Assert.True(scene.Parts.Count > 1); Assert.Equal(count, scene.Parts.Sum(p => p.Splats.Length));
        Assert.Equal(-123.5, scene.Parts[^1].Splats[^1].Center.X);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(33)]
    public void RejectsEmptyAndPartialRecords(int length)
    {
        using var file = new SpzTestFile(new byte[length]);
        Assert.Throws<InvalidDataException>(() => SplatReader.Read(file.Path));
    }
    [Theory]
    [InlineData(0, float.NaN)]
    [InlineData(12, 0)]
    [InlineData(16, -1)]
    [InlineData(20, float.PositiveInfinity)]
    public void RejectsNonfinitePositionsOrInvalidScales(int offset, float value)
    {
        byte[] bytes = Record(); BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), value);
        using var file = new SpzTestFile(bytes);
        Assert.Throws<InvalidDataException>(() => SplatReader.Read(file.Path));
    }
    [Fact]
    public void RejectsZeroQuaternionAndHonorsCancellation()
    {
        byte[] bytes = Record(); Array.Fill(bytes, (byte)128, 28, 4);
        using var file = new SpzTestFile(bytes);
        Assert.Throws<InvalidDataException>(() => SplatReader.Read(file.Path));
        Assert.Throws<OperationCanceledException>(() => SplatReader.Read(file.Path, new CancellationToken(true)));
    }
    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - 1e-7, expected + 1e-7);
}
