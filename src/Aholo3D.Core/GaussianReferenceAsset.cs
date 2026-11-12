using System.Security.Cryptography;

namespace Aholo3D.Core;

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
        Exception? cacheError = null;
        if (File.Exists(destination))
        {
            try { return ReadVerified(destination, hash, false, ct); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { cacheError = ex; }
        }
        string? source = reference.AssetPath;
        if (string.IsNullOrWhiteSpace(source) || !Path.IsPathFullyQualified(source) || source.StartsWith("\\\\", StringComparison.Ordinal) ||
            source.StartsWith("//", StringComparison.Ordinal) || !string.Equals(Path.GetExtension(source), ".ply", StringComparison.OrdinalIgnoreCase))
            throw new IOException(UiText.Get("This GS has no local PLY reference. Restore its cache or re-import the original file with Aholo3DPanel. Bounds are retained."));
        if (!File.Exists(source))
        {
            if (cacheError is not null) throw new IOException(UiText.Get("GS cache is corrupt and the original reference is unavailable. Corrupt cache retained; re-import the original file."), cacheError);
            throw new FileNotFoundException(UiText.Get("The GS reference PLY cache is missing. Restore the cache or re-import with Aholo3DPanel. Bounds are retained."));
        }
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
            bool published = GaussianCachePublisher.Publish(temporary, destination, hash, ct);
            return data with { Path = destination, CopiedToCache = published };
        }
        catch (Exception ex) when (cacheError is not null && (ex is IOException or InvalidDataException))
        {
            throw new IOException(UiText.Get("Could not recover the corrupt GS cache from its original reference. Cache and source retained; re-import the original file."), ex);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static GaussianReferenceData ReadVerified(string path, string hash, bool copied, CancellationToken ct)
    {
        if (Digest(path, ct) != hash) throw new IOException(UiText.Get("The GS asset hash does not match the document record. Bounds are retained; restore the cache or re-import."));
        GaussianSplat[] splats = PlyReader.ReadGaussians(path, ct).ToArray();
        if (splats.Length == 0) throw new InvalidDataException(UiText.Get("The referenced PLY has no complete Gaussian data."));
        return new(path, splats, copied);
    }

    private static string Digest(string path, CancellationToken ct)
    {
        // Permit atomic cache replacement while a reader finishes hashing the previous file.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072]; int count;
        while ((count = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, count); }
        ct.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
