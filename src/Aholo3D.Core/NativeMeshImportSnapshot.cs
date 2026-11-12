using System.Security.Cryptography;
namespace Aholo3D.Core;

public interface IMeshImportSnapshot : IDisposable
{
    string Path { get; }
    string Sha256 { get; }
}

/// <summary>Private, suffix-preserving native input. Full mesh geometry is audited by Rhino before acceptance.</summary>
public sealed class NativeMeshImportSnapshot : IMeshImportSnapshot
{
    private int disposed;
    private NativeMeshImportSnapshot(string path,string hash){Path=path;Sha256=hash;}
    public string Path{get;}
    public string Sha256{get;}
    public static IMeshImportSnapshot Create(string source,string? expectedHash,HostImportTrace? trace=null,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        string extension=System.IO.Path.GetExtension(source).ToLowerInvariant();
        if(extension==".glb")return GlbImportSnapshot.Create(source,expectedHash,trace);
        if(extension is not (".ply" or ".stl" or ".3mf"))throw new InvalidDataException(UiText.Get("Choose a static GLB, mesh PLY, STL or core 3MF file."));
        string directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"Aholo3D-Rhino",Guid.NewGuid().ToString("N"));
        try
        {
            if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            string path=System.IO.Path.Combine(directory,"asset"+extension);
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            string hash;
            using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(path,options))
            using(var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer=new byte[128*1024];int read;
                while((read=input.Read(buffer))>0){ct.ThrowIfCancellationRequested();digest.AppendData(buffer,0,read);output.Write(buffer,0,read);}
                ct.ThrowIfCancellationRequested();output.Flush(true);hash=Convert.ToHexString(digest.GetHashAndReset());
            }
            if(expectedHash is not null&&!hash.Equals(expectedHash,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(UiText.Get("The mesh hash does not match the task record."));
            if(extension==".ply")PlyReader.ValidateMesh(path,ct);
            else if(extension==".3mf")ThreeMfPreflight.Validate(path,ct);
            else if(new FileInfo(path).Length==0)throw new InvalidDataException(UiText.Get("The mesh file is empty."));
            ct.ThrowIfCancellationRequested();trace?.Record("preflight_file_ready",extension);
            return new NativeMeshImportSnapshot(path,hash);
        }
        catch{try{Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}throw;}
    }
    public static void Verify(string path,string expectedHash,CancellationToken ct=default)
    {
        using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=new byte[128*1024];int read;
        while((read=stream.Read(buffer))>0){ct.ThrowIfCancellationRequested();digest.AppendData(buffer,0,read);}
        ct.ThrowIfCancellationRequested();
        if(!Convert.ToHexString(digest.GetHashAndReset()).Equals(expectedHash,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.Get("The mesh hash does not match the task record."));
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        try{Directory.Delete(System.IO.Path.GetDirectoryName(Path)!,true);}catch(IOException){}catch(UnauthorizedAccessException){}
    }
}
