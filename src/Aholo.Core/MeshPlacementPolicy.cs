using System.Security.Cryptography;
using System.Text;
namespace Aholo.Core;

public static class MeshPlacementPolicy
{
    // Keep the identity used by the original integrated GLB importer.
    public static Guid ImportId(Guid taskId,string artifactKey)
    {
        if(taskId==Guid.Empty||string.IsNullOrEmpty(artifactKey))throw new ArgumentException("A task and result identity are required.");
        return new(SHA256.HashData(Encoding.UTF8.GetBytes(taskId.ToString("N")+":"+artifactKey)).AsSpan(0,16));
    }
    public static bool SupportsAutomaticPlacement(string operation)=>operation is "lux.text-to-3d" or "lux.img-to-3d";
    public static TaskArtifact SelectStaticMesh(LocalTask task,string? artifactKey=null)
    {
        var artifact=artifactKey is null?task.Artifacts.FirstOrDefault(a=>a.Format=="glb"&&a.Error is null):task.Artifacts.FirstOrDefault(a=>a.Key==artifactKey);
        if(artifact is null||artifact.Format!="glb"||artifact.Error is not null)throw new InvalidOperationException(UiText.Get("Choose an available static GLB result. Use Library for GS, spatial or other formats."));
        return artifact;
    }
}
