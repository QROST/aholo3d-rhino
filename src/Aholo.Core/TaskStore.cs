using System.Text.Json;
namespace Aholo.Core;

/// <summary>Atomic local journal. API keys and expiring signed URLs are never persisted here.</summary>
public sealed class TaskStore
{
    public string Root { get; }
    public static string DefaultRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aholo-Rhino");
    public TaskStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (string name in new[] { "tasks", "assets", "inputs", "exports" }) Directory.CreateDirectory(Path.Combine(Root, name));
    }
    private string TaskPath(Guid id) => Path.Combine(Root, "tasks", id.ToString("N") + ".json");
    public LocalTask Get(Guid id)
    {
        using var stream = new FileStream(TaskPath(id), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var record = JsonSerializer.Deserialize<LocalTask>(stream, AholoJson.Options) ?? throw new IOException("任务记录损坏。");
        if (record.Id != id) throw new IOException("任务记录标识不匹配。");
        return Recover(record);
    }
    public IReadOnlyList<LocalTask> List() => Directory.EnumerateFiles(Path.Combine(Root, "tasks"), "*.json")
        .Select(p => Guid.TryParseExact(Path.GetFileNameWithoutExtension(p), "N", out var id) ? TryGet(id) : null)
        .Where(t => t != null).Cast<LocalTask>().OrderByDescending(t => t.CreatedAt).Take(100).ToArray();
    private LocalTask? TryGet(Guid id) { try { return Get(id); } catch (Exception e) when (e is IOException or JsonException) { return new LocalTask { Id = id, Name = "无法读取的任务记录", Status = "RecordError", Error = "本地记录无法读取，文件已保留。请核对云端历史，避免重复提交。" }; } }
    private static LocalTask Recover(LocalTask t) => t.Status switch
    {
        "Submitting" or "RenderSubmitting" or "MeshSubmitting" => t with { Status = "SubmissionUnknown", Error = "提交期间中断，结果未知。请核对 Aholo 历史；不会自动重复提交。" },
        "Preparing" or "Uploading" => t with { Status = "Interrupted", Error = "输入准备中断，尚未提交生成或渲染。" },
        _ => t
    };
    public void Save(LocalTask task) => WriteAtomic(TaskPath(task.Id), JsonSerializer.SerializeToUtf8Bytes(task with
    {
        Name = DisplayText(task.Name, 512), Error = task.Error is null ? null : DisplayText(task.Error, 1024),
        Render = task.Render is null ? null : task.Render with { Name = DisplayText(task.Render.Name, 512) },
        PlyUrl = null, SpzUrl = null, ImageUrl = null
    }, AholoJson.Options));
    public static string DisplayText(string value, int maxLength = 512) => value.Length <= maxLength ? value : value[..maxLength] + "…";
    public FileStream LockMutation() => new(Path.Combine(Root, ".mutation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    public static void WriteAtomic(string destination, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                stream.Write(content); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string SnapshotInput(Guid id, string path, bool glb)
    {
        path = Path.GetFullPath(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (glb ? extension != ".glb" : extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) throw new ArgumentException("输入文件格式不支持。");
        string target = Path.Combine(Root, "inputs", id.ToString("N") + extension);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length == 0) throw new ArgumentException("输入文件为空。");
        Span<byte> prefix = stackalloc byte[12]; int read = source.Read(prefix);
        bool valid = glb ? read >= 12 && prefix[..4].SequenceEqual("glTF"u8) :
            extension is ".jpg" or ".jpeg" ? read >= 3 && prefix[0] == 255 && prefix[1] == 216 && prefix[2] == 255 :
            extension == ".png" ? read >= 8 && prefix[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) :
            read >= 12 && prefix[..4].SequenceEqual("RIFF"u8) && prefix[8..12].SequenceEqual("WEBP"u8);
        if (!valid) throw new ArgumentException("文件内容与扩展名不匹配。");
        source.Position = 0;
        using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        source.CopyTo(destination); destination.Flush(true); return target;
    }
}
