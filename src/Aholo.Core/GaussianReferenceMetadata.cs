namespace Aholo.Core;

/// <summary>Versioned GS block reference, independent of the producer's namespace.</summary>
public sealed record GaussianReferenceMetadata(string Prefix, string Hash, string? AssetPath)
{
    public const string NativePrefix = "Aholo.GS";
    public bool IsNative => Prefix == NativePrefix;
    public bool Antialiased { get; init; }

    public static bool HasMarker(Func<string, string?> read, Func<IEnumerable<string>> keys)
        => read(NativePrefix + ".Version") is not null || keys().Any(key => key.EndsWith(".GS.Version", StringComparison.Ordinal));

    public static GaussianReferenceMetadata? Resolve(Func<string, string?> read, Func<IEnumerable<string>> keys)
    {
        // An explicitly authored native marker owns the reference; do not mix it with foreign fields.
        if (read(NativePrefix + ".Version") is not null) return ReadPrefix(NativePrefix, read);
        GaussianReferenceMetadata? result = null;
        foreach (string key in keys().Distinct(StringComparer.Ordinal))
        {
            if (!key.EndsWith(".GS.Version", StringComparison.Ordinal)) continue;
            var candidate = ReadPrefix(key[..^".Version".Length], read);
            if (candidate is null) continue;
            // Ambiguous references must not choose a producer by dictionary/enumeration order.
            if (result is not null) return null;
            result = candidate;
        }
        return result;
    }

    private static GaussianReferenceMetadata? ReadPrefix(string prefix, Func<string, string?> read)
    {
        if (read(prefix + ".Version") != "1") return null;
        string? hash = read(prefix + ".Sha256");
        if (hash is null || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) return null;
        return new(prefix, hash.ToLowerInvariant(), read(prefix + ".AssetPath")) { Antialiased = read(prefix + ".Antialiased") == "true" };
    }
}
