using System.Text.Json;

namespace Aholo.Core;

/// <summary>
/// Exports complete GS parts to the official static OpenUSD schema. Positions are rebased per part before float32 encoding;
/// scales, normalized rotations and activated opacities also use float32, SH retains float precision, transforms use matrix4d.
/// This is not an arbitrary-double lossless format. Mixed SH degrees are zero-padded to a uniform degree without changing radiance.
/// </summary>
public static class UsdGaussianWriter
{
    public const string PrecisionNotice = "USD stores rebased positions, scales, normalized rotations and opacity in float32; object transforms remain double precision. Small rounding differences are expected.";

    public static void Write(string path, GaussianScene scene, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrEmpty(path);
        string destination = Path.GetFullPath(path), extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not (".usd" or ".usda" or ".usdc" or ".usdz")) throw new NotSupportedException("Choose a USD, USDA, USDC or USDZ export filename.");
        if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new DirectoryNotFoundException("The USD export directory does not exist.");
        using var destinationLease=OutputFileLease.Acquire(destination,ct);
        Validate(scene, ct);
        string workspace = UsdBridgeProcess.CreateWorkspace();
        string pending = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            WriteBridgeInput(workspace, scene, ct);
            UsdBridgeProcess.Run(Path.Combine(workspace, "export.json"), workspace, ct, extension[1..]);
            string produced = Path.Combine(workspace, "result" + extension);
            if (!File.Exists(produced) || new FileInfo(produced).Length == 0) throw new InvalidDataException("OpenUSD export did not produce a complete output file.");
            using (var input = File.OpenRead(produced))
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pending, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                byte[] buffer = new byte[131072]; int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) != 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
                output.Flush(flushToDisk: true);
            }
            // Final rename is the commit point. The temporary sibling keeps replacement on the destination filesystem.
            ct.ThrowIfCancellationRequested();
            File.Move(pending, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(pending)) File.Delete(pending); }
            finally { UsdBridgeProcess.RemoveWorkspace(workspace); }
        }
    }

    /// <summary>Produces validated canonical transport files in a caller-owned empty workspace, without requiring OpenUSD.</summary>
    public static void WriteBridgeInput(string directory, GaussianScene scene, CancellationToken ct = default)
    {
        Validate(scene, ct);
        var parts = new List<object>(scene.Parts.Count);
        for (int i = 0; i < scene.Parts.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            GaussianPart part = scene.Parts[i];
            string file = $"part-{i:D6}.ply";
            GaussianPlyWriter.Write(Path.Combine(directory, file), part.Splats, ct);
            GaussianVector origin = part.Splats.Length == 0 ? GaussianVector.Zero : part.Splats[0].Center;
            GaussianMatrix m = part.Transform.Linear;
            GaussianVector t = part.Transform.Translation;
            parts.Add(new
            {
                name = part.Name, file, count = part.Splats.LongLength, degree = part.Splats.Length == 0 ? 0 : part.Splats.Max(s => s.Degree),
                transform = new[] { m.M11, m.M12, m.M13, m.M21, m.M22, m.M23, m.M31, m.M32, m.M33, t.X, t.Y, t.Z },
                origin = new[] { origin.X, origin.Y, origin.Z }, antialiased = part.Antialiased
            });
        }
        ct.ThrowIfCancellationRequested();
        File.WriteAllText(Path.Combine(directory, "export.json"), JsonSerializer.Serialize(new
        {
            protocol = 1, openUsdVersion = "26.11", upAxis = scene.UpAxis, metersPerUnit = scene.MetersPerUnit,
            precision = PrecisionNotice, parts
        }));
    }

    private static void Validate(GaussianScene scene, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(scene); ArgumentNullException.ThrowIfNull(scene.Parts);
        if (scene.UpAxis is not ("Y" or "Z") || scene.MetersPerUnit is not { } units || !double.IsFinite(units) || units <= 0)
            throw new ArgumentException("USD export requires an explicit Y/Z up axis and positive metersPerUnit. Use the actual document units; unspecified units cannot be guessed.", nameof(scene));
        long total = 0;
        foreach (GaussianPart part in scene.Parts)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(part); ArgumentNullException.ThrowIfNull(part.Splats);
            ArgumentNullException.ThrowIfNull(part.Name);
            if (!part.Transform.Translation.IsFinite) throw new ArgumentException("USD export requires a finite object translation.", nameof(scene));
            _ = part.Transform.Linear.Inverse();
            GaussianVector origin = part.Splats.Length == 0 ? GaussianVector.Zero : part.Splats[0]?.Center ?? throw new ArgumentException("A Gaussian part contains a null splat.", nameof(scene));
            if (!part.Transform.TransformPoint(origin).IsFinite) throw new ArgumentException("Rebased USD object translation exceeds double precision.", nameof(scene));
            foreach (GaussianSplat splat in part.Splats)
            {
                ct.ThrowIfCancellationRequested(); ArgumentNullException.ThrowIfNull(splat);
                GaussianVector local = splat.Center - origin;
                CheckFloat(local.X, false, "rebased position"); CheckFloat(local.Y, false, "rebased position"); CheckFloat(local.Z, false, "rebased position");
                CheckFloat(Math.Exp(splat.LogScale.X), true, "scale"); CheckFloat(Math.Exp(splat.LogScale.Y), true, "scale"); CheckFloat(Math.Exp(splat.LogScale.Z), true, "scale");
                _ = splat.Rotation.Normalized();
                total = checked(total + 1);
            }
        }
        if (total == 0) throw new ArgumentException("The scene contains no Gaussian splats to export.", nameof(scene));
    }

    private static void CheckFloat(double value, bool positive, string label)
    {
        float encoded = (float)value;
        if (!double.IsFinite(value) || !float.IsFinite(encoded) || positive && encoded <= 0)
            throw new ArgumentException($"USD {label} cannot be represented by the official float32 schema. No clamping or epsilon substitution was applied.");
    }
}
