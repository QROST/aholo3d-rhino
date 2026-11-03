using System.Globalization;
using System.Text;

namespace Aholo.Core;

/// <summary>Lossless cache for supported GS fields. Double precision geometric values, float SH, no sampling.</summary>
public static class GaussianPlyWriter
{
    public static void Write(string path, IReadOnlyList<GaussianSplat> splats, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(splats);
        ct.ThrowIfCancellationRequested();
        int degree = 0;
        foreach (GaussianSplat splat in splats)
        {
            ct.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(splat);
            degree = Math.Max(degree, splat.Degree);
        }
        int rest = (degree + 1) * (degree + 1) - 1;
        // Commit a complete file only. A canceled conversion never overwrites an existing valid cache.
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var header = new StringBuilder("ply\nformat binary_little_endian 1.0\ncomment Aholo full Gaussian cache\nelement vertex ");
                header.Append(splats.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
                foreach (string field in new[] { "x", "y", "z", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3", "opacity" })
                    header.Append("property double ").Append(field).Append('\n');
                for (int i = 0; i < 3; i++) header.Append("property float f_dc_").Append(i.ToString(CultureInfo.InvariantCulture)).Append('\n');
                for (int i = 0; i < 3 * rest; i++) header.Append("property float f_rest_").Append(i.ToString(CultureInfo.InvariantCulture)).Append('\n');
                header.Append("end_header\n");
                writer.Write(Encoding.ASCII.GetBytes(header.ToString()));
                foreach (GaussianSplat s in splats)
                {
                    ct.ThrowIfCancellationRequested();
                    writer.Write(s.Center.X); writer.Write(s.Center.Y); writer.Write(s.Center.Z);
                    writer.Write(s.LogScale.X); writer.Write(s.LogScale.Y); writer.Write(s.LogScale.Z);
                    writer.Write(s.Rotation.W); writer.Write(s.Rotation.X); writer.Write(s.Rotation.Y); writer.Write(s.Rotation.Z);
                    writer.Write(s.OpacityLogit);
                    ReadOnlySpan<float> sh = s.Coefficients.Span;
                    for (int channel = 0; channel < 3; channel++) writer.Write(sh[channel]);
                    // PLY stores non-DC coefficients channel-major. Zero padding raises degree without changing radiance.
                    for (int channel = 0; channel < 3; channel++)
                        for (int coefficient = 1; coefficient <= rest; coefficient++)
                            writer.Write(coefficient * 3 + channel < sh.Length ? sh[coefficient * 3 + channel] : 0f);
                }
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
