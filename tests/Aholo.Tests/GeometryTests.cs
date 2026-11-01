using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Aholo.Core;

namespace Aholo.Tests;

public sealed class GeometryTests
{
    [Fact]
    public void AsciiSkipsUnknownElementsAndListsAndUsesNamedProperties()
    {
        using var file = new TempFile("""
ply
format ascii 1.0
comment ignored
element metadata 1
property list uchar short ignored
element vertex 2
property uchar blue
property double z
property list uint float unused
property float x
property float y
property uchar red
property uchar green
element face 1
property list uchar int vertex_indices
end_header
2 -100 50
30 3.25 2 9.5 -2.5 1.25 2.5 10 20
255 -3 0 -1 -2 0 128
3 0 1 0
""");
        Assert.Equal(new[] { new SplatPoint(1.25, 2.5, 3.25, 10, 20, 30), new SplatPoint(-1, -2, -3, 0, 128, 255) }, PlyReader.Read(file.Path).ToArray());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BinaryTypedValuesRespectByteOrderAndSkipLists(bool little)
    {
        using var file = new TempFile();
        using (var stream = File.Create(file.Path))
        {
            stream.Write(Encoding.ASCII.GetBytes($"ply\nformat binary_{(little ? "little" : "big")}_endian 1.0\nelement metadata 1\nproperty list ushort int values\nelement vertex 1\nproperty double x\nproperty short y\nproperty float z\nproperty char unused\nproperty uint extra\nproperty uchar red\nproperty uchar green\nproperty uchar blue\nelement face 1\nproperty list uchar int vertex_indices\nend_header\n"));
            Value(stream, 2, "ushort", little); Value(stream, -111, "int", little); Value(stream, 222, "int", little);
            Value(stream, 123.125, "double", little); Value(stream, -1234, "short", little); Value(stream, 9.75, "float", little);
            stream.WriteByte(255); Value(stream, 4_000_000_000, "uint", little);
            stream.Write(new byte[] { 14, 140, 250, 3 });
            Value(stream, 0, "int", little); Value(stream, 0, "int", little); Value(stream, 0, "int", little);
        }
        Assert.Equal(new SplatPoint(123.125, -1234, 9.75, 14, 140, 250), Assert.Single(PlyReader.Read(file.Path)));
    }

    [Fact]
    public void GaussianSphericalHarmonicsBecomeClampedPreviewColors()
    {
        using var file = new TempFile(Header("property float f_dc_0\nproperty float f_dc_1\nproperty float f_dc_2\n") + "0 0 0 0 2 -2\n");
        Assert.Equal(new SplatPoint(0, 0, 0, 128, 255, 0), Assert.Single(PlyReader.Read(file.Path)));
    }

    [Fact]
    public void RgbTakesPrecedenceOverSphericalHarmonics()
    {
        using var file = new TempFile(Header("property uchar red\nproperty uchar green\nproperty uchar blue\nproperty float f_dc_0\nproperty float f_dc_1\nproperty float f_dc_2\n") + "0 0 0 1 2 3 0 0 0\n");
        Assert.Equal(new SplatPoint(0, 0, 0, 1, 2, 3), Assert.Single(PlyReader.Read(file.Path)));
    }

    [Theory]
    [InlineData("0 0")]
    [InlineData("NaN 0 0")]
    [InlineData("Infinity 0 0")]
    [InlineData("0 0 0 unexpected")]
    public void MalformedAsciiFailsRatherThanTruncating(string body)
    {
        using var file = new TempFile(Header() + body);
        Assert.Throws<InvalidDataException>(() => PlyReader.Read(file.Path).ToArray());
    }

    [Fact]
    public void MalformedElementAfterVerticesStillFailsFullEnumeration()
    {
        using var file = new TempFile("ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nproperty float y\nproperty float z\nelement face 1\nproperty list uchar int indices\nend_header\n0 0 0\n3 0 0\n");
        Assert.Throws<InvalidDataException>(() => PlyReader.Read(file.Path).ToArray());
    }

    [Theory]
    [InlineData("property uchar red\n", "0 0 0 5")]
    [InlineData("property uchar red\nproperty uchar green\nproperty uchar blue\n", "0 0 0 256 0 0")]
    [InlineData("property list char int unused\n", "0 0 0 -1")]
    [InlineData("property list float int unused\n", "0 0 0 0")]
    public void InvalidColorAndListTypesAreRejected(string properties, string body)
    {
        using var file = new TempFile(Header(properties) + body);
        Assert.Throws<InvalidDataException>(() => PlyReader.Read(file.Path).ToArray());
    }

    [Fact]
    public void TruncatedBinaryIsRejected()
    {
        using var file = new TempFile("ply\nformat binary_little_endian 1.0\nelement vertex 1\nproperty double x\nproperty double y\nproperty double z\nend_header\n");
        using (var stream = new FileStream(file.Path, FileMode.Append)) stream.Write(new byte[23]);
        Assert.Throws<InvalidDataException>(() => PlyReader.Read(file.Path).ToArray());
    }

    [Fact]
    public void MoreThanOneMillionPointsStreamWithoutPreviewLimit()
    {
        const int count = 1_000_001;
        using var file = new TempFile();
        using (var writer = new StreamWriter(file.Path, false, new UTF8Encoding(false)))
        {
            writer.Write(Header().Replace("vertex 1\n", $"vertex {count}\n"));
            for (var i = 0; i < count; i++) writer.WriteLine("0 0 0");
        }
        Assert.Equal(count, PlyReader.Read(file.Path).Count());
    }

    [Fact]
    public void EmptyRecordWithHugeCountIsRejectedWithoutIteration()
    {
        using var file = new TempFile("ply\nformat ascii 1.0\nelement unused 9223372036854775807\nelement vertex 0\nproperty float x\nproperty float y\nproperty float z\nend_header\n");
        Assert.Throws<InvalidDataException>(() => PlyReader.Read(file.Path).ToArray());
    }

    [Fact]
    public void CancelledPlyReadStopsBeforeYielding()
    {
        using var file = new TempFile(Header() + "0 0 0");
        Assert.Throws<OperationCanceledException>(() => PlyReader.Read(file.Path, new CancellationToken(true)).ToArray());
    }

    [Fact]
    public void GlbStoresMetersYUpAndEachMeshWithItsOwnMaterial()
    {
        using var file = new TempFile();
        GlbWriter.Write(file.Path, new[]
        {
            new MeshData(new float[] { 1000, 2000, 3000, 0, 0, 0, -1000, -2000, -3000 }, new[] { 0, 1, 2 }, 255, 0, 0),
            new MeshData(new float[] { 0, 0, 0, 1000, 0, 0, 0, 1000, 0 }, new[] { 0, 1, 2 }, 0, 128, 255)
        });
        using var reader = new BinaryReader(File.OpenRead(file.Path));
        Assert.Equal(0x46546C67u, reader.ReadUInt32()); Assert.Equal(2u, reader.ReadUInt32());
        Assert.Equal(reader.BaseStream.Length, reader.ReadUInt32());
        var jsonLength = reader.ReadInt32(); Assert.Equal(0, jsonLength % 4); Assert.Equal(0x4E4F534Au, reader.ReadUInt32());
        using var json = JsonDocument.Parse(reader.ReadBytes(jsonLength));
        var root = json.RootElement;
        Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());
        Assert.Equal(2, root.GetProperty("meshes").GetArrayLength()); Assert.Equal(2, root.GetProperty("nodes").GetArrayLength());
        Assert.Equal(2, root.GetProperty("materials").GetArrayLength());
        Assert.Equal(1, root.GetProperty("meshes")[1].GetProperty("primitives")[0].GetProperty("material").GetInt32());
        Assert.Equal(new float[] { -1, -3, -2 }, root.GetProperty("accessors")[0].GetProperty("min").EnumerateArray().Select(e => e.GetSingle()));
        Assert.Equal(new float[] { 1, 3, 2 }, root.GetProperty("accessors")[0].GetProperty("max").EnumerateArray().Select(e => e.GetSingle()));
        var green = root.GetProperty("materials")[1].GetProperty("pbrMetallicRoughness").GetProperty("baseColorFactor")[1].GetDouble();
        Assert.InRange(green, 0.2158, 0.2159);
        var binaryLength = reader.ReadUInt32(); Assert.Equal(0x004E4942u, reader.ReadUInt32());
        Assert.Equal(root.GetProperty("buffers")[0].GetProperty("byteLength").GetInt64(), binaryLength);
        var binaryStart = reader.BaseStream.Position;
        Assert.Equal(1f, reader.ReadSingle()); Assert.Equal(3f, reader.ReadSingle()); Assert.Equal(-2f, reader.ReadSingle());
        reader.BaseStream.Position = binaryStart + root.GetProperty("bufferViews")[1].GetProperty("byteOffset").GetInt64();
        Assert.Equal(0u, reader.ReadUInt32()); Assert.Equal(1u, reader.ReadUInt32()); Assert.Equal(2u, reader.ReadUInt32());
        Assert.Equal(binaryStart + binaryLength, reader.BaseStream.Length);
    }

    [Fact]
    public void InvalidGlbGeometryDoesNotOverwriteExistingFile()
    {
        using var file = new TempFile("keep");
        var badIndex = new MeshData(new float[9], new[] { 0, 1, 3 }, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => GlbWriter.Write(file.Path, new[] { badIndex }));
        Assert.Equal("keep", File.ReadAllText(file.Path));
        var nan = new MeshData(new float[] { float.NaN, 0, 0, 0, 0, 0, 0, 0, 0 }, new[] { 0, 1, 2 }, 0, 0, 0);
        Assert.Throws<ArgumentException>(() => GlbWriter.Write(file.Path, new[] { nan }));
        Assert.Equal("keep", File.ReadAllText(file.Path));
    }

    [Fact]
    public void CancelledGlbWriteDoesNotOverwriteExistingFile()
    {
        using var file = new TempFile("keep");
        var mesh = new MeshData(new float[9], new[] { 0, 1, 2 }, 0, 0, 0);
        Assert.Throws<OperationCanceledException>(() => GlbWriter.Write(file.Path, new[] { mesh }, new CancellationToken(true)));
        Assert.Equal("keep", File.ReadAllText(file.Path));
    }

    [Fact]
    public void UsdaUsesPayloadReferenceCanonicalTransformAndInvariantNumbers()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var camera = Camera();
            var text = UsdaSceneBuilder.Build("ABC_123-xyz", camera, 1280, 720, 1.25);
            Assert.StartsWith("#usda 1.0", text);
            Assert.Contains("payload = @manycore:/mesh/ABC_123-xyz@</mesh>", text);
            Assert.Contains("((1000, 0, 0, 0), (0, 0, 1000, 0), (0, -1000, 0, 0), (0, 0, 0, 1))", text);
            Assert.Contains("(1250.5, -3500.25, 2000, 1)", text);
            Assert.Contains("float2 clippingRange = (0.1, 100000)", text);
            Assert.Contains("int2 resolution = (1280, 720)", text);
            Assert.Contains("giIntensity = (1.25, 1.25, 1.25)", text);
            Assert.DoesNotContain("metersPerUnit", text);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("a@</mesh>\ndef Mesh hacked")]
    [InlineData("../../abc")]
    [InlineData("")]
    public void UsdaRejectsPayloadInjection(string id) => Assert.Throws<ArgumentException>(() => UsdaSceneBuilder.Build(id, Camera(), 1280, 720));

    [Theory]
    [InlineData(0, 720)]
    [InlineData(1280, -1)]
    [InlineData(4501, 720)]
    public void UsdaHonorsDocumentedResolutionRange(int width, int height) => Assert.Throws<ArgumentOutOfRangeException>(() => UsdaSceneBuilder.Build("abc", Camera(), width, height));

    [Fact]
    public void UsdaRejectsInvalidOpticsIntensityAndPose()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UsdaSceneBuilder.Build("abc", Camera() with { FocalLength = double.NaN }, 1280, 720));
        Assert.Throws<ArgumentOutOfRangeException>(() => UsdaSceneBuilder.Build("abc", Camera(), 1280, 720, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => UsdaSceneBuilder.Build("abc", Camera(), 1280, 720, double.MaxValue));
        Assert.Throws<ArgumentException>(() => UsdaSceneBuilder.Build("abc", Camera() with { Far = 0.05 }, 1280, 720));
        Assert.Throws<ArgumentException>(() => UsdaSceneBuilder.Build("abc", Camera() with { Transform = new double[15] }, 1280, 720));
        var pose = Camera().Transform;
        pose[3] = 1;
        Assert.Throws<ArgumentException>(() => UsdaSceneBuilder.Build("abc", Camera() with { Transform = pose }, 1280, 720));
    }

    private static RenderCamera Camera() => new(new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 1250.5, -3500.25, 2000, 1 }, 35, 36, 20.25, 0.1, 100000);
    private static string Header(string extra = "") => "ply\nformat ascii 1.0\nelement vertex 1\nproperty float x\nproperty float y\nproperty float z\n" + extra + "end_header\n";
    private static void Value(Stream stream, double value, string type, bool little)
    {
        Span<byte> bytes = stackalloc byte[8];
        var size = type is "ushort" or "short" ? 2 : type == "double" ? 8 : 4;
        switch (type)
        {
            case "ushort": if (little) BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value); else BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value); break;
            case "short": if (little) BinaryPrimitives.WriteInt16LittleEndian(bytes, (short)value); else BinaryPrimitives.WriteInt16BigEndian(bytes, (short)value); break;
            case "int": if (little) BinaryPrimitives.WriteInt32LittleEndian(bytes, (int)value); else BinaryPrimitives.WriteInt32BigEndian(bytes, (int)value); break;
            case "uint": if (little) BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value); else BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value); break;
            case "float": if (little) BinaryPrimitives.WriteSingleLittleEndian(bytes, (float)value); else BinaryPrimitives.WriteSingleBigEndian(bytes, (float)value); break;
            case "double": if (little) BinaryPrimitives.WriteDoubleLittleEndian(bytes, value); else BinaryPrimitives.WriteDoubleBigEndian(bytes, value); break;
        }
        stream.Write(bytes[..size]);
    }
    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aholo-geometry-" + Guid.NewGuid().ToString("N"));
        public TempFile(string? contents = null)
        {
            if (contents is not null) File.WriteAllText(Path, contents, new UTF8Encoding(false));
        }
        public void Dispose() { if (File.Exists(Path)) File.Delete(Path); }
    }
}
