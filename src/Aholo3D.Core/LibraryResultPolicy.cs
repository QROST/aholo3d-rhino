namespace Aholo3D.Core;

public enum LibraryResultKind { Mesh, Gaussian, Spatial, Image, DownloadOnly }
public sealed record LibraryResultAccess(bool IsCached,bool CanDownload,bool CanPlace,bool CanPreview,bool CanOpenImage,bool CanOpenFolder,string Status,string NextStep);
public sealed record LibraryResultRow(TaskArtifact Artifact,bool IsCached)
{
    public override string ToString()=>LibraryResultPolicy.Label(Artifact,IsCached);
}

/// <summary>Library presentation only. Format recognition is not validation or permission to create a task.</summary>
public static class LibraryResultPolicy
{
    public static TaskArtifact[] Outputs(LocalTask task)=>task.Kind switch
    {
        "world"=>new[]{new TaskArtifact("ply","Gaussian scene","ply",LocalPath:task.PlyPath),new TaskArtifact("spz","Compressed Gaussian scene","spz",LocalPath:task.SpzPath),new TaskArtifact("image","Panorama","image",LocalPath:task.ImagePath)}.Concat(task.Artifacts).ToArray(),
        "render"=>new[]{new TaskArtifact("image","Rendered image","image",LocalPath:task.ImagePath)},_=>task.Artifacts
    };
    public static bool Cached(TaskArtifact artifact,Func<string,bool> exists)=>artifact.LocalPath is {Length:>0} path&&exists(path);
    public static string Label(TaskArtifact artifact,bool cached,string? language=null)=>UiText.TranslateKnownMessage(artifact.Label,language)+" · "+artifact.Format.ToUpperInvariant()+
        (cached?" · "+UiText.Get("Saved locally",language):artifact.LocalPath is not null?" · "+UiText.Get("Cached file missing",language):"")+
        (artifact.Error is null?"":" · "+UiText.Get("Unavailable",language));
    public static LibraryResultKind Kind(LocalTask task,TaskArtifact artifact)=>
        MeshPlacementPolicy.CanPlaceMesh(task,artifact)?LibraryResultKind.Mesh:
        task.Kind=="world"&&artifact.Format is "ply" or "spz"?LibraryResultKind.Gaussian:
        task.Kind=="spatial"&&artifact.Format=="dsl"?LibraryResultKind.Spatial:
        artifact.Format=="image"?LibraryResultKind.Image:LibraryResultKind.DownloadOnly;
    internal static bool Unique(IReadOnlyList<TaskArtifact> rows,string key)=>!string.IsNullOrEmpty(key)&&rows.Count(a=>a.Key==key)==1;
    public static int DefaultIndex(LocalTask task,IReadOnlyList<TaskArtifact> rows,Func<string,bool> exists)
    {
        var keys=rows.ToLookup(a=>a.Key,StringComparer.Ordinal);
        int best=-1;var rank=(int.MaxValue,int.MaxValue,int.MaxValue);
        for(int i=0;i<rows.Count;i++)
        {
            var row=rows[i];var kind=Kind(task,row);
            int use=kind is LibraryResultKind.Mesh or LibraryResultKind.Gaussian or LibraryResultKind.Spatial?0:kind==LibraryResultKind.Image?1:2;
            int format=kind==LibraryResultKind.Mesh?row.Format switch{"glb"=>0,"ply"=>1,"stl"=>2,"3mf"=>3,"obj"=>4,"obj_zip"=>5,_=>6}:kind==LibraryResultKind.Gaussian&&row.Format=="spz"?1:0;
            var candidate=(row.Error is null&&!string.IsNullOrEmpty(row.Key)&&keys[row.Key].Count()==1?use:3,Cached(row,exists)?0:1,format);
            if(candidate.CompareTo(rank)<0){rank=candidate;best=i;}
        }
        return best;
    }
    public static LibraryResultAccess Access(LocalTask? task,IReadOnlyList<TaskArtifact> rows,TaskArtifact? artifact,ConnectionState connection,Func<string,bool> exists,string? language=null)
    {
        string T(string value)=>UiText.Get(value,language);
        LibraryResultAccess Disabled(string status)=>new(false,false,false,false,false,false,status,"");
        if(task is null||artifact is null||!rows.Contains(artifact))return Disabled(T("No result selected."));
        if(task.SchemaVersion!=LocalTask.CurrentSchemaVersion||task.Status is "RecordError" or "UnsupportedSchema")return Disabled(T("This task record cannot be used by this version."));
        if(!Unique(rows,artifact.Key))return Disabled(T("This result has an ambiguous identity. Refresh the original task before using it."));
        if(artifact.Error is not null)return Disabled(UiText.TranslateKnownMessage(artifact.Error,language));
        bool cached=Cached(artifact,exists),download=connection.Connected&&task.Region==connection.Region&&task.ConnectionId==connection.ConnectionId&&TaskCapabilities.For(task).CanDownload;
        var kind=Kind(task,artifact);
        // Mesh placement first resolves the persisted receipt, even offline without the cached source.
        bool place=kind==LibraryResultKind.Mesh||cached&&kind is LibraryResultKind.Gaussian or LibraryResultKind.Spatial;
        string state=T(cached?"Saved locally":artifact.LocalPath is not null?"Cached file missing":"Not downloaded.");
        string next=kind switch
        {
            LibraryResultKind.Mesh=>cached?"Preview checks the static mesh before placement. Place can also locate an earlier placement.":download?"Preview or Place downloads this result from the original task; it does not generate a new model.":"Place can check for an earlier placement. A new placement needs the file or access to the completed task in its original account and region.",
            LibraryResultKind.Gaussian or LibraryResultKind.Spatial=>cached?"Use Place in current document. A downloaded file is not automatically placed.":download?"Download this result, then use Place in current document.":"To download this result, wait for task completion and connect to its original account and region.",
            LibraryResultKind.Image=>cached?"Use Open image to view this result.":download?"Download this result, then use Open image.":"To download this result, wait for task completion and connect to its original account and region.",
            _=>cached?"Use Open result folder. This format cannot be placed directly.":download?"Download only: this format cannot be placed directly. Choose a supported model result to preview or place.":"This format cannot be placed directly. Download requires a completed task and its original account and region."
        };
        return new(cached,download,place,kind==LibraryResultKind.Mesh&&(cached||download),cached&&kind==LibraryResultKind.Image,cached,state,T(next));
    }
}

/// <summary>Panel-local explicit choices, separated by full task identity. Automatic defaults are never remembered.</summary>
public sealed class LibraryResultSelection
{
    private readonly Dictionary<(TaskIdentity Identity,string? Operation),(string Key,string Format)> choices=new();
    private static (TaskIdentity,string?) Identity(LocalTask task)=>(TaskIdentity.From(task),task.ServiceOperation);
    public int Choose(LocalTask task,IReadOnlyList<TaskArtifact> rows,Func<string,bool> exists)
    {
        var identity=Identity(task);
        if(choices.TryGetValue(identity,out var choice))
        {
            if(LibraryResultPolicy.Unique(rows,choice.Key))
                for(int i=0;i<rows.Count;i++)if(rows[i].Key==choice.Key&&rows[i].Format==choice.Format)return i;
            choices.Remove(identity);
        }
        return LibraryResultPolicy.DefaultIndex(task,rows,exists);
    }
    public void Remember(LocalTask task,IReadOnlyList<TaskArtifact> rows,int index)
    {
        if(index<0||index>=rows.Count||!LibraryResultPolicy.Unique(rows,rows[index].Key))return;
        choices[Identity(task)]=(rows[index].Key,rows[index].Format);
    }
}
