using System.Security.Cryptography;
using System.Text;
namespace Aholo3D.Core;

public static class MeshPlacementPolicy
{
    // Keep the identity used by the original integrated GLB importer.
    public static Guid ImportId(Guid taskId,string artifactKey)
    {
        if(taskId==Guid.Empty||string.IsNullOrEmpty(artifactKey))throw new ArgumentException("A task and result identity are required.");
        return new(SHA256.HashData(Encoding.UTF8.GetBytes(taskId.ToString("N")+":"+artifactKey)).AsSpan(0,16));
    }
    public static string AutomaticOperationScope(string documentPrefix,Guid panelId,Guid taskId)
    {
        if(string.IsNullOrEmpty(documentPrefix)||!documentPrefix.EndsWith(':')||panelId==Guid.Empty||taskId==Guid.Empty)throw new ArgumentException("A document scope, panel and task identity are required.");
        return documentPrefix+"automatic-placement:"+panelId.ToString("N")+":"+taskId.ToString("N");
    }
    public static OperationResource ResultResource(uint serial,string documentSession,Guid importId)
    {
        if(string.IsNullOrEmpty(documentSession)||importId==Guid.Empty)throw new ArgumentException("A document session and import identity are required.");
        return new("mesh-result",serial.ToString(System.Globalization.CultureInfo.InvariantCulture)+"/"+documentSession,importId.ToString("N"));
    }
    public static bool IsNativeMeshFormat(string format)=>format is "glb" or "ply" or "stl" or "3mf";
    public static bool IsMeshFormat(string format)=>IsNativeMeshFormat(format)||format is "usd" or "usda" or "usdc" or "usdz" or "obj" or "obj_zip";
    public static bool IsLocalMeshExtension(string extension)=>extension=="zip"||extension!="obj_zip"&&IsMeshFormat(extension);
    public static bool CanPlaceMesh(LocalTask task,TaskArtifact artifact)=>task.SchemaVersion==LocalTask.CurrentSchemaVersion&&task.Kind=="object"&&IsMeshFormat(artifact.Format)&&artifact.Error is null;
    public static void ValidateTask(LocalTask task)
    {
        if(task.SchemaVersion!=LocalTask.CurrentSchemaVersion||task.Kind!="object")throw new InvalidOperationException(UiText.Get("Choose a static object mesh result. Use Import GS for Gaussian scenes."));
    }
    public static bool SupportsAutomaticPlacement(string operation)=>operation is "lux.text-to-3d" or "lux.img-to-3d";
    public static TaskArtifact SelectStaticMesh(LocalTask task,string? artifactKey=null)
    {
        ValidateTask(task);
        var artifact=artifactKey is null?task.Artifacts.FirstOrDefault(a=>IsMeshFormat(a.Format)&&a.Error is null):task.Artifacts.FirstOrDefault(a=>a.Key==artifactKey);
        if(artifact is null||!CanPlaceMesh(task,artifact)||task.Artifacts.Count(a=>a.Key==artifact.Key)!=1)throw new InvalidOperationException(UiText.Get("Choose an available static GLB, mesh PLY, STL, supported 3MF, OBJ or USD mesh result. Use Import GS for Gaussian files."));
        return artifact;
    }
}
