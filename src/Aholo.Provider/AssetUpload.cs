using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Aholo.Core;

namespace Aholo.Provider;

public sealed partial class AholoApiClient
{
    public TimeSpan UploadCompletionTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public Task<string> UploadAsync(ApiKeyCredential key, string filePath, CancellationToken ct = default)
        => UploadAsync(key, filePath, null, ct);

    public async Task<string> UploadAsync(ApiKeyCredential key, string filePath, AssetUploadOptions? upload, CancellationToken ct = default)
    {
        if (UploadCompletionTimeout <= TimeSpan.Zero || UploadCompletionTimeout > TimeSpan.FromMinutes(15))
            throw new ArgumentOutOfRangeException(nameof(UploadCompletionTimeout));
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None, Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using var snapshot = new FileStream(Path.Combine(Path.GetTempPath(), "aholo-upload-" + Guid.NewGuid().ToString("N")), options);
        await using (var source = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            await source.CopyToAsync(snapshot, ct).ConfigureAwait(false);
        if (snapshot.Length == 0) throw new ArgumentException("The upload file cannot be empty.", nameof(filePath));
        snapshot.Position = 0;
        string md5 = Convert.ToHexString(await MD5.HashDataAsync(snapshot, ct).ConfigureAwait(false)).ToLowerInvariant();
        using var tokenRequest = Gateway(key, HttpMethod.Get, "asset/v1/token");
        var token = await SendAsync(_apiHttp, tokenRequest, ct).ConfigureAwait(false);
        if (token.ValueKind != JsonValueKind.Object) throw InvalidResponse();
        string ousToken = RequiredString(token, "ousToken");
        if (ousToken.Any(char.IsControl) || ousToken.Any(char.IsWhiteSpace)) throw InvalidResponse();
        var host = new Uri(HttpsUrl(RequiredString(token, "globalDomain")));
        if (!host.Host.Equals(AholoRegions.Get(key.Region).UploadHost, StringComparison.OrdinalIgnoreCase) || host.Port != 443 ||
            host.UserInfo.Length != 0 || host.AbsolutePath != "/" || host.Query.Length != 0 || host.Fragment.Length != 0)
            throw new ProviderException("invalid_upload_origin", "The upload address is outside the verified upload service for this region.");
        if (!token.TryGetProperty("blockSize", out var block) || block.ValueKind != JsonValueKind.Number || !block.TryGetInt64(out var blockSize) || blockSize <= 0) throw InvalidResponse();
        string name = Path.GetFileName(filePath);
        long blocks = 1 + (snapshot.Length - 1) / blockSize;
        if (snapshot.Length <= blockSize)
        {
            if (upload?.HasValues == true) throw new ArgumentException("Custom upload metadata and names are supported only for multipart files. Clear these settings for a small file.");
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(md5), "md5");
            form.Add(UploadContent(snapshot, 0, snapshot.Length), "file", name);
            var result = await SendOusAsync(host, ousToken, "single/upload", HttpMethod.Post, form, ct).ConfigureAwait(false);
            if (result.ValueKind != JsonValueKind.Object) throw InvalidResponse();
            _ = RequiredString(result, "taskId");
        }
        else
        {
            // The public part index is int32 even though init's total count is int64.
            if (blocks > int.MaxValue) throw new ArgumentException("The upload exceeds the service protocol's representable part index.");
            string query = "block/upload/init?md5=" + md5 + "&blocks=" + blocks.ToString(CultureInfo.InvariantCulture) + "&size=" + snapshot.Length.ToString(CultureInfo.InvariantCulture) + "&name=" + Uri.EscapeDataString(name);
            foreach (var pair in new[] { ("metadata", upload?.Metadata), ("customPrefix", upload?.CustomPrefix), ("customFilename", upload?.CustomFilename) })
                if (pair.Item2 is not null) query += "&" + pair.Item1 + "=" + Uri.EscapeDataString(pair.Item2);
            var initialized = await SendOusAsync(host, ousToken, query, HttpMethod.Post, null, ct).ConfigureAwait(false);
            if (initialized.ValueKind != JsonValueKind.Object || !initialized.TryGetProperty("taskId", out var taskId) || taskId.ValueKind != JsonValueKind.Number || !taskId.TryGetInt64(out _)) throw InvalidResponse();
            bool deduplicated = initialized.TryGetProperty("deduplicated", out var dedup) && dedup.ValueKind == JsonValueKind.True;
            if (!deduplicated)
            {
                initialized.TryGetProperty("lackBlocks", out var missing);
                foreach (var range in ReadUploadRanges(missing, blocks))
                    for (long n = range.Start; n <= range.End; n++)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var part = new MultipartFormDataContent();
                        part.Add(new StringContent(n.ToString(CultureInfo.InvariantCulture)), "block");
                        long offset = checked((n - 1) * blockSize);
                        part.Add(UploadContent(snapshot, offset, Math.Min(blockSize, snapshot.Length - offset)), "file", name + ".part-" + n.ToString(CultureInfo.InvariantCulture));
                        var result = await SendOusAsync(host, ousToken, "block/upload/part", HttpMethod.Post, part, ct).ConfigureAwait(false);
                        if (result.ValueKind != JsonValueKind.Null) throw InvalidResponse();
                    }
            }
        }
        // A completion deadline includes HTTP waits, not just the delay between polls.
        using var completion = CancellationTokenSource.CreateLinkedTokenSource(ct);
        completion.CancelAfter(UploadCompletionTimeout);
        bool missingParts = false;
        try
        {
            while (true)
            {
                var result = await SendOusAsync(host, ousToken, "upload/status", HttpMethod.Get, null, completion.Token).ConfigureAwait(false);
                if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Number || !status.TryGetInt32(out var value)) throw InvalidResponse();
                if (value == 5) return HttpsUrl(RequiredString(result, "url"));
                if (value is 6 or 8) throw new ProviderException("upload_failed", "File upload failed. Retry the upload.");
                if (result.TryGetProperty("blockUpload", out var details) && details.ValueKind == JsonValueKind.Object && details.TryGetProperty("lackBlocks", out var lack) && lack.ValueKind != JsonValueKind.Null)
                    missingParts = ReadUploadRanges(lack, blocks).Count != 0;
                await Task.Delay(TimeSpan.FromMilliseconds(250), completion.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && completion.IsCancellationRequested)
        {
            throw new ProviderException("upload_timeout", missingParts
                ? "The upload still has missing parts. Retry the original input to request server-directed resume. No generation task was submitted."
                : "File upload has not finished. Retry the upload later.");
        }
    }

    private static IReadOnlyList<UploadBlockRanges.Range> ReadUploadRanges(JsonElement value, long blocks)
    {
        try { return UploadBlockRanges.Parse(value, blocks); }
        catch (InvalidDataException) { throw InvalidResponse(); }
    }

    private static HttpContent UploadContent(Stream source, long offset, long count)
    {
        var content = new SnapshotContent(source, offset, count);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }
}

/// <summary>Validated inclusive service ranges; never allocate one entry per file part.</summary>
public static class UploadBlockRanges
{
    public readonly record struct Range(long Start, long End);
    public static IReadOnlyList<Range> Parse(JsonElement value, long blocks)
    {
        if (blocks <= 0 || blocks > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(blocks));
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return new[] { new Range(1, blocks) };
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid missing upload part ranges.");
        var sorted = new List<Range>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) throw new InvalidDataException("Invalid missing upload part range.");
            string[] pair = item.GetString()!.Split('-');
            if (pair.Length != 2 || !long.TryParse(pair[0], NumberStyles.None, CultureInfo.InvariantCulture, out long start) ||
                !long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out long end) || start < 1 || end < start || end > blocks)
                throw new InvalidDataException("Invalid missing upload part range.");
            sorted.Add(new(start, end));
        }
        sorted.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<Range>();
        foreach (var range in sorted)
        {
            if (merged.Count == 0 || range.Start > merged[^1].End + 1) merged.Add(range);
            else merged[^1] = new(merged[^1].Start, Math.Max(merged[^1].End, range.End));
        }
        return merged;
    }
}
