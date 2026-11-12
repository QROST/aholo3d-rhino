using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aholo3D.Core;

/// <summary>English source text with optional Chinese presentation. Never changes process formatting cultures.</summary>
public static class UiText
{
    public sealed record Entry(string English, string Chinese);
    private static readonly IReadOnlyList<Entry> Entries = Load();
    private static readonly Dictionary<string, string> Chinese = Entries.GroupBy(e => e.English).ToDictionary(g => g.Key, g => g.First().Chinese, StringComparer.Ordinal);
    // Several English labels may legitimately share a translation. Keep direct translation usable;
    // ambiguous reverse messages stay unchanged rather than guessing a different English meaning.
    private static readonly Dictionary<string, string> English = Entries.GroupBy(e => e.Chinese, StringComparer.Ordinal)
        .Where(g => g.Select(e => e.English).Distinct(StringComparer.Ordinal).Count() == 1)
        .ToDictionary(g => g.Key, g => g.First().English, StringComparer.Ordinal);
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Lazy<Template[]> Templates = new(() => Entries.Where(e => Placeholder.IsMatch(e.English)).Select(e => new Template(e)).ToArray());
    private static string _language = "en";
    public static string Language => Volatile.Read(ref _language);
    public static IReadOnlyList<Entry> Catalog => Entries;

    /// <summary>A valid Rhino UI language takes precedence over the system UI language, even when unsupported.</summary>
    public static string SelectLanguage(int? rhinoLanguageId, string? systemUiLanguage)
    {
        if (rhinoLanguageId is > 0 and not 127)
        {
            try
            {
                string name = CultureInfo.GetCultureInfo(rhinoLanguageId.Value).Name;
                if (!string.IsNullOrEmpty(name)) return IsChinese(name) ? "zh" : "en";
            }
            catch (CultureNotFoundException) { }
        }
        return IsChinese(systemUiLanguage) ? "zh" : "en";
    }
    private static bool IsChinese(string? name) => name is not null && (name.Equals("zh", StringComparison.OrdinalIgnoreCase) || name.StartsWith("zh-", StringComparison.OrdinalIgnoreCase));
    public static void Initialize(int? rhinoLanguageId, string? systemUiLanguage) => Volatile.Write(ref _language, SelectLanguage(rhinoLanguageId, systemUiLanguage));
    public static string Get(string english, string? language = null) => (language ?? Language) == "zh" && Chinese.TryGetValue(english, out string? chinese) ? chinese : english;
    public static string Format(string english, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, Get(english), arguments);

    /// <summary>Translate only whole, known product errors. Never pass user prompts, names or arbitrary response text here.</summary>
    public static string TranslateKnownMessage(string message, string? language = null)
    {
        bool zh = (language ?? Language) == "zh";
        if (English.TryGetValue(message, out string? normalized)) return zh ? message : normalized;
        if (Chinese.TryGetValue(message, out string? translated)) return zh ? translated : message;
        if (message.Length > 32768) return message;
        foreach (Template template in Templates.Value)
        {
            try
            {
                Match match = (zh ? template.English : template.Chinese).Match(message);
                if (!match.Success) continue;
                // Captures are already formatted. Keep their bytes intact, including any user-supplied path.
                return Placeholder.Replace(zh ? template.Entry.Chinese : template.Entry.English, p => match.Groups["p" + p.Groups[1].Value].Value);
            }
            catch (RegexMatchTimeoutException) { return message; }
        }
        return message;
    }

    public static string Status(string code, string? language = null) => Get(code switch
    {
        "Preparing" => "Preparing", "Uploading" => "Uploading", "Submitting" => "Submitting",
        "MeshSubmitting" => "Submitting mesh", "RenderSubmitting" => "Submitting render",
        "MeshProcessing" => "Processing mesh", "MeshReady" => "Ready to render", "UnsupportedSchema" => "Unsupported task version", "Running" or "RUNNING" => "Running",
        "PENDING" => "Pending", "PREPROCESSING" => "Preprocessing", "SUCCEEDED" => "Succeeded",
        "FAILED" => "Failed", "CANCELED" => "Cancelled", "TIMEOUT" => "Timed out", "REJECTED" => "Rejected",
        "RecordError" => "Record error", "SubmissionUnknown" => "Submission unconfirmed", "Interrupted" => "Interrupted",
        "Creating" => "Creating cloud preview", "Connecting" => "Connecting", "Streaming" => "Receiving cloud images",
        "Stopping" => "Closing cloud preview", "Closed" => "Cloud preview closed", "NotSubmitted" => "Not submitted",
        "CleanupRequired" => "Server cleanup pending", "ExternallyResolved" => "Externally resolved (user confirmed)",
        _ => code
    }, language);

    private static IReadOnlyList<Entry> Load()
    {
        using Stream stream = typeof(UiText).Assembly.GetManifestResourceStream("Aholo3D.Core.UiStrings.json") ?? throw new InvalidOperationException("Missing Aholo3D UI language resource.");
        return Array.AsReadOnly(JsonSerializer.Deserialize<Entry[]>(stream) ?? throw new InvalidOperationException("Invalid Aholo3D UI language resource."));
    }
    private sealed class Template
    {
        public Entry Entry { get; }
        public Regex English { get; }
        public Regex Chinese { get; }
        public Template(Entry entry) { Entry = entry; English = Pattern(entry.English); Chinese = Pattern(entry.Chinese); }
        private static Regex Pattern(string text)
        {
            var result = new System.Text.StringBuilder("\\A"); int offset = 0;
            foreach (Match p in Placeholder.Matches(text))
            {
                result.Append(Regex.Escape(text[offset..p.Index]));
                result.Append("(?<p").Append(p.Groups[1].Value).Append(">.*?)"); offset = p.Index + p.Length;
            }
            result.Append(Regex.Escape(text[offset..])).Append("\\z");
            return new Regex(result.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
        }
    }
}
