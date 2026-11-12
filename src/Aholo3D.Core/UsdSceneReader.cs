using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aholo3D.Core;

public sealed record UsdScene(GaussianScene? Gaussians, UsdMeshScene? Meshes, string SourceSha256,
    IReadOnlyList<string> DependencyHashes, IReadOnlyList<string> Warnings);

/// <summary>Decode both geometry types from one frozen dependency snapshot, accepting only a complete scene.</summary>
public static class UsdSceneReader
{
    public static UsdScene Read(string path, string? expectedSha256=null, CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        string input=Path.GetFullPath(path);
        if(Path.GetExtension(input).ToLowerInvariant() is not (".usd" or ".usda" or ".usdc" or ".usdz"))
            throw new NotSupportedException("Choose a USD, USDA, USDC or USDZ file.");
        string workspace=UsdBridgeProcess.CreateWorkspace();
        try
        {
            UsdBridgeProcess.Run(input,workspace,ct,scene:true);
            var scene=ReadBridgeOutput(workspace,ct);
            if(expectedSha256 is not null&&!scene.SourceSha256.Equals(expectedSha256,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(UiText.Get("The source scene changed while being read. Import stopped; save the file and retry."));
            return scene;
        }
        finally{UsdBridgeProcess.RemoveWorkspace(workspace);}
    }

    public static UsdScene ReadBridgeOutput(string directory,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"scene-import.json")));
        var root=json.RootElement;
        if(root.GetProperty("protocol").GetInt32()!=1||root.GetProperty("openUsdVersion").GetString()!="26.11")throw Invalid("Unsupported USD scene transport.");
        string axis=root.GetProperty("upAxis").GetString()??"",hash=root.GetProperty("sourceSha256").GetString()??"";
        double units=root.GetProperty("metersPerUnit").GetDouble();
        string[] dependencies=root.GetProperty("dependencyHashes").EnumerateArray().Select(v=>v.GetString()??"").ToArray();
        if(axis is not ("Y" or "Z")||!double.IsFinite(units)||units<=0)throw Invalid("Invalid USD scene units or axis.");
        if(!IsHash(hash)||dependencies.Length==0||dependencies.Any(h=>!IsHash(h))||!dependencies.Contains(hash,StringComparer.OrdinalIgnoreCase))throw Invalid("Invalid USD scene provenance.");
        bool hasGs=root.GetProperty("gaussians").GetBoolean(),hasMesh=root.GetProperty("meshes").GetBoolean();
        if(!hasGs&&!hasMesh)throw Invalid("USD contains no supported static scene geometry.");
        foreach(var entry in new[]{(File:"manifest.json",Expected:hasGs),(File:"mesh-import.json",Expected:hasMesh)})
        {
            string file=Path.Combine(directory,entry.File);
            if(File.Exists(file)!=entry.Expected)throw Invalid("USD scene payloads disagree with the complete-scene manifest.");
            if(!entry.Expected)continue;
            using var child=JsonDocument.Parse(File.ReadAllText(file));var meta=child.RootElement;
            var childDependencies=meta.GetProperty("dependencyHashes").EnumerateArray().Select(v=>v.GetString()??"");
            if(meta.GetProperty("upAxis").GetString()!=axis||meta.GetProperty("metersPerUnit").GetDouble()!=units||
                !string.Equals(meta.GetProperty("sourceSha256").GetString(),hash,StringComparison.OrdinalIgnoreCase)||
                !childDependencies.SequenceEqual(dependencies,StringComparer.OrdinalIgnoreCase))throw Invalid("USD geometry was not decoded from the same source snapshot.");
        }
        var gaussians=hasGs?UsdGaussianReader.ReadBridgeOutput(directory,ct):null;
        var meshes=hasMesh?UsdMeshReader.ReadBridgeOutput(directory,ct):null;
        ct.ThrowIfCancellationRequested();
        return new(gaussians,meshes,hash,dependencies,root.GetProperty("warnings").EnumerateArray().Select(v=>v.GetString()??"").ToArray());
    }
    private static bool IsHash(string value)=>Regex.IsMatch(value,@"\A[a-fA-F0-9]{64}\z");
    private static InvalidDataException Invalid(string message)=>new(message);
}
