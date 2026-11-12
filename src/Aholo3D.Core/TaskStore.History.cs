using System.Text.Json;
namespace Aholo3D.Core;

public sealed partial class TaskStore
{
    private sealed record HistoryEntry(long Length,long Modified,LocalTask Task);
    private readonly object historyGate=new();
    private readonly Dictionary<Guid,HistoryEntry> historyCache=new();
    // Keep recovery-sensitive states out of the cache: their meaning depends on a live writer lease.
    private static bool StableHistoryRow(LocalTask task)=>task.Status is "SUCCEEDED" or "FAILED" or "CANCELED";
    // Copy all nested mutable arrays/dictionaries, including future journal fields, only for the returned page.
    private static LocalTask CloneHistoryRow(LocalTask task)=>JsonSerializer.Deserialize<LocalTask>(
        JsonSerializer.SerializeToUtf8Bytes(task,Aholo3DJson.Options),Aholo3DJson.Options)!;
    private static (long Length,long Modified)? HistoryStamp(string path)
    {
        try{var file=new FileInfo(path);return file.Exists?(file.Length,file.LastWriteTimeUtc.Ticks):null;}
        catch(IOException){return null;}
    }
    private LocalTask[] HistoryRows(CancellationToken ct,bool forceRefresh,Guid? authoritativeId=null)
    {
        ct.ThrowIfCancellationRequested();
        // Cancellation also works while another history scan owns the cache.
        while(!Monitor.TryEnter(historyGate,25))ct.ThrowIfCancellationRequested();
        try
        {
            ct.ThrowIfCancellationRequested();
            var rows=new List<LocalTask>();var seen=new HashSet<Guid>();
            foreach(string path in Directory.EnumerateFiles(Path.Combine(Root,"tasks"),"*.json"))
            {
                ct.ThrowIfCancellationRequested();
                if(!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path),"N",out var id))continue;
                seen.Add(id);var before=HistoryStamp(path);
                if(!forceRefresh&&authoritativeId!=id&&before is{} stamp&&historyCache.TryGetValue(id,out var cached)&&
                    cached.Length==stamp.Length&&cached.Modified==stamp.Modified){rows.Add(cached.Task);continue;}
                var task=TryGet(id)!;rows.Add(task);
                var after=HistoryStamp(path);
                if(before is{} stable&&before==after&&StableHistoryRow(task))
                    historyCache[id]=new(stable.Length,stable.Modified,task);
                else historyCache.Remove(id);
            }
            foreach(var id in historyCache.Keys.Where(id=>!seen.Contains(id)).ToArray())historyCache.Remove(id);
            return rows.ToArray();
        }
        finally{Monitor.Exit(historyGate);}
    }
}
