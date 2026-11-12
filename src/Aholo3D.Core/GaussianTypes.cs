namespace Aholo3D.Core;

/// <summary>Double precision world-space vector; no dependency on a host graphics API.</summary>
public readonly record struct GaussianVector(double X, double Y, double Z)
{
    public static GaussianVector Zero => new(0, 0, 0);
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public double Length => Math.Sqrt(Dot(this, this));
    public GaussianVector Normalized()
    {
        double largest = Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z)));
        if (!IsFinite || largest == 0) throw new ArgumentException("A finite nonzero direction is required.");
        GaussianVector scaled = this / largest;
        return scaled / scaled.Length;
    }
    public static double Dot(GaussianVector a, GaussianVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    public static GaussianVector operator +(GaussianVector a, GaussianVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static GaussianVector operator -(GaussianVector a, GaussianVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static GaussianVector operator -(GaussianVector a) => new(-a.X, -a.Y, -a.Z);
    public static GaussianVector operator *(GaussianVector a, double b) => new(a.X * b, a.Y * b, a.Z * b);
    public static GaussianVector operator /(GaussianVector a, double b) => new(a.X / b, a.Y / b, a.Z / b);
}

/// <summary>Row-labelled matrix operating on column vectors.</summary>
public readonly record struct GaussianMatrix(double M11, double M12, double M13, double M21, double M22, double M23, double M31, double M32, double M33)
{
    public static GaussianMatrix Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);
    public bool IsFinite => double.IsFinite(M11) && double.IsFinite(M12) && double.IsFinite(M13) &&
        double.IsFinite(M21) && double.IsFinite(M22) && double.IsFinite(M23) && double.IsFinite(M31) && double.IsFinite(M32) && double.IsFinite(M33);
    public GaussianVector Transform(GaussianVector p) => new(M11 * p.X + M12 * p.Y + M13 * p.Z, M21 * p.X + M22 * p.Y + M23 * p.Z, M31 * p.X + M32 * p.Y + M33 * p.Z);
    public GaussianMatrix Transpose() => new(M11, M21, M31, M12, M22, M32, M13, M23, M33);
    public GaussianMatrix Inverse()
    {
        double scale = Math.Max(Math.Max(Math.Abs(M11), Math.Max(Math.Abs(M12), Math.Abs(M13))),
            Math.Max(Math.Max(Math.Abs(M21), Math.Max(Math.Abs(M22), Math.Abs(M23))), Math.Max(Math.Abs(M31), Math.Max(Math.Abs(M32), Math.Abs(M33)))));
        if (!IsFinite || scale == 0) throw new ArgumentException("Gaussian transform must be finite and invertible.");
        double a = M11 / scale, b = M12 / scale, c = M13 / scale, d = M21 / scale, e = M22 / scale, f = M23 / scale, g = M31 / scale, h = M32 / scale, i = M33 / scale;
        double determinant = a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
        if (determinant == 0 || !double.IsFinite(determinant)) throw new ArgumentException("Gaussian transform must be invertible.");
        double factor = 1 / determinant / scale;
        var result = new GaussianMatrix((e * i - f * h) * factor, (c * h - b * i) * factor, (b * f - c * e) * factor,
            (f * g - d * i) * factor, (a * i - c * g) * factor, (c * d - a * f) * factor,
            (d * h - e * g) * factor, (b * g - a * h) * factor, (a * e - b * d) * factor);
        if (!result.IsFinite) throw new ArgumentException("Gaussian transform inverse exceeds numeric precision.");
        return result;
    }
    public static GaussianMatrix operator *(GaussianMatrix a, GaussianMatrix b)
    {
        GaussianVector x = a.Transform(new(b.M11, b.M21, b.M31)), y = a.Transform(new(b.M12, b.M22, b.M32)), z = a.Transform(new(b.M13, b.M23, b.M33));
        return new(x.X, y.X, z.X, x.Y, y.Y, z.Y, x.Z, y.Z, z.Z);
    }
}

public readonly record struct GaussianTransform(GaussianMatrix Linear, GaussianVector Translation)
{
    public static GaussianTransform Identity => new(GaussianMatrix.Identity, GaussianVector.Zero);
    public GaussianVector TransformPoint(GaussianVector point) => Linear.Transform(point) + Translation;
}

public readonly record struct GaussianQuaternion(double W, double X, double Y, double Z)
{
    public GaussianQuaternion Normalized()
    {
        double largest = Math.Max(Math.Abs(W), Math.Max(Math.Abs(X), Math.Max(Math.Abs(Y), Math.Abs(Z))));
        if (!double.IsFinite(largest) || !double.IsFinite(W) || !double.IsFinite(X) || !double.IsFinite(Y) || !double.IsFinite(Z) || largest == 0)
            throw new ArgumentException("A finite nonzero Gaussian quaternion is required.");
        double w = W / largest, x = X / largest, y = Y / largest, z = Z / largest, length = Math.Sqrt(w * w + x * x + y * y + z * z);
        return new(w / length, x / length, y / length, z / length);
    }
}

/// <summary>Unquantized SH radiance values; clamped to display range only at image output.</summary>
public readonly record struct GaussianColor(double R, double G, double B)
{
    public static GaussianColor Black => new(0, 0, 0);
    public bool IsFinite => double.IsFinite(R) && double.IsFinite(G) && double.IsFinite(B);
}

/// <summary>Original 3DGS parameters. SH storage is coefficient-major RGB, including DC, with degree 0 through 3.</summary>
public sealed class GaussianSplat
{
    private readonly float[] _coefficients;
    public GaussianVector Center { get; }
    public GaussianVector LogScale { get; }
    public GaussianQuaternion Rotation { get; }
    public double OpacityLogit { get; }
    public int Degree { get; }
    public ReadOnlyMemory<float> Coefficients => _coefficients;
    public GaussianSplat(GaussianVector center, GaussianVector logScale, GaussianQuaternion rotation, double opacityLogit, int degree, float[] coefficients)
    {
        if (!center.IsFinite || !logScale.IsFinite || !double.IsFinite(opacityLogit)) throw new ArgumentException("Gaussian parameters must be finite.");
        _ = rotation.Normalized();
        if (degree is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(degree));
        ArgumentNullException.ThrowIfNull(coefficients);
        if (coefficients.Length != 3 * (degree + 1) * (degree + 1) || coefficients.Any(v => !float.IsFinite(v)))
            throw new ArgumentException("Gaussian SH coefficients must be complete and finite.", nameof(coefficients));
        Center = center; LogScale = logScale; Rotation = rotation; OpacityLogit = opacityLogit; Degree = degree;
        _coefficients = (float[])coefficients.Clone();
    }
}

/// <summary>Pixel centers have integer coordinates, Y points down; depth is positive distance along Forward, not hardware Z.</summary>
public sealed record GaussianCamera(GaussianVector Eye, GaussianVector Right, GaussianVector Up, GaussianVector Forward,
    bool Perspective, int Width, int Height, double Fx, double Fy, double Cx, double Cy, double Near, double Far)
{
    public void Validate()
    {
        if (!Eye.IsFinite || !Right.IsFinite || !Up.IsFinite || !Forward.IsFinite || Width <= 0 || Height <= 0 ||
            !double.IsFinite(Fx) || !double.IsFinite(Fy) || Fx <= 0 || Fy <= 0 || !double.IsFinite(Cx) || !double.IsFinite(Cy) ||
            !double.IsFinite(Near) || !double.IsFinite(Far) || Near <= 0 || Far <= Near)
            throw new ArgumentException("Gaussian camera has invalid dimensions, projection, or clipping range.");
        if (Math.Abs(Right.Length - 1) > 1e-6 || Math.Abs(Up.Length - 1) > 1e-6 || Math.Abs(Forward.Length - 1) > 1e-6 ||
            Math.Abs(GaussianVector.Dot(Right, Up)) > 1e-6 || Math.Abs(GaussianVector.Dot(Right, Forward)) > 1e-6 || Math.Abs(GaussianVector.Dot(Up, Forward)) > 1e-6)
            throw new ArgumentException("Gaussian camera basis must be orthonormal.");
    }
    public GaussianVector WorldAtPixel(double x, double y, double depth)
    {
        Validate();
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(depth) || depth <= 0) throw new ArgumentException("Pixel coordinates and positive depth must be finite.");
        double scale = Perspective ? depth : 1;
        return Eye + Right * ((x - Cx) * scale / Fx) - Up * ((y - Cy) * scale / Fy) + Forward * depth;
    }
}

/// <summary>Standard 3DGS center-depth projection, not a reconstructed surface. Axis vectors include SupportSigma.</summary>
public sealed record ProjectedGaussian(double CenterX, double CenterY, double Depth, double CovXX, double CovXY, double CovYY,
    GaussianColor Color, double Opacity, double Axis1X, double Axis1Y, double Axis2X, double Axis2Y, double SupportSigma);
