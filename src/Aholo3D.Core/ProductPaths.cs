namespace Aholo3D.Core;

/// <summary>One data/lock authority across the product rename. Never copies or moves user data.</summary>
public static class ProductPaths
{
    public const string DirectoryName = "Aholo3D-Rhino";
    public const string LegacyDirectoryName = "Aholo-Rhino";
    public static string? EnvironmentValue(string suffix) => EnvironmentValue(suffix, Environment.GetEnvironmentVariable);
    public static string? EnvironmentValue(string suffix, Func<string,string?> read)
    {
        string? current = read("AHOLO3D_" + suffix);
        if (!string.IsNullOrWhiteSpace(current)) return current;
        string? legacy=read("AHOLO_" + suffix);
        return !string.IsNullOrWhiteSpace(legacy) ? legacy : null;
    }
    public static string DefaultDataRoot => ResolveDataRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    public static string DataRoot => EnvironmentValue("RHINO_STATE_DIR") ?? DefaultDataRoot;
    public static string ResolveDataRoot(string parent)
    {
        string current = Path.Combine(parent, DirectoryName), legacy = Path.Combine(parent, LegacyDirectoryName);
        bool hasCurrent = Directory.Exists(current), hasLegacy = Directory.Exists(legacy);
        if (hasCurrent && hasLegacy)
        {
            string Target(string path) => new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(path);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Target(current), Target(legacy), comparison))
                throw new IOException("Separate Aholo3D and legacy data directories exist. Keep both; resolve the storage conflict or set AHOLO3D_RHINO_STATE_DIR explicitly.");
        }
        // The installer can create a new-name alias to the existing directory. If unavailable,
        // retain the original authority, including absolute document references and USD venv paths.
        if (hasCurrent) return current;
        if (File.Exists(current) || new DirectoryInfo(current).LinkTarget is not null)
            throw new IOException("The Aholo3D data directory is blocked or its directory link is broken. Repair the path before continuing.");
        return hasLegacy ? legacy : current;
    }
}
