using System.Text.Json;

namespace Aholo3D.Core;

/// <summary>Reads the official static OpenUSD Gaussian schema through an isolated, version-pinned local process.</summary>
public static class UsdGaussianReader
{
    public const string PythonEnvironmentVariable = "AHOLO3D_USD_PYTHON";

    public static GaussianScene Read(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrEmpty(path);
        string input = Path.GetFullPath(path);
        if (!File.Exists(input)) throw new FileNotFoundException("USD input file does not exist.", input);
        if (!new[] { ".usd", ".usda", ".usdc", ".usdz" }.Contains(Path.GetExtension(input), StringComparer.OrdinalIgnoreCase))
            throw new NotSupportedException("Expected a USD, USDA, USDC or USDZ file containing official Gaussian splat primitives.");
        string output = UsdBridgeProcess.CreateWorkspace();
        try
        {
            UsdBridgeProcess.Run(input, output, ct);
            return ReadBridgeOutput(output, ct);
        }
        finally { UsdBridgeProcess.RemoveWorkspace(output); }
    }

    /// <summary>Validates the bridge manifest and local PLY payloads. This entry point also permits offline protocol tests.</summary>
    public static GaussianScene ReadBridgeOutput(string directory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(Path.Combine(directory, "manifest.json"));
        using JsonDocument json = JsonDocument.Parse(stream);
        JsonElement root = json.RootElement;
        if (root.GetProperty("protocol").GetInt32() != 1) throw new InvalidDataException("Unsupported USD bridge protocol.");
        if (root.GetProperty("openUsdVersion").GetString() != "26.11") throw new InvalidDataException("Unexpected OpenUSD bridge version.");
        string? upAxis = root.GetProperty("upAxis").GetString();
        double units = root.GetProperty("metersPerUnit").GetDouble();
        if (upAxis is not ("Y" or "Z") || !double.IsFinite(units) || units <= 0) throw new InvalidDataException("Invalid USD stage axis or units.");
        var parts = new List<GaussianPart>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (JsonElement part in root.GetProperty("parts").EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            string name = part.GetProperty("name").GetString() ?? "";
            string file = part.GetProperty("file").GetString() ?? "";
            if (name.Length == 0 || !name.StartsWith('/') || !names.Add(name)) throw new InvalidDataException("USD bridge prim paths must be distinct absolute paths.");
            if (file.Length == 0 || file.Contains('/') || file.Contains('\\') || file.Contains(':') || file is "." or ".." ||
                Path.IsPathRooted(file) || !file.EndsWith(".ply", StringComparison.Ordinal) || !files.Add(file))
                throw new InvalidDataException("USD bridge payload must be a distinct local PLY file.");
            string payload = Path.Combine(directory, file);
            if (new FileInfo(payload).LinkTarget is not null) throw new InvalidDataException("USD bridge payload cannot be a symbolic link.");
            double[] matrix = part.GetProperty("transform").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            if (matrix.Length != 12 || matrix.Any(value => !double.IsFinite(value))) throw new InvalidDataException("USD bridge affine transform must contain twelve finite values.");
            var transform = new GaussianTransform(new(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5], matrix[6], matrix[7], matrix[8]), new(matrix[9], matrix[10], matrix[11]));
            try { _ = transform.Linear.Inverse(); }
            catch (ArgumentException ex) { throw new InvalidDataException("USD contains a singular or numerically unsupported Gaussian transform.", ex); }
            int degree = part.GetProperty("degree").GetInt32();
            long count = part.GetProperty("count").GetInt64();
            if (degree is < 0 or > 3 || count < 0) throw new InvalidDataException("Unsupported USD Gaussian degree or count.");
            GaussianSplat[] splats = PlyReader.ReadGaussians(payload, ct).ToArray();
            if (splats.LongLength != count || splats.Any(splat => splat.Degree != degree)) throw new InvalidDataException("USD bridge payload does not match its manifest.");
            total = checked(total + splats.LongLength);
            string displayName = part.TryGetProperty("sourceName", out JsonElement sourceName) ? sourceName.GetString() ?? name : name;
            bool antialiased = part.TryGetProperty("antialiased", out JsonElement antialias) && antialias.GetBoolean();
            parts.Add(new GaussianPart(displayName, splats, transform, antialiased));
        }
        if (total == 0) throw new InvalidDataException("The USD stage contains no supported Gaussian splats.");
        string[] warnings = root.GetProperty("warnings").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        return new GaussianScene(parts, upAxis, units, "OpenUSD ParticleField3DGaussianSplat", warnings);
    }

}
