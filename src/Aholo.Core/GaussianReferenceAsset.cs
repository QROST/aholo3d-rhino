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
            throw new IOException("找不到此 GS 的本地 PLY 引用；请恢复原缓存，或用 AholoPanel 重新导入原始文件。包围盒仍保留。");
        if (!File.Exists(source)) throw new FileNotFoundException("GS 引用的 PLY 缓存不存在；请恢复原缓存，或用 AholoPanel 重新导入。包围盒仍保留。");
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
                if (Digest(destination, ct) != hash) throw new IOException("GS 本地缓存与文档记录的哈希不一致；原始资产未修改。");
            }
            return data with { Path = destination };
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static GaussianReferenceData ReadVerified(string path, string hash, bool copied, CancellationToken ct)
    {
        if (Digest(path, ct) != hash) throw new IOException("GS 资源内容与文档记录的哈希不一致；保留包围盒，请恢复原缓存或重新导入。");
        GaussianSplat[] splats = PlyReader.ReadGaussians(path, ct).ToArray();
        if (splats.Length == 0) throw new InvalidDataException("GS 引用的 PLY 没有完整高斯数据。");
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
