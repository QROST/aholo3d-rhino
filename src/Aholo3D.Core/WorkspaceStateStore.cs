using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Aholo3D.Core;

/// <summary>Local UI state only. Never carries credentials, task authority or automatic-placement intent.</summary>
public sealed record WorkspaceState
{
    public int Version { get; init; }=1;
    public string Revision { get; init; }="";
    public bool RememberDrafts { get; init; }
    public WorkspacePage Page { get; init; }
    public string Operation { get; init; }="world.generate";
    public Dictionary<string,Dictionary<string,string[]>> Drafts { get; init; }=new();
    public Dictionary<string,string> Preferences { get; init; }=new();
}

public sealed class WorkspaceStateStore
{
    // Bounds only UI state, never geometry or service generation limits.
    private const int MaximumBytes=2*1024*1024;
    private readonly string root;
    public WorkspaceStateStore(string dataRoot)=>root=Path.Combine(Path.GetFullPath(dataRoot),"ui","workspaces");
    public static string? DocumentKey(string? documentPath)
    {
        if(string.IsNullOrWhiteSpace(documentPath))return null;
        string path=Path.GetFullPath(documentPath);
        if(OperatingSystem.IsWindows())path=path.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path))).ToLowerInvariant();
    }
    private string StatePath(string key)
    {
        if(key.Length!=64||key.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Invalid workspace identity.");
        return Path.Combine(root,key+".json");
    }
    public WorkspaceState? Read(string key)
    {
        string path=StatePath(key);
        try
        {
            using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
            if(stream.Length>MaximumBytes)throw new IOException("Saved workspace is too large. The original file was retained.");
            var state=JsonSerializer.Deserialize<WorkspaceState>(stream,Aholo3DJson.Options);
            if(state is null||state.Version!=1||!Guid.TryParseExact(state.Revision,"N",out _))
                throw new IOException("Saved workspace is unsupported or damaged. The original file was retained.");
            return Sanitize(state);
        }
        catch(FileNotFoundException){return null;}
        catch(DirectoryNotFoundException){return null;}
        catch(Exception ex) when(ex is JsonException or ArgumentException or NullReferenceException)
        {throw new IOException("Saved workspace is unsupported or damaged. The original file was retained.",ex);}
    }
    public WorkspaceState Save(string key,string? expectedRevision,WorkspaceState state)
    {
        string path=StatePath(key);Directory.CreateDirectory(root);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        using var lease=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var current=Read(key);
        if(current?.Revision!=expectedRevision)throw new IOException("Workspace changed in another window or Rhino session. Reopen the document before saving it again.");
        var safe=Sanitize(state) with{Revision=Guid.NewGuid().ToString("N")};
        var bytes=JsonSerializer.SerializeToUtf8Bytes(safe,Aholo3DJson.Options);
        if(bytes.Length>MaximumBytes)throw new IOException("Workspace text is too large to save. The previous saved draft was retained.");
        TaskStore.WriteAtomic(path,bytes);return safe;
    }
    public static WorkspaceState Sanitize(WorkspaceState state)
    {
        if(state.Version!=1)throw new ArgumentException("Unsupported workspace version.");
        var drafts=new Dictionary<string,Dictionary<string,string[]>>();
        if(state.RememberDrafts)
            foreach(var pair in state.Drafts)
            {
                if(!ServiceOperations.All.Any(x=>x.Id==pair.Key))continue;
                var allowed=ServiceOperations.RequestSchema(pair.Key).GetProperty("properties").EnumerateObject()
                    .Where(x=>!CreationDraftValidation.IsFileField(x.Name)&&x.Name!="name").Select(x=>x.Name).ToHashSet(StringComparer.Ordinal);
                var fields=new Dictionary<string,string[]>();
                foreach(var field in pair.Value)
                {
                    var parts=field.Key.Split(':',2);
                    if(field.Key is "$name" or "$advanced" || parts.Length==2&&allowed.Contains(parts[0])&&parts[1] is "text" or "choice" or "selected" or "matrix" or "enabled")
                        fields[field.Key]=field.Value.Select(value=>value??"").ToArray();
                }
                drafts[pair.Key]=fields;
            }
        var prefs=new Dictionary<string,string>();
        foreach(var pair in state.Preferences)
        {
            bool valid=pair.Key switch
            {
                "scale" or "lightRadius" or "lightMultiplier"=>double.TryParse(pair.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)&&double.IsFinite(value)&&value>0,
                "axis"=>pair.Value is "0" or "1" or "2",
                "sourceUnits" or "resolution"=>pair.Value is "0" or "1",
                "renderTextures" or "renderLights"=>pair.Value is "true" or "false",
                _=>false
            };
            if(valid)prefs[pair.Key]=pair.Value;
        }
        return state with
        {
            Page=Enum.IsDefined(typeof(WorkspacePage),state.Page)?state.Page:WorkspacePage.Create,
            Operation=ServiceOperations.All.Any(x=>x.Id==state.Operation)?state.Operation:"world.generate",
            Drafts=drafts,Preferences=prefs
        };
    }
}

/// <summary>Shared by successive panel owners. Revision checks prevent stale sessions overwriting saved drafts.</summary>
public sealed class WorkspaceStateHandle
{
    private readonly WorkspaceStateStore store;
    private string? key,revision;
    public WorkspaceState State { get; private set; }=new();
    public string? Error { get; private set; }
    public bool CanPersist=>key is not null&&Error is null;
    public WorkspaceStateHandle(WorkspaceStateStore store,string? path)
    {
        this.store=store;key=WorkspaceStateStore.DocumentKey(path);
        try{if(key is not null){State=store.Read(key)??new();revision=State.Revision.Length>0?State.Revision:null;}}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){Error=ex.Message;}
    }
    public void Save(string? path,WorkspaceState state)
    {
        State=WorkspaceStateStore.Sanitize(state);
        string? next=WorkspaceStateStore.DocumentKey(path);
        if(next is null)return; // Untitled documents never share a disk bucket.
        if(next!=key){key=next;revision=null;Error=null;} // Save As may create a new bucket, never overwrite an existing one.
        try{State=store.Save(key!,revision,State);revision=State.Revision;Error=null;}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){Error=ex.Message;throw;}
    }
}
