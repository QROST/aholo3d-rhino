using System.Security.Cryptography;
using System.Text;

namespace Aholo3D.Provider;

public sealed class ProviderException(string code, string message, bool submissionMayHaveSucceeded = false) : Exception(message)
{
    public string Code { get; } = code;
    public bool SubmissionMayHaveSucceeded { get; } = submissionMayHaveSucceeded;
}

/// <summary>Cancellation known to occur before HTTP admission; distinct from an uncertain sent request.</summary>
public sealed class RequestNotSentCanceledException(CancellationToken token)
    : OperationCanceledException("The request was cancelled before it was sent.", token);

public sealed class ApiKeyCredential
{
    internal string Value { get; }
    public string ConnectionId { get; }
    public string Region { get; }
    public ApiKeyCredential(string key, string region = Aholo3D.Core.Aholo3DRegions.International)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 8192 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl) || key.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter the original API Key for the selected region.", nameof(key));
        Region = Aholo3D.Core.Aholo3DRegions.Get(region).Id;
        Value = key;
        ConnectionId = Region + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }
    public override string ToString() => "Aholo3D API Key (redacted)";
}

public sealed record WorldResult(string WorldId, string Name, string Status, double? Progress, string? UpAxis, string? PlyUrl, string? SpzUrl, string? ImageUrl)
{
    public string? CoverUrl { get; init; }
    public string? Scene { get; init; }
    public string? LodMetaUrl { get; init; }
    public long? Created { get; init; }
    public long? Updated { get; init; }
}
public sealed record MeshResult(string TaskId, int Status, string? MeshId)
{ public IReadOnlyList<string> MeshIds { get; init; } = Array.Empty<string>(); }
public sealed record RenderOperation(string OperationId, bool Done, string? ResultUrl, string? Error);
