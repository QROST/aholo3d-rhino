using System.Text.Json;

namespace Aholo.Core;

/// <summary>Static tessellated mesh in Z-up millimetres, rebased around a double-precision origin.</summary>
public sealed record UsdMeshPart(string Name, MeshData Mesh, GaussianVector Origin);
public sealed record UsdMeshExportResult(string Path, string? AssetDirectory, int Meshes, long Triangles);

/// <summary>Local mesh USD exchange. No GLB intermediate or artificial geometry quota.</summary>
public static class UsdMeshWriter
{
    public static UsdMeshExportResult Write(string path, IReadOnlyList<UsdMeshPart> parts, CancellationToken ct = default)
    {
        var result = UsdExportBundle.Write(path, (workspace, assets, format, token) =>
        {
            WriteBridgeInput(workspace, parts, assets, token);
            UsdBridgeProcess.Run(Path.Combine(workspace, "mesh-export.json"), workspace, token, format, mesh: true);
        }, ct);
        return new(result.Path, result.AssetDirectory, parts.Count, parts.Sum(p => (long)p.Mesh.Indices.Length / 3));
    }

    /// <summary>Write validated parts into a caller-owned empty workspace. The caller must remove it on failure.</summary>
    public static void WriteBridgeInput(string directory, IReadOnlyList<UsdMeshPart> parts, string assetDirectory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0) throw new ArgumentException("At least one mesh is required.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(assetDirectory, @"\Ausd-assets-[a-f0-9]{32}\z")) throw new ArgumentException("Invalid USD asset directory.");
        var records = new List<object>(parts.Count); var images = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            ct.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(part); ArgumentNullException.ThrowIfNull(part.Mesh);
            var mesh = part.Mesh; ArgumentNullException.ThrowIfNull(part.Name);
            if (!part.Origin.IsFinite || mesh.Positions is null || mesh.Positions.Length == 0 || mesh.Positions.Length % 3 != 0 || mesh.Indices is null || mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0)
                throw new ArgumentException("USD mesh positions and triangle indices must be non-empty triples with a finite origin.");
            int vertices = mesh.Positions.Length / 3;
            if (mesh.Normals is { } n && n.Length != mesh.Positions.Length || mesh.TexCoords is { } uv && uv.LongLength != (long)vertices * 2)
                throw new ArgumentException("USD normals and UVs must match the vertex count.");
            double[] color = mesh.Material?.LinearBaseColor() ?? new[] { Linear(mesh.R), Linear(mesh.G), Linear(mesh.B), 1d };
            string file = $"mesh-{records.Count:D6}.bin";
            using (var writer = new BinaryWriter(new FileStream(Path.Combine(directory, file), FileMode.CreateNew, FileAccess.Write, FileShare.None)))
            {
                for (int i = 0; i < mesh.Positions.Length; i++)
                {
                    if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                    if (!float.IsFinite(mesh.Positions[i])) throw new ArgumentException("USD mesh positions must be finite.");
                    writer.Write(mesh.Positions[i]);
                }
                for (int i = 0; i < mesh.Indices.Length; i++)
                {
                    if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                    if (mesh.Indices[i] < 0 || mesh.Indices[i] >= vertices) throw new ArgumentException("USD mesh index is outside its vertex array.");
                    writer.Write(mesh.Indices[i]);
                }
                if (mesh.Normals is { } normals) for (int i = 0; i < normals.Length; i += 3)
                {
                    if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                    var normal = new GaussianVector(normals[i], normals[i + 1], normals[i + 2]).Normalized();
                    writer.Write((float)normal.X); writer.Write((float)normal.Y); writer.Write((float)normal.Z);
                }
                if (mesh.TexCoords is { } coords) for (int i = 0; i < coords.Length; i++)
                {
                    if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                    if (!float.IsFinite(coords[i])) throw new ArgumentException("USD texture coordinates must be finite.");
                    writer.Write(coords[i]);
                }
            }
            object? texture = null;
            if (mesh.BaseColorTexture is { } image)
            {
                if (mesh.TexCoords is null) throw new ArgumentException("USD base-color images require texture coordinates.");
                string imageName = image.Sha256.ToLowerInvariant() + (image.MimeType == "image/png" ? ".png" : ".jpg");
                string imagePath = assetDirectory + "/" + imageName;
                if (images.Add(imageName))
                {
                    Directory.CreateDirectory(Path.Combine(directory, assetDirectory));
                    using var output = new FileStream(Path.Combine(directory, assetDirectory, imageName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    for (int i = 0; i < image.Length;)
                    { ct.ThrowIfCancellationRequested(); int length = Math.Min(131072, image.Length - i); output.Write(image.Bytes.Slice(i, length)); i += length; }
                }
                texture = new { file = imagePath, useAlpha = image.HasAlpha && image.UseAlpha, wrapS = (int)image.WrapU, wrapT = (int)image.WrapV, minFilter = (int)image.MinFilter, magFilter = (int)image.MagFilter };
            }
            records.Add(new { name = part.Name, file, vertices, indices = mesh.Indices.Length, normals = mesh.Normals is not null, uv = mesh.TexCoords is not null,
                origin = new[] { part.Origin.X, part.Origin.Y, part.Origin.Z }, color, metallic = mesh.Material?.Metallic ?? 0, roughness = mesh.Material?.Roughness ?? .75, texture });
        }
        ct.ThrowIfCancellationRequested();
        File.WriteAllText(Path.Combine(directory, "mesh-export.json"), JsonSerializer.Serialize(new { protocol = 1, openUsdVersion = "26.11", assets = assetDirectory, parts = records }));
    }
    private static double Linear(byte value)
    { double s = value / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
}
