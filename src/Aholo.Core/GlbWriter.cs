using System.Text;
using System.Text.Json;

namespace Aholo.Core;

/// <summary>Writes triangle meshes with optional smooth normals and UVs as glTF 2.0, in metres with Y up.</summary>
public static class GlbWriter
{
    public static void Write(string path, IReadOnlyList<MeshData> meshes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        if (meshes.Count == 0) throw new ArgumentException("At least one mesh is required.", nameof(meshes));
        var views = new List<object>();
        var accessors = new List<object>();
        var gltfMeshes = new List<object>();
        var materials = new List<object>();
        var images = new List<object>();
        var imageIndices = new Dictionary<string, int>(StringComparer.Ordinal);
        var textures = new List<object>();
        var samplers = new List<object>();
        // Prepared pixel payloads are fixed before either layout or writing; sampler settings stay per mesh.
        var preparedImages = new MeshTexture?[meshes.Count];
        var opaqueImages = new Dictionary<string,MeshTexture>(StringComparer.Ordinal);
        long binaryLength = 0;
        for (var m = 0; m < meshes.Count; m++)
        {
            ct.ThrowIfCancellationRequested();
            var mesh = meshes[m] ?? throw new ArgumentException("Mesh cannot be null.", nameof(meshes));
            if (mesh.Positions is null || mesh.Positions.Length == 0 || mesh.Positions.Length % 3 != 0 || mesh.Indices is null || mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0)
                throw new ArgumentException("Mesh positions and triangle indices must be non-empty triples.", nameof(meshes));
            var count = mesh.Positions.Length / 3;
            var min = new[] { float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity };
            var max = new[] { float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity };
            for (var v = 0; v < mesh.Positions.Length; v += 3)
            {
                if ((v & 4095) == 0) ct.ThrowIfCancellationRequested();
                for (var a = 0; a < 3; a++)
                {
                    if (!float.IsFinite(mesh.Positions[v + a])) throw new ArgumentException("Mesh positions must be finite.", nameof(meshes));
                    var converted = Coordinate(mesh.Positions, v, a);
                    min[a] = Math.Min(min[a], converted);
                    max[a] = Math.Max(max[a], converted);
                }
            }
            for (var i = 0; i < mesh.Indices.Length; i++)
            {
                if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                if (mesh.Indices[i] < 0 || mesh.Indices[i] >= count) throw new ArgumentException("Triangle index is outside its vertex array.", nameof(meshes));
            }
            var positionBytes = checked((long)mesh.Positions.Length * sizeof(float));
            var indexBytes = checked((long)mesh.Indices.Length * sizeof(uint));
            var positionView = views.Count;
            views.Add(new { buffer = 0, byteOffset = binaryLength, byteLength = positionBytes, target = 34962 });
            binaryLength = checked(binaryLength + positionBytes);
            var indexView = views.Count;
            views.Add(new { buffer = 0, byteOffset = binaryLength, byteLength = indexBytes, target = 34963 });
            binaryLength = checked(binaryLength + indexBytes);
            if (binaryLength > uint.MaxValue) throw new InvalidDataException("Geometry exceeds the GLB format's 32-bit file size field.");
            var positionAccessor = accessors.Count;
            accessors.Add(new { bufferView = positionView, byteOffset = 0, componentType = 5126, count, type = "VEC3", min, max });
            var indexAccessor = accessors.Count;
            accessors.Add(new { bufferView = indexView, byteOffset = 0, componentType = 5125, count = mesh.Indices.Length, type = "SCALAR" });
            var attributes = new Dictionary<string, int> { ["POSITION"] = positionAccessor };
            if (mesh.Normals is { } normals)
            {
                if (normals.Length != mesh.Positions.Length) throw new ArgumentException("Normals must match the vertex count.");
                for (int v = 0; v < normals.Length; v += 3)
                {
                    if ((v & 4095) == 0) ct.ThrowIfCancellationRequested();
                    _ = new GaussianVector(normals[v], normals[v + 1], normals[v + 2]).Normalized();
                }
                attributes["NORMAL"] = accessors.Count;
                accessors.Add(new { bufferView = views.Count, byteOffset = 0, componentType = 5126, count, type = "VEC3" });
                long bytes = checked((long)normals.Length * sizeof(float));
                views.Add(new { buffer = 0, byteOffset = binaryLength, byteLength = bytes, target = 34962 });
                binaryLength = checked(binaryLength + bytes);
            }
            if (mesh.TexCoords is { } uv)
            {
                if (uv.LongLength != (long)count * 2 || uv.Any(v => !float.IsFinite(v))) throw new ArgumentException("Texture coordinates must contain two finite values per vertex.");
                attributes["TEXCOORD_0"] = accessors.Count;
                accessors.Add(new { bufferView = views.Count, byteOffset = 0, componentType = 5126, count, type = "VEC2" });
                long bytes = checked((long)uv.Length * sizeof(float));
                views.Add(new { buffer = 0, byteOffset = binaryLength, byteLength = bytes, target = 34962 });
                binaryLength = checked(binaryLength + bytes);
            }
            gltfMeshes.Add(new { primitives = new[] { new { attributes, indices = indexAccessor, material = m, mode = 4 } } });
            var pbr = new Dictionary<string, object>();
            if (mesh.Material is { } material)
            {
                pbr["baseColorFactor"] = material.LinearBaseColor();
                pbr["metallicFactor"] = material.Metallic; pbr["roughnessFactor"] = material.Roughness;
            }
            else
            {
                pbr["baseColorFactor"] = new[] { Linear(mesh.R), Linear(mesh.G), Linear(mesh.B), 1.0 };
                pbr["metallicFactor"] = 0.0; pbr["roughnessFactor"] = 0.75;
            }
            var gltfMaterial = new Dictionary<string, object> { ["pbrMetallicRoughness"] = pbr, ["doubleSided"] = true };
            if (mesh.Material is not null || mesh.BaseColorTexture is not null)
                gltfMaterial["alphaMode"] = mesh.Material?.Alpha < 1 || mesh.BaseColorTexture is { HasAlpha: true, UseAlpha: true } ? "BLEND" : "OPAQUE";
            if (mesh.BaseColorTexture is { } texture)
            {
                if (mesh.TexCoords is null) throw new ArgumentException("A base-color texture requires UV coordinates.");
                var image=texture;
                if (texture.HasAlpha && !texture.UseAlpha && mesh.Material?.Alpha < 1)
                {
                    if(!opaqueImages.TryGetValue(texture.Sha256,out image))
                        opaqueImages.Add(texture.Sha256,image=texture.WithoutAlpha(ct));
                }
                preparedImages[m]=image;
                int samplerIndex = samplers.Count, textureIndex = textures.Count;
                if (!imageIndices.TryGetValue(image.Sha256, out int imageIndex))
                {
                    imageIndex = images.Count; imageIndices.Add(image.Sha256, imageIndex);
                    images.Add(new { bufferView = views.Count, mimeType = image.MimeType });
                    views.Add(new { buffer = 0, byteOffset = binaryLength, byteLength = image.Length });
                    binaryLength = checked(binaryLength + ((image.Length + 3L) & ~3L));
                }
                samplers.Add(new { wrapS = (int)texture.WrapU, wrapT = (int)texture.WrapV, minFilter = (int)texture.MinFilter, magFilter = (int)texture.MagFilter });
                textures.Add(new { sampler = samplerIndex, source = imageIndex });
                pbr["baseColorTexture"] = new { index = textureIndex, texCoord = 0 };
            }
            materials.Add(gltfMaterial);
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            asset = new { version = "2.0", generator = "Aholo for Rhino / CuriousArc" },
            scene = 0,
            scenes = new[] { new { nodes = Enumerable.Range(0, meshes.Count).ToArray() } },
            nodes = Enumerable.Range(0, meshes.Count).Select(m => new { mesh = m }).ToArray(),
            meshes = gltfMeshes,
            materials,
            images = images.Count == 0 ? null : images,
            textures = textures.Count == 0 ? null : textures,
            samplers = samplers.Count == 0 ? null : samplers,
            buffers = new[] { new { byteLength = binaryLength } },
            bufferViews = views,
            accessors
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        var jsonLength = checked(((long)json.Length + 3) & ~3L);
        var totalLength = checked(12L + 8 + jsonLength + 8 + binaryLength);
        if (totalLength > uint.MaxValue) throw new InvalidDataException("Geometry exceeds the GLB format's 32-bit file size field.");
        var outputPath = Path.GetFullPath(path);
        var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var writer = new BinaryWriter(new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan), Encoding.UTF8))
            {
                writer.Write(0x46546C67u);
                writer.Write(2u);
                writer.Write((uint)totalLength);
                writer.Write((uint)jsonLength);
                writer.Write(0x4E4F534Au);
                writer.Write(json);
                for (var padding = json.Length; padding < jsonLength; padding++) writer.Write((byte)' ');
                writer.Write((uint)binaryLength);
                writer.Write(0x004E4942u);
                var writtenImages = new HashSet<string>(StringComparer.Ordinal);
                for(int meshIndex=0;meshIndex<meshes.Count;meshIndex++)
                {
                    var mesh=meshes[meshIndex];
                    for (var v = 0; v < mesh.Positions.Length; v += 3)
                    {
                        if ((v & 4095) == 0) ct.ThrowIfCancellationRequested();
                        writer.Write(Coordinate(mesh.Positions, v, 0));
                        writer.Write(Coordinate(mesh.Positions, v, 1));
                        writer.Write(Coordinate(mesh.Positions, v, 2));
                    }
                    for (var i = 0; i < mesh.Indices.Length; i++)
                    {
                        if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                        writer.Write((uint)mesh.Indices[i]);
                    }
                    if (mesh.Normals is { } normals)
                        for (int v = 0; v < normals.Length; v += 3)
                        {
                            if ((v & 4095) == 0) ct.ThrowIfCancellationRequested();
                            var n = new GaussianVector(normals[v], normals[v + 1], normals[v + 2]).Normalized();
                            // (x,z,-y) is a proper rotation (determinant +1), not a reflection.
                            writer.Write((float)n.X); writer.Write((float)n.Z); writer.Write((float)-n.Y);
                        }
                    if (mesh.TexCoords is { } uv)
                        for (int i = 0; i < uv.Length; i++)
                        {
                            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
                            writer.Write(uv[i]);
                        }
                    if (preparedImages[meshIndex] is { } texture && writtenImages.Add(texture.Sha256))
                    {
                        for (int i = 0; i < texture.Length;)
                        {
                            ct.ThrowIfCancellationRequested(); int length = Math.Min(65536, texture.Length - i);
                            writer.Write(texture.Bytes.Slice(i, length)); i += length;
                        }
                        for (int i = 0; i < ((4 - texture.Length % 4) % 4); i++) writer.Write((byte)0);
                    }
                }
                writer.Flush();
                if (writer.BaseStream.Length != totalLength) throw new InvalidDataException("GLB byte length does not match its header.");
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static float Coordinate(float[] values, int start, int axis) => axis switch
    {
        0 => values[start] / 1000f,
        1 => values[start + 2] / 1000f,
        _ => -values[start + 1] / 1000f
    };
    private static double Linear(byte value)
    {
        var srgb = value / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }
}
