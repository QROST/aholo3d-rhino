using System.Text;
using System.Text.Json;
using Aholo.Core;

namespace Aholo.Tests;

public sealed class UsdGaussianReaderTests
{
    [Fact]
    public void PreservesEveryPartAffineLocalShAndEndpointOpacity()
    {
        using var fixture = new BridgeFixture();
        fixture.WritePly("a.ply", -double.MaxValue);
        fixture.WritePly("b.ply", double.MaxValue);
        fixture.Parts.Add(Part("/World/One", "a.ply"));
        double[] affine = { 0, -3, 0, 2, 0, 0, 0, 0, 4, 10, 20, 30 };
        fixture.Parts.Add(Part("/World/Two", "b.ply", affine));
        fixture.WriteManifest();
        GaussianScene result = UsdGaussianReader.ReadBridgeOutput(fixture.Directory);
        Assert.Equal(2, result.Parts.Count); Assert.Equal("Z", result.UpAxis); Assert.Equal(.001, result.MetersPerUnit);
        Assert.Equal(new GaussianVector(4, 22, 42), result.Parts[1].Transform.TransformPoint(result.Parts[1].Splats[0].Center));
        Assert.Equal(new GaussianVector(1, 2, 3), result.Parts[1].Splats[0].Center);
        Assert.Equal(new GaussianQuaternion(.5, .5, -.5, .5), result.Parts[1].Splats[0].Rotation);
        Assert.Equal(0, GaussianMath.Sigmoid(result.Parts[0].Splats[0].OpacityLogit));
        Assert.Equal(1, GaussianMath.Sigmoid(result.Parts[1].Splats[0].OpacityLogit));
        Assert.Equal(new float[] { -.5f, .25f, .75f, 1, 4, 7, 2, 5, 8, 3, 6, 9 }, result.Parts[0].Splats[0].Coefficients.ToArray());
        Assert.Contains("fixture diagnostic", result.Warnings!);
    }

    [Fact]
    public void PreservesEmptyAndDuplicateSourceNamesAndAntialiasedFlagSeparateFromUniquePrimPaths()
    {
        using var fixture = new BridgeFixture();
        for (int i = 0; i < 3; i++)
        {
            string file = $"part-{i}.ply"; fixture.WritePly(file, 0);
            fixture.Parts.Add(new { name = $"/Part{i}", file, count = 1, degree = 1,
                sourceName = i == 2 ? "" : "duplicate 用户 / name", antialiased = i == 1,
                transform = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 } });
        }
        fixture.WriteManifest(); GaussianScene scene = UsdGaussianReader.ReadBridgeOutput(fixture.Directory);
        Assert.Equal(new[] { "duplicate 用户 / name", "duplicate 用户 / name", "" }, scene.Parts.Select(p => p.Name));
        Assert.Equal(new[] { false, true, false }, scene.Parts.Select(p => p.Antialiased));
    }

    [Theory]
    [InlineData("../outside.ply")]
    [InlineData("folder/part.ply")]
    [InlineData("folder\\part.ply")]
    [InlineData("C:part.ply")]
    [InlineData("/part.ply")]
    public void RejectsManifestPayloadPathsOutsidePrivateDirectory(string payload)
    {
        using var fixture = new BridgeFixture(); fixture.Parts.Add(Part("/Gs", payload)); fixture.WriteManifest();
        Assert.Throws<InvalidDataException>(() => UsdGaussianReader.ReadBridgeOutput(fixture.Directory));
    }

    [Fact]
    public void RejectsDuplicatePrimNamesAndReusedPayloadFiles()
    {
        using var fixture = new BridgeFixture(); fixture.WritePly("a.ply", 0); fixture.WritePly("b.ply", 0);
        fixture.Parts.Add(Part("/Gs", "a.ply")); fixture.Parts.Add(Part("/Gs", "b.ply")); fixture.WriteManifest();
        Assert.Throws<InvalidDataException>(() => UsdGaussianReader.ReadBridgeOutput(fixture.Directory));
        fixture.Parts[1] = Part("/Other", "a.ply"); fixture.WriteManifest();
        Assert.Throws<InvalidDataException>(() => UsdGaussianReader.ReadBridgeOutput(fixture.Directory));
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 3)]
    [InlineData(-1, 1)]
    [InlineData(1, 4)]
    public void RejectsCountsOrShDegreesThatDoNotMatchCompletePayload(int count, int degree)
    {
        using var fixture = new BridgeFixture(); fixture.WritePly("a.ply", 0);
        fixture.Parts.Add(Part("/Gs", "a.ply", count: count, degree: degree)); fixture.WriteManifest();
        Assert.Throws<InvalidDataException>(() => UsdGaussianReader.ReadBridgeOutput(fixture.Directory));
    }

    [Fact]
    public void RejectsSingularTransformInsteadOfBakingPositionsAndDroppingLocalFrame()
    {
        using var fixture = new BridgeFixture(); fixture.WritePly("a.ply", 0);
        fixture.Parts.Add(Part("/Gs", "a.ply", new double[12])); fixture.WriteManifest();
        Assert.Throws<InvalidDataException>(() => UsdGaussianReader.ReadBridgeOutput(fixture.Directory));
    }

    [Fact]
    public void RejectsEmptyStage()
    {
        using var fixture = new BridgeFixture(); fixture.WriteManifest();
        Assert.Throws<InvalidDataException>(() => UsdGaussianReader.ReadBridgeOutput(fixture.Directory));
    }

    [Theory]
    [InlineData("X", .001, 1, "26.11")]
    [InlineData("Z", 0, 1, "26.11")]
    [InlineData("Z", .001, 2, "26.11")]
    [InlineData("Z", .001, 1, "26.03")]
    public void RejectsInvalidStageOrProtocolMetadata(string upAxis, double units, int protocol, string version)
    {
        using var fixture = new BridgeFixture(); fixture.WritePly("a.ply", 0); fixture.Parts.Add(Part("/Gs", "a.ply"));
        fixture.WriteManifest(upAxis, units, protocol, version);
        Assert.Throws<InvalidDataException>(() => UsdGaussianReader.ReadBridgeOutput(fixture.Directory));
    }

    [Fact]
    public void CancellationPreventsAnyRuntimeOrManifestAccess()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => UsdGaussianReader.Read("not-opened.usdz", cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => UsdGaussianReader.ReadBridgeOutput("not-opened", cancellation.Token));
    }

    private static object Part(string name, string file, double[]? matrix = null, int count = 1, int degree = 1) => new
    {
        name, file, count, degree, transform = matrix ?? new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 }
    };

    private sealed class BridgeFixture : IDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "gsrhino-usd-protocol-" + Guid.NewGuid().ToString("N"));
        internal List<object> Parts { get; } = new();
        internal BridgeFixture() => System.IO.Directory.CreateDirectory(Directory);
        internal void WriteManifest(string upAxis = "Z", double units = .001, int protocol = 1, string version = "26.11") =>
            File.WriteAllText(Path.Combine(Directory, "manifest.json"), JsonSerializer.Serialize(new
            {
                protocol, openUsdVersion = version, upAxis, metersPerUnit = units, parts = Parts, warnings = new[] { "fixture diagnostic" }
            }));
        internal void WritePly(string filename, double opacity)
        {
            using var stream = File.Create(Path.Combine(Directory, filename));
            string[] fields = { "x", "y", "z", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3", "opacity" };
            string header = "ply\nformat binary_little_endian 1.0\nelement vertex 1\n" + string.Join("", fields.Select(field => "property double " + field + "\n")) +
                string.Join("", Enumerable.Range(0, 3).Select(i => "property float f_dc_" + i + "\n")) +
                string.Join("", Enumerable.Range(0, 9).Select(i => "property float f_rest_" + i + "\n")) + "end_header\n";
            stream.Write(Encoding.ASCII.GetBytes(header));
            using var writer = new BinaryWriter(stream);
            foreach (double value in new[] { 1d, 2, 3, Math.Log(2), Math.Log(.5), 0, .5, .5, -.5, .5, opacity }) writer.Write(value);
            foreach (float value in new[] { -.5f, .25f, .75f, 1, 2, 3, 4, 5, 6, 7, 8, 9 }) writer.Write(value);
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
