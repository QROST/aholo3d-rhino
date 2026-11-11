using System.IO.Compression;
using System.IO.Packaging;
using System.Xml;
using System.Text;

namespace Aholo.Core;

/// <summary>Core 3MF package preflight, without extraction or geometry quotas. Rhino owns mesh decoding.</summary>
public static class ThreeMfPreflight
{
    private const string Core = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";
    private const string Relations = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string Start = "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel";
    private const string ModelType = "application/vnd.ms-package.3dmanufacturing-3dmodel+xml";
    private const string RelationshipType = "application/vnd.openxmlformats-package.relationships+xml";
    private const string Xml = "http://www.w3.org/XML/1998/namespace";
    private const string Xmlns = "http://www.w3.org/2000/xmlns/";
    private sealed record Part(Uri Uri, ZipArchiveEntry Entry);
    private static InvalidDataException Invalid() => new(UiText.Get("The 3MF package is invalid or contains unresolved or external resources."));
    private static InvalidDataException Unsupported() => new(UiText.Get("This 3MF uses extensions not yet supported by mesh placement. Export a core 3MF or static GLB instead."));

    public static void Validate(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try { ValidatePackage(path, ct); }
        catch (Exception ex) when (ex is XmlException or UriFormatException or ArgumentException or NotSupportedException)
        { throw new InvalidDataException(Invalid().Message, ex); }
    }
    private static void ValidatePackage(string path, CancellationToken ct)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        ZipArchiveEntry? types = null;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw Invalid();
            if (entry.FullName == "[Content_Types].xml")
            { if (types is not null) throw Invalid(); types = entry; continue; }
            if (entry.FullName.StartsWith('/')) throw Invalid();
            bool directory = entry.FullName.EndsWith('/');
            string name = directory ? entry.FullName[..^1] : entry.FullName;
            Uri uri = PartUri("/" + name);
            string key = Key(uri);
            if (!names.Add(key) || (directory && entry.Length != 0)) throw Invalid();
            if (!directory) parts.Add(key, new(uri, entry));
        }
        if (types is null) throw Invalid();
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        ReadXml(types, ct, reader =>
        {
            RequireRoot(reader, "Types", ContentTypes);
            if (reader.Depth == 0) return;
            if (reader.Depth != 1 || reader.NamespaceURI != ContentTypes) throw Invalid();
            string value = Required(reader, "ContentType");
            if (reader.LocalName == "Default")
            {
                string extension = Required(reader, "Extension");
                if (extension.IndexOfAny(new[] {'.', '/', '\\', ':', '%', '?', '#'}) >= 0 || !defaults.TryAdd(extension, value)) throw Invalid();
            }
            else if (reader.LocalName == "Override")
            { if (!overrides.TryAdd(Key(PartUri(Required(reader, "PartName"))), value)) throw Invalid(); }
            else throw Invalid();
        });
        string Type(Part part) => overrides.TryGetValue(Key(part.Uri), out var type) ? type :
            defaults.TryGetValue(Path.GetExtension(part.Uri.OriginalString).TrimStart('.'), out type) ? type : throw Invalid();
        foreach (string key in overrides.Keys) if (!parts.ContainsKey(key)) throw Invalid();
        string? primary = null;
        bool rootRelations = false;
        var models = new List<Part>();
        foreach (var part in parts.Values)
        {
            ct.ThrowIfCancellationRequested();
            string type = Type(part);
            bool relationship = PackUriHelper.IsRelationshipPartUri(part.Uri);
            if (relationship != (type == RelationshipType)) throw Invalid();
            if (relationship)
            {
                Uri source = PackUriHelper.GetSourcePartUriFromRelationshipPartUri(part.Uri);
                bool root = source.OriginalString == "/";
                if (root) rootRelations = true;
                else if (!parts.ContainsKey(Key(source))) throw Invalid();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var links = new HashSet<(string, string)>();
                ReadXml(part.Entry, ct, reader =>
                {
                    RequireRoot(reader, "Relationships", Relations);
                    if (reader.Depth == 0) return;
                    if (reader.Depth != 1 || reader.NamespaceURI != Relations || reader.LocalName != "Relationship") throw Invalid();
                    string id = Required(reader, "Id"), kind = Required(reader, "Type");
                    if (!ids.Add(id) || !Uri.TryCreate(kind, UriKind.Absolute, out _)) throw Invalid();
                    string? mode = reader.GetAttribute("TargetMode");
                    if (mode is not (null or "Internal")) throw Invalid();
                    string target = Key(Resolve(source, Required(reader, "Target")));
                    if (!parts.TryGetValue(target, out var destination) || PackUriHelper.IsRelationshipPartUri(destination.Uri) || !links.Add((kind, target))) throw Invalid();
                    if (root && kind == Start)
                    { if (primary is not null || Type(destination) != ModelType) throw Invalid(); primary = target; }
                });
            }
            else if (type == ModelType) models.Add(part);
            else if (part.Uri.OriginalString.EndsWith(".model", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        }
        if (!rootRelations || primary is null || models.Count == 0) throw Invalid();
        foreach (var model in models)
            ReadXml(model.Entry, ct, reader =>
            {
                RequireRoot(reader, "model", Core);
                if (reader.NamespaceURI != Core) throw Unsupported();
                if (reader.Depth == 0 && !string.IsNullOrWhiteSpace(reader.GetAttribute("requiredextensions"))) throw Unsupported();
                if (reader.LocalName == "object" && reader.GetAttribute("thumbnail") is { } thumbnail &&
                    !parts.ContainsKey(Key(Resolve(model.Uri, thumbnail)))) throw Invalid();
                if (reader.MoveToFirstAttribute()) do
                {
                    if (reader.NamespaceURI.Length != 0 && reader.NamespaceURI != Xmlns &&
                        !(reader.NamespaceURI == Xml && reader.LocalName == "lang")) throw Unsupported();
                    // URI-bearing extension attributes must not enter the native importer unnoticed.
                    if (reader.NamespaceURI.Length == 0 && reader.LocalName is "path" or "href" or "src") throw Unsupported();
                } while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            });
        // Do not extract. Drain every remaining resource to detect truncation/unsupported compression.
        // ZipArchive/Rhino are the ZIP decoders; this is not a complete ZIP/CRC or 3MF schema validator.
        byte[] buffer = new byte[128 * 1024];
        foreach (var part in parts.Values)
        {
            ct.ThrowIfCancellationRequested();
            if (PackUriHelper.IsRelationshipPartUri(part.Uri) || Type(part) == ModelType) continue;
            using var stream = part.Entry.Open();
            long count = 0; int read;
            while ((read = stream.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); count = checked(count + read); }
            if (count != part.Entry.Length) throw Invalid();
        }
        ct.ThrowIfCancellationRequested();
    }
    private sealed class CancelableReadStream(Stream input, CancellationToken ct) : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        { ct.ThrowIfCancellationRequested(); int read = input.Read(buffer); ct.ThrowIfCancellationRequested(); return read; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static string Required(XmlReader reader, string name) =>
        reader.GetAttribute(name) is { Length: > 0 } value ? value : throw Invalid();
    private static void RequireRoot(XmlReader reader, string name, string ns)
    { if (reader.Depth == 0 && (reader.LocalName != name || reader.NamespaceURI != ns)) throw Invalid(); }
    private static void ReadXml(ZipArchiveEntry entry, CancellationToken ct, Action<XmlReader> inspect)
    {
        using var input = entry.Open();
        using var stream = new CancelableReadStream(input, ct);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        bool root = false;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element) continue;
            root = true; inspect(reader);
        }
        if (!root) throw Invalid();
    }
    private static string Key(Uri uri) => PackUriHelper.GetNormalizedPartUri(new Uri(uri.OriginalString, UriKind.Relative)).OriginalString;
    private static void CheckCharacters(string value)
    {
        if (value.Length == 0 || value.Contains('\\') || value.Contains(':') || value.Contains('?') || value.Contains('#') || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) throw Invalid();
        for (int i = 0; i < value.Length; i++) if (value[i] == '%')
        {
            var bytes = new List<byte>();
            do
            {
                if (i + 2 >= value.Length || !Uri.IsHexDigit(value[i + 1]) || !Uri.IsHexDigit(value[i + 2])) throw Invalid();
                byte octet = Convert.ToByte(value.Substring(i + 1, 2), 16); bytes.Add(octet); i += 3;
                char ascii = (char)octet;
                // OPC disallows escaped unreserved characters/separators; nested escapes are unsupported.
                if (char.IsAsciiLetterOrDigit(ascii) || ascii is '-' or '.' or '_' or '~' or '/' or '\\' or '%' or ':') throw Invalid();
            } while (i < value.Length && value[i] == '%');
            i--;
            // UTF-8 continuation bytes in 0x80–0x9F are not themselves Unicode control characters.
            if (new UTF8Encoding(false, true).GetString(bytes.ToArray()).Any(char.IsControl)) throw Invalid();
        }
    }
    private static Uri PartUri(string value)
    {
        CheckCharacters(value);
        if (!value.StartsWith('/') || value.StartsWith("//") || value.EndsWith('/')) throw Invalid();
        if (value[1..].Split('/').Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.'))) throw Invalid();
        return PackUriHelper.CreatePartUri(new Uri(value, UriKind.Relative));
    }
    private static Uri Resolve(Uri source, string target)
    {
        CheckCharacters(target);
        if (target.StartsWith("//") || target.EndsWith('/')) throw Invalid();
        int depth = target.StartsWith('/') ? 0 : source.OriginalString.Count(c => c == '/') - 1;
        foreach (string segment in target.TrimStart('/').Split('/'))
        {
            if (segment.Length == 0) throw Invalid();
            if (segment == ".") continue;
            if (segment == "..") { if (--depth < 0) throw Invalid(); }
            else depth++;
        }
        return PartUri(PackUriHelper.ResolvePartUri(source, new Uri(target, UriKind.Relative)).OriginalString);
    }
}
