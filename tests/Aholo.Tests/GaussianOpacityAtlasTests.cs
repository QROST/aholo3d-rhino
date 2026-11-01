using Aholo.Core;
namespace Aholo.Tests;

public sealed class GaussianOpacityAtlasTests
{
    [Fact]
    public void EveryOpacityMapsToDistinctTexelInsetTile()
    {
        var bounds = Enumerable.Range(0, 256).Select(i => GaussianOpacityAtlas.GetUvBounds(i / 255.0)).ToArray();
        Assert.Equal(256, bounds.Distinct().Count());
        Assert.Equal(.5 / 1040, bounds[0].U0);
        Assert.Equal(.5 / 1040, bounds[0].V0);
        Assert.Equal(1039.5 / 1040, bounds[255].U1);
        Assert.Equal(1039.5 / 1040, bounds[255].V1);
        foreach (var tile in bounds)
        {
            Assert.InRange(tile.U0, 0, 1); Assert.InRange(tile.V0, 0, 1);
            Assert.InRange(tile.U1, 0, 1); Assert.InRange(tile.V1, 0, 1);
            Assert.Equal(64.0 / 1040, tile.U1 - tile.U0, 12);
            Assert.Equal(64.0 / 1040, tile.V1 - tile.V0, 12);
        }
        Assert.True(bounds[0].U1 < bounds[1].U0);
        Assert.True(bounds[0].V1 < bounds[16].V0);
    }

    [Fact]
    public void OpacityPrecisionHasKnownBoundAndRejectsNonfiniteInput()
    {
        for (int i = 0; i <= 10000; i++)
        {
            double opacity = i / 10000.0;
            Assert.InRange(Math.Abs(GaussianOpacityAtlas.GetLevel(opacity) / 255.0 - opacity), 0, .5 / 255 + 1e-15);
        }
        Assert.Equal(0, GaussianOpacityAtlas.GetLevel(-1));
        Assert.Equal(255, GaussianOpacityAtlas.GetLevel(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianOpacityAtlas.GetLevel(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => GaussianOpacityAtlas.GetLevel(double.PositiveInfinity));
    }
}
