using System.Collections;
using System.Diagnostics;
using Aholo.Core;
using Xunit.Abstractions;

namespace Aholo.Tests;

public sealed class GaussianBatchTests
{
    private readonly ITestOutputHelper _output;
    public GaussianBatchTests(ITestOutputHelper output) => _output = output;
    private static GaussianCamera Camera(bool perspective = true) => new(new(3, -2, 30), new(.6, .8, 0), new(-.8, .6, 0), new(0, 0, -1), perspective,
        640, 480, perspective ? 450 : 25, perspective ? 470 : 30, 319.5, 239.5, .1, 1000);
    private static GaussianSplat Splat(int degree = 0, GaussianVector? center = null) => new(center ?? new(0, 0, 0), new(-1, -1.7, -2), new(2, .2, .1, .3), .4,
        degree, Enumerable.Range(0, 3 * (degree + 1) * (degree + 1)).Select(i => (float)Math.Sin(i * .7)).ToArray());

    [Theory]
    [InlineData(0, true, false)][InlineData(1, true, false)][InlineData(2, true, false)][InlineData(3, true, false)]
    [InlineData(0, false, false)][InlineData(1, false, false)][InlineData(2, false, false)][InlineData(3, false, false)]
    [InlineData(0, true, true)][InlineData(1, true, true)][InlineData(2, true, true)][InlineData(3, true, true)]
    [InlineData(0, false, true)][InlineData(1, false, true)][InlineData(2, false, true)][InlineData(3, false, true)]
    public void BatchMatchesIndependentOracleForCameraInstanceAndAllShDegrees(int degree, bool perspective, bool antialiased)
    {
        var random = new Random(7289 + degree);
        var source = new GaussianSplat[128];
        for (int i = 0; i < source.Length; ++i)
        {
            var sh = new float[3 * (degree + 1) * (degree + 1)];
            for (int j = 0; j < sh.Length; ++j) sh[j] = (float)(random.NextDouble() * 4 - 2);
            source[i] = new(new(random.NextDouble() * 12 - 6, random.NextDouble() * 12 - 6, random.NextDouble() * 6 - 3),
                new(-1 - random.NextDouble(), -1.3 - random.NextDouble(), -2 - random.NextDouble()),
                new(random.NextDouble() + .1, random.NextDouble(), random.NextDouble(), random.NextDouble()), random.NextDouble() * 8 - 4, degree, sh);
        }
        var transform = new GaussianTransform(new(1.2, .15, .2, -.1, .8, .05, .1, -.15, 1.8), new(2, 1, -3));
        GaussianCamera camera = Camera(perspective);
        GaussianPreparedAsset asset = GaussianPreparedAsset.Prepare(source);
        GaussianProjectionContext context = GaussianProjectionContext.Create(camera, transform, 73, antialiased: antialiased);
        var output = new GaussianProjectedValue[source.Length];
        int visible = context.Project(asset, output, 4000);
        int expectedVisible = 0;
        for (int i = 0; i < source.Length; ++i)
        {
            ProjectedGaussian? expected = GaussianProjector.Project(source[i], camera, transform, antialiased: antialiased);
            bool present = context.TryProject(in asset[i], i, 4000 + i, out GaussianProjectedValue actual);
            Assert.Equal(expected is not null, present);
            if (expected is null) continue;
            Equivalent(expected, actual);
            Assert.Equal(73, actual.InstanceId); Assert.Equal(i, actual.SourceIndex); Assert.Equal(4000 + i, actual.GlobalOrdinal);
            Assert.Equal(actual, output[expectedVisible++]);
            GaussianVector point = context.WorldAtPixel(actual.CenterX + actual.Axis1X, actual.CenterY + actual.Axis1Y, actual.Depth);
            GaussianVector oraclePoint = camera.WorldAtPixel(actual.CenterX + actual.Axis1X, actual.CenterY + actual.Axis1Y, actual.Depth);
            Assert.Equal(oraclePoint, point);
        }
        Assert.Equal(expectedVisible, visible); Assert.True(visible > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceAxisUnitsAndReflectedInstanceMatchOracle(bool reflected)
    {
        GaussianSplat source = Splat(3, new(2, 3, 4));
        var linear = reflected ? new GaussianMatrix(-1000, 0, 0, 0, 1000, 0, 0, 0, 1000) : new GaussianMatrix(1000, 0, 0, 0, 0, -1000, 0, 1000, 0);
        var transform = new GaussianTransform(linear, new(1000, -1000, 2000));
        var camera = new GaussianCamera(new(0, -30000, 4000), new(1, 0, 0), new(0, 0, 1), new(0, 1, 0), true, 1280, 720, 800, 800, 639.5, 359.5, 1, 100000);
        GaussianPreparedAsset asset = GaussianPreparedAsset.Prepare(new[] { source });
        var context = GaussianProjectionContext.Create(camera, transform);
        Assert.True(context.TryProject(in asset[0], 0, long.MaxValue, out GaussianProjectedValue projected));
        Equivalent(GaussianProjector.Project(source, camera, transform)!, projected);
        var output = new GaussianProjectedValue[1];
        Assert.Equal(1, context.Project(asset, output, long.MaxValue));
        Assert.Equal(long.MaxValue, output[0].GlobalOrdinal);
    }

    [Fact]
    public void PreparedAssetOwnsArrayAndPreservesOriginalSourceData()
    {
        GaussianSplat first = Splat(3), replacement = Splat(0);
        var inputs = new[] { first };
        GaussianPreparedAsset asset = GaussianPreparedAsset.Prepare(inputs); inputs[0] = replacement;
        Assert.Same(first, asset[0].Source);
        Assert.Equal(first.Coefficients.ToArray(), asset[0].Source.Coefficients.ToArray());
        GaussianMatrix expected = GaussianMath.Covariance(first, GaussianTransform.Identity);
        Assert.Equal(new GaussianCovariance(expected.M11, expected.M12, expected.M13, expected.M22, expected.M23, expected.M33), asset[0].Covariance);
        Assert.Equal(GaussianMath.Sigmoid(first.OpacityLogit), asset[0].Opacity);
    }

    [Fact]
    public void BatchPreservesFootprintsAtViewportEdgesAndNearFarClipping()
    {
        var source = new[] { Splat(center: new(-1, 0, 10)), Splat(center: new(-100, 0, 10)), Splat(center: new(0, 0, .01)), Splat(center: new(0, 0, 2000)) };
        var camera = new GaussianCamera(new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), false, 10, 10, 1, 1, 0, 5, .1, 1000);
        GaussianPreparedAsset asset = GaussianPreparedAsset.Prepare(source);
        GaussianProjectionContext context = GaussianProjectionContext.Create(camera, GaussianTransform.Identity);
        var results = new GaussianProjectedValue[source.Length];
        int count = context.Project(asset, results);
        Assert.Equal(1, count); Assert.Equal(0, results[0].SourceIndex);
        Equivalent(GaussianProjector.Project(source[0], camera, GaussianTransform.Identity)!, results[0]);
    }

    [Fact]
    public void MultipleInstancesKeepSourceIdentityAndGlobalTransparentOrder()
    {
        GaussianPreparedAsset asset = GaussianPreparedAsset.Prepare(new[] { Splat(), Splat() });
        var a = GaussianProjectionContext.Create(Camera(), GaussianTransform.Identity, 100);
        var b = GaussianProjectionContext.Create(Camera(), new(GaussianMatrix.Identity, new(0, 0, -5)), 200);
        var output = new GaussianProjectedValue[4];
        Assert.Equal(2, a.Project(asset, output, 0)); Assert.Equal(2, b.Project(asset, output.AsSpan(2), 2));
        int[] indices = { 0, 1, 2, 3 }; var scratch = new int[4];
        GaussianDepthSorter.SortBackToFront(indices, output, scratch);
        Assert.Equal(new[] { 3, 2, 1, 0 }, indices);
        Assert.Equal(new[] { 200, 200, 100, 100 }, indices.Select(i => output[i].InstanceId));
        Assert.Equal(new[] { 1, 0, 1, 0 }, indices.Select(i => output[i].SourceIndex));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(17)]
    [InlineData(128)]
    [InlineData(1025)]
    public void CancellableMergeSortMatchesDepthThenReverseInsertionOrder(int count)
    {
        var random = new Random(1337);
        var values = new GaussianProjectedValue[count];
        for (int i = 0; i < count; ++i) values[i] = Value(random.Next(1, 20), i);
        int[] indices = Enumerable.Range(0, count).Reverse().ToArray();
        int[] expected = indices.OrderByDescending(i => values[i].Depth).ThenByDescending(i => values[i].GlobalOrdinal).ToArray();
        GaussianDepthSorter.SortBackToFront(indices, values, new int[count]);
        Assert.Equal(expected, indices);
    }

    [Fact]
    public void EqualDepthNativeOrderReproducesOracleAlpha()
    {
        GaussianProjectedValue[] values = { Value(2, 0) with { Color = new(1, 0, 0) }, Value(2, 1) with { Color = new(0, 0, 1) } };
        int[] indices = { 0, 1 };
        GaussianDepthSorter.SortBackToFront(indices, values, new int[2]);
        double r = 0, b = 0;
        foreach (int index in indices) { r = values[index].Color.R * .5 + r * .5; b = values[index].Color.B * .5 + b * .5; }
        Assert.Equal(.5, r); Assert.Equal(.25, b); Assert.Equal(new[] { 1, 0 }, indices);
    }

    [Fact]
    public void SortingCanBeCancelledAfterTheMergeHasStarted()
    {
        const int count = 131_072;
        var values = new GaussianProjectedValue[count]; var indices = new int[count]; var scratch = new int[count];
        for (int i = 0; i < count; ++i) { values[i] = Value(1 + i % 137, i); indices[i] = i; scratch[i] = -1; }
        using var cancel = new CancellationTokenSource(); using var ready = new ManualResetEventSlim(); int done = 0;
        var observer = new Thread(() =>
        {
            ready.Set();
            while (Volatile.Read(ref scratch[0]) < 0 && Volatile.Read(ref done) == 0) Thread.SpinWait(20);
            if (Volatile.Read(ref done) == 0) cancel.Cancel();
        }) { IsBackground = true };
        observer.Start(); ready.Wait();
        try { Assert.Throws<OperationCanceledException>(() => GaussianDepthSorter.SortBackToFront(indices, values, scratch, cancel.Token)); }
        finally { Volatile.Write(ref done, 1); Assert.True(observer.Join(TimeSpan.FromSeconds(5))); }
        Assert.True(scratch[0] >= 0); // Cancellation was requested after validation and actual merge output.
    }

    [Fact]
    public void PreparationObservesCancellationWhileEnumeratingAnAsset()
    {
        using var cancel = new CancellationTokenSource();
        var source = new CancellingSource(Splat(), cancel);
        Assert.Throws<OperationCanceledException>(() => GaussianPreparedAsset.Prepare(source, cancel.Token));
        Assert.InRange(source.Reads, 301, 384);
    }

    [Fact]
    public void InvalidBuffersContextsAndCancelledCallsFailExplicitly()
    {
        GaussianPreparedAsset asset = GaussianPreparedAsset.Prepare(new[] { Splat() });
        var context = GaussianProjectionContext.Create(Camera(), GaussianTransform.Identity);
        Assert.Throws<ArgumentException>(() => context.Project(asset, Array.Empty<GaussianProjectedValue>()));
        Assert.Throws<ArgumentException>(() => GaussianProjectionContext.Create(Camera() with { Fx = 0 }, GaussianTransform.Identity));
        Assert.Throws<ArgumentException>(() => GaussianProjectionContext.Create(Camera(), new(default, default)));
        Assert.Throws<ArgumentException>(() => context.TryProject(default, 0, 0, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => context.Project(asset, new GaussianProjectedValue[1], -1));
        Assert.Throws<OperationCanceledException>(() => context.Project(asset, new GaussianProjectedValue[1], ct: new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => GaussianPreparedAsset.Prepare(new[] { Splat() }, new CancellationToken(true)));
        int[] overlap = { 0, 1 };
        Assert.Throws<ArgumentException>(() => GaussianDepthSorter.SortBackToFront(overlap, new[] { Value(1, 0), Value(2, 1) }, overlap));
        Assert.Throws<ArgumentException>(() => GaussianDepthSorter.SortBackToFront(new[] { 1 }, new[] { Value(1, 0) }, new int[1]));
        Assert.Throws<ArgumentException>(() => GaussianDepthSorter.SortBackToFront(new[] { 0 }, new[] { Value(double.NaN, 0) }, new int[1]));
        Assert.Throws<OperationCanceledException>(() => GaussianDepthSorter.SortBackToFront(new[] { 0 }, new[] { Value(1, 0) }, new int[1], new CancellationToken(true)));
    }

    [Theory]
    [InlineData(0, true, false)][InlineData(3, true, false)][InlineData(3, false, false)]
    [InlineData(0, true, true)][InlineData(3, true, true)][InlineData(3, false, true)]
    public void RealWorldSizedBatchHasNoPerGaussianHeapAllocationOrInputLimit(int degree, bool perspective, bool antialiased)
    {
        const int count = 212_864;
        GaussianSplat splat = Splat(degree);
        var source = new GaussianSplat[count]; Array.Fill(source, splat);
        long preparationStart = Stopwatch.GetTimestamp(); long preparationBefore = GC.GetAllocatedBytesForCurrentThread();
        GaussianPreparedAsset asset = GaussianPreparedAsset.Prepare(source);
        long preparationBytes = GC.GetAllocatedBytesForCurrentThread() - preparationBefore;
        double preparationMs = Stopwatch.GetElapsedTime(preparationStart).TotalMilliseconds;
        GaussianProjectionContext context = GaussianProjectionContext.Create(Camera(perspective), GaussianTransform.Identity, antialiased: antialiased);
        var output = new GaussianProjectedValue[count]; var indices = new int[count]; var scratch = new int[count];
        for (int i = 0; i < count; ++i) indices[i] = i;
        // Warm the same hot paths before measuring managed allocations. Caller buffers are allocated above.
        for (int i = 0; i < 512; ++i) Assert.True(context.TryProject(in asset[0], 0, 0, out _));
        GaussianDepthSorter.SortBackToFront(indices.AsSpan(0, 2), new[] { Value(1, 0), Value(1, 1) }, scratch.AsSpan(0, 2));
        indices[0] = 0; indices[1] = 1;
        // Warm the complete batching loop as well as the scalar helper before measuring steady-state allocations.
        context.Project(asset, output);
        long projectStart = Stopwatch.GetTimestamp(), before = GC.GetAllocatedBytesForCurrentThread();
        int visible = context.Project(asset, output);
        long afterProjection = GC.GetAllocatedBytesForCurrentThread(); double projectMs = Stopwatch.GetElapsedTime(projectStart).TotalMilliseconds;
        long sortStart = Stopwatch.GetTimestamp(), beforeSort = GC.GetAllocatedBytesForCurrentThread();
        GaussianDepthSorter.SortBackToFront(indices, output.AsSpan(0, visible), scratch);
        long afterSort = GC.GetAllocatedBytesForCurrentThread(); double sortMs = Stopwatch.GetElapsedTime(sortStart).TotalMilliseconds;
        Assert.Equal(count, asset.Count); Assert.Equal(count, visible);
        Assert.True(afterProjection == before, $"Projection allocated {afterProjection - before} bytes.");
        Assert.True(afterSort == beforeSort, $"Sorting allocated {afterSort - beforeSort} bytes.");
        Assert.Equal(count - 1, indices[0]); Assert.Equal(0, indices[^1]);
        _output.WriteLine($"CPU only, repeated synthetic source: {count:N0} GS, degree={degree}, perspective={perspective}, antialiased={antialiased}; prepare={preparationMs:F1}ms/{preparationBytes:N0} bytes, project={projectMs:F1}ms/0 bytes, sort={sortMs:F1}ms/0 bytes. Excludes native resources and draw time.");
    }

    private static GaussianProjectedValue Value(double depth, long ordinal) => new(0, 0, depth, 1, 0, 1, new(.5, .5, .5), .5, 3, 0, 0, 3, 3, 0, 0, ordinal);
    private static void Equivalent(ProjectedGaussian expected, GaussianProjectedValue actual)
    {
        double[] a = { expected.CenterX, expected.CenterY, expected.Depth, expected.CovXX, expected.CovXY, expected.CovYY, expected.Color.R, expected.Color.G, expected.Color.B,
            expected.Opacity, expected.Axis1X, expected.Axis1Y, expected.Axis2X, expected.Axis2Y, expected.SupportSigma };
        double[] b = { actual.CenterX, actual.CenterY, actual.Depth, actual.CovXX, actual.CovXY, actual.CovYY, actual.Color.R, actual.Color.G, actual.Color.B,
            actual.Opacity, actual.Axis1X, actual.Axis1Y, actual.Axis2X, actual.Axis2Y, actual.SupportSigma };
        for (int i = 0; i < a.Length; ++i)
        {
            double tolerance = 1e-10 * Math.Max(1, Math.Abs(a[i])); Assert.InRange(b[i], a[i] - tolerance, a[i] + tolerance);
        }
    }
    private sealed class CancellingSource : IReadOnlyList<GaussianSplat>
    {
        private readonly GaussianSplat _splat; private readonly CancellationTokenSource _cancel;
        public CancellingSource(GaussianSplat splat, CancellationTokenSource cancel) { _splat = splat; _cancel = cancel; }
        public int Reads { get; private set; }
        public int Count => 1000;
        public GaussianSplat this[int index] { get { Reads++; if (index == 300) _cancel.Cancel(); return _splat; } }
        public IEnumerator<GaussianSplat> GetEnumerator() => throw new NotSupportedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
