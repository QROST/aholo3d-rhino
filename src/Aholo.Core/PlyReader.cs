using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Aholo.Core;

/// <summary>Streaming PLY 1.0 reader. Fully enumerate successfully before modifying a Rhino document.</summary>
public static partial class PlyReader
{
    private enum Scalar { Int8, UInt8, Int16, UInt16, Int32, UInt32, Float32, Float64 }
    private sealed record Property(string Name, Scalar Type, Scalar? CountType);
    private sealed record Element(string Name, long Count, List<Property> Properties);
    private const double Sh0 = 0.28209479177387814;

    public static IEnumerable<SplatPoint> Read(string path, CancellationToken ct = default) => ReadVertices<SplatPoint>(path, header =>
    {
        var properties = header.Elements.Single(e => e.Name == "vertex").Properties;
        int x = Index(properties, "x"), y = Index(properties, "y"), z = Index(properties, "z");
        int r = Index(properties, header.SphericalHarmonics ? "f_dc_0" : "red"),
            g = Index(properties, header.SphericalHarmonics ? "f_dc_1" : "green"),
            b = Index(properties, header.SphericalHarmonics ? "f_dc_2" : "blue");
        return values =>
        {
            if (!double.IsFinite(values[x]) || !double.IsFinite(values[y]) || !double.IsFinite(values[z])) throw Invalid("Non-finite vertex position.");
            double red = r < 0 ? 255 : values[r], green = g < 0 ? 255 : values[g], blue = b < 0 ? 255 : values[b];
            if (header.SphericalHarmonics)
            {
                red = (.5 + Sh0 * red) * 255; green = (.5 + Sh0 * green) * 255; blue = (.5 + Sh0 * blue) * 255;
            }
            return new SplatPoint(values[x], values[y], values[z], Color(red), Color(green), Color(blue));
        };
    }, ct);

    /// <summary>Strict original 3DGS PLY decoding. No color quantization or silent point-cloud fallback. Fully enumerate before host insertion.</summary>
    public static IEnumerable<GaussianSplat> ReadGaussians(string path, CancellationToken ct = default) => ReadVertices<GaussianSplat>(path, header =>
    {
        var properties = header.Elements.Single(e => e.Name == "vertex").Properties;
        int Required(string name)
        {
            int index = Index(properties, name);
            if (index < 0) throw Invalid("Gaussian vertices require scalar " + name + ".");
            return index;
        }
        int x = Required("x"), y = Required("y"), z = Required("z"), sx = Required("scale_0"), sy = Required("scale_1"), sz = Required("scale_2"),
            rw = Required("rot_0"), rx = Required("rot_1"), ry = Required("rot_2"), rz = Required("rot_3"), opacity = Required("opacity");
        int[] dc = { Required("f_dc_0"), Required("f_dc_1"), Required("f_dc_2") };
        var restNames = properties.Where(p => p.Name.StartsWith("f_rest_", StringComparison.Ordinal)).Select(p => p.Name).ToArray();
        int degree = restNames.Length switch { 0 => 0, 9 => 1, 24 => 2, 45 => 3, _ => throw Invalid("Gaussian SH requires complete degree 0, 1, 2 or 3.") };
        int[] rest = Enumerable.Range(0, restNames.Length).Select(i => Required("f_rest_" + i.ToString(CultureInfo.InvariantCulture))).ToArray();
        if (properties.Count(p => p.Name.StartsWith("scale_", StringComparison.Ordinal)) != 3 ||
            properties.Count(p => p.Name.StartsWith("rot_", StringComparison.Ordinal)) != 4 ||
            properties.Count(p => p.Name.StartsWith("f_dc_", StringComparison.Ordinal)) != 3)
            throw Invalid("Unsupported Gaussian scale, quaternion or DC field layout.");
        int perChannel = rest.Length / 3;
        return values =>
        {
            var coefficients = new float[3 * (perChannel + 1)];
            for (int channel = 0; channel < 3; ++channel)
            {
                coefficients[channel] = (float)values[dc[channel]];
                for (int index = 0; index < perChannel; ++index)
                    coefficients[3 * (index + 1) + channel] = (float)values[rest[channel * perChannel + index]];
            }
            try
            {
                return new GaussianSplat(new(values[x], values[y], values[z]), new(values[sx], values[sy], values[sz]),
                    new(values[rw], values[rx], values[ry], values[rz]), values[opacity], degree, coefficients);
            }
            catch (ArgumentException error) { throw Invalid("Invalid Gaussian parameters: " + error.Message); }
        };
    }, ct);

    private static int Index(List<Property> properties, string name) => properties.FindIndex(p => p.Name == name && p.CountType is null);

    // Header, scalar parsing, element/list skipping and trailing-data validation are shared by both projections.
    private static IEnumerable<T> ReadVertices<T>(string path, Func<(string Format, List<Element> Elements, bool SphericalHarmonics), Func<double[], T>> prepare, CancellationToken ct)
    {
        using var stream = new BufferedStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), 65536);
        var header = ReadHeader(stream, ct);
        Func<double[], T> decode = prepare(header);
        var reader = new Values(stream, header.Format, ct);
        foreach (var element in header.Elements)
        {
            bool vertex = element.Name == "vertex";
            var values = vertex ? new double[element.Properties.Count] : Array.Empty<double>();
            for (long i = 0; i < element.Count; ++i)
            {
                ct.ThrowIfCancellationRequested();
                for (int index = 0; index < element.Properties.Count; ++index)
                {
                    Property property = element.Properties[index];
                    if (property.CountType is { } countType)
                    {
                        double count = reader.Read(countType);
                        if (count < 0 || count > uint.MaxValue || count != Math.Truncate(count)) throw Invalid("Invalid list count.");
                        for (long n = 0; n < (long)count; ++n)
                        {
                            if ((n & 4095) == 0) ct.ThrowIfCancellationRequested();
                            reader.Read(property.Type);
                        }
                    }
                    else
                    {
                        double value = reader.Read(property.Type);
                        if (vertex) values[index] = value;
                    }
                }
                if (vertex) yield return decode(values);
            }
        }
        reader.RequireEnd();
    }

    private static byte Color(double value)
    {
        if (!double.IsFinite(value)) throw Invalid("Non-finite vertex color.");
        return (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }

    private static (string Format, List<Element> Elements, bool SphericalHarmonics) ReadHeader(Stream stream, CancellationToken ct)
    {
        if (Line(stream, ct) != "ply") throw Invalid("Missing PLY signature.");
        string? format = null;
        var elements = new List<Element>();
        Element? current = null;
        while (true)
        {
            var line = Line(stream, ct) ?? throw Invalid("Incomplete header.");
            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0) continue;
            if (fields[0] is "comment" or "obj_info") continue;
            if (fields[0] == "end_header")
            {
                if (fields.Length != 1) throw Invalid("Invalid header terminator.");
                break;
            }
            if (fields[0] == "format")
            {
                if (format is not null || elements.Count != 0 || fields.Length != 3 || fields[2] != "1.0" ||
                    fields[1] is not ("ascii" or "binary_little_endian" or "binary_big_endian")) throw Invalid("Unsupported PLY format.");
                format = fields[1];
            }
            else if (fields[0] == "element")
            {
                if (format is null || fields.Length != 3 || !long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 0)
                    throw Invalid("Invalid element declaration.");
                if (elements.Any(e => e.Name == fields[1])) throw Invalid("Duplicate element name.");
                current = new Element(fields[1], count, new List<Property>());
                elements.Add(current);
            }
            else if (fields[0] == "property")
            {
                if (current is null) throw Invalid("Property without element.");
                Property property;
                if (fields.Length == 3) property = new Property(fields[2], Type(fields[1]), null);
                else if (fields.Length == 5 && fields[1] == "list")
                {
                    var countType = Type(fields[2]);
                    if (countType is Scalar.Float32 or Scalar.Float64) throw Invalid("List count must be integral.");
                    property = new Property(fields[4], Type(fields[3]), countType);
                }
                else throw Invalid("Invalid property declaration.");
                if (current.Properties.Any(p => p.Name == property.Name)) throw Invalid("Duplicate property name.");
                current.Properties.Add(property);
            }
            else throw Invalid("Unsupported header declaration.");
        }
        if (format is null) throw Invalid("Missing format.");
        if (elements.Any(e => e.Count > 0 && e.Properties.Count == 0)) throw Invalid("Non-empty elements require properties.");
        var vertex = elements.SingleOrDefault(e => e.Name == "vertex") ?? throw Invalid("Missing vertex element.");
        if (new[] { "x", "y", "z" }.Any(name => !vertex.Properties.Any(p => p.Name == name && p.CountType is null)))
            throw Invalid("Vertices require scalar x, y and z properties.");
        var rgb = new[] { "red", "green", "blue" }.Count(name => vertex.Properties.Any(p => p.Name == name && p.CountType is null));
        var sh = new[] { "f_dc_0", "f_dc_1", "f_dc_2" }.Count(name => vertex.Properties.Any(p => p.Name == name && p.CountType is null));
        if (rgb is 1 or 2 || (rgb == 0 && sh is 1 or 2)) throw Invalid("Incomplete vertex color properties.");
        return (format, elements, rgb == 0 && sh == 3);
    }

    private static string? Line(Stream stream, CancellationToken ct)
    {
        var line = new StringBuilder();
        while (true)
        {
            if ((line.Length & 4095) == 0) ct.ThrowIfCancellationRequested();
            var next = stream.ReadByte();
            if (next < 0) return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
            if (next == '\n') return line.ToString().TrimEnd('\r');
            if (next > 127 || next == 0) throw Invalid("Header must be ASCII.");
            line.Append((char)next);
        }
    }

    private static Scalar Type(string name) => name switch
    {
        "char" or "int8" => Scalar.Int8,
        "uchar" or "uint8" => Scalar.UInt8,
        "short" or "int16" => Scalar.Int16,
        "ushort" or "uint16" => Scalar.UInt16,
        "int" or "int32" => Scalar.Int32,
        "uint" or "uint32" => Scalar.UInt32,
        "float" or "float32" => Scalar.Float32,
        "double" or "float64" => Scalar.Float64,
        _ => throw Invalid("Unsupported scalar type.")
    };

    private static InvalidDataException Invalid(string detail) => new("Invalid PLY: " + detail);

    private sealed class Values
    {
        private readonly Stream _stream;
        private readonly bool _ascii;
        private readonly bool _little;
        private readonly CancellationToken _ct;
        public Values(Stream stream, string format, CancellationToken ct)
        {
            _stream = stream;
            _ascii = format == "ascii";
            _little = format == "binary_little_endian";
            _ct = ct;
        }
        public double Read(Scalar type)
        {
            if (_ascii) return ReadAscii(type);
            var size = type switch { Scalar.Int8 or Scalar.UInt8 => 1, Scalar.Int16 or Scalar.UInt16 => 2, Scalar.Int32 or Scalar.UInt32 or Scalar.Float32 => 4, _ => 8 };
            Span<byte> bytes = stackalloc byte[8];
            try { _stream.ReadExactly(bytes[..size]); }
            catch (EndOfStreamException) { throw Invalid("Truncated binary data."); }
            return type switch
            {
                Scalar.Int8 => unchecked((sbyte)bytes[0]),
                Scalar.UInt8 => bytes[0],
                Scalar.Int16 => _little ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : BinaryPrimitives.ReadInt16BigEndian(bytes),
                Scalar.UInt16 => _little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
                Scalar.Int32 => _little ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes),
                Scalar.UInt32 => _little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
                Scalar.Float32 => _little ? BinaryPrimitives.ReadSingleLittleEndian(bytes) : BinaryPrimitives.ReadSingleBigEndian(bytes),
                _ => _little ? BinaryPrimitives.ReadDoubleLittleEndian(bytes) : BinaryPrimitives.ReadDoubleBigEndian(bytes)
            };
        }
        private double ReadAscii(Scalar type)
        {
            var token = Token() ?? throw Invalid("Truncated ASCII data.");
            try
            {
                return type switch
                {
                    Scalar.Int8 => sbyte.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    Scalar.UInt8 => byte.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    Scalar.Int16 => short.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    Scalar.UInt16 => ushort.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    Scalar.Int32 => int.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    Scalar.UInt32 => uint.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    Scalar.Float32 => float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture),
                    _ => double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture)
                };
            }
            catch (Exception e) when (e is FormatException or OverflowException) { throw Invalid("Value does not match its scalar type."); }
        }
        private string? Token()
        {
            var token = new StringBuilder();
            long scanned = 0;
            while (true)
            {
                if ((scanned++ & 4095) == 0) _ct.ThrowIfCancellationRequested();
                var next = _stream.ReadByte();
                if (next < 0) return token.Length == 0 ? null : token.ToString();
                if (next is 9 or 10 or 13 or 32)
                {
                    if (token.Length == 0) continue;
                    return token.ToString();
                }
                if (next < 33 || next > 126) throw Invalid("Invalid ASCII data.");
                // A scalar token cannot require this length; this is a syntax bound, not a geometry limit.
                if (token.Length >= 128) throw Invalid("Invalid scalar token.");
                token.Append((char)next);
            }
        }
        public void RequireEnd()
        {
            if (_ascii ? Token() is not null : _stream.ReadByte() >= 0) throw Invalid("Unexpected trailing data.");
        }
    }
}
