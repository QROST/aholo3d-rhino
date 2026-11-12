using System.Text;
namespace Aholo3D.Core;

/// <summary>Process-local admission for an atomic output replacement. Does not lock other applications.</summary>
public sealed class OutputFileLease : IDisposable
{
    private static readonly object gate=new();
    private static readonly Dictionary<string,OutputFileLease> active=new(
        OperatingSystem.IsWindows()||OperatingSystem.IsMacOS()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
    private readonly string key;
    private bool disposed;
    private OutputFileLease(string key){this.key=key;}
    public static OutputFileLease Acquire(string path,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        string destination=Path.GetFullPath(path),parent=Path.GetDirectoryName(destination)!;
        if(!Directory.Exists(parent))throw new DirectoryNotFoundException("The output directory does not exist.");
        // Resolve parent aliases, but not the final file: rename replaces that directory entry, not its symlink target.
        string key=Path.Combine(ResolveDirectory(parent,0),Path.GetFileName(destination));
        if(OperatingSystem.IsWindows())
        {
            // DOS device spellings still address the same ordinary drive/UNC directory entry.
            if(key.StartsWith(@"\\?\UNC\",StringComparison.OrdinalIgnoreCase)||key.StartsWith(@"\\.\UNC\",StringComparison.OrdinalIgnoreCase))key=@"\\"+key[8..];
            else if(key.Length>=7&&(key.StartsWith(@"\\?\",StringComparison.Ordinal)||key.StartsWith(@"\\.\",StringComparison.Ordinal))&&char.IsAsciiLetter(key[4])&&key[5]==':'&&key[6]=='\\')key=key[4..];
        }
        // Conservative on case-sensitive Mac/Windows volumes: case-only and Unicode-equivalent names share admission.
        if(OperatingSystem.IsWindows()||OperatingSystem.IsMacOS())key=key.Normalize(NormalizationForm.FormC);
        lock(gate)
        {
            ct.ThrowIfCancellationRequested();
            if(active.ContainsKey(key))throw new InvalidOperationException(UiText.Get("Another export is writing this destination. Wait for it to finish or choose a different filename."));
            var lease=new OutputFileLease(key);active.Add(key,lease);return lease;
        }
    }
    private static string ResolveDirectory(string path,int depth)
    {
        if(depth>40)throw new IOException("The output directory contains too many symbolic links.");
        path=Path.GetFullPath(path);string root=Path.GetPathRoot(path)!,resolved=root;
        foreach(string part in path[root.Length..].Split(new[]{Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar},StringSplitOptions.RemoveEmptyEntries))
        {
            resolved=Path.Combine(resolved,part);
            var target=new DirectoryInfo(resolved).ResolveLinkTarget(returnFinalTarget:true);
            if(target is not null)resolved=ResolveDirectory(target.FullName,depth+1);
        }
        return resolved;
    }
    public void Dispose()
    {
        lock(gate)
        {
            if(disposed)return;disposed=true;
            if(active.TryGetValue(key,out var current)&&ReferenceEquals(current,this))active.Remove(key);
        }
    }
}
