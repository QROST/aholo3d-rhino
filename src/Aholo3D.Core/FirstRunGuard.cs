using System.Text.Json;
namespace Aholo3D.Core;

/// <summary>Machine-level first-run setup marker. Kept outside per-document workspace state so setup is offered once per install, not per document.</summary>
public static class FirstRunGuard
{
    private const string FileName = "first-run.json";
    public static bool IsDismissed(string root)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(root, FileName));
            return JsonSerializer.Deserialize<Marker>(stream)?.Dismissed == true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return false; }
    }
    public static void Dismiss(string root)
    {
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, FileName), JsonSerializer.Serialize(new Marker { Dismissed = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* First-run prompting is advisory; an IO failure only delays it. */ }
    }
    private sealed class Marker { public bool Dismissed { get; set; } }
}
