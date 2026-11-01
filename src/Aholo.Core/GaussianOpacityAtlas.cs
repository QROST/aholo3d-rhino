namespace Aholo.Core;

public readonly record struct GaussianAtlasUv(double U0, double V0, double U1, double V1);

/// <summary>Fixed-size straight-alpha texture atlas. Opacity has 8-bit display precision;
/// source splats and their floating-point opacity are never modified or discarded.</summary>
public static class GaussianOpacityAtlas
{
    public const int LevelCount = 256;
    public const int TilesPerSide = 16;
    public const int TileSize = 65;
    public const int TextureSize = TilesPerSide * TileSize;

    public static int GetLevel(double opacity)
    {
        if (!double.IsFinite(opacity)) throw new ArgumentOutOfRangeException(nameof(opacity));
        return (int)Math.Round(Math.Clamp(opacity, 0, 1) * (LevelCount - 1));
    }

    public static GaussianAtlasUv GetUvBounds(double opacity)
    {
        int level = GetLevel(opacity), column = level % TilesPerSide, row = level / TilesPerSide;
        return new((column * TileSize + .5) / TextureSize, (row * TileSize + .5) / TextureSize,
            (column * TileSize + TileSize - .5) / TextureSize, (row * TileSize + TileSize - .5) / TextureSize);
    }
}
