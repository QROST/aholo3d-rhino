using System.Text.Json;
using Aholo.Core;
namespace Aholo.Sidecar;

/// <summary>Non-secret cleanup obligations survive process loss. A file lease identifies the live owner.</summary>
public sealed class RealtimeJournal
{
    private readonly string root;
    public RealtimeJournal(string dataRoot)
    {
        root=Path.Combine(dataRoot,"realtime");
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(root);
        else Directory.CreateDirectory(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
    }
    private string PathFor(Guid id){if(id==Guid.Empty)throw new ArgumentException("A preview identity is required.");return Path.Combine(root,id.ToString("N")+".json");}
    public FileStream LockAdmission()=>new(Path.Combine(root,"start.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    public FileStream Lock(Guid id)=>new(PathFor(id)+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
    public bool Exists(Guid id)=>File.Exists(PathFor(id));
    public RealtimePreviewRecord Read(Guid id)
    {
        using var input=new FileStream(PathFor(id),FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        var record=JsonSerializer.Deserialize<RealtimePreviewRecord>(input,AholoJson.Options)??throw new IOException("Invalid realtime journal.");
        if(record.Version!=1||record.Id!=id)throw new IOException("Unsupported realtime journal identity or version.");
        return record;
    }
    public void Save(RealtimePreviewRecord record)=>TaskStore.WriteAtomic(PathFor(record.Id),JsonSerializer.SerializeToUtf8Bytes(record,AholoJson.Options));
    public RealtimePreviewRecord[] Pending()=>Directory.EnumerateFiles(root,"*.json").Select(p=>Guid.TryParseExact(Path.GetFileNameWithoutExtension(p),"N",out var id)?Read(id):null)
        .Where(r=>r?.NeedsCleanup==true).Cast<RealtimePreviewRecord>().OrderByDescending(r=>r.UpdatedAt).ToArray();
}
