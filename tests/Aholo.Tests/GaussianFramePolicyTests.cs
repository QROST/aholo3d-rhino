using Aholo.Core;

namespace Aholo.Tests;

public sealed class GaussianFramePolicyTests
{
    private static GaussianCamera Camera() => new(new(10, -20, 30), new(1, 0, 0), new(0, 1, 0), new(0, 0, -1), true,
        1024, 768, 900, 950, 511.5, 383.5, .1, 100);
    private static GaussianCamera ScalePixels(GaussianCamera camera, int scale) => camera with
    {
        Width = camera.Width * scale, Height = camera.Height * scale, Fx = camera.Fx * scale, Fy = camera.Fy * scale,
        Cx = (camera.Cx + .5) * scale - .5, Cy = (camera.Cy + .5) * scale - .5
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LogicalAndPhysicalDpiFramesShareProjection(bool perspective)
    {
        GaussianCamera logical = Camera() with { Perspective = perspective }, physical = ScalePixels(logical, 2);
        Assert.True(GaussianFramePolicy.SameProjection(logical, physical));
        Assert.True(GaussianFramePolicy.SameProjection(physical, logical));
        Assert.True(GaussianFramePolicy.CanReuse(logical, physical));
        Assert.Same(logical, GaussianFramePolicy.CoverDepthRange(logical, physical));
    }

    [Fact]
    public void HalfPixelConventionIsRequiredForDpiEquivalence()
    {
        GaussianCamera logical = Camera(), incorrectlyScaled = ScalePixels(logical, 2) with { Cx = logical.Cx * 2, Cy = logical.Cy * 2 };
        Assert.False(GaussianFramePolicy.SameProjection(logical, incorrectlyScaled));
    }

    [Fact]
    public void DerivedIntrinsicRoundoffDoesNotCancelAnEquivalentFrame()
    {
        GaussianCamera original = Camera(), observed = original with
        {
            Fx = original.Fx * (1 + 1e-11), Fy = original.Fy * (1 - 1e-11), Cx = original.Cx + 1e-9, Cy = original.Cy - 1e-9,
            Right = new(1, 1e-13, 0), Up = new(-1e-13, 1, 0)
        };
        Assert.True(GaussianFramePolicy.SameProjection(original, observed));
        Assert.True(GaussianFramePolicy.CanReuse(original, observed));
        GaussianCamera expanded = GaussianFramePolicy.CoverDepthRange(original, observed with { Far = 200 });
        Assert.Equal(original.Right, expanded.Right); Assert.Equal(original.Fx, expanded.Fx); Assert.Equal(original.Cx, expanded.Cx);
    }

    [Fact]
    public void ClipExpansionRequiresUnionAndClipContractionReusesCurrentNativeDepth()
    {
        GaussianCamera prepared = Camera(), observed = prepared with { Near = .2, Far = 200 };
        Assert.True(GaussianFramePolicy.SameProjection(prepared, observed));
        Assert.False(GaussianFramePolicy.CanReuse(prepared, observed));
        GaussianCamera covered = GaussianFramePolicy.CoverDepthRange(prepared, observed);
        Assert.InRange(covered.Near, .1 * (1 - 2e-9), .1); Assert.InRange(covered.Far, 200, 200 * (1 + 2e-9));
        Assert.True(GaussianFramePolicy.CanReuse(covered, prepared)); Assert.True(GaussianFramePolicy.CanReuse(covered, observed));
        Assert.True(GaussianFramePolicy.CanReuse(covered, prepared with { Near = .5, Far = 50 }));
        Assert.False(GaussianFramePolicy.CanReuse(covered, prepared with { Near = .05 }));
        GaussianCamera nearExpanded = GaussianFramePolicy.CoverDepthRange(covered, prepared with { Near = .05 });
        Assert.InRange(nearExpanded.Near, .05 * (1 - 2e-9), .05); Assert.True(nearExpanded.Far >= covered.Far);
        Assert.Same(nearExpanded, GaussianFramePolicy.CoverDepthRange(nearExpanded, observed));
    }

    [Fact]
    public void MoreThanTwoHundredAlternatingObservationsFinishAll104BatchesAfterOneCoverageExpansion()
    {
        GaussianCamera logical = Camera(), physical = ScalePixels(logical, 2) with { Near = .2, Far = 200 };
        GaussianCamera? accepted = null;
        const int totalBatches = 104;
        int generations = 0, expansions = 0, uploaded = 0, completions = 0;
        for (int tick = 0; tick < 256; ++tick)
        {
            GaussianCamera observed = tick % 2 == 0 ? logical : physical;
            if (accepted is null) { accepted = observed; generations++; }
            else if (!GaussianFramePolicy.CanReuse(accepted, observed))
            {
                Assert.True(GaussianFramePolicy.SameProjection(accepted, observed));
                accepted = GaussianFramePolicy.CoverDepthRange(accepted, observed); uploaded = 0; generations++; expansions++;
            }
            if (uploaded < totalBatches && ++uploaded == totalBatches) completions++;
        }
        Assert.Equal(2, generations); Assert.Equal(1, expansions); Assert.Equal(totalBatches, uploaded); Assert.Equal(1, completions);
        Assert.Equal(logical.Width, accepted!.Width); Assert.Equal(logical.Height, accepted.Height);
        Assert.Equal(logical.Fx, accepted.Fx); Assert.Equal(logical.Cx, accepted.Cx);
        Assert.True(GaussianFramePolicy.CanReuse(accepted, logical)); Assert.True(GaussianFramePolicy.CanReuse(accepted, physical));
    }

    [Fact]
    public void ContinuousClipUlpNoiseConvergesAfterOneExpansionAndFinishesAll104Batches()
    {
        GaussianCamera original = Camera(), accepted = original, observed = original;
        int expansions = 0, uploaded = 0, completions = 0;
        for (int tick = 0; tick < 256; ++tick)
        {
            observed = observed with { Near = Math.BitDecrement(observed.Near), Far = Math.BitIncrement(observed.Far) };
            GaussianCamera dpiObservation = tick % 2 == 0 ? observed : ScalePixels(observed, 2);
            if (!GaussianFramePolicy.CanReuse(accepted, dpiObservation))
            {
                accepted = GaussianFramePolicy.CoverDepthRange(accepted, dpiObservation); expansions++; uploaded = 0;
            }
            if (uploaded < 104 && ++uploaded == 104) completions++;
        }
        Assert.Equal(1, expansions); Assert.Equal(104, uploaded); Assert.Equal(1, completions);
        Assert.True(GaussianFramePolicy.CanReuse(accepted, observed));
        Assert.Same(accepted, GaussianFramePolicy.CoverDepthRange(accepted, observed));
        Assert.Equal(original.Width, accepted.Width); Assert.Equal(original.Fx, accepted.Fx);
    }

    [Fact]
    public void CoveragePaddingStaysPositiveAndFiniteAtDoubleExtremes()
    {
        GaussianCamera prepared = Camera() with { Near = double.Epsilon * 2, Far = double.MaxValue / 2 };
        GaussianCamera current = prepared with { Near = double.Epsilon, Far = double.MaxValue };
        GaussianCamera covered = GaussianFramePolicy.CoverDepthRange(prepared, current);
        Assert.Equal(double.Epsilon, covered.Near); Assert.Equal(double.MaxValue, covered.Far);
        covered.Validate();
        Assert.True(GaussianFramePolicy.CanReuse(covered, current));
        Assert.Same(covered, GaussianFramePolicy.CoverDepthRange(covered, current));
        GaussianCamera tinyPrepared = Camera() with { Near = double.Epsilon * 4, Far = double.Epsilon * 16 };
        GaussianCamera tinyExpanded = GaussianFramePolicy.CoverDepthRange(tinyPrepared, tinyPrepared with { Far = double.Epsilon * 17 });
        Assert.True(tinyExpanded.Near > 0 && tinyExpanded.Near < tinyPrepared.Near);
        Assert.True(tinyExpanded.Far > double.Epsilon * 17 && double.IsFinite(tinyExpanded.Far));
        tinyExpanded.Validate();
    }

    [Fact]
    public void RealCameraMotionZoomAspectAndProjectionChangesInvalidate()
    {
        GaussianCamera original = Camera();
        double angle = 1e-6, sin = Math.Sin(angle), cos = Math.Cos(angle);
        GaussianCamera[] different =
        {
            original with { Eye = original.Eye + new GaussianVector(1e-10, 0, 0) },
            original with { Right = new(cos, sin, 0), Up = new(-sin, cos, 0) },
            original with { Fx = original.Fx * 1.001 },
            original with { Cx = original.Cx + .01 },
            original with { Width = original.Width + 1 },
            original with { Perspective = false }
        };
        foreach (GaussianCamera current in different)
        {
            Assert.False(GaussianFramePolicy.SameProjection(original, current));
            Assert.False(GaussianFramePolicy.CanReuse(original, current));
            Assert.Throws<ArgumentException>(() => GaussianFramePolicy.CoverDepthRange(original, current));
        }
    }

    [Fact]
    public void LargeWorldCoordinatesNeverProduceRelativePositionTolerance()
    {
        GaussianCamera original = Camera() with { Eye = new(1e12, -1e12, 30) };
        GaussianCamera moved = original with { Eye = original.Eye + new GaussianVector(.001, 0, 0) };
        Assert.NotEqual(original.Eye, moved.Eye);
        Assert.False(GaussianFramePolicy.SameProjection(original, moved));
    }

    [Fact]
    public void ComparingToAcceptedFramePreventsAccumulatedSubToleranceZoomDrift()
    {
        GaussianCamera accepted = Camera(); bool exceeded = false;
        for (int i = 1; i <= 100; ++i)
        {
            GaussianCamera observed = accepted with { Fx = accepted.Fx * (1 + i * 1e-10) };
            if (!GaussianFramePolicy.SameProjection(accepted, observed)) { exceeded = true; break; }
        }
        Assert.True(exceeded);
    }

    [Fact]
    public void InvalidOrNonFiniteCamerasFailClosed()
    {
        GaussianCamera valid = Camera();
        GaussianCamera?[] invalid =
        {
            null, valid with { Width = 0 }, valid with { Fx = double.NaN }, valid with { Far = double.PositiveInfinity },
            valid with { Near = 0 }, valid with { Near = 200 }, valid with { Cx = double.NaN },
            valid with { Eye = new(double.NaN, 0, 0) }, valid with { Right = new(0, 0, 0) }, valid with { Up = new(1, 0, 0) }
        };
        foreach (GaussianCamera? camera in invalid)
        {
            Assert.False(GaussianFramePolicy.SameProjection(valid, camera)); Assert.False(GaussianFramePolicy.SameProjection(camera, valid));
            Assert.False(GaussianFramePolicy.CanReuse(valid, camera));
            Assert.ThrowsAny<ArgumentException>(() => GaussianFramePolicy.CoverDepthRange(valid, camera!));
        }
    }
}
