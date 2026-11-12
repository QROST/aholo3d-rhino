namespace Aholo3D.Core;

/// <summary>A stable sampling grid associated with the independently observed native window size.</summary>
public readonly record struct GaussianNativeSampling(int Width, int Height, int WindowWidth, int WindowHeight);

/// <summary>
/// Separates a Gaussian frame's projective camera identity from its CPU depth-culling coverage.
/// Sampling dimensions may scale proportionally (logical versus physical DPI pixels). The accepted frame keeps its original
/// sampling resolution and pixel filter. Host view/document/placement identity and semantic window resize remain adapter concerns.
/// </summary>
public static class GaussianFramePolicy
{
    // These tolerances cover arithmetic noise, not visible camera movement. World positions are never compared relatively.
    public const double IntrinsicRelativeTolerance = 1e-9;
    public const double IntrinsicAbsoluteTolerance = 1e-12;
    public const double AspectRelativeTolerance = 1e-12;
    public const double BasisAbsoluteTolerance = 1e-12;
    public const double DepthCoverageRelativePadding = 1e-9;

    /// <summary>
    /// Accept only a real native size or its DPI conversion, allowing one pixel of integer rounding in either direction.
    /// The native size must come from the model view inventory outside a display callback, never from a preview viewport.
    /// Logical/device inventory representations may alternate, so the conversion is intentionally symmetric.
    /// </summary>
    public static bool MatchesNativeSampling(int width, int height, int nativeWidth, int nativeHeight, double dpi)
    {
        if (width <= 0 || height <= 0 || nativeWidth <= 0 || nativeHeight <= 0) return false;
        if (width == nativeWidth && height == nativeHeight) return true;
        if (!double.IsFinite(dpi) || dpi <= 0) return false;
        return RoundedScale(width, height, nativeWidth, nativeHeight, dpi) || RoundedScale(nativeWidth, nativeHeight, width, height, dpi);
    }

    /// <summary>
    /// Keep the first accepted framebuffer grid while the independent native window bounds stay unchanged. Inventory
    /// logical/device size changes alone cannot replace it. A real window resize starts a new grid and must also change
    /// the adapter's scene key, even for a proportional resize whose normalized camera happens to remain identical.
    /// </summary>
    public static GaussianNativeSampling ObserveNativeWindow(GaussianNativeSampling? previous, int frameWidth, int frameHeight, int windowWidth, int windowHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0 || windowWidth <= 0 || windowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(frameWidth), "Native frame and window sizes must be positive.");
        if (previous is { Width: > 0, Height: > 0 } stable && stable.WindowWidth == windowWidth && stable.WindowHeight == windowHeight) return stable;
        return new(frameWidth, frameHeight, windowWidth, windowHeight);
    }

    /// <summary>
    /// Resample a camera only after MatchesNativeSampling has confirmed that this is a native framebuffer. This removes
    /// fractional-DPI integer aspect drift without relaxing any pose/frustum comparison. The half-pixel convention and
    /// the full observed pose and depth interval are preserved; the first grid also preserves its original pixel filter.
    /// </summary>
    public static GaussianCamera NormalizeSampling(GaussianCamera camera, GaussianNativeSampling sampling)
    {
        ArgumentNullException.ThrowIfNull(camera); camera.Validate();
        if (sampling.Width <= 0 || sampling.Height <= 0 || sampling.WindowWidth <= 0 || sampling.WindowHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampling));
        if (camera.Width == sampling.Width && camera.Height == sampling.Height) return camera;
        double x = (double)sampling.Width / camera.Width, y = (double)sampling.Height / camera.Height;
        return camera with
        {
            Width = sampling.Width, Height = sampling.Height, Fx = camera.Fx * x, Fy = camera.Fy * y,
            Cx = (camera.Cx + .5) * x - .5, Cy = (camera.Cy + .5) * y - .5
        };
    }

    private static bool RoundedScale(int width, int height, int nativeWidth, int nativeHeight, double dpi) =>
        Math.Abs(width - Math.Round(nativeWidth * dpi)) <= 1 && Math.Abs(height - Math.Round(nativeHeight * dpi)) <= 1;

    /// <summary>
    /// Near/far are deliberately excluded. Normalized intrinsics include the half-pixel center convention.
    /// Compare against the camera of the accepted/requested frame, never a repeatedly updated last-observed camera: otherwise
    /// many sub-tolerance observations could accumulate into an unbounded unacknowledged change.
    /// </summary>
    public static bool SameProjection(GaussianCamera? prepared, GaussianCamera? current)
    {
        if (!Valid(prepared) || !Valid(current)) return false;
        if (prepared!.Perspective != current!.Perspective || prepared.Eye != current.Eye) return false;
        if (!SameBasis(prepared.Right, current.Right) || !SameBasis(prepared.Up, current.Up) || !SameBasis(prepared.Forward, current.Forward)) return false;
        double previousAspect = (double)prepared.Width / prepared.Height, currentAspect = (double)current.Width / current.Height;
        if (!Near(previousAspect, currentAspect, AspectRelativeTolerance, 0)) return false;
        return Intrinsic(prepared.Fx / prepared.Width, current.Fx / current.Width) &&
            Intrinsic(prepared.Fy / prepared.Height, current.Fy / current.Height) &&
            Intrinsic((prepared.Cx + .5) / prepared.Width, (current.Cx + .5) / current.Width) &&
            Intrinsic((prepared.Cy + .5) / prepared.Height, (current.Cy + .5) / current.Height);
    }

    /// <summary>
    /// Reuse requires the new clip interval to be contained in the interval used to prepare the frame. Native geometry depth
    /// tests still use the host's current camera/clip mapping at draw time. No expanded range may silently expose missing GS.
    /// </summary>
    public static bool CanReuse(GaussianCamera? prepared, GaussianCamera? current) =>
        SameProjection(prepared, current) && current!.Near >= prepared!.Near && current.Far <= prepared.Far;

    /// <summary>
    /// Preserve the previous accepted pose, intrinsics, and sampling resolution, and expand depth coverage monotonically.
    /// On expansion, add a tiny outward relative margin at both ends so successive clip-plane ULP noise does not repeatedly
    /// cancel uploads. Already-contained observations leave the accepted interval unchanged. Near stays positive and far finite.
    /// This only broadens CPU center-culling coverage; the current native depth test still determines actual visibility.
    /// Alternating equivalent cameras therefore converge after one union instead of cancelling each other's uploads.
    /// Call only when host document, view, asset and placement keys also match. A real projection change requires a fresh frame.
    /// </summary>
    public static GaussianCamera CoverDepthRange(GaussianCamera prepared, GaussianCamera current)
    {
        ArgumentNullException.ThrowIfNull(prepared); ArgumentNullException.ThrowIfNull(current);
        if (!SameProjection(prepared, current)) throw new ArgumentException("Depth coverage can only be combined for equivalent Gaussian projections.", nameof(current));
        double near = Math.Min(prepared.Near, current.Near), far = Math.Max(prepared.Far, current.Far);
        if (near == prepared.Near && far == prepared.Far) return prepared;
        double paddedNear = Math.Max(double.Epsilon, near * (1 - DepthCoverageRelativePadding));
        double paddedFar = far * (1 + DepthCoverageRelativePadding);
        if (!double.IsFinite(paddedFar)) paddedFar = double.MaxValue;
        // At subnormal magnitudes the relative margin may round away. Retain at least one outward ULP where possible.
        if (paddedNear == near && near > double.Epsilon) paddedNear = Math.BitDecrement(near);
        if (paddedFar == far && far < double.MaxValue) paddedFar = Math.BitIncrement(far);
        return prepared with { Near = paddedNear, Far = paddedFar };
    }

    private static bool Valid(GaussianCamera? camera)
    {
        if (camera is null) return false;
        try { camera.Validate(); return true; }
        catch (ArgumentException) { return false; }
    }
    private static bool Intrinsic(double a, double b) => Near(a, b, IntrinsicRelativeTolerance, IntrinsicAbsoluteTolerance);
    private static bool SameBasis(GaussianVector a, GaussianVector b) =>
        Math.Abs(a.X - b.X) <= BasisAbsoluteTolerance && Math.Abs(a.Y - b.Y) <= BasisAbsoluteTolerance && Math.Abs(a.Z - b.Z) <= BasisAbsoluteTolerance;
    private static bool Near(double a, double b, double relativeTolerance, double absoluteTolerance) =>
        double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= absoluteTolerance + relativeTolerance * Math.Max(Math.Abs(a), Math.Abs(b));
}
