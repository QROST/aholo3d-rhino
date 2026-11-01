using Aholo.Core;
namespace Aholo.Sidecar;

public sealed class AssetDownloader(HttpClient http, TaskStore store)
{
    public async Task<string> DownloadAsync(Guid id, string url, string kind, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new IOException("服务返回了不安全的下载地址。");
        if (kind is not ("ply" or "spz" or "image")) throw new ArgumentException("下载类型无效。");
        string temporary = Path.Combine(store.Root, "assets", id.ToString("N") + "." + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new IOException($"资产下载失败 (HTTP {(int)response.StatusCode})；可刷新任务后再次下载。");
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await source.CopyToAsync(output, ct).ConfigureAwait(false); await output.FlushAsync(ct).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is long expected && output.Length != expected) throw new IOException("资产下载不完整。");
            }
            string extension = kind;
            using (var file = File.OpenRead(temporary))
            {
                byte[] head = new byte[12]; int count = file.Read(head);
                if (count == 0) throw new IOException("资产文件为空。");
                if (kind == "ply" && (count < 4 || head[0] != 112 || head[1] != 108 || head[2] != 121 || head[3] is not (10 or 13))) throw new IOException("返回的资产不是 PLY 文件。");
                if (kind == "image") extension = count >= 8 && head.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) ? "png" :
                    count >= 3 && head[0] == 255 && head[1] == 216 && head[2] == 255 ? "jpg" :
                    count >= 12 && head.AsSpan(0,4).SequenceEqual("RIFF"u8) && head.AsSpan(8,4).SequenceEqual("WEBP"u8) ? "webp" : throw new IOException("渲染结果不是支持的图片格式。");
            }
            string target = Path.Combine(store.Root, "assets", id.ToString("N") + "." + extension);
            ct.ThrowIfCancellationRequested(); File.Move(temporary, target, true); return target;
        }
        catch (HttpRequestException) { throw new IOException("资产下载连接失败；原任务保留，可稍后重试下载。"); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
