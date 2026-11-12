namespace Aholo3D.Core;

/// <summary>Independent, portable CPU oracle for small test scenes. Float RGBA is premultiplied; it is not a production realtime backend.</summary>
public sealed record GaussianReferenceImage(int Width, int Height, float[] Rgba);

public static class GaussianReferenceRenderer
{
    /// <summary>
    /// Front-to-back Gaussian alpha accumulation with per-contribution opaque depth testing.
    /// Depth is positive linear camera depth; positive infinity means no native geometry. Equal depth belongs to the native surface.
    /// A supplied opaque/background color makes output alpha one. Without it, output is transparent premultiplied RGBA.
    /// Standard Gaussian center-depth ordering is an approximation, not physical volumetric surface reconstruction.
    /// </summary>
    public static GaussianReferenceImage Render(IEnumerable<ProjectedGaussian> gaussians, int width, int height,
        float[]? nativeLinearDepth = null, GaussianColor[]? opaqueBackground = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(gaussians);
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        int pixels = checked(width * height);
        if (nativeLinearDepth is not null && (nativeLinearDepth.Length != pixels || nativeLinearDepth.Any(v => float.IsNaN(v) || v <= 0)))
            throw new ArgumentException("Native depth must contain one positive linear depth or positive infinity per pixel.", nameof(nativeLinearDepth));
        if (opaqueBackground is not null && (opaqueBackground.Length != pixels || opaqueBackground.Any(c => !c.IsFinite || c.R < 0 || c.G < 0 || c.B < 0)))
            throw new ArgumentException("Background must contain one finite nonnegative RGB color per pixel.", nameof(opaqueBackground));
        ct.ThrowIfCancellationRequested();
        var ordered = new List<ProjectedGaussian>();
        foreach (ProjectedGaussian gaussian in gaussians) { ct.ThrowIfCancellationRequested(); Validate(gaussian); ordered.Add(gaussian); }
        // LINQ ordering is stable, making equal-depth fixtures and later backend comparisons deterministic.
        ProjectedGaussian[] sorted = ordered.OrderBy(g => g.Depth).ToArray();
        var accumulated = new double[checked(pixels * 4)];
        foreach (ProjectedGaussian gaussian in sorted)
        {
            ct.ThrowIfCancellationRequested();
            double extentX = gaussian.SupportSigma * Math.Sqrt(gaussian.CovXX), extentY = gaussian.SupportSigma * Math.Sqrt(gaussian.CovYY);
            int minX = (int)Math.Clamp(Math.Ceiling(gaussian.CenterX - extentX), 0, width - 1), maxX = (int)Math.Clamp(Math.Floor(gaussian.CenterX + extentX), 0, width - 1),
                minY = (int)Math.Clamp(Math.Ceiling(gaussian.CenterY - extentY), 0, height - 1), maxY = (int)Math.Clamp(Math.Floor(gaussian.CenterY + extentY), 0, height - 1);
            double determinant = gaussian.CovXX * gaussian.CovYY - gaussian.CovXY * gaussian.CovXY;
            double inverseXX = gaussian.CovYY / determinant, inverseXY = -gaussian.CovXY / determinant, inverseYY = gaussian.CovXX / determinant;
            for (int y = minY; y <= maxY; ++y)
            {
                ct.ThrowIfCancellationRequested();
                for (int x = minX; x <= maxX; ++x)
                {
                    int pixel = y * width + x, offset = 4 * pixel;
                    if (nativeLinearDepth is not null && gaussian.Depth >= nativeLinearDepth[pixel]) continue;
                    double dx = x - gaussian.CenterX, dy = y - gaussian.CenterY;
                    double quadratic = inverseXX * dx * dx + 2 * inverseXY * dx * dy + inverseYY * dy * dy;
                    if (quadratic > gaussian.SupportSigma * gaussian.SupportSigma) continue;
                    double alpha = Math.Clamp(gaussian.Opacity * Math.Exp(-.5 * Math.Max(0, quadratic)), 0, 1);
                    double weight = (1 - accumulated[offset + 3]) * alpha;
                    accumulated[offset] += gaussian.Color.R * weight;
                    accumulated[offset + 1] += gaussian.Color.G * weight;
                    accumulated[offset + 2] += gaussian.Color.B * weight;
                    accumulated[offset + 3] += weight;
                }
            }
        }
        var rgba = new float[accumulated.Length];
        for (int pixel = 0; pixel < pixels; ++pixel)
        {
            if ((pixel & 4095) == 0) ct.ThrowIfCancellationRequested();
            int offset = pixel * 4;
            if (opaqueBackground is not null)
            {
                double transmittance = 1 - accumulated[offset + 3];
                accumulated[offset] += transmittance * opaqueBackground[pixel].R;
                accumulated[offset + 1] += transmittance * opaqueBackground[pixel].G;
                accumulated[offset + 2] += transmittance * opaqueBackground[pixel].B;
                accumulated[offset + 3] = 1;
            }
            for (int channel = 0; channel < 4; ++channel)
            {
                rgba[offset + channel] = (float)accumulated[offset + channel];
                if (!float.IsFinite(rgba[offset + channel])) throw new ArgumentException("Reference output exceeds float precision.");
            }
        }
        return new(width, height, rgba);
    }
    private static void Validate(ProjectedGaussian gaussian)
    {
        ArgumentNullException.ThrowIfNull(gaussian);
        double determinant = gaussian.CovXX * gaussian.CovYY - gaussian.CovXY * gaussian.CovXY;
        if (new[] { gaussian.CenterX, gaussian.CenterY, gaussian.Depth, gaussian.CovXX, gaussian.CovXY, gaussian.CovYY, gaussian.Opacity, gaussian.SupportSigma, determinant }.Any(v => !double.IsFinite(v)) ||
            gaussian.Depth <= 0 || gaussian.CovXX <= 0 || gaussian.CovYY <= 0 || determinant <= 0 || gaussian.Opacity < 0 || gaussian.Opacity > 1 || gaussian.SupportSigma <= 0 ||
            !gaussian.Color.IsFinite || gaussian.Color.R < 0 || gaussian.Color.G < 0 || gaussian.Color.B < 0)
            throw new ArgumentException("Reference renderer requires finite positive-definite projected Gaussians.");
    }
}
