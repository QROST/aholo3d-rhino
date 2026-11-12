using System.Buffers.Binary;
using System.Text;
namespace Aholo3D.Core;

public enum MeshTextureWrap { Repeat = 10497, Clamp = 33071 }
public enum MeshTextureFilter { Nearest = 9728, Linear = 9729 }

/// <summary>Private encoded sRGB bitmap snapshot. Structural validation does not decode JPEG pixels.</summary>
public sealed class MeshTexture
{
    private readonly byte[] bytes;
    internal ReadOnlySpan<byte> Bytes => bytes;
    public int Length => bytes.Length;
    public string MimeType { get; }
    public bool HasAlpha { get; }
    public bool UseAlpha { get; }
    public string Sha256 { get; }
    public MeshTextureWrap WrapU { get; }
    public MeshTextureWrap WrapV { get; }
    public MeshTextureFilter MinFilter { get; }
    public MeshTextureFilter MagFilter { get; }
    public MeshTexture(byte[] encoded, MeshTextureWrap wrapU = MeshTextureWrap.Repeat, MeshTextureWrap wrapV = MeshTextureWrap.Repeat,
        MeshTextureFilter minFilter = MeshTextureFilter.Linear, MeshTextureFilter magFilter = MeshTextureFilter.Linear, bool useAlpha = true, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        if (!Enum.IsDefined(typeof(MeshTextureWrap), wrapU) || !Enum.IsDefined(typeof(MeshTextureWrap), wrapV) ||
            !Enum.IsDefined(typeof(MeshTextureFilter), minFilter) || !Enum.IsDefined(typeof(MeshTextureFilter), magFilter)) throw new ArgumentException("Unsupported texture sampler.");
        ct.ThrowIfCancellationRequested();
        bytes = (byte[])encoded.Clone();
        (MimeType, HasAlpha) = ValidateImage(bytes, ct);
        UseAlpha = useAlpha; Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        WrapU = wrapU; WrapV = wrapV; MinFilter = minFilter; MagFilter = magFilter;
    }
    /// <summary>Derive opaque RGB pixels without changing their samples, this image, or its sampler.</summary>
    public MeshTexture WithoutAlpha(CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();if(!HasAlpha)return this;
        return new(PngOpaqueCopy.Convert(bytes,ct),WrapU,WrapV,MinFilter,MagFilter,false,ct);
    }
    /// <summary>Copy the immutable, validated encoded bitmap without exposing its mutable backing array.</summary>
    public void CopyEncodedTo(Stream output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(output); ct.ThrowIfCancellationRequested();
        for (int i=0;i<bytes.Length;)
        { ct.ThrowIfCancellationRequested(); int count=Math.Min(131072,bytes.Length-i); output.Write(bytes,i,count); i+=count; }
    }
    private static (string, bool) ValidateImage(ReadOnlySpan<byte> data, CancellationToken ct)
    {
        if (data.StartsWith(new byte[]{137,80,78,71,13,10,26,10}))
        {
            int offset = 8; bool first = true, image = false, alpha = false, imageEnded = false, palette = false;
            while (offset <= data.Length - 12)
            {
                ct.ThrowIfCancellationRequested();
                uint size = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
                if (size > (uint)(data.Length - offset - 12)) throw Invalid();
                int length = (int)size; var chunk = data.Slice(offset + 4, length + 4);
                uint crc = 0xffffffff;
                for (int index = 0; index < chunk.Length; index++)
                {
                    if ((index & 16383) == 0) ct.ThrowIfCancellationRequested();
                    crc ^= chunk[index];
                    for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320 : crc >> 1;
                }
                if (~crc != BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 8 + length)..])) throw Invalid();
                string type = Encoding.ASCII.GetString(chunk[..4]); var value = chunk[4..];
                if (first)
                {
                    if (type != "IHDR" || length != 13 || BinaryPrimitives.ReadUInt32BigEndian(value) is 0 or > int.MaxValue ||
                        BinaryPrimitives.ReadUInt32BigEndian(value[4..]) is 0 or > int.MaxValue || value[8] != 8 || value[9] is not (2 or 6) || value[10] != 0 || value[11] != 0 || value[12] > 1) throw Invalid();
                    alpha = value[9] == 6; first = false;
                }
                else if (type == "IHDR") throw Invalid();
                else if (type == "IDAT") { if (imageEnded) throw Invalid(); image = true; }
                else if (type == "IEND")
                {
                    if (length != 0 || !image || offset + 12 != data.Length) throw Invalid();
                    return ("image/png", alpha);
                }
                else
                {
                    if (image) imageEnded = true;
                    if (type == "PLTE")
                    {
                        if (palette || image || length < 3 || length > 768 || length % 3 != 0) throw Invalid();
                        palette = true;
                    }
                    if (type is "iCCP" or "eXIf") throw UnsupportedColor();
                    if (type == "gAMA" && (length != 4 || BinaryPrimitives.ReadUInt32BigEndian(value) != 45455)) throw UnsupportedColor();
                    if (type == "sRGB" && (length != 1 || value[0] > 3)) throw UnsupportedColor();
                    if (type == "cICP" && !value.SequenceEqual(new byte[]{1,13,0,1})) throw UnsupportedColor();
                    if (type == "cHRM")
                    {
                        uint[] standard = {31270,32900,64000,33000,30000,60000,15000,6000};
                        if (length != 32) throw UnsupportedColor();
                        for (int i=0; i<8; i++) if (BinaryPrimitives.ReadUInt32BigEndian(value[(i*4)..]) != standard[i]) throw UnsupportedColor();
                    }
                    if (type == "tRNS") throw new InvalidDataException(UiText.Get("PNG color-key transparency is unsupported. Use an 8-bit RGB or RGBA PNG."));
                    if (type.Length != 4 || type.Any(c => !char.IsAsciiLetter(c)) || char.IsUpper(type[0]) && type != "PLTE") throw Invalid();
                }
                offset += length + 12;
            }
            throw Invalid();
        }
        if (data.Length >= 4 && data[0] == 255 && data[1] == 216 && data[^2] == 255 && data[^1] == 217)
        {
            _ = JpegFrameSize.Validate(data, long.MaxValue);
            int offset = 2; bool frame = false;
            while (offset < data.Length - 2)
            {
                ct.ThrowIfCancellationRequested();
                if (data[offset++] != 255) throw Invalid();
                while (offset < data.Length && data[offset] == 255) offset++;
                if (offset >= data.Length) throw Invalid(); byte marker = data[offset++];
                if (marker == 1) continue;
                if (offset > data.Length - 2) throw Invalid();
                int length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                if (length < 2 || length > data.Length - offset) throw Invalid();
                var metadata = data.Slice(offset + 2, length - 2);
                if (marker == 0xe1 && metadata.StartsWith("Exif\0\0"u8) || marker == 0xe2 && metadata.StartsWith("ICC_PROFILE\0"u8)) throw UnsupportedColor();
                if (marker is 0xc0 or 0xc1 or 0xc2)
                {
                    if (frame || length != 17 || data[offset + 2] != 8 || data[offset + 7] != 3) throw Invalid();
                    frame = true;
                }
                if (marker == 0xda)
                {
                    if (!frame || length < 6 || offset + length >= data.Length - 2) throw Invalid();
                    return ("image/jpeg", false);
                }
                offset += length;
            }
        }
        throw Invalid();
    }
    private static InvalidDataException UnsupportedColor() => new(UiText.Get("Embedded color profiles, non-sRGB declarations and EXIF metadata are unsupported. Export an untagged sRGB PNG/JPEG or disable bitmap export."));
    private static InvalidDataException Invalid() => new(UiText.Get("The base-color bitmap must be a structurally valid 8-bit RGB JPEG or RGB/RGBA PNG."));
}

/// <summary>A local texture file selected by the host. No URI or network-share resolution.</summary>
public sealed record MeshTextureSource(string Path, long Length, long LastWriteTicks, MeshTextureWrap WrapU, MeshTextureWrap WrapV,
    MeshTextureFilter MinFilter, MeshTextureFilter MagFilter, bool UseAlpha = true)
{
    public static void RequireLocalPath(string path)
    {
        if (!System.IO.Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
            path.Contains("://", StringComparison.Ordinal) || OperatingSystem.IsWindows() && path[2..].Contains(':'))
            throw new InvalidDataException(UiText.Get("Base-color bitmaps must be local files, not URLs or network shares."));
    }
    public MeshTexture Load(CancellationToken ct = default)
    {
        RequireLocalPath(Path); ct.ThrowIfCancellationRequested();
        void Verify()
        {
            var file = new FileInfo(Path);
            if (!file.Exists || file.Length != Length || file.LastWriteTimeUtc.Ticks != LastWriteTicks) throw new IOException(UiText.Get("A texture changed while preparing the render. Capture the selection again."));
        }
        Verify();
        using var input = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        // Encoded images are represented by a managed byte array; this is not a geometry quota.
        if (input.Length > Array.MaxLength) throw new InvalidDataException(UiText.Get("The encoded bitmap exceeds the runtime byte-array capacity."));
        byte[] bytes = new byte[(int)input.Length]; int offset = 0;
        while (offset < bytes.Length)
        {
            ct.ThrowIfCancellationRequested(); int read = input.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
            if (read == 0) throw new EndOfStreamException(); offset += read;
        }
        if (input.ReadByte() != -1) throw new IOException(UiText.Get("A texture changed while preparing the render. Capture the selection again."));
        Verify(); ct.ThrowIfCancellationRequested();
        return new(bytes, WrapU, WrapV, MinFilter, MagFilter, UseAlpha, ct);
    }
}
