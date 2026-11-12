namespace Aholo3D.Core;

/// <summary>One 3DGS center and its view-independent color. Scale, rotation and opacity remain in the original PLY.</summary>
public sealed record SplatPoint(double X, double Y, double Z, byte R, byte G, byte B);

/// <summary>Triangle mesh in Rhino Z-up millimetres. RGB is the object's sRGB display color.</summary>
public sealed record MeshData(float[] Positions, int[] Indices, byte R, byte G, byte B)
{
    public MeshMaterial? Material { get; init; }
    /// <summary>Optional unit directions in Rhino Z-up coordinates, one per vertex.</summary>
    public float[]? Normals { get; init; }
    /// <summary>Optional UV pairs, with origin at the image upper-left as required by glTF.</summary>
    public float[]? TexCoords { get; init; }
    public MeshTexture? BaseColorTexture { get; init; }
}
