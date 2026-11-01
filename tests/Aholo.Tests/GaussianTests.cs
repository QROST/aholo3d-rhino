using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Aholo.Core;

namespace Aholo.Tests;

public sealed class GaussianTests
{
    private static GaussianSplat Splat(GaussianVector? center = null, GaussianVector? scale = null, GaussianQuaternion? rotation = null,
        int degree = 0, float[]? coefficients = null, double logit = 0) => new(center ?? new(0, 0, 10), scale ?? new(0, 0, 0), rotation ?? new(1, 0, 0, 0), logit, degree, coefficients ?? new float[3 * (degree + 1) * (degree + 1)]);
    private static GaussianCamera Camera(bool perspective = true) => new(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), perspective, 101, 101, 100, 100, 50, 50, .1, 1000);
    private static void Near(double expected, double actual, double tolerance = 1e-9) => Assert.InRange(actual, expected - tolerance, expected + tolerance);

    [Theory]
    [InlineData("ascii", 0)]
    [InlineData("ascii", 1)]
    [InlineData("ascii", 2)]
    [InlineData("ascii", 3)]
    [InlineData("binary_little_endian", 3)]
    [InlineData("binary_big_endian", 3)]
    public void StrictPlyPreservesAllGaussianFieldsAndReordersNamedSh(string format, int degree)
    {
        Dictionary<string, float> fields = Fields(degree);
        fields["x"] = 1.25f; fields["scale_0"] = -2.5f; fields["opacity"] = -4.25f; fields["rot_0"] = 2;
        // Sorting the property names lexically deliberately puts f_rest_10 before f_rest_2.
        using var file = new TempPly(fields.OrderBy(f => f.Key, StringComparer.Ordinal).ToArray(), format);
        GaussianSplat splat = Assert.Single(PlyReader.ReadGaussians(file.Path));
        Assert.Equal(new GaussianVector(1.25, 0, 10), splat.Center);
        Assert.Equal(-2.5, splat.LogScale.X); Assert.Equal(-4.25, splat.OpacityLogit);
        Assert.Equal(new GaussianQuaternion(2, 0, 0, 0), splat.Rotation);
        Assert.Equal(degree, splat.Degree);
        Assert.Equal(new float[] { -2, .5f, 3 }, splat.Coefficients.Span[..3].ToArray());
        int perChannel = (degree + 1) * (degree + 1) - 1;
        for (int channel = 0; channel < 3; ++channel)
            for (int coefficient = 0; coefficient < perChannel; ++coefficient)
                Assert.Equal(fields["f_rest_" + (channel * perChannel + coefficient)], splat.Coefficients.Span[(coefficient + 1) * 3 + channel]);
    }

    [Fact]
    public void StrictPlyUsesShEvenWhenRgbExistsAndNeverQuantizesIt()
    {
        Dictionary<string, float> fields = Fields(0); fields["red"] = 255; fields["green"] = 255; fields["blue"] = 255;
        using var file = new TempPly(fields.ToArray());
        Assert.Equal(new float[] { -2, .5f, 3 }, Assert.Single(PlyReader.ReadGaussians(file.Path)).Coefficients.ToArray());
        Assert.Equal((byte)255, Assert.Single(PlyReader.Read(file.Path)).R);
    }

    [Theory]
    [InlineData("opacity")]
    [InlineData("scale_1")]
    [InlineData("rot_3")]
    [InlineData("f_dc_2")]
    [InlineData("f_rest_2")]
    public void StrictPlyRejectsMissingGaussianData(string removed)
    {
        Dictionary<string, float> fields = Fields(1); fields.Remove(removed);
        using var file = new TempPly(fields.ToArray());
        Assert.Throws<InvalidDataException>(() => PlyReader.ReadGaussians(file.Path).ToArray());
    }

    [Theory]
    [InlineData("rot_0", 0)]
    [InlineData("scale_0", float.NaN)]
    [InlineData("opacity", float.PositiveInfinity)]
    [InlineData("f_rest_1", float.NaN)]
    public void StrictPlyRejectsInvalidValues(string field, float value)
    {
        Dictionary<string, float> fields = Fields(1); fields[field] = value;
        using var file = new TempPly(fields.ToArray());
        Assert.Throws<InvalidDataException>(() => PlyReader.ReadGaussians(file.Path).ToArray());
    }

    [Theory]
    [InlineData("f_rest_1", "f_rest_01")]
    [InlineData("scale_1", "scale_3")]
    [InlineData("rot_1", "rot_4")]
    public void StrictPlyRejectsUnknownOrNoncontiguousParameterLayout(string oldName, string newName)
    {
        Dictionary<string, float> fields = Fields(1); float value = fields[oldName]; fields.Remove(oldName); fields[newName] = value;
        using var file = new TempPly(fields.ToArray());
        Assert.Throws<InvalidDataException>(() => PlyReader.ReadGaussians(file.Path).ToArray());
    }

    [Fact]
    public void StrictPlySharesListSkippingAndFullTrailingValidation()
    {
        using var file = new TempPly(Fields(1).ToArray());
        string text = File.ReadAllText(file.Path).Replace("element vertex 1\n", "element metadata 1\nproperty list uchar int ignored\nelement vertex 1\n")
            .Replace("end_header\n", "element face 1\nproperty list uchar int vertex_indices\nend_header\n2 -123 456\n") + "3 0 0 0\n";
        File.WriteAllText(file.Path, text);
        Assert.Single(PlyReader.ReadGaussians(file.Path));
        File.AppendAllText(file.Path, "extra");
        Assert.Throws<InvalidDataException>(() => PlyReader.ReadGaussians(file.Path).ToArray());
    }

    [Fact]
    public void StrictPlyHonorsCancellationAndBinaryTruncation()
    {
        using var file = new TempPly(Fields(3).ToArray(), "binary_little_endian");
        Assert.Throws<OperationCanceledException>(() => PlyReader.ReadGaussians(file.Path, new CancellationToken(true)).ToArray());
        using (FileStream stream = File.OpenWrite(file.Path)) stream.SetLength(stream.Length - 1);
        Assert.Throws<InvalidDataException>(() => PlyReader.ReadGaussians(file.Path).ToArray());
    }

    [Fact]
    public void GaussianCoefficientsAreCopiedWithoutQuantization()
    {
        float[] sh = { -10, 10, 0 }; GaussianSplat splat = Splat(coefficients: sh); sh[0] = 0;
        Assert.Equal(-10, splat.Coefficients.Span[0]);
        GaussianColor color = GaussianHarmonics.Evaluate(splat, new(0, 0, 1));
        Assert.Equal(0, color.R); Assert.True(color.G > 1); Assert.Equal(.5, color.B);
    }

    [Fact]
    public void CovarianceKeepsAnisotropyQuaternionNormalizationAndSign()
    {
        GaussianVector scale = new(Math.Log(2), 0, Math.Log(.5));
        GaussianMatrix identity = GaussianMath.Covariance(Splat(scale: scale), GaussianTransform.Identity);
        Near(4, identity.M11); Near(1, identity.M22); Near(.25, identity.M33); Near(0, identity.M12);
        var rotation = new GaussianQuaternion(2, 0, 0, 2);
        GaussianMatrix rotated = GaussianMath.Covariance(Splat(scale: scale, rotation: rotation), GaussianTransform.Identity);
        Near(1, rotated.M11); Near(4, rotated.M22); Near(.25, rotated.M33); Near(0, rotated.M12);
        GaussianMatrix opposite = GaussianMath.Covariance(Splat(scale: scale, rotation: new(-2, 0, 0, -2)), GaussianTransform.Identity);
        Assert.Equal(rotated, opposite);
    }

    [Fact]
    public void NonuniformInstanceTransformAppliesToCovarianceAndHasRoundtripInverse()
    {
        var linear = new GaussianMatrix(2, 1, 0, 0, 3, 0, 0, 0, 4);
        GaussianMatrix covariance = GaussianMath.Covariance(Splat(), new(linear, new(10, 20, 30)));
        Near(5, covariance.M11); Near(3, covariance.M12); Near(9, covariance.M22); Near(16, covariance.M33);
        GaussianVector original = new(7, -4, 2), recovered = linear.Inverse().Transform(linear.Transform(original));
        Near(original.X, recovered.X); Near(original.Y, recovered.Y); Near(original.Z, recovered.Z);
    }

    [Fact]
    public void PerspectiveProjectionMatchesClosedFormOffAxisJacobian()
    {
        GaussianCamera camera = Camera() with { Fy = 200 };
        ProjectedGaussian result = GaussianProjector.Project(Splat(center: new(2, -3, 10)), camera, GaussianTransform.Identity, 0)!;
        Near(70, result.CenterX); Near(110, result.CenterY); Near(10, result.Depth);
        Near(104, result.CovXX); Near(12, result.CovXY); Near(436, result.CovYY);
        GaussianVector world = camera.WorldAtPixel(result.CenterX, result.CenterY, result.Depth);
        Near(2, world.X); Near(-3, world.Y); Near(10, world.Z);
        Near(result.CovXX * 9, result.Axis1X * result.Axis1X + result.Axis2X * result.Axis2X);
        Near(result.CovXY * 9, result.Axis1X * result.Axis1Y + result.Axis2X * result.Axis2Y);
        Near(result.CovYY * 9, result.Axis1Y * result.Axis1Y + result.Axis2Y * result.Axis2Y);
    }

    [Fact]
    public void OrthographicFootprintAndShDirectionDoNotChangeWithPositionOrDepth()
    {
        var coefficients = new float[12]; coefficients[3 * 3] = 1;
        GaussianCamera camera = Camera(false) with { Fx = 10, Fy = 20 };
        ProjectedGaussian a = GaussianProjector.Project(Splat(center: new(-1, 0, 10), degree: 1, coefficients: coefficients), camera, GaussianTransform.Identity, 0)!;
        ProjectedGaussian b = GaussianProjector.Project(Splat(center: new(1, 0, 100), degree: 1, coefficients: coefficients), camera, GaussianTransform.Identity, 0)!;
        Near(100, a.CovXX); Near(400, a.CovYY); Near(a.CovXX, b.CovXX); Near(a.CovYY, b.CovYY); Near(.5, a.Color.R); Near(.5, b.Color.R);
        Assert.Equal(new GaussianVector(-1, 0, 10), camera.WorldAtPixel(a.CenterX, a.CenterY, a.Depth));
    }

    [Fact]
    public void PerspectiveShUsesInverseInstanceRotation()
    {
        var coefficients = new float[12]; coefficients[3 * 3] = 1;
        GaussianSplat splat = Splat(degree: 1, coefficients: coefficients);
        GaussianCamera camera = Camera() with { Eye = new(-10, 0, 0), Fx = 10, Fy = 10 };
        ProjectedGaussian unrotated = GaussianProjector.Project(splat, camera, GaussianTransform.Identity)!;
        var transform = new GaussianTransform(new(-1, 0, 0, 0, -1, 0, 0, 0, 1), new(0, 0, 0));
        ProjectedGaussian rotated = GaussianProjector.Project(splat, camera, transform)!;
        double variation = Math.Sqrt(3 / (4 * Math.PI)) / Math.Sqrt(2);
        Near(.5 - variation, unrotated.Color.R); Near(.5 + variation, rotated.Color.R);
    }

    [Fact]
    public void FootprintCanIntersectScreenWhileCenterIsOffscreen()
    {
        GaussianCamera camera = Camera(false) with { Fx = 1, Fy = 1, Cx = 0, Cy = 50 };
        Assert.NotNull(GaussianProjector.Project(Splat(center: new(-1, 0, 10)), camera, GaussianTransform.Identity, 0));
        Assert.Null(GaussianProjector.Project(Splat(center: new(-4, 0, 10)), camera, GaussianTransform.Identity, 0));
        Assert.Null(GaussianProjector.Project(Splat(center: new(0, 0, .05)), camera, GaussianTransform.Identity));
        Assert.Null(GaussianProjector.Project(Splat(center: new(0, 0, 1001)), camera, GaussianTransform.Identity));
    }

    [Theory]
    [InlineData(-1000, 0)]
    [InlineData(0, .5)]
    [InlineData(1000, 1)]
    public void OpacityActivationIsNumericallyStable(double logit, double expected) => Assert.Equal(expected, GaussianMath.Sigmoid(logit));

    [Fact]
    public void ShBasisMatchesKnownSignedAxesAndThirdDegree()
    {
        double[] x = GaussianHarmonics.Basis(3, new(1, 0, 0)), minusX = GaussianHarmonics.Basis(3, new(-1, 0, 0)), z = GaussianHarmonics.Basis(3, new(0, 0, 1));
        Near(1 / Math.Sqrt(4 * Math.PI), x[0]); Near(-Math.Sqrt(3 / (4 * Math.PI)), x[3]); Near(-x[3], minusX[3]);
        Near(Math.Sqrt(7 / (4 * Math.PI)), z[12]); Near(-Math.Sqrt(35 / (32 * Math.PI)), x[15]);
        var coefficients = new float[12]; coefficients[9] = 1;
        Near(.5 - Math.Sqrt(3 / (4 * Math.PI)), GaussianHarmonics.Evaluate(Splat(degree: 1, coefficients: coefficients), new(1, 0, 0)).R);
    }

    [Fact]
    public void AllShBandsObeyParityAndAdditionTheorem()
    {
        GaussianVector direction = new(2, -3, 4);
        double[] basis = GaussianHarmonics.Basis(3, direction), reverse = GaussianHarmonics.Basis(3, -direction);
        for (int l = 0; l <= 3; ++l)
        {
            double squares = 0;
            for (int m = -l; m <= l; ++m)
            {
                int index = l * (l + 1) + m;
                squares += basis[index] * basis[index]; Near((l % 2 == 0 ? 1 : -1) * basis[index], reverse[index]);
            }
            Near((2 * l + 1) / (4 * Math.PI), squares);
        }
    }

    [Fact]
    public void ReferenceCompositorSortsLayersAndPreservesPremultipliedAlpha()
    {
        ProjectedGaussian red = Projected(2, new(1, 0, 0)), blue = Projected(4, new(0, 0, 1));
        GaussianReferenceImage image = GaussianReferenceRenderer.Render(new[] { blue, red }, 1, 1);
        Assert.Equal(new float[] { .5f, 0, .25f, .75f }, image.Rgba);
        Assert.Equal(image.Rgba, GaussianReferenceRenderer.Render(new[] { red, blue }, 1, 1).Rgba);
    }

    [Theory]
    [InlineData(1, 0, 1, 0)]
    [InlineData(2, 0, 1, 0)]
    [InlineData(3, .5, .5, 0)]
    [InlineData(5, .5, .25, .25)]
    public void NativeOpaquePlaneTestsEachGaussianBeforeCompositing(float nativeDepth, float r, float g, float b)
    {
        GaussianReferenceImage image = GaussianReferenceRenderer.Render(new[] { Projected(4, new(0, 0, 1)), Projected(2, new(1, 0, 0)) }, 1, 1,
            new[] { nativeDepth }, new[] { new GaussianColor(0, 1, 0) });
        Assert.Equal(new[] { r, g, b, 1f }, image.Rgba);
    }

    [Fact]
    public void DepthMaskKeepsSmoothGaussianFalloffAtDifferentPixels()
    {
        ProjectedGaussian splat = Projected(2, new(1, 0, 0)) with { CenterX = 1 };
        GaussianReferenceImage image = GaussianReferenceRenderer.Render(new[] { splat }, 3, 1, new[] { 1f, float.PositiveInfinity, 3f });
        Assert.Equal(0, image.Rgba[0]); Near(.5, image.Rgba[4]); Near(.5 * Math.Exp(-.5), image.Rgba[8], 1e-7); Near(image.Rgba[8], image.Rgba[11]);
    }

    [Fact]
    public void InvalidInputsFailRatherThanProducingInvisibleOrNanFrames()
    {
        Assert.Throws<ArgumentException>(() => Splat(rotation: new(0, 0, 0, 0)));
        Assert.Throws<ArgumentException>(() => Splat(coefficients: new[] { float.NaN, 0, 0 }));
        Assert.Throws<ArgumentException>(() => GaussianProjector.Project(Splat(), Camera() with { Up = new(1, 0, 0) }, GaussianTransform.Identity));
        Assert.Throws<ArgumentException>(() => GaussianProjector.Project(Splat(), Camera(), new(default, default)));
        Assert.Throws<ArgumentException>(() => GaussianProjector.Project(Splat(scale: new(1000, 0, 0)), Camera(), GaussianTransform.Identity));
        Assert.Throws<ArgumentException>(() => GaussianReferenceRenderer.Render(new[] { Projected(2, new(1, 0, 0)) }, 1, 1, new[] { float.NaN }));
        Assert.Throws<OperationCanceledException>(() => GaussianReferenceRenderer.Render(new[] { Projected(2, new(1, 0, 0)) }, 1, 1, ct: new CancellationToken(true)));
    }

    [Fact]
    public void SyntheticRepositoryFixturesContainFullGaussianData()
    {
        string? root = AppContext.BaseDirectory;
        while (root is not null && !Directory.Exists(System.IO.Path.Combine(root, "fixtures"))) root = Directory.GetParent(root)?.FullName;
        Assert.NotNull(root);
        Assert.Equal(4, PlyReader.ReadGaussians(System.IO.Path.Combine(root!, "fixtures", "gaussian-anisotropic.ply")).Count());
        Assert.Equal(3, Assert.Single(PlyReader.ReadGaussians(System.IO.Path.Combine(root!, "fixtures", "gaussian-sh3.ply"))).Degree);
    }

    private static ProjectedGaussian Projected(double depth, GaussianColor color) => new(0, 0, depth, 1, 0, 1, color, .5, 3, 0, 0, 3, 3);
    private static Dictionary<string, float> Fields(int degree)
    {
        var fields = new Dictionary<string, float> { ["x"] = 0, ["y"] = 0, ["z"] = 10, ["scale_0"] = 0, ["scale_1"] = 0, ["scale_2"] = 0,
            ["rot_0"] = 1, ["rot_1"] = 0, ["rot_2"] = 0, ["rot_3"] = 0, ["opacity"] = 0, ["f_dc_0"] = -2, ["f_dc_1"] = .5f, ["f_dc_2"] = 3 };
        for (int i = 0; i < 3 * ((degree + 1) * (degree + 1) - 1); ++i) fields["f_rest_" + i] = (i - 12) * .125f;
        return fields;
    }
    private sealed class TempPly : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aholo-gaussian-test-" + Guid.NewGuid().ToString("N") + ".ply");
        public TempPly(KeyValuePair<string, float>[] fields, string format = "ascii")
        {
            using var stream = File.Create(Path);
            string header = "ply\nformat " + format + " 1.0\nelement vertex 1\n" + string.Concat(fields.Select(f => "property float " + f.Key + "\n")) + "end_header\n";
            stream.Write(Encoding.ASCII.GetBytes(header));
            if (format == "ascii") stream.Write(Encoding.ASCII.GetBytes(string.Join(" ", fields.Select(f => f.Value.ToString("R", CultureInfo.InvariantCulture))) + "\n"));
            else
            {
                Span<byte> bytes = stackalloc byte[4];
                foreach (var field in fields)
                {
                    if (format == "binary_little_endian") BinaryPrimitives.WriteSingleLittleEndian(bytes, field.Value); else BinaryPrimitives.WriteSingleBigEndian(bytes, field.Value);
                    stream.Write(bytes);
                }
            }
        }
        public void Dispose() => File.Delete(Path);
    }
}
