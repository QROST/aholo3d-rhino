using System.Buffers.Binary;

namespace Aholo.Core;

/// <summary>Reads the headerless 32-byte antimatter15 .splat convention, without sampling.</summary>
public static class SplatReader
{
    // Format reference: https://github.com/antimatter15/splat/blob/main/convert.py
    private const double Sh0 = 0.28209479177387814;
    public static GaussianScene Read(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length == 0 || input.Length % 32 != 0)
            throw new InvalidDataException("SPLAT must contain complete 32-byte Gaussian records.");
        long count = input.Length / 32;
        var parts = new List<GaussianPart>();
        var buffer = new byte[SplatReadSupport.ChunkSize * 32];
        for (long first = 0; first < count;)
        {
            ct.ThrowIfCancellationRequested();
            int length = (int)Math.Min(SplatReadSupport.ChunkSize, count - first);
            SplatReadSupport.ReadExactly(input, buffer.AsSpan(0, length * 32), ct);
            var splats = new GaussianSplat[length];
            for (int i = 0; i < length; ++i)
            {
                if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> record = buffer.AsSpan(i * 32, 32);
                double scaleX = SplatReadSupport.Float(record[12..]), scaleY = SplatReadSupport.Float(record[16..]), scaleZ = SplatReadSupport.Float(record[20..]);
                if (!(scaleX > 0) || !(scaleY > 0) || !(scaleZ > 0))
                    throw new InvalidDataException("SPLAT scales must be positive and finite.");
                try
                {
                    // SPLAT stores WXYZ, unlike SPZ's XYZW encoding.
                    var rotation = new GaussianQuaternion((record[28] - 128) / 128.0, (record[29] - 128) / 128.0,
                        (record[30] - 128) / 128.0, (record[31] - 128) / 128.0).Normalized();
                    splats[i] = new GaussianSplat(new(SplatReadSupport.Float(record), SplatReadSupport.Float(record[4..]), SplatReadSupport.Float(record[8..])),
                        new(Math.Log(scaleX), Math.Log(scaleY), Math.Log(scaleZ)), rotation, SplatReadSupport.Logit(record[27]), 0,
                        new[] { (float)((record[24] / 255.0 - .5) / Sh0), (float)((record[25] / 255.0 - .5) / Sh0), (float)((record[26] / 255.0 - .5) / Sh0) });
                }
                catch (ArgumentException error) { throw new InvalidDataException("Invalid SPLAT Gaussian parameters.", error); }
            }
            parts.Add(new GaussianPart(SplatReadSupport.PartName(path, parts.Count), splats, GaussianTransform.Identity));
            first += length;
        }
        if (input.ReadByte() != -1) throw new InvalidDataException("SPLAT changed while being read.");
        ct.ThrowIfCancellationRequested();
        return new GaussianScene(parts, Format: "splat");
    }
}

internal static class SplatReadSupport
{
    // Storage block size, not a limit on the total number of Gaussians.
    internal const int ChunkSize = 16384;
    internal static string PartName(string path, int index) => $"{Path.GetFileNameWithoutExtension(path)} [{index + 1}]";
    internal static float Float(ReadOnlySpan<byte> bytes) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
    internal static double Logit(byte alpha) => alpha switch
    {
        0 => -double.MaxValue,
        255 => double.MaxValue,
        _ => Math.Log(alpha / (255.0 - alpha))
    };
    internal static void ReadExactly(Stream input, Span<byte> destination, CancellationToken ct)
    {
        while (!destination.IsEmpty)
        {
            ct.ThrowIfCancellationRequested();
            int read = input.Read(destination[..Math.Min(destination.Length, 65536)]);
            if (read == 0) throw new InvalidDataException("Truncated Gaussian data.");
            destination = destination[read..];
        }
    }
    internal static void CopyExactly(Stream input, Stream? output, long count, CancellationToken ct)
    {
        byte[] buffer = new byte[65536];
        while (count > 0)
        {
            int length = (int)Math.Min(count, buffer.Length);
            ReadExactly(input, buffer.AsSpan(0, length), ct);
            output?.Write(buffer, 0, length);
            count -= length;
        }
    }
}
