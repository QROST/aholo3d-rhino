// SPZ decoding follows https://github.com/nianticlabs/spz (MIT).
// Copyright (c) 2025 Niantic Labs
// Copyright (c) 2025-2026 Adobe Inc.
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
using System.Buffers.Binary;
using System.IO.Compression;
using ZstdSharp;

namespace Aholo.Core;

/// <summary>SPZ 1–4 decoder. Full supported SH is preserved; unsupported degrees fail explicitly.</summary>
public static class SpzReader
{
    private const uint Magic = 0x5053474E;
    private const uint CoordinateExtension = 0xADBE0003;
    private const uint OrbitExtension = 0xADBE0002;
    private sealed record Header(uint Version, uint Count, int Degree, int FractionalBits, byte Flags, long[] Lengths);
    private sealed class Metadata
    {
        internal uint CoordinateSystem = 4; // RUB
        internal bool HasCoordinates;
        internal readonly List<string> Warnings = new();
        internal readonly HashSet<uint> UnknownTypes = new();
    }

    public static GaussianScene Read(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> signature = stackalloc byte[4];
        SplatReadSupport.ReadExactly(input, signature, ct);
        input.Position = 0;
        bool modern = BinaryPrimitives.ReadUInt32LittleEndian(signature) == Magic;
        if (!modern && (signature[0] != 0x1F || signature[1] != 0x8B))
            throw Invalid("Missing NGSP or gzip signature.");
        // Attribute streams are staged on disk, avoiding simultaneous inflated copies in managed memory.
        // DeleteOnClose also cleans up failed or cancelled reads. The original file remains untouched.
        var temporaryOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite,
            Share = FileShare.None, BufferSize = 65536, Options = FileOptions.DeleteOnClose | FileOptions.RandomAccess };
        if (!OperatingSystem.IsWindows()) temporaryOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var packed = new FileStream(Path.Combine(Path.GetTempPath(), $"aholo-spz-{Guid.NewGuid():N}.tmp"), temporaryOptions);
        var metadata = new Metadata();
        try
        {
            Header header = modern ? ReadModern(input, packed, metadata, ct) : ReadLegacy(input, packed, metadata, ct);
            ct.ThrowIfCancellationRequested();
            return Decode(packed, header, metadata, path, ct);
        }
        catch (EndOfStreamException error) { throw Invalid("Truncated compressed SPZ data.", error); }
        catch (ZstdException error) { throw Invalid("Invalid Zstandard SPZ stream.", error); }
        catch (OverflowException error) { throw Invalid("SPZ byte lengths overflow the supported file address space.", error); }
    }

    private static Header ParseHeader(ReadOnlySpan<byte> bytes, bool modern)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic) throw Invalid("Missing SPZ header signature.");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        if (modern ? version != 4 : version is < 1 or > 3)
            throw new NotSupportedException($"SPZ version {version} is not supported in this compression container.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        if (count == 0) throw Invalid("SPZ contains no Gaussians.");
        int degree = bytes[12];
        if (degree > 3) throw new NotSupportedException($"SPZ SH degree {degree} is not supported. This viewer supports degree 0–3; coefficients were not truncated.");
        byte flags = bytes[14];
        if ((flags & ~3) != 0) throw new NotSupportedException("SPZ contains unknown header flags.");
        if (modern ? HasNonzero(bytes[20..32]) : bytes[15] != 0)
            throw Invalid("SPZ reserved header bytes must be zero.");
        long n = count;
        return new(version, count, degree, bytes[13], flags, new[] { n * (version == 1 ? 6 : 9), n, n * 3, n * 3,
            n * (version < 3 ? 3 : 4), n * 3 * ((degree + 1) * (degree + 1) - 1) });
    }

    private static Header ReadModern(FileStream input, FileStream packed, Metadata metadata, CancellationToken ct)
    {
        Span<byte> bytes = stackalloc byte[32];
        SplatReadSupport.ReadExactly(input, bytes, ct);
        Header header = ParseHeader(bytes, true);
        int streams = bytes[15], expectedStreams = header.Degree == 0 ? 5 : 6;
        if (streams != expectedStreams) throw Invalid("SPZ attribute stream count does not match its SH degree.");
        long tocOffset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);
        long tocEnd = checked(tocOffset + streams * 16L);
        if (tocOffset < 32 || tocEnd > input.Length) throw Invalid("SPZ table of contents is outside the file.");
        if ((header.Flags & 2) != 0)
        {
            using var extensions = new SpzBoundedStream(input, tocOffset - 32);
            ReadExtensions(extensions, metadata, ct);
            if (extensions.Remaining != 0) throw Invalid("Truncated SPZ extension block.");
        }
        else if (tocOffset != 32) throw Invalid("SPZ has an undeclared extension block.");
        var compressedLengths = new long[streams];
        long compressedEnd = tocEnd;
        Span<byte> entry = stackalloc byte[16];
        for (int i = 0; i < streams; ++i)
        {
            SplatReadSupport.ReadExactly(input, entry, ct);
            ulong compressed = BinaryPrimitives.ReadUInt64LittleEndian(entry), expanded = BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
            if (compressed == 0 || compressed > (ulong)(input.Length - compressedEnd) || expanded != (ulong)header.Lengths[i])
                throw Invalid("SPZ attribute byte lengths do not match the file or Gaussian count.");
            compressedLengths[i] = checked((long)compressed);
            compressedEnd = checked(compressedEnd + compressedLengths[i]);
        }
        if (compressedEnd != input.Length) throw Invalid("Unexpected bytes after SPZ attribute streams.");
        for (int i = 0; i < streams; ++i)
        {
            ct.ThrowIfCancellationRequested();
            using var segment = new SpzBoundedStream(input, compressedLengths[i]);
            using var decompressed = new DecompressionStream(segment, checkEndOfStream: true, leaveOpen: true);
            SplatReadSupport.CopyExactly(decompressed, packed, header.Lengths[i], ct);
            ct.ThrowIfCancellationRequested();
            if (decompressed.ReadByte() != -1 || segment.Remaining != 0)
                throw Invalid("SPZ decompressed attribute length is inconsistent.");
        }
        return header;
    }

    private static Header ReadLegacy(FileStream input, FileStream packed, Metadata metadata, CancellationToken ct)
    {
        if (input.Length < 18) throw Invalid("Truncated gzip container.");
        Span<byte> trailer = stackalloc byte[8];
        input.Position = input.Length - trailer.Length;
        SplatReadSupport.ReadExactly(input, trailer, ct);
        uint expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(trailer), expectedLength = BinaryPrimitives.ReadUInt32LittleEndian(trailer[4..]);
        input.Position = 0;
        using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var tracked = new SpzCrcStream(gzip);
        Span<byte> bytes = stackalloc byte[16];
        SplatReadSupport.ReadExactly(tracked, bytes, ct);
        Header header = ParseHeader(bytes, false);
        foreach (long length in header.Lengths) SplatReadSupport.CopyExactly(tracked, packed, length, ct);
        if ((header.Flags & 2) != 0) ReadExtensions(tracked, metadata, ct);
        else if (tracked.ReadByte() != -1) throw Invalid("Unexpected bytes after SPZ Gaussian attributes.");
        if (tracked.Crc != expectedCrc || tracked.ModuloLength != expectedLength)
            throw Invalid("SPZ gzip checksum or uncompressed length is invalid; concatenated containers are not supported.");
        return header;
    }

    private static void ReadExtensions(Stream input, Metadata metadata, CancellationToken ct)
    {
        Span<byte> record = stackalloc byte[8];
        Span<byte> payload = stackalloc byte[12];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int first = input.ReadByte();
            if (first < 0) break;
            record[0] = (byte)first;
            SplatReadSupport.ReadExactly(input, record[1..], ct);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(record), length = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            if (type == CoordinateExtension)
            {
                if (length != 4 || metadata.HasCoordinates) throw Invalid("Invalid or duplicate SPZ coordinate-system extension.");
                SplatReadSupport.ReadExactly(input, payload[..4], ct);
                uint system = BinaryPrimitives.ReadUInt32LittleEndian(payload);
                if (system > 16) throw new NotSupportedException($"Unknown SPZ coordinate system {system}.");
                metadata.HasCoordinates = true;
                if (system == 0) metadata.Warnings.Add("SPZ specifies an unspecified coordinate system; the format default RUB is used.");
                else metadata.CoordinateSystem = system;
            }
            else if (type == OrbitExtension)
            {
                if (length != 12) throw Invalid("Invalid SPZ safe-orbit extension length.");
                SplatReadSupport.ReadExactly(input, payload, ct);
                for (int offset = 0; offset < 12; offset += 4)
                    if (!float.IsFinite(SplatReadSupport.Float(payload[offset..]))) throw Invalid("Non-finite SPZ safe-orbit metadata.");
                if (metadata.UnknownTypes.Add(type)) metadata.Warnings.Add("SPZ safe-orbit camera limits are present but are not enforced by this viewer.");
            }
            else
            {
                SplatReadSupport.CopyExactly(input, null, length, ct);
                if (metadata.UnknownTypes.Add(type)) metadata.Warnings.Add($"SPZ extension 0x{type:X8} is not interpreted; it may affect appearance. The original file preserves its data.");
            }
        }
    }

    private static GaussianScene Decode(FileStream packed, Header header, Metadata metadata, string path, CancellationToken ct)
    {
        var offsets = new long[6];
        for (int i = 1; i < offsets.Length; ++i) offsets[i] = checked(offsets[i - 1] + header.Lengths[i - 1]);
        if (packed.Length != checked(offsets[^1] + header.Lengths[^1])) throw Invalid("Incomplete staged SPZ attributes.");
        int positionBytes = header.Version == 1 ? 6 : 9, rotationBytes = header.Version < 3 ? 3 : 4;
        int shBytes = 3 * ((header.Degree + 1) * (header.Degree + 1) - 1);
        int[] strides = { positionBytes, 1, 3, 3, rotationBytes, shBytes };
        var blocks = strides.Select(stride => new byte[SplatReadSupport.ChunkSize * stride]).ToArray();
        var parts = new List<GaussianPart>();
        GaussianTransform transform = CoordinateTransform(metadata.CoordinateSystem);
        for (long first = 0; first < header.Count;)
        {
            ct.ThrowIfCancellationRequested();
            int length = (int)Math.Min(SplatReadSupport.ChunkSize, header.Count - first);
            for (int attribute = 0; attribute < blocks.Length; ++attribute)
            {
                packed.Position = checked(offsets[attribute] + first * strides[attribute]);
                SplatReadSupport.ReadExactly(packed, blocks[attribute].AsSpan(0, length * strides[attribute]), ct);
            }
            var splats = new GaussianSplat[length];
            for (int i = 0; i < length; ++i)
            {
                if ((i & 255) == 0) ct.ThrowIfCancellationRequested();
                ReadOnlySpan<byte> positions = blocks[0].AsSpan(i * positionBytes, positionBytes);
                GaussianVector center = header.Version == 1
                    ? new(Half(positions), Half(positions[2..]), Half(positions[4..]))
                    : new(Fixed(positions, header.FractionalBits), Fixed(positions[3..], header.FractionalBits), Fixed(positions[6..], header.FractionalBits));
                var scale = new GaussianVector(blocks[3][i * 3] / 16.0 - 10, blocks[3][i * 3 + 1] / 16.0 - 10, blocks[3][i * 3 + 2] / 16.0 - 10);
                GaussianQuaternion rotation = Rotation(blocks[4].AsSpan(i * rotationBytes, rotationBytes));
                var coefficients = new float[shBytes + 3];
                for (int channel = 0; channel < 3; ++channel)
                    coefficients[channel] = (float)((blocks[2][i * 3 + channel] / 255.0 - .5) / .15);
                for (int coefficient = 0; coefficient < shBytes; ++coefficient)
                    coefficients[coefficient + 3] = (blocks[5][i * shBytes + coefficient] - 128) / 128f;
                try { splats[i] = new GaussianSplat(center, scale, rotation, SplatReadSupport.Logit(blocks[1][i]), header.Degree, coefficients); }
                catch (ArgumentException error) { throw Invalid("Invalid SPZ Gaussian parameters.", error); }
            }
            parts.Add(new GaussianPart(SplatReadSupport.PartName(path, parts.Count), splats, transform, (header.Flags & 1) != 0));
            first += length;
        }
        ct.ThrowIfCancellationRequested();
        // Parts map their source coordinates to RUB (Y-up); SH remains in each part's local frame.
        return new GaussianScene(parts, UpAxis: "Y", Format: $"spz-v{header.Version}", Warnings: metadata.Warnings.ToArray());
    }

    private static double Half(ReadOnlySpan<byte> bytes) => (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes));
    private static double Fixed(ReadOnlySpan<byte> bytes, int fractionalBits)
    {
        int value = bytes[0] | bytes[1] << 8 | bytes[2] << 16;
        if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
        return Math.ScaleB((double)value, -fractionalBits);
    }
    private static GaussianQuaternion Rotation(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 3)
        {
            double x = bytes[0] / 127.5 - 1, y = bytes[1] / 127.5 - 1, z = bytes[2] / 127.5 - 1;
            return new GaussianQuaternion(Math.Sqrt(Math.Max(0, 1 - x * x - y * y - z * z)), x, y, z).Normalized();
        }
        uint bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        int largest = (int)(bits >> 30);
        Span<double> xyzw = stackalloc double[4];
        double sum = 0;
        for (int index = 3; index >= 0; --index)
        {
            if (index == largest) continue;
            double component = (bits & 511) / (511.0 * Math.Sqrt(2));
            if ((bits & 512) != 0) component = -component;
            xyzw[index] = component;
            sum += component * component;
            bits >>= 10;
        }
        if (sum > 1) throw Invalid("SPZ quaternion components cannot form a unit rotation.");
        xyzw[largest] = Math.Sqrt(1 - sum);
        return new GaussianQuaternion(xyzw[3], xyzw[0], xyzw[1], xyzw[2]).Normalized();
    }
    private static GaussianTransform CoordinateTransform(uint system)
    {
        // Match coordinateConverter(source, RUB) in the pinned SPZ implementation.
        // The rotated family is defined operationally: within-family flips, then Rx(-pi/2).
        // Reading the enum's three-letter names as standalone axis vectors gives different signs.
        uint bits = (system - 1) & 7;
        double x = (bits & 1) != 0 ? 1 : -1, y = (bits & 2) != 0 ? 1 : -1, z = (bits & 4) == 0 ? 1 : -1;
        GaussianMatrix matrix = system <= 8 ? new(x, 0, 0, 0, y, 0, 0, 0, z) : new(x, 0, 0, 0, 0, z, 0, -y, 0);
        return new GaussianTransform(matrix, GaussianVector.Zero);
    }
    private static bool HasNonzero(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) if (value != 0) return true;
        return false;
    }
    private static InvalidDataException Invalid(string message, Exception? inner = null) => new(message, inner);
}
