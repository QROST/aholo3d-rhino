using System.Security.Cryptography;
using System.Text.Json;
using Aholo3D.Core;

namespace Aholo3D.Tests;

public sealed class SpzOfficialTests
{
    [Theory]
    [InlineData("spz-official-v2")]
    [InlineData("spz-official-v3")]
    [InlineData("spz-official-v4")]
    [InlineData("spz-official-v4-coordinate6")]
    [InlineData("spz-official-v4-coordinate10")]
    [InlineData("spz-official-v4-coordinate16")]
    public void MatchesOfficialCppEncoderDecoderIncludingViewDependentColor(string name)
    {
        string directory = FixtureDirectory();
        byte[] bytes = File.ReadAllBytes(Path.Combine(directory, name + ".spz"));
        using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "spz-official-provenance.json")));
        Assert.Equal(provenance.RootElement.GetProperty("sha256").GetProperty(name + ".spz").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        byte[] expectedBytes = File.ReadAllBytes(Path.Combine(directory, name + ".json"));
        Assert.Equal(provenance.RootElement.GetProperty("sha256").GetProperty(name + ".json").GetString(), Convert.ToHexString(SHA256.HashData(expectedBytes)).ToLowerInvariant());
        using JsonDocument expected = JsonDocument.Parse(expectedBytes);
        JsonElement reference = expected.RootElement;
        GaussianPart part = Assert.Single(SpzReader.Read(Path.Combine(directory, name + ".spz")).Parts);
        Assert.True(part.Antialiased); Assert.Equal(3, part.Splats.Length);
        double[] positions = Values(reference, "positions"), scales = Values(reference, "scales"), rotations = Values(reference, "rotations"), colors = Values(reference, "colors"), sh = Values(reference, "sh"),
            worldPositions = Values(reference, "worldPositions"), worldRotations = Values(reference, "worldRotations"), worldSh = Values(reference, "worldSh");
        for (int i = 0; i < 3; ++i)
        {
            GaussianSplat splat = part.Splats[i];
            NearVector(Vector(positions, i * 3), splat.Center); NearVector(Vector(scales, i * 3), splat.LogScale);
            NearVector(Vector(worldPositions, i * 3), part.Transform.TransformPoint(splat.Center));
            GaussianQuaternion rotation = Quaternion(rotations, i * 4).Normalized();
            Near(rotation.W, splat.Rotation.W); Near(rotation.X, splat.Rotation.X); Near(rotation.Y, splat.Rotation.Y); Near(rotation.Z, splat.Rotation.Z);
            float[] worldCoefficients = new float[48];
            for (int j = 0; j < 3; ++j) { Near(colors[i * 3 + j], splat.Coefficients.Span[j]); worldCoefficients[j] = (float)colors[i * 3 + j]; }
            for (int j = 0; j < 45; ++j) { Near(sh[i * 45 + j], splat.Coefficients.Span[j + 3]); worldCoefficients[j + 3] = (float)worldSh[i * 45 + j]; }
            var worldSplat = new GaussianSplat(Vector(worldPositions, i * 3), splat.LogScale, Quaternion(worldRotations, i * 4), splat.OpacityLogit, 3, worldCoefficients);
            GaussianMatrix actualCovariance = GaussianMath.Covariance(splat, part.Transform), referenceCovariance = GaussianMath.Covariance(worldSplat, GaussianTransform.Identity);
            NearVector(referenceCovariance.Transform(new(1, 0, 0)), actualCovariance.Transform(new(1, 0, 0)), 1e-4);
            NearVector(referenceCovariance.Transform(new(0, 1, 0)), actualCovariance.Transform(new(0, 1, 0)), 1e-4);
            NearVector(referenceCovariance.Transform(new(0, 0, 1)), actualCovariance.Transform(new(0, 0, 1)), 1e-4);
            foreach (GaussianVector direction in new[] { new GaussianVector(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new GaussianVector(1, 2, 3).Normalized() })
            {
                GaussianColor a = GaussianHarmonics.Evaluate(splat, part.Transform.Linear.Inverse().Transform(direction));
                GaussianColor b = GaussianHarmonics.Evaluate(worldSplat, direction);
                Near(b.R, a.R); Near(b.G, a.G); Near(b.B, a.B);
            }
            Assert.Equal(i switch { 0 => 0, 1 => 128 / 255.0, _ => 1 }, GaussianMath.Sigmoid(splat.OpacityLogit), 12);
        }
    }
    private static double[] Values(JsonElement json, string name) => json.GetProperty(name).EnumerateArray().Select(value => value.GetDouble()).ToArray();
    private static GaussianVector Vector(double[] values, int index) => new(values[index], values[index + 1], values[index + 2]);
    private static GaussianQuaternion Quaternion(double[] values, int index) => new(values[index + 3], values[index], values[index + 1], values[index + 2]);
    private static void Near(double expected, double actual, double tolerance = 2e-6) => Assert.InRange(actual, expected - tolerance, expected + tolerance);
    private static void NearVector(GaussianVector expected, GaussianVector actual, double tolerance = 2e-6)
    { Near(expected.X, actual.X, tolerance); Near(expected.Y, actual.Y, tolerance); Near(expected.Z, actual.Z, tolerance); }
    private static string FixtureDirectory()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(Path.Combine(root, "fixtures"))) root = Directory.GetParent(root)?.FullName;
        Assert.NotNull(root);
        return Path.Combine(root!, "fixtures");
    }
}
