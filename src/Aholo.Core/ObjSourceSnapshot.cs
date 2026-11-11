using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Aholo.Core;

/// <summary>Private immutable copies of an OBJ and its explicitly requested local dependencies.</summary>
public sealed class ObjSourceSnapshot : IDisposable
{
    private readonly string workspace;
    private readonly string? localRoot;
    private readonly FileStream? archiveFile;
    private readonly ZipArchive? archive;
    private readonly Dictionary<string,ZipArchiveEntry> entries=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,ObjSourceFile> captured=new(StringComparer.OrdinalIgnoreCase);
    private bool disposed;
    private string? entryName;
    public string EntryName => entryName ?? throw new InvalidOperationException(UiText.Get("Select an OBJ entry first."));
    public IReadOnlyList<string> CandidateEntries { get; private set; } = Array.Empty<string>();
    public string SourceSha256 { get; }
    public IReadOnlyList<string> DependencyHashes => captured.Values.Select(x=>x.Sha256).Append(SourceSha256).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
    public bool IsArchive => archive is not null;

    private ObjSourceSnapshot(string path,string? expectedHash,string? selectedEntry,bool deferSelection,CancellationToken ct)
    {
        string source=Path.GetFullPath(path),extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension is not (".obj" or ".zip"))throw new InvalidDataException(UiText.Get("Choose an OBJ file or an OBJ ZIP package."));
        workspace=Path.Combine(Path.GetTempPath(),"Aholo-Rhino","obj-"+Guid.NewGuid().ToString("N"));
        try
        {
            if(OperatingSystem.IsWindows())Directory.CreateDirectory(workspace);
            else Directory.CreateDirectory(workspace,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            {
                var root=Copy(input,"root"+extension,null,ct);SourceSha256=root.Sha256;
                if(expectedHash is not null&&!SourceSha256.Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(UiText.Get("The mesh hash does not match the task record."));
                if(extension==".obj")
                {
                    if(selectedEntry is not null)throw new ArgumentException(UiText.Get("An entry name applies only to an OBJ ZIP package."));
                    localRoot=Path.GetDirectoryName(source)!;entryName=Normalize(Path.GetFileName(source),false);CandidateEntries=Array.AsReadOnly(new[]{entryName});
                    captured.Add(EntryName,root with{Name=EntryName});return;
                }
            }
            archiveFile=new FileStream(Path.Combine(workspace,"root.zip"),FileMode.Open,FileAccess.Read,FileShare.Read);
            archive=new ZipArchive(archiveFile,ZipArchiveMode.Read,false,Encoding.UTF8);
            var allNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                int kind=(item.ExternalAttributes>>16)&0xF000;
                bool directory=item.FullName.EndsWith('/');
                if(kind is not (0 or 0x8000 or 0x4000)||kind==0x4000&&!directory||directory&&(item.Length!=0||kind==0x8000))
                    throw new InvalidDataException(UiText.Get("OBJ package contains a link, special file or inconsistent directory."));
                string name=Normalize(directory?item.FullName[..^1]:item.FullName,false);
                if(!allNames.Add(name))throw new InvalidDataException(UiText.Get("OBJ package contains ambiguous duplicate names."));
                if(!directory)entries.Add(name,item);
            }
            foreach(string name in allNames)
            {
                ct.ThrowIfCancellationRequested();int slash=name.IndexOf('/');
                while(slash>=0){ct.ThrowIfCancellationRequested();if(entries.ContainsKey(name[..slash]))throw new InvalidDataException(UiText.Get("OBJ package contains a file/directory collision."));slash=name.IndexOf('/',slash+1);}
            }
            string[] candidates=entries.Keys.Where(x=>Path.GetExtension(x).Equals(".obj",StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
            CandidateEntries=Array.AsReadOnly(candidates);
            if(candidates.Length==0)throw new InvalidDataException(UiText.Get("The ZIP contains no OBJ entry."));
            if(deferSelection&&selectedEntry is null)return;
            if(selectedEntry is null&&candidates.Length!=1)throw new InvalidDataException(UiText.Get("The ZIP must contain exactly one OBJ entry, or an explicit OBJ entry must be selected."));
            SelectEntry(selectedEntry??candidates[0],ct);
        }
        catch{archive?.Dispose();archiveFile?.Dispose();Remove();throw;}
    }
    public static ObjSourceSnapshot Create(string path,string? expectedHash=null,string? entryName=null,CancellationToken ct=default)
    {ct.ThrowIfCancellationRequested();return new(path,expectedHash,entryName,false,ct);}

    /// <summary>Freeze the root first; selection and parsing use this same private copy.</summary>
    public static ObjSourceSnapshot OpenForSelection(string path,string? expectedHash=null,CancellationToken ct=default)
    {ct.ThrowIfCancellationRequested();return new(path,expectedHash,null,true,ct);}
    public void SelectEntry(string name,CancellationToken ct=default)
    {
        ThrowIfDisposed();ct.ThrowIfCancellationRequested();string chosen=Normalize(name,false);
        string? candidate=CandidateEntries.FirstOrDefault(x=>x.Equals(chosen,StringComparison.OrdinalIgnoreCase));
        if(candidate is null)throw new InvalidDataException(UiText.Get("The selected OBJ entry is missing from the package."));
        if(entryName is not null&&!entryName.Equals(candidate,StringComparison.Ordinal))throw new InvalidOperationException(UiText.Get("The captured OBJ selection cannot change."));
        _=Capture(candidate,ct);entryName=candidate;
    }
    public ObjSourceFile Root(CancellationToken ct=default)=>Capture(EntryName,ct);
    public ObjSourceFile Resolve(string referringName,string reference,CancellationToken ct=default)
    {
        ThrowIfDisposed();ct.ThrowIfCancellationRequested();
        if(!captured.ContainsKey(Normalize(referringName,false)))throw new InvalidDataException(UiText.Get("OBJ dependencies must be relative to an already captured source."));
        // OBJ files commonly author Windows separators, but ZIP entry names must remain portable.
        reference=reference.Replace('\\','/');
        if(reference.StartsWith('/')||reference.Contains(':')||reference.Length==0)throw new InvalidDataException(UiText.Get("OBJ dependencies must be relative local paths."));
        string parent=referringName.Contains('/')?referringName[..referringName.LastIndexOf('/')]+"/":"";
        return Capture(Normalize(parent+reference,true),ct);
    }
    private ObjSourceFile Capture(string name,CancellationToken ct)
    {
        ThrowIfDisposed();ct.ThrowIfCancellationRequested();
        if(captured.TryGetValue(name,out var ready))return ready;
        string target="asset-"+captured.Count.ToString("D6",System.Globalization.CultureInfo.InvariantCulture)+Path.GetExtension(name).ToLowerInvariant();
        ObjSourceFile result;
        if(archive is not null)
        {
            if(!entries.TryGetValue(name,out var item))throw new InvalidDataException(UiText.Format("OBJ package dependency is missing: {0}",name));
            using var input=item.Open();result=Copy(input,target,item.Length,ct) with{Name=name};
        }
        else
        {
            string current=localRoot!;
            foreach(string segment in name.Split('/'))
            {
                ct.ThrowIfCancellationRequested();
                string[] matches=Directory.EnumerateFileSystemEntries(current).Where(x=>{ct.ThrowIfCancellationRequested();return Path.GetFileName(x).Normalize(NormalizationForm.FormC).Equals(segment,StringComparison.OrdinalIgnoreCase);}).Take(2).ToArray();
                if(matches.Length!=1)throw new InvalidDataException(UiText.Format("OBJ dependency is missing or has ambiguous case/Unicode names: {0}",name));
                current=matches[0];var info=new FileInfo(current);
                if(info.LinkTarget is not null||(File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException(UiText.Get("OBJ dependencies cannot traverse symbolic links."));
            }
            using var input=new FileStream(current,FileMode.Open,FileAccess.Read,FileShare.Read);
            result=Copy(input,target,null,ct) with{Name=name};
        }
        captured.Add(name,result);return result;
    }
    private ObjSourceFile Copy(Stream input,string filename,long? expectedLength,CancellationToken ct)
    {
        string output=Path.Combine(workspace,filename);
        var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
        if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        using var destination=new FileStream(output,options);using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=new byte[128*1024];long length=0;int read;
        while((read=input.Read(buffer))>0)
        {ct.ThrowIfCancellationRequested();length=checked(length+read);digest.AppendData(buffer,0,read);destination.Write(buffer,0,read);}
        ct.ThrowIfCancellationRequested();
        if(expectedLength.HasValue&&length!=expectedLength)throw new InvalidDataException(UiText.Get("OBJ package dependency is truncated."));
        destination.Flush(true);return new(filename,output,Convert.ToHexString(digest.GetHashAndReset()));
    }
    internal static string Normalize(string value,bool allowParents)
    {
        if(value.Length==0||value.StartsWith('/')||value.Contains('\\')||value.Contains(':')||value.Any(char.IsControl))throw new InvalidDataException(UiText.Get("Invalid OBJ dependency path."));
        var segments=new List<string>();
        foreach(string raw in value.Split('/'))
        {
            string part=raw.Normalize(NormalizationForm.FormC);
            if(allowParents&&part==".")continue;
            if(allowParents&&part=="..")
            {if(segments.Count==0)throw new InvalidDataException(UiText.Get("OBJ dependency escapes its source directory."));segments.RemoveAt(segments.Count-1);continue;}
            if(part.Length==0||part is "." or ".."||part.EndsWith('.')||part.EndsWith(' ')||part.IndexOfAny(new[]{'"','<','>','|','?','*'})>=0)
                throw new InvalidDataException(UiText.Get("OBJ dependency name is not portable."));
            string stem=part.Split('.')[0].ToUpperInvariant();
            if(stem is "CON" or "PRN" or "AUX" or "NUL"||stem.Length==4&&(stem.StartsWith("COM")||stem.StartsWith("LPT"))&&stem[3]>='0'&&stem[3]<='9')
                throw new InvalidDataException(UiText.Get("OBJ dependency uses a reserved file name."));
            segments.Add(part);
        }
        if(segments.Count==0)throw new InvalidDataException(UiText.Get("OBJ dependency does not name a file."));
        return string.Join('/',segments);
    }
    private void ThrowIfDisposed(){if(disposed)throw new ObjectDisposedException(nameof(ObjSourceSnapshot));}
    private void Remove(){try{Directory.Delete(workspace,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    public void Dispose(){if(disposed)return;disposed=true;archive?.Dispose();archiveFile?.Dispose();Remove();}
}

public sealed record ObjSourceFile(string Name,string Path,string Sha256)
{
    public StreamReader OpenText()=>new(new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read),new UTF8Encoding(false,true),true);
}
