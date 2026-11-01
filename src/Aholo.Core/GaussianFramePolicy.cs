namespace Aholo.Core;

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
