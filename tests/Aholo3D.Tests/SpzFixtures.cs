using System.Buffers.Binary;
using System.IO.Compression;
using ZstdSharp;

namespace Aholo3D.Tests;

// Independent, literal wire-format fixtures. No production decoder or packing helpers are used.
internal static class SpzFixtures
{
    internal static byte[] File(int version = 4, int degree = 0, int count = 1, byte flags = 0, byte[]? extensions = null)
    {
        byte[][] attributes = Attributes(version, degree, count);
        return Container(version, degree, count, flags, extensions ?? Array.Empty<byte>(), attributes);
    }
    internal static byte[][] Attributes(int version, int degree, int count)
    {
        int[] strides = { version == 1 ? 6 : 9, 1, 3, 3, version < 3 ? 3 : 4, 3 * ((degree + 1) * (degree + 1) - 1) };
        byte[][] data = strides.Select(stride => new byte[stride * count]).ToArray();
        for (int i = 0; i < count; ++i)
        {
            if (version == 1)
            {
                for (int c = 0; c < 3; ++c) BinaryPrimitives.WriteUInt16LittleEndian(data[0].AsSpan(i * 6 + c * 2), BitConverter.HalfToUInt16Bits((Half)(new[] { 1.25, -2.5, 3.75 }[c])));
            }
            else
            {
                // Exact signed 24-bit coordinates for 1.25, -2.5, 3.75 with 12 fractional bits.
                new byte[] { 0, 20, 0, 0, 216, 255, 0, 60, 0 }.CopyTo(data[0], i * 9);
            }
            data[1][i] = (i % 3) switch { 0 => (byte)0, 1 => (byte)128, _ => (byte)255 };
            new byte[] { 0, 128, 255 }.CopyTo(data[2], i * 3);
            new byte[] { 128, 160, 192 }.CopyTo(data[3], i * 3);
            (version < 3 ? new byte[] { 128, 128, 128 } : new byte[] { 0, 0, 0, 192 }).CopyTo(data[4], i * strides[4]);
            for (int j = 0; j < strides[5]; ++j) data[5][i * strides[5] + j] = (byte)((j * 7 + i) & 255);
        }
        return data;
    }
    internal static byte[] Container(int version, int degree, int count, byte flags, byte[] extensions, byte[][] attributes)
    {
        byte[] header = new byte[version == 4 ? 32 : 16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x5053474E);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)version);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), (uint)count);
        header[12] = (byte)degree; header[13] = 12; header[14] = flags;
        using var output = new MemoryStream();
        if (version < 4)
        {
            output.Write(header);
            foreach (byte[] attribute in attributes) output.Write(attribute);
            output.Write(extensions);
            return Gzip(output.ToArray());
        }
        using var compressor = new Compressor(3);
        byte[][] streams = attributes.Where(a => a.Length > 0).Select(a => compressor.Wrap(a).ToArray()).ToArray();
        header[15] = (byte)streams.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), (uint)(32 + extensions.Length));
        output.Write(header); output.Write(extensions);
        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        int index = 0;
        foreach (byte[] attribute in attributes.Where(a => a.Length > 0)) { writer.Write((ulong)streams[index++].Length); writer.Write((ulong)attribute.Length); }
        foreach (byte[] stream in streams) output.Write(stream);
        return output.ToArray();
    }
    internal static byte[] Gzip(byte[] payload)
    {
        using var output = new MemoryStream();
        using (var zip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true)) zip.Write(payload);
        return output.ToArray();
    }
    internal static byte[] Gunzip(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var zip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(); zip.CopyTo(output); return output.ToArray();
    }
    internal static byte[] Extension(uint type, byte[] payload)
    {
        var result = new byte[payload.Length + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(result, type);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(result, 8); return result;
    }
    internal static byte[] Coordinate(uint value)
    {
        var payload = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(payload, value); return Extension(0xADBE0003, payload);
    }
}

internal sealed class SpzTestFile : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aholo3d-format-test-" + Guid.NewGuid().ToString("N"));
    internal SpzTestFile(byte[] bytes) => System.IO.File.WriteAllBytes(Path, bytes);
    public void Dispose() => System.IO.File.Delete(Path);
}
