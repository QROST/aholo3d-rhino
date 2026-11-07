using System.Security.Cryptography;

namespace Aholo.Core;

public sealed record GaussianReferenceData(string Path, GaussianSplat[] Splats, bool CopiedToCache);

/// <summary>Reads a compatible local reference without changing its document or its original asset.</summary>
public static class GaussianReferenceAsset
{
    public static GaussianReferenceData Load(GaussianReferenceMetadata reference, string cacheDirectory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string hash = reference.Hash;
        if (hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Invalid GS reference hash.");
        hash = hash.ToLowerInvariant();
        string destination = Path.Combine(cacheDirectory, hash + ".ply");
        if (File.Exists(destination)) return ReadVerified(destination, hash, false, ct);
        string? source = reference.AssetPath;
        if (string.IsNullOrWhiteSpace(source) || !Path.IsPathFullyQualified(source) || source.StartsWith("\\\\", StringComparison.Ordinal) ||
            source.StartsWith("//", StringComparison.Ordinal) || !string.Equals(Path.GetExtension(source), ".ply", StringComparison.OrdinalIgnoreCase))
            throw new IOException("This GS has no local PLY reference. Restore its cache or re-import the original file with AholoPanel. Bounds are retained.");
        if (!File.Exists(source)) throw new FileNotFoundException("The GS reference PLY cache is missing. Restore the cache or re-import with AholoPanel. Bounds are retained.");
        Directory.CreateDirectory(cacheDirectory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(cacheDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string temporary = Path.Combine(cacheDirectory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 131072 };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072))
            using (var output = new FileStream(temporary, options))
            {
                var buffer = new byte[131072]; int count;
                while ((count = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                output.Flush(true);
            }
            // Validate the snapshot, not a mutable source file. No unvalidated or partial cache is published.
            GaussianReferenceData data = ReadVerified(temporary, hash, true, ct);
            ct.ThrowIfCancellationRequested();
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination))
            {
                if (Digest(destination, ct) != hash) throw new IOException("The local GS cache hash does not match the document record. Original assets are unchanged.");
            }
            return data with { Path = destination };
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static GaussianReferenceData ReadVerified(string path, string hash, bool copied, CancellationToken ct)
    {
        if (Digest(path, ct) != hash) throw new IOException("The GS asset hash does not match the document record. Bounds are retained; restore the cache or re-import.");
        GaussianSplat[] splats = PlyReader.ReadGaussians(path, ct).ToArray();
        if (splats.Length == 0) throw new InvalidDataException("The referenced PLY has no complete Gaussian data.");
        return new(path, splats, copied);
    }

    private static string Digest(string path, CancellationToken ct)
    {
        using var input = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072]; int count;
        while ((count = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        ct.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
