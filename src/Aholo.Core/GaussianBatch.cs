namespace Aholo.Core;

/// <summary>Six source-space covariance entries. Column-vector convention, symmetric matrix.</summary>
public readonly record struct GaussianCovariance(double XX, double XY, double XZ, double YY, double YZ, double ZZ)
{
    public GaussianVector Transform(GaussianVector v) => new(XX * v.X + XY * v.Y + XZ * v.Z,
        XY * v.X + YY * v.Y + YZ * v.Z, XZ * v.X + YZ * v.Y + ZZ * v.Z);
}

/// <summary>One immutable source Gaussian plus view-independent values, prepared once per asset.</summary>
public readonly struct GaussianPreparedSplat
{
    public GaussianSplat Source { get; }
    internal GaussianMatrix PrincipalAxes { get; }
    public GaussianCovariance Covariance
    {
        get
        {
            GaussianMatrix value = PrincipalAxes * PrincipalAxes.Transpose();
            return new(value.M11,value.M12,value.M13,value.M22,value.M23,value.M33);
        }
    }
    public double Opacity { get; }
    public GaussianColor DcColor { get; }
    internal GaussianPreparedSplat(GaussianSplat source, GaussianMatrix axes, double opacity, GaussianColor dcColor)
    { Source = source; PrincipalAxes = axes; Opacity = opacity; DcColor = dcColor; }
}

/// <summary>Owns its prepared array; original immutable source Gaussians and every SH coefficient remain available.</summary>
public sealed class GaussianPreparedAsset
{
    private readonly GaussianPreparedSplat[] _splats;
    private GaussianPreparedAsset(GaussianPreparedSplat[] splats) => _splats = splats;
    public int Count => _splats.Length;
    public ref readonly GaussianPreparedSplat this[int index] => ref _splats[index];
    public static GaussianPreparedAsset Prepare(IReadOnlyList<GaussianSplat> source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source); ct.ThrowIfCancellationRequested();
        var prepared = new GaussianPreparedSplat[source.Count];
        for (int i = 0; i < prepared.Length; ++i)
        {
            if ((i & 127) == 0) ct.ThrowIfCancellationRequested();
            GaussianSplat splat = source[i] ?? throw new ArgumentException("Gaussian source cannot contain null entries.", nameof(source));
            GaussianMatrix axes = GaussianMath.PrincipalAxes(splat, GaussianTransform.Identity);
            if (!(axes * axes.Transpose()).IsFinite) throw new ArgumentException("Gaussian covariance exceeds numeric precision.");
            ReadOnlySpan<float> sh = splat.Coefficients.Span;
            double dc = Math.Sqrt(1 / (4 * Math.PI));
            var color = new GaussianColor(Math.Max(0, .5 + dc * sh[0]), Math.Max(0, .5 + dc * sh[1]), Math.Max(0, .5 + dc * sh[2]));
            prepared[i] = new(splat, axes, GaussianMath.Sigmoid(splat.OpacityLogit), color);
        }
        ct.ThrowIfCancellationRequested();
        return new GaussianPreparedAsset(prepared);
    }
}

/// <summary>Allocation-free projected value. Identity fields let one frame combine multiple instances and preserve deterministic transparent ordering.</summary>
public readonly record struct GaussianProjectedValue(double CenterX, double CenterY, double Depth, double CovXX, double CovXY, double CovYY,
    GaussianColor Color, double Opacity, double Axis1X, double Axis1Y, double Axis2X, double Axis2Y, double SupportSigma,
    int InstanceId, int SourceIndex, long GlobalOrdinal);

/// <summary>
/// Validated immutable camera/instance context. Camera basis, inverse instance transform and orthographic SH basis are computed once.
/// No host objects, native calls, or per-Gaussian heap allocations are used; safe for background projection.
/// </summary>
public sealed class GaussianProjectionContext
{
    private readonly GaussianCamera _camera;
    private readonly GaussianVector _rowX, _rowY, _rowZ, _offset, _sourceEye;
    private readonly double[]? _orthographicBasis;
    private readonly double _filterVariance, _supportSigma;
    public GaussianCamera Camera => _camera;
    public int InstanceId { get; }
    public bool Antialiased { get; }
    private GaussianProjectionContext(GaussianCamera camera, GaussianTransform transform, GaussianMatrix inverse, int instanceId, double filterVariance, double supportSigma, bool antialiased)
    {
        _camera = camera; InstanceId = instanceId; Antialiased = antialiased; _filterVariance = filterVariance; _supportSigma = supportSigma;
        GaussianMatrix transposed = transform.Linear.Transpose();
        _rowX = transposed.Transform(camera.Right); _rowY = transposed.Transform(-camera.Up); _rowZ = transposed.Transform(camera.Forward);
        GaussianVector translated = transform.Translation - camera.Eye;
        _offset = new(GaussianVector.Dot(camera.Right, translated), -GaussianVector.Dot(camera.Up, translated), GaussianVector.Dot(camera.Forward, translated));
        _sourceEye = inverse.Transform(camera.Eye - transform.Translation);
        if (!_rowX.IsFinite || !_rowY.IsFinite || !_rowZ.IsFinite || !_offset.IsFinite || !_sourceEye.IsFinite)
            throw new ArgumentException("Gaussian camera/instance combination exceeds numeric precision.");
        if (!camera.Perspective)
        {
            _orthographicBasis = new double[16];
            FillBasis(3, inverse.Transform(camera.Forward), _orthographicBasis);
        }
    }
    public static GaussianProjectionContext Create(GaussianCamera camera, GaussianTransform transform, int instanceId = 0, double filterVariance = .3, double supportSigma = 3, bool antialiased = false)
    {
        ArgumentNullException.ThrowIfNull(camera); camera.Validate();
        if (!double.IsFinite(filterVariance) || filterVariance < 0 || !double.IsFinite(supportSigma) || supportSigma <= 0)
            throw new ArgumentException("Gaussian pixel filter and support must be finite and nonnegative/positive.");
        if (!transform.Translation.IsFinite) throw new ArgumentException("Gaussian translation must be finite.");
        GaussianMatrix inverse = transform.Linear.Inverse();
        return new(camera, transform, inverse, instanceId, filterVariance, supportSigma, antialiased);
    }

    /// <summary>Writes the visible prefix. No input-count cap or LOD is applied. Cancellation leaves a partial destination that must not be published.</summary>
    public int Project(GaussianPreparedAsset asset, Span<GaussianProjectedValue> destination, long firstOrdinal = 0, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asset); ct.ThrowIfCancellationRequested();
        if (destination.Length < asset.Count) throw new ArgumentException("Destination must have room for the entire input asset.", nameof(destination));
        if (firstOrdinal < 0 || (asset.Count > 0 && firstOrdinal > long.MaxValue - (asset.Count - 1))) throw new ArgumentOutOfRangeException(nameof(firstOrdinal));
        int visible = 0;
        for (int i = 0; i < asset.Count; ++i)
        {
            if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
            if (TryProject(in asset[i], i, firstOrdinal + i, out GaussianProjectedValue projected)) destination[visible++] = projected;
        }
        ct.ThrowIfCancellationRequested(); return visible;
    }

    public bool TryProject(in GaussianPreparedSplat prepared, int sourceIndex, long globalOrdinal, out GaussianProjectedValue result)
    {
        result = default;
        GaussianSplat source = prepared.Source ?? throw new ArgumentException("Gaussian must be prepared before projection.", nameof(prepared));
        if (sourceIndex < 0 || globalOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        GaussianVector center = source.Center;
        double x = GaussianVector.Dot(_rowX, center) + _offset.X, y = GaussianVector.Dot(_rowY, center) + _offset.Y, z = GaussianVector.Dot(_rowZ, center) + _offset.Z;
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) throw new ArgumentException("Transformed Gaussian center exceeds numeric precision.");
        if (z < _camera.Near || z > _camera.Far) return false;
        double centerX = _camera.Cx + _camera.Fx * (_camera.Perspective ? x / z : x), centerY = _camera.Cy + _camera.Fy * (_camera.Perspective ? y / z : y);
        // Chain-rule Jacobians in source coordinates avoid constructing A * covariance * transpose(A) per view.
        GaussianVector jx = _rowX * (_camera.Perspective ? _camera.Fx / z : _camera.Fx), jy = _rowY * (_camera.Perspective ? _camera.Fy / z : _camera.Fy);
        if (_camera.Perspective)
        {
            jx -= _rowZ * (_camera.Fx * x / (z * z)); jy -= _rowZ * (_camera.Fy * y / (z * z));
        }
        GaussianMatrix axes = prepared.PrincipalAxes.Transpose();
        GaussianVector projectedX = axes.Transform(jx), projectedY = axes.Transform(jy);
        double rawXX = GaussianVector.Dot(projectedX, projectedX), rawYY = GaussianVector.Dot(projectedY, projectedY);
        double xx = rawXX + _filterVariance, xy = GaussianVector.Dot(projectedX, projectedY), yy = rawYY + _filterVariance;
        double determinant = xx * yy - xy * xy;
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY) || !double.IsFinite(xx) || !double.IsFinite(xy) || !double.IsFinite(yy) || !double.IsFinite(determinant) || xx <= 0 || yy <= 0 || determinant <= 0)
            throw new ArgumentException("Projected Gaussian covariance is not finite and positive definite.");
        double extentX = _supportSigma * Math.Sqrt(xx), extentY = _supportSigma * Math.Sqrt(yy);
        if (!double.IsFinite(extentX) || !double.IsFinite(extentY)) throw new ArgumentException("Projected Gaussian support exceeds numeric precision.");
        if (centerX + extentX < -.5 || centerY + extentY < -.5 || centerX - extentX > _camera.Width - .5 || centerY - extentY > _camera.Height - .5) return false;
        double theta = .5 * Math.Atan2(2 * xy, xx - yy), cosine = Math.Cos(theta), sine = Math.Sin(theta);
        double lambda1 = xx * cosine * cosine + 2 * xy * cosine * sine + yy * sine * sine, lambda2 = determinant / lambda1;
        double major = _supportSigma * Math.Sqrt(lambda1), minor = _supportSigma * Math.Sqrt(lambda2);
        if (!double.IsFinite(major) || !double.IsFinite(minor) || major <= 0 || minor <= 0) throw new ArgumentException("Projected Gaussian axes exceed numeric precision.");
        GaussianColor color = source.Degree == 0 ? prepared.DcColor : Evaluate(source);
        double opacity = prepared.Opacity;
        if (Antialiased && _filterVariance > 0) opacity *= GaussianAntialiasing.Compensation(rawXX, xy, rawYY, determinant);
        result = new(centerX, centerY, z, xx, xy, yy, color, opacity, major * cosine, major * sine,
            -minor * sine, minor * cosine, _supportSigma, InstanceId, sourceIndex, globalOrdinal);
        return true;
    }

    /// <summary>Quad reconstruction using the already validated camera. No repeated orthonormality checks.</summary>
    public GaussianVector WorldAtPixel(double x, double y, double depth)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(depth) || depth <= 0) throw new ArgumentException("Pixel coordinates and positive depth must be finite.");
        double scale = _camera.Perspective ? depth : 1;
        return _camera.Eye + _camera.Right * ((x - _camera.Cx) * scale / _camera.Fx) - _camera.Up * ((y - _camera.Cy) * scale / _camera.Fy) + _camera.Forward * depth;
    }
    private GaussianColor Evaluate(GaussianSplat source)
    {
        // This helper returns on each Gaussian, so stack scratch never accumulates across a batch loop.
        Span<double> scratch = stackalloc double[16];
        scoped ReadOnlySpan<double> basis;
        int count = (source.Degree + 1) * (source.Degree + 1);
        if (_orthographicBasis is not null) basis = _orthographicBasis.AsSpan(0, count);
        else { FillBasis(source.Degree, source.Center - _sourceEye, scratch); basis = scratch[..count]; }
        ReadOnlySpan<float> coefficients = source.Coefficients.Span;
        double r = .5, g = .5, b = .5;
        for (int i = 0; i < basis.Length; ++i)
        { r += basis[i] * coefficients[3 * i]; g += basis[i] * coefficients[3 * i + 1]; b += basis[i] * coefficients[3 * i + 2]; }
        return new(Math.Max(0, r), Math.Max(0, g), Math.Max(0, b));
    }
    private static void FillBasis(int degree, GaussianVector direction, Span<double> output)
    {
        GaussianVector unit = direction.Normalized();
        double z = Math.Clamp(unit.Z, -1, 1), phi = Math.Atan2(unit.Y, unit.X), sine = Math.Sqrt(Math.Max(0, 1 - z * z)), diagonal = 1;
        for (int m = 0; m <= degree; ++m)
        {
            if (m != 0) diagonal *= -(2 * m - 1) * sine;
            double previous = 0, current = diagonal;
            double sin = Math.Sin(m * phi), cos = Math.Cos(m * phi);
            for (int l = m; l <= degree; ++l)
            {
                if (l > m)
                {
                    double next = ((2 * l - 1) * z * current - (l + m - 1) * previous) / (l - m);
                    previous = current; current = next;
                }
                double ratio = 1;
                for (int k = l - m + 1; k <= l + m; ++k) ratio /= k;
                double value = Math.Sqrt((2 * l + 1) * ratio / (4 * Math.PI)) * current;
                if (m == 0) output[l * (l + 1)] = value;
                else { output[l * (l + 1) - m] = Math.Sqrt(2) * value * sin; output[l * (l + 1) + m] = Math.Sqrt(2) * value * cos; }
            }
        }
    }
}

/// <summary>Cancellable allocation-free global index sort. Caller owns buffers (including any ArrayPool rental lifecycle).</summary>
public static class GaussianDepthSorter
{
    /// <summary>
    /// Back-to-front depth, then reverse source insertion order to match the CPU oracle's stable front-to-back compositor.
    /// Cancellation may partially reorder indices; discard the interrupted generation rather than publishing it.
    /// </summary>
    public static void SortBackToFront(Span<int> indices, ReadOnlySpan<GaussianProjectedValue> values, Span<int> scratch, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (scratch.Length < indices.Length) throw new ArgumentException("Sort scratch must be at least as long as the index range.", nameof(scratch));
        if (indices.Overlaps(scratch)) throw new ArgumentException("Sort scratch must not overlap the indices.", nameof(scratch));
        for (int i = 0; i < indices.Length; ++i)
        {
            if ((i & 1023) == 0) ct.ThrowIfCancellationRequested();
            int index = indices[i];
            if ((uint)index >= (uint)values.Length || !double.IsFinite(values[index].Depth) || values[index].Depth <= 0)
                throw new ArgumentException("Every sort index must reference a finite positive projected depth.", nameof(indices));
        }
        if (indices.Length < 2) return;
        Span<int> source = indices, destination = scratch[..indices.Length];
        bool sourceIsIndices = true;
        for (int width = 1; width < indices.Length; width = width > indices.Length / 2 ? indices.Length : width * 2)
        {
            ct.ThrowIfCancellationRequested();
            int start = 0;
            while (start < indices.Length)
            {
                ct.ThrowIfCancellationRequested();
                int middle = start + Math.Min(width, indices.Length - start), end = middle + Math.Min(width, indices.Length - middle);
                int left = start, right = middle;
                for (int output = start; output < end; ++output)
                {
                    if ((output & 1023) == 0) ct.ThrowIfCancellationRequested();
                    bool takeLeft = right >= end || (left < middle && BeforeOrEqual(in values[source[left]], in values[source[right]]));
                    destination[output] = takeLeft ? source[left++] : source[right++];
                }
                start = end;
            }
            Span<int> temp = source; source = destination; destination = temp; sourceIsIndices = !sourceIsIndices;
        }
        if (!sourceIsIndices)
            for (int i = 0; i < indices.Length; i += Math.Min(1024, indices.Length - i))
            {
                ct.ThrowIfCancellationRequested(); int length = Math.Min(1024, indices.Length - i); source.Slice(i, length).CopyTo(indices.Slice(i, length));
            }
        ct.ThrowIfCancellationRequested();
    }
    private static bool BeforeOrEqual(in GaussianProjectedValue left, in GaussianProjectedValue right) =>
        left.Depth > right.Depth || (left.Depth == right.Depth && left.GlobalOrdinal >= right.GlobalOrdinal);
}
