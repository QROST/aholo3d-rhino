namespace Aholo.Core;

/// <summary>Single local import entry point. Fully decodes and validates before the host changes a document.</summary>
public static class GaussianSceneReader
{
    public static readonly IReadOnlyList<string> Extensions = Array.AsReadOnly(new[] { ".ply", ".splat", ".spz", ".usd", ".usda", ".usdc", ".usdz" });

    public static GaussianScene Read(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ct.ThrowIfCancellationRequested();
        GaussianScene scene = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ply" => new GaussianScene(new[] { new GaussianPart(Path.GetFileNameWithoutExtension(path), PlyReader.ReadGaussians(path, ct).ToArray(), GaussianTransform.Identity) }, Format: "PLY (3DGS)"),
            ".splat" => SplatReader.Read(path, ct),
            ".spz" => SpzReader.Read(path, ct),
            ".usd" or ".usda" or ".usdc" or ".usdz" => UsdGaussianReader.Read(path, ct),
            _ => throw new NotSupportedException("Unsupported GS format. Choose a Gaussian PLY, SPLAT, SPZ or official Gaussian USD/USDZ scene.")
        };
        if (scene.Parts.Count == 0 || scene.Parts.All(p => p.Splats.Length == 0))
            throw new InvalidDataException("The file contains no supported Gaussian splats.");
        if (scene.UpAxis is not (null or "Y" or "Z")) throw new InvalidDataException("Unsupported source up axis.");
        if (scene.MetersPerUnit is { } units && (!double.IsFinite(units) || units <= 0)) throw new InvalidDataException("Invalid source unit scale.");
        foreach (GaussianPart part in scene.Parts)
        {
            ct.ThrowIfCancellationRequested();
            if (!part.Transform.Translation.IsFinite) throw new InvalidDataException("Non-finite scene translation.");
            try { _ = part.Transform.Linear.Inverse(); }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid Gaussian scene transform.", e); }
        }
        return scene;
    }
}
