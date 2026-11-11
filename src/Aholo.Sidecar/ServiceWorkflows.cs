using System.Security.Cryptography;
using Aholo.Core;
using Aholo.Provider;
namespace Aholo.Sidecar;

public sealed partial class Workflows
{
    public async Task<LocalTask> CreateServiceAsync(ApiKeyCredential key,CreateServiceRequest request,CancellationToken ct)
    {
        var contract=ServiceOperations.Get(request.Operation);
        if(request.Operation=="spatial.understand"&&!(request.Files?.Any(f=>f.Field=="inputFileUrl")==true))throw new ArgumentException("Choose a local indoor PLY so upload provenance can be verified.");
        _=ServiceRequest.Bind(request,ServiceRequest.Placeholder); // Complete validation before upload or paid submission.
        var task=new LocalTask{Kind=contract.Service switch{TaskService.World=>"world",TaskService.Lux3D=>"object",_=>"spatial"},ServiceOperation=contract.Id,Name=Name(request.Name,contract.Title),Region=key.Region,ConnectionId=key.ConnectionId};
        using var gate=store.LockTask(task.Id);store.Save(task);
        try
        {
            var snapshots=new Dictionary<(string,int),string>();
            foreach(var input in request.Files??Array.Empty<ServiceFileInput>())for(int i=0;i<input.Paths.Length;i++)
            { ct.ThrowIfCancellationRequested();snapshots[(input.Field,i)]=SnapshotServiceInput(task.Id,input.Paths[i],ct); }
            var urls=new Dictionary<(string,int),string>();
            foreach(var entry in snapshots)
            {task=Save(task with{Status="Uploading"});urls[entry.Key]=await api.UploadAsync(key,entry.Value,request.Upload,ct).ConfigureAwait(false);}
            var body=ServiceRequest.Bind(request,(field,i)=>urls[(field.Field,i)]);
            ct.ThrowIfCancellationRequested();task=Save(task with{Status="Submitting"});
            var remote=await api.CreateServiceTaskAsync(key,request.Operation,body,ct).ConfigureAwait(false);
            return Save(task with{Status="PENDING",WorldId=remote.Service==TaskService.World?remote.Id:null,LuxTaskId=remote.Service==TaskService.Lux3D?remote.Id:null,SpatialTaskId=remote.Service==TaskService.SpatialUnderstanding?remote.Id:null});
        }
        catch(Exception ex)when(Expected(ex)){return Save(Failure(task,ex));}
    }
    private string SnapshotServiceInput(Guid id,string path,CancellationToken ct)
    {
        string extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension is not (".png" or ".jpg" or ".jpeg" or ".webp" or ".glb" or ".zip" or ".ply" or ".mp4" or ".mov" or ".insv"))throw new ArgumentException("Unsupported input file format.");
        string directory=Path.Combine(store.Root,"inputs",id.ToString("N"));Directory.CreateDirectory(directory);
        string snapshot=Path.Combine(directory,Guid.NewGuid().ToString("N")+extension);
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        if(input.Length==0)throw new ArgumentException("The input file is empty.");
        try
        {
            using(var output=new FileStream(snapshot,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                if(!OperatingSystem.IsWindows())File.SetUnixFileMode(snapshot,UnixFileMode.UserRead|UnixFileMode.UserWrite);
                byte[] buffer=new byte[81920];int read;while((read=input.Read(buffer))>0){ct.ThrowIfCancellationRequested();output.Write(buffer,0,read);}output.Flush(true);
            }
            using(var check=File.OpenRead(snapshot))
            {
                byte[] head=new byte[12];int n=check.Read(head);
                bool valid=extension switch
                {
                    ".png"=>n>=8&&head.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),
                    ".jpg" or ".jpeg"=>n>=3&&head[0]==255&&head[1]==216&&head[2]==255,
                    ".webp"=>n>=12&&head.AsSpan(0,4).SequenceEqual("RIFF"u8)&&head.AsSpan(8,4).SequenceEqual("WEBP"u8),
                    ".ply"=>n>=4&&head.AsSpan(0,3).SequenceEqual("ply"u8)&&head[3] is 10 or 13,
                    ".zip"=>n>=4&&head[0]==80&&head[1]==75,
                    _=>true
                };
                if(!valid)throw new ArgumentException("The input file does not match its selected format.");
            }
            if(extension==".glb")GlbEnvelope.Validate(snapshot);
            return snapshot;
        }
        catch{File.Delete(snapshot);throw;}
    }
    private async Task<LocalTask> RefreshServiceAsync(ApiKeyCredential key,LocalTask task,CancellationToken ct)
    {
        if(task.Kind=="object"&&task.LuxTaskId is not null)
        {
            var result=await api.GetLuxTaskAsync(key,task.LuxTaskId,ct).ConfigureAwait(false);
            var artifacts=LuxArtifacts.Decode(task.ServiceOperation??"",result.Outputs);
            return Save(task with{Status=result.Status switch{0=>"PENDING",1=>"RUNNING",3=>"SUCCEEDED",4=>"FAILED",6=>"CANCELED",_=>"UnknownRemoteStatus"},LastQueriedAt=DateTimeOffset.UtcNow,Artifacts=MergeArtifacts(task,artifacts),Error=result.Status==4?"The original service task failed. Check the service console for details.":null});
        }
        if(task.Kind=="spatial"&&task.SpatialTaskId is not null)
        {
            var result=await api.GetSpatialTaskAsync(key,task.SpatialTaskId,ct).ConfigureAwait(false);
            return Save(task with{Status=result.Status,LastQueriedAt=DateTimeOffset.UtcNow,Artifacts=MergeArtifacts(task,result.ResultUrl is null?Array.Empty<TaskArtifact>():new[]{new TaskArtifact("result","Spatial scene","dsl",result.ResultUrl)}),Error=result.Status is "FAILED" or "TIMEOUT"?"Spatial understanding did not finish successfully.":null});
        }
        return task;
    }
    private static TaskArtifact[] MergeArtifacts(LocalTask task,IReadOnlyList<TaskArtifact> results)
    {
        var merged=results.Select(a=>{var old=task.Artifacts.FirstOrDefault(p=>p.Key==a.Key&&p.Format==a.Format);return a with{LocalPath=old?.LocalPath,Sha256=old?.Sha256};}).ToList();
        // Omitted outputs do not revoke already downloaded, immutable local files.
        foreach(var old in task.Artifacts.Where(a=>a.LocalPath is not null&&!merged.Any(b=>b.Key==a.Key)))merged.Add(old with{Url=null});
        return merged.ToArray();
    }
    private async Task<LocalTask> DownloadArtifactAsync(ApiKeyCredential key,Guid id,string artifactKey,CancellationToken ct)
    {
        using var gate=store.LockTask(id);var task=await RefreshCoreAsync(key,store.GetForUpdate(id),ct).ConfigureAwait(false);
        if(task.Status!="SUCCEEDED")throw new InvalidOperationException("The task has not succeeded yet; no result is available to download.");
        var artifact=task.Artifacts.SingleOrDefault(a=>a.Key==artifactKey)??throw new ArgumentException("Choose an available task result.");
        if(artifact.Url is null||artifact.Error is not null)throw new InvalidOperationException("This output is unavailable. Query the original task again.");
        // The local filename contains a hash of the slot, never provider-controlled labels or paths.
        string suffix=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(artifact.Key)))[..16];
        string path=await downloader.DownloadAsync(id,artifact.Url,artifact.Format,ct,suffix).ConfigureAwait(false);
        using var file=File.OpenRead(path);string hash=Convert.ToHexString(SHA256.HashData(file));
        return Save(task with{Artifacts=task.Artifacts.Select(a=>a.Key==artifactKey?a with{LocalPath=path,Sha256=hash}:a).ToArray()});
    }
}
