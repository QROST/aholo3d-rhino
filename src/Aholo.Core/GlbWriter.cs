using System.Text;
using System.Text.Json;

namespace Aholo.Core;

/// <summary>Writes untextured triangle meshes as glTF 2.0, in metres with Y up.</summary>
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
            gltfMeshes.Add(new { primitives = new[] { new { attributes = new { POSITION = positionAccessor }, indices = indexAccessor, material = m, mode = 4 } } });
            if(mesh.Material is{} material)
                materials.Add(new { pbrMetallicRoughness = new { baseColorFactor = material.LinearBaseColor(), metallicFactor = material.Metallic, roughnessFactor = material.Roughness }, alphaMode = material.Alpha < 1 ? "BLEND" : "OPAQUE", doubleSided = true });
            else
                materials.Add(new { pbrMetallicRoughness = new { baseColorFactor = new[] { Linear(mesh.R), Linear(mesh.G), Linear(mesh.B), 1.0 }, metallicFactor = 0.0, roughnessFactor = 0.75 }, doubleSided = true });
        }
        var json = JsonSerializer.SerializeToUtf8Bytes(new
        {
            asset = new { version = "2.0", generator = "Aholo for Rhino / CuriousArc" },
            scene = 0,
            scenes = new[] { new { nodes = Enumerable.Range(0, meshes.Count).ToArray() } },
            nodes = Enumerable.Range(0, meshes.Count).Select(m => new { mesh = m }).ToArray(),
            meshes = gltfMeshes,
            materials,
            buffers = new[] { new { byteLength = binaryLength } },
            bufferViews = views,
            accessors
        });
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
                foreach (var mesh in meshes)
                {
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
