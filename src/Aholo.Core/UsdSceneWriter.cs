using System.Text.Json;

namespace Aholo.Core;

public sealed record UsdSceneExportResult(string Path, string? AssetDirectory, int GaussianParts, long Gaussians, int Meshes, long Triangles);

/// <summary>Static mixed scene exchange; GS retain their native local frames and stage units, meshes enter as Z-up millimetres.</summary>
public static class UsdSceneWriter
{
    public static UsdSceneExportResult Write(string path, GaussianScene? gaussians, IReadOnlyList<UsdMeshPart> meshes, CancellationToken ct = default)
    {
        var result = UsdExportBundle.Write(path, (workspace, assets, format, token) =>
        {
            WriteBridgeInput(workspace, gaussians, meshes, assets, token);
            UsdBridgeProcess.Run(Path.Combine(workspace, "scene-export.json"), workspace, token, format, scene: true);
        }, ct);
        return new(result.Path, result.AssetDirectory, gaussians?.Parts.Count ?? 0,
            gaussians?.Parts.Sum(p => (long)p.Splats.Length) ?? 0, meshes.Count, meshes.Sum(p => (long)p.Mesh.Indices.Length / 3));
    }

    /// <summary>Write fixed-name validated inputs into a caller-owned empty workspace.</summary>
    public static void WriteBridgeInput(string directory, GaussianScene? gaussians, IReadOnlyList<UsdMeshPart> meshes, string assetDirectory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(meshes);
        if (gaussians is null && meshes.Count == 0) throw new ArgumentException("Select Gaussian blocks or meshes to export.");
        if (gaussians is not null) UsdGaussianWriter.WriteBridgeInput(directory, gaussians, ct);
        if (meshes.Count > 0) UsdMeshWriter.WriteBridgeInput(directory, meshes, assetDirectory, ct);
        ct.ThrowIfCancellationRequested();
        File.WriteAllText(Path.Combine(directory, "scene-export.json"), JsonSerializer.Serialize(new
        { protocol = 1, openUsdVersion = "26.11", gaussians = gaussians is not null, meshes = meshes.Count > 0 }));
    }
}
