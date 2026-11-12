using System.Text.Json;
using Aholo3D.Core;

namespace Aholo3D.Tests;

public sealed class UsdGaussianWriterTests
{
    [Fact]
    public void TransportPreservesAllPartsNamesOriginalDoubleGeometryAndAffineShFrame()
    {
        using var directory = new TemporaryDirectory();
        GaussianSplat first = Splat(new(1e9 + .01, -2e8 + .04, 3e7 + .05), degree: 3, opacity: -double.MaxValue);
        GaussianSplat second = Splat(first.Center + new GaussianVector(.013, -.027, .031), degree: 3, opacity: double.MaxValue);
        var transform = new GaussianTransform(new(-2, .25, 0, .5, 3, .75, 0, -.125, 4), new(10, -5, 20));
        var scene = new GaussianScene(new[]
        {
            new GaussianPart("重复 / 名称", new[] { first, second }, transform, true),
            new GaussianPart("重复 / 名称", new[] { first }, GaussianTransform.Identity),
            new GaussianPart("", new[] { second }, GaussianTransform.Identity)
        }, "Y", .3048);
        UsdGaussianWriter.WriteBridgeInput(directory.Path, scene);
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(directory.Path, "export.json")));
        JsonElement root = json.RootElement;
        Assert.Equal("Y", root.GetProperty("upAxis").GetString()); Assert.Equal(.3048, root.GetProperty("metersPerUnit").GetDouble());
        JsonElement[] parts = root.GetProperty("parts").EnumerateArray().ToArray();
        Assert.Equal(3, parts.Length); Assert.Equal(3, parts.Select(p => p.GetProperty("file").GetString()).Distinct().Count());
        for (int i = 0; i < parts.Length; i++)
        {
            Assert.Equal(scene.Parts[i].Name, parts[i].GetProperty("name").GetString());
            Assert.Equal(scene.Parts[i].Antialiased, parts[i].GetProperty("antialiased").GetBoolean());
            GaussianSplat[] decoded = PlyReader.ReadGaussians(System.IO.Path.Combine(directory.Path, parts[i].GetProperty("file").GetString()!)).ToArray();
            Assert.Equal(scene.Parts[i].Splats.Length, decoded.Length);
            for (int j = 0; j < decoded.Length; j++)
            {
                GaussianSplat original = scene.Parts[i].Splats[j];
                Assert.Equal(original.Center, decoded[j].Center); Assert.Equal(original.LogScale, decoded[j].LogScale);
                Assert.Equal(original.Rotation, decoded[j].Rotation); Assert.Equal(original.OpacityLogit, decoded[j].OpacityLogit);
                Assert.Equal(original.Coefficients.ToArray(), decoded[j].Coefficients.ToArray());
            }
        }
        Assert.Equal(new[] { first.Center.X, first.Center.Y, first.Center.Z }, parts[0].GetProperty("origin").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Equal(new double[] { -2, .25, 0, .5, 3, .75, 0, -.125, 4, 10, -5, 20 }, parts[0].GetProperty("transform").EnumerateArray().Select(x => x.GetDouble()));
        Assert.Contains("float32", root.GetProperty("precision").GetString());
    }

    [Fact]
    public void MixedDegreeTransportPadsWithoutDiscardingAnyCoefficientsOrChangingRadiance()
    {
        using var directory = new TemporaryDirectory();
        var input = new[] { Splat(new(0, 0, 0), degree: 0), Splat(new(1, 2, 3), degree: 3) };
        UsdGaussianWriter.WriteBridgeInput(directory.Path, Scene(input));
        var output = PlyReader.ReadGaussians(System.IO.Path.Combine(directory.Path, "part-000000.ply")).ToArray();
        Assert.All(output, s => Assert.Equal(3, s.Degree));
        Assert.Equal(input[1].Coefficients.ToArray(), output[1].Coefficients.ToArray());
        foreach (GaussianVector direction in new[] { new GaussianVector(1, 2, 3), new GaussianVector(-2, 3, -1) })
            Assert.Equal(GaussianHarmonics.Evaluate(input[0], direction), GaussianHarmonics.Evaluate(output[0], direction));
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("X", 1)]
    [InlineData("Z", 0)]
    [InlineData("Y", -1)]
    [InlineData("Y", double.NaN)]
    public void RefusesUnknownOrInvalidUnitsBeforeWriting(string? axis, double units)
    {
        using var directory = new TemporaryDirectory();
        Assert.Throws<ArgumentException>(() => UsdGaussianWriter.WriteBridgeInput(directory.Path, Scene(new[] { Splat(new(0, 0, 0)) }) with { UpAxis = axis, MetersPerUnit = units }));
        Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public void RefusesNullUnitsAndEmptyGeometry()
    {
        using var directory = new TemporaryDirectory();
        Assert.Throws<ArgumentException>(() => UsdGaussianWriter.WriteBridgeInput(directory.Path, Scene(new[] { Splat(new(0, 0, 0)) }) with { MetersPerUnit = null }));
        Assert.Throws<ArgumentException>(() => UsdGaussianWriter.WriteBridgeInput(directory.Path, Scene(Array.Empty<GaussianSplat>())));
    }

    [Theory]
    [InlineData(-200)]
    [InlineData(100)]
    public void RefusesOfficialFloatScaleUnderflowAndOverflowWithoutEpsilon(double logScale)
    {
        using var directory = new TemporaryDirectory();
        GaussianSplat splat = new(new(0, 0, 0), new(logScale, 0, 0), new(1, 0, 0, 0), 0, 0, new float[3]);
        ArgumentException error = Assert.Throws<ArgumentException>(() => UsdGaussianWriter.WriteBridgeInput(directory.Path, Scene(new[] { splat })));
        Assert.Contains("float32", error.Message); Assert.Empty(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public void RelativePositionOverflowDoesNotReplaceExistingDestination()
    {
        using var directory = new TemporaryDirectory(); string output = System.IO.Path.Combine(directory.Path, "existing.usdz");
        File.WriteAllText(output, "existing valid content");
        Assert.Throws<ArgumentException>(() => UsdGaussianWriter.Write(output, Scene(new[] { Splat(new(0, 0, 0)), Splat(new(1e40, 0, 0)) })));
        Assert.Equal("existing valid content", File.ReadAllText(output)); Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public void CancellationDoesNotStartRuntimeOrReplaceExistingDestination()
    {
        using var directory = new TemporaryDirectory(); string output = System.IO.Path.Combine(directory.Path, "existing.usdz");
        File.WriteAllText(output, "existing valid content");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var scene = Scene(new[] { Splat(new(0, 0, 0)) });
        Assert.Throws<OperationCanceledException>(() => UsdGaussianWriter.Write(output, scene, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => UsdGaussianWriter.WriteBridgeInput(directory.Path, scene, cancellation.Token));
        Assert.Equal("existing valid content", File.ReadAllText(output)); Assert.Single(Directory.EnumerateFiles(directory.Path));
    }

    [Fact]
    public void RejectsSingularObjectTransformWithoutBakingTheLocalShFrame()
    {
        using var directory = new TemporaryDirectory();
        var scene = new GaussianScene(new[] { new GaussianPart("name", new[] { Splat(new(0, 0, 0)) }, new(new GaussianMatrix(), new(0, 0, 0))) }, "Z", 1);
        Assert.Throws<ArgumentException>(() => UsdGaussianWriter.WriteBridgeInput(directory.Path, scene));
    }

    private static GaussianSplat Splat(GaussianVector center, int degree = 1, double opacity = .25) => new(center,
        new(Math.Log(2e-5), Math.Log(7e-6), Math.Log(3e-5)), new(1, 1, -1, 1), opacity, degree,
        Enumerable.Range(0, 3 * (degree + 1) * (degree + 1)).Select(i => (i - 10) * .03125f).ToArray());
    private static GaussianScene Scene(GaussianSplat[] splats) => new(new[] { new GaussianPart("part", splats, GaussianTransform.Identity) }, "Z", .001);
    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gsrhino-usd-write-tests-" + Guid.NewGuid().ToString("N"));
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
