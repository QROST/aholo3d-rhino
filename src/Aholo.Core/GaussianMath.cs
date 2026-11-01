namespace Aholo.Core;

/// <summary>Independently implemented linear algebra and Gaussian activation, using mathematical definitions.</summary>
public static class GaussianMath
{
    public static double Sigmoid(double logit)
    {
        if (!double.IsFinite(logit)) throw new ArgumentException("Opacity logit must be finite.");
        if (logit >= 0) return 1 / (1 + Math.Exp(-logit));
        double exponential = Math.Exp(logit); return exponential / (1 + exponential);
    }
    public static GaussianMatrix Rotation(GaussianQuaternion quaternion)
    {
        GaussianQuaternion q = quaternion.Normalized();
        // Standard unit-quaternion rotation of column vectors; source order is w,x,y,z.
        return new(1 - 2 * (q.Y * q.Y + q.Z * q.Z), 2 * (q.X * q.Y - q.W * q.Z), 2 * (q.X * q.Z + q.W * q.Y),
            2 * (q.X * q.Y + q.W * q.Z), 1 - 2 * (q.X * q.X + q.Z * q.Z), 2 * (q.Y * q.Z - q.W * q.X),
            2 * (q.X * q.Z - q.W * q.Y), 2 * (q.Y * q.Z + q.W * q.X), 1 - 2 * (q.X * q.X + q.Y * q.Y));
    }
    public static GaussianMatrix Covariance(GaussianSplat splat, GaussianTransform transform)
    {
        ArgumentNullException.ThrowIfNull(splat);
        if (!transform.Translation.IsFinite || !transform.Linear.IsFinite) throw new ArgumentException("Gaussian transform must be finite.");
        _ = transform.Linear.Inverse();
        GaussianMatrix rotation = Rotation(splat.Rotation);
        double sx = Math.Exp(splat.LogScale.X), sy = Math.Exp(splat.LogScale.Y), sz = Math.Exp(splat.LogScale.Z);
        if (!double.IsFinite(sx) || !double.IsFinite(sy) || !double.IsFinite(sz) || sx <= 0 || sy <= 0 || sz <= 0)
            throw new ArgumentException("Gaussian scales exceed numeric precision.");
        GaussianMatrix axes = transform.Linear * rotation * new GaussianMatrix(sx, 0, 0, 0, sy, 0, 0, 0, sz);
        GaussianMatrix result = axes * axes.Transpose();
        if (!result.IsFinite) throw new ArgumentException("Gaussian covariance exceeds numeric precision.");
        return result;
    }
}

/// <summary>Real orthonormal spherical harmonics via associated Legendre recurrence; degree/index convention l(l+1)+m.</summary>
public static class GaussianHarmonics
{
    public static double[] Basis(int degree, GaussianVector direction)
    {
        if (degree is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(degree));
        GaussianVector unit = direction.Normalized();
        double z = Math.Clamp(unit.Z, -1, 1), phi = Math.Atan2(unit.Y, unit.X), sine = Math.Sqrt(Math.Max(0, 1 - z * z));
        var result = new double[(degree + 1) * (degree + 1)];
        double diagonal = 1;
        for (int m = 0; m <= degree; ++m)
        {
            if (m != 0) diagonal *= -(2 * m - 1) * sine; // Condon-Shortley phase.
            double previous = 0, current = diagonal;
            for (int l = m; l <= degree; ++l)
            {
                if (l > m)
                {
                    double next = ((2 * l - 1) * z * current - (l + m - 1) * previous) / (l - m);
                    previous = current; current = next;
                }
                double ratio = 1;
                for (int k = l - m + 1; k <= l + m; ++k) ratio /= k;
                double basis = Math.Sqrt((2 * l + 1) * ratio / (4 * Math.PI)) * current;
                if (m == 0) result[l * (l + 1)] = basis;
                else
                {
                    result[l * (l + 1) - m] = Math.Sqrt(2) * basis * Math.Sin(m * phi);
                    result[l * (l + 1) + m] = Math.Sqrt(2) * basis * Math.Cos(m * phi);
                }
            }
        }
        return result;
    }
    public static GaussianColor Evaluate(GaussianSplat splat, GaussianVector sourceDirection)
    {
        ArgumentNullException.ThrowIfNull(splat);
        double[] basis = Basis(splat.Degree, sourceDirection);
        ReadOnlySpan<float> coefficients = splat.Coefficients.Span;
        double r = .5, g = .5, b = .5;
        for (int i = 0; i < basis.Length; ++i)
        {
            r += basis[i] * coefficients[3 * i]; g += basis[i] * coefficients[3 * i + 1]; b += basis[i] * coefficients[3 * i + 2];
        }
        return new(Math.Max(0, r), Math.Max(0, g), Math.Max(0, b));
    }
}

public static class GaussianProjector
{
    public static ProjectedGaussian? Project(GaussianSplat splat, GaussianCamera camera, GaussianTransform transform, double filterVariance = .3, double supportSigma = 3)
    {
        ArgumentNullException.ThrowIfNull(splat); ArgumentNullException.ThrowIfNull(camera); camera.Validate();
        if (!double.IsFinite(filterVariance) || filterVariance < 0 || !double.IsFinite(supportSigma) || supportSigma <= 0)
            throw new ArgumentException("Gaussian pixel filter and support must be finite and nonnegative/positive.");
        GaussianMatrix inverse = transform.Linear.Inverse();
        GaussianVector world = transform.TransformPoint(splat.Center);
        if (!world.IsFinite) throw new ArgumentException("Transformed Gaussian center exceeds numeric precision.");
        GaussianVector relative = world - camera.Eye;
        double x = GaussianVector.Dot(camera.Right, relative), y = -GaussianVector.Dot(camera.Up, relative), z = GaussianVector.Dot(camera.Forward, relative);
        // This is standard center-based clipping. It does not claim volumetric near-plane clipping.
        if (z < camera.Near || z > camera.Far) return null;
        double centerX = camera.Cx + camera.Fx * (camera.Perspective ? x / z : x), centerY = camera.Cy + camera.Fy * (camera.Perspective ? y / z : y);
        GaussianMatrix covariance = GaussianMath.Covariance(splat, transform);
        GaussianVector jx = camera.Right * (camera.Perspective ? camera.Fx / z : camera.Fx);
        GaussianVector jy = -camera.Up * (camera.Perspective ? camera.Fy / z : camera.Fy);
        if (camera.Perspective)
        {
            jx -= camera.Forward * (camera.Fx * x / (z * z));
            jy -= camera.Forward * (camera.Fy * y / (z * z));
        }
        double xx = GaussianVector.Dot(jx, covariance.Transform(jx)) + filterVariance,
            xy = GaussianVector.Dot(jx, covariance.Transform(jy)), yy = GaussianVector.Dot(jy, covariance.Transform(jy)) + filterVariance;
        double determinant = xx * yy - xy * xy;
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY) || !double.IsFinite(xx) || !double.IsFinite(xy) || !double.IsFinite(yy) || !double.IsFinite(determinant) || xx <= 0 || yy <= 0 || determinant <= 0)
            throw new ArgumentException("Projected Gaussian covariance is not finite and positive definite.");
        // Axis-aligned Gaussian support bounds are exact, including centers outside the viewport.
        double extentX = supportSigma * Math.Sqrt(xx), extentY = supportSigma * Math.Sqrt(yy);
        if (!double.IsFinite(extentX) || !double.IsFinite(extentY)) throw new ArgumentException("Projected Gaussian support exceeds numeric precision.");
        if (centerX + extentX < -.5 || centerY + extentY < -.5 || centerX - extentX > camera.Width - .5 || centerY - extentY > camera.Height - .5) return null;
        double theta = .5 * Math.Atan2(2 * xy, xx - yy), cosine = Math.Cos(theta), sine = Math.Sin(theta);
        double lambda1 = xx * cosine * cosine + 2 * xy * cosine * sine + yy * sine * sine;
        // det/lambda1 avoids cancellation for highly anisotropic footprints.
        double lambda2 = determinant / lambda1;
        double major = supportSigma * Math.Sqrt(lambda1), minor = supportSigma * Math.Sqrt(lambda2);
        if (!double.IsFinite(major) || !double.IsFinite(minor) || major <= 0 || minor <= 0)
            throw new ArgumentException("Projected Gaussian axes exceed numeric precision.");
        GaussianVector direction = inverse.Transform(camera.Perspective ? relative : camera.Forward);
        GaussianColor color = GaussianHarmonics.Evaluate(splat, direction);
        return new(centerX, centerY, z, xx, xy, yy, color, GaussianMath.Sigmoid(splat.OpacityLogit),
            major * cosine, major * sine, -minor * sine, minor * cosine, supportSigma);
    }
}
