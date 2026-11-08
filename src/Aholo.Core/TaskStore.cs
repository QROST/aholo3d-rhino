using System.Text.Json;
using System.Security.Cryptography;
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
        FileStream lease;
        try { lease = LockTask(id); }
        catch (IOException) { return Read(id, recoverInterrupted: false); }
        // Keep the probe lock through deserialization: a writer must not enter between
        // the ownership check and the decision that a submission was interrupted.
        using (lease) return Read(id, recoverInterrupted: true);
    }
    // A newly acquired exclusive task lock proves the previous owner no longer holds it.
    // The caller must keep that lock until the updated journal has been published.
    public LocalTask GetForUpdate(Guid id) => Read(id, recoverInterrupted: true);
    private LocalTask Read(Guid id, bool recoverInterrupted)
    {
        using var stream = new FileStream(TaskPath(id), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var record = JsonSerializer.Deserialize<LocalTask>(stream, AholoJson.Options) ?? throw new IOException("The task record is corrupt.");
        if (record.Id != id) throw new IOException("The task record identity does not match.");
        if (record.SchemaVersion != LocalTask.CurrentSchemaVersion)
            return record with { Status = "UnsupportedSchema", Error = "This task uses a different journal version. Its file is retained; use a compatible plugin to continue." };
        return recoverInterrupted ? Recover(record) : record;
    }
    private IEnumerable<LocalTask> All() => Directory.EnumerateFiles(Path.Combine(Root, "tasks"), "*.json")
        .Select(p => Guid.TryParseExact(Path.GetFileNameWithoutExtension(p), "N", out var id) ? TryGet(id) : null)
        .Where(t => t != null).Cast<LocalTask>();
    public IReadOnlyList<LocalTask> ActiveTasks(string connectionId) => All().Where(t=>t.ConnectionId==connectionId&&(TaskCapabilities.For(t).IsRunning||t.Status=="MeshReady")).ToArray();
    public IReadOnlyList<LocalTask> List() => Page(new TaskPageRequest(Limit: 100)).Items;
    public TaskPage Page(TaskPageRequest request)
    {
        if (request.Offset < 0 || request.Limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request));
        var rows = All().Where(t => (request.Kind is null || t.Kind == request.Kind) &&
            (string.IsNullOrWhiteSpace(request.Search) || t.Name.Contains(request.Search, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id).ToArray();
        var items = rows.Skip(request.Offset).Take(request.Limit).ToArray();
        return new(items, request.Offset, rows.Length, (long)request.Offset + items.Length < rows.Length);
    }
    public LocalTask? FindWorld(string worldId, string connectionId) => All().FirstOrDefault(t =>
        t.Kind == "world" && t.WorldId == worldId && t.ConnectionId == connectionId);
    private LocalTask? TryGet(Guid id) { try { return Get(id); } catch (Exception e) when (e is IOException or JsonException) { return new LocalTask { Id = id, Name = "Unreadable task record", Status = "RecordError", Error = "The local record cannot be read; the file is retained. Check cloud history to avoid duplicate submissions." }; } }
    public FileStream LockTask(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("A task identity is required.", nameof(id));
        string directory = Path.Combine(Root, "locks"); Directory.CreateDirectory(directory);
        return new FileStream(Path.Combine(directory, id.ToString("N") + ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public bool IsTaskActive(Guid id)
    {
        try { using var lease = LockTask(id); return false; }
        catch (IOException) { return true; }
    }
    private static LocalTask Recover(LocalTask t) => t.Status switch
    {
        "Submitting" or "RenderSubmitting" or "MeshSubmitting" => t with { Status = "SubmissionUnknown", Error = "Submission was interrupted and the result is unknown. Check Aholo history; no automatic resubmission will occur." },
        "Preparing" or "Uploading" => t with { Status = "Interrupted", Error = "Input preparation was interrupted. No generation or rendering was submitted." },
        _ => t
    };
    public void Save(LocalTask task)
    {
        if (task.SchemaVersion != LocalTask.CurrentSchemaVersion) throw new IOException("Unsupported task journal version; the original file is retained.");
        string path = TaskPath(task.Id);
        if (File.Exists(path))
        {
            byte[] original = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(original);
            if (document.RootElement.TryGetProperty("schemaVersion", out var version))
            {
                if (!version.TryGetInt32(out int value) || value != LocalTask.CurrentSchemaVersion)
                    throw new IOException("Unsupported task journal version; the original file is retained.");
            }
            else
            {
                string backup = Path.Combine(Root, "tasks", "backups", task.Id.ToString("N") + "." + Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant() + ".json");
                if (!File.Exists(backup)) WriteAtomic(backup, original);
            }
        }
        WriteAtomic(path, JsonSerializer.SerializeToUtf8Bytes(task with
        {
            Name = DisplayText(task.Name, 512), Error = task.Error is null ? null : DisplayText(task.Error, 1024),
            Render = task.Render is null ? null : task.Render with { Name = DisplayText(task.Render.Name, 512) },
            PlyUrl = null, SpzUrl = null, ImageUrl = null
        }, AholoJson.Options));
    }
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
        if (glb ? extension != ".glb" : extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) throw new ArgumentException("Unsupported input file format.");
        string target = Path.Combine(Root, "inputs", id.ToString("N") + extension);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (source.Length == 0) throw new ArgumentException("The input file is empty.");
        Span<byte> prefix = stackalloc byte[12]; int read = source.Read(prefix);
        bool valid = glb ? read >= 12 && prefix[..4].SequenceEqual("glTF"u8) :
            extension is ".jpg" or ".jpeg" ? read >= 3 && prefix[0] == 255 && prefix[1] == 216 && prefix[2] == 255 :
            extension == ".png" ? read >= 8 && prefix[..8].SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}) :
            read >= 12 && prefix[..4].SequenceEqual("RIFF"u8) && prefix[8..12].SequenceEqual("WEBP"u8);
        if (!valid) throw new ArgumentException("File contents do not match the extension.");
        source.Position = 0;
        using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        source.CopyTo(destination); destination.Flush(true); return target;
    }
}
