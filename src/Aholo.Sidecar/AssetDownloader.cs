using Aholo.Core;
using System.Security.Cryptography;
namespace Aholo.Sidecar;

public sealed class AssetDownloader(HttpClient http, TaskStore store)
{
    public async Task<string> DownloadAsync(Guid id, string url, string kind, CancellationToken ct, string? slot=null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new IOException("The service returned an unsafe download address.");
        if(slot is not null&&(slot.Length!=16||slot.Any(c=>!Uri.IsHexDigit(c))))throw new ArgumentException("Invalid result slot.");
        if (kind is not ("ply" or "spz" or "image" or "glb" or "zip" or "usdz" or "obj_zip" or "fbx_zip" or "stl" or "3mf" or "fbx" or "dsl" or "metadata")) throw new ArgumentException("Invalid download type.");
        string temporary = Path.Combine(store.Root, "assets", id.ToString("N") + "." + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw new IOException(string.Format(System.Globalization.CultureInfo.CurrentCulture, "Asset download failed (HTTP {0}); refresh the task and retry the download.", (int)response.StatusCode));
            await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                await source.CopyToAsync(output, ct).ConfigureAwait(false); await output.FlushAsync(ct).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is long expected && output.Length != expected) throw new IOException("Asset download is incomplete.");
            }
            string extension = kind is "obj_zip" or "fbx_zip" ? "zip" : kind;
            using (var file = File.OpenRead(temporary))
            {
                byte[] head = new byte[12]; int count = file.Read(head);
                if (count == 0) throw new IOException("The asset file is empty.");
                if (kind == "ply" && (count < 4 || head[0] != 112 || head[1] != 108 || head[2] != 121 || head[3] is not (10 or 13))) throw new IOException("The returned asset is not a PLY file.");
                if(kind=="glb" && (count<12||!head.AsSpan(0,4).SequenceEqual("glTF"u8)||System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4))!=2||System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(8))!=file.Length))throw new IOException("The result is not a complete GLB file.");
                if(kind is "zip" or "usdz" or "obj_zip" or "fbx_zip" or "3mf")
                    if(count<4||head[0]!=80||head[1]!=75)throw new IOException("The result is not a ZIP container.");
                if (kind == "image") extension = count >= 8 && head.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) ? "png" :
                    count >= 3 && head[0] == 255 && head[1] == 216 && head[2] == 255 ? "jpg" :
                    count >= 12 && head.AsSpan(0,4).SequenceEqual("RIFF"u8) && head.AsSpan(8,4).SequenceEqual("WEBP"u8) ? "webp" : throw new IOException("The render result is not a supported image format.");
            }
            string hash;
            using(var completed=File.OpenRead(temporary))hash=Convert.ToHexString(await SHA256.HashDataAsync(completed,ct).ConfigureAwait(false)).ToLowerInvariant();
            string target = Path.Combine(store.Root, "assets", id.ToString("N") + (slot is null ? "" : "-"+slot) + "-" + hash + "." + extension);
            ct.ThrowIfCancellationRequested();
            try{File.Move(temporary,target,false);}
            catch(IOException)when(File.Exists(target))
            {
                using var existing=File.OpenRead(target);
                if(!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(existing,ct).ConfigureAwait(false)),hash,StringComparison.OrdinalIgnoreCase))throw new IOException("Existing local artifact is corrupt; its bytes were retained.");
            }
            return target;
        }
        catch (HttpRequestException) { throw new IOException("Asset download connection failed. The original task is retained; retry the download later."); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
