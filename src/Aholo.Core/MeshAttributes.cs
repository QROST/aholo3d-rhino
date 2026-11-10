namespace Aholo.Core;

/// <summary>Attribute transforms are independent of position units and face winding.</summary>
public static class MeshAttributes
{
    public static float[] TransformNormals(float[] source, GaussianMatrix linear)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Length == 0 || source.Length % 3 != 0) throw new ArgumentException("Normals must be nonempty triples.");
        var normalMatrix = linear.Inverse().Transpose();
        var result = new float[source.Length];
        for (int i = 0; i < source.Length; i += 3)
        {
            var n = normalMatrix.Transform(new(source[i], source[i + 1], source[i + 2])).Normalized();
            result[i] = (float)n.X; result[i + 1] = (float)n.Y; result[i + 2] = (float)n.Z;
        }
        return result;
    }
}
