using System.Security.Cryptography;
using System.Text;

namespace Aholo.Provider;

public sealed class ProviderException(string code, string message, bool submissionMayHaveSucceeded = false) : Exception(message)
{
    public string Code { get; } = code;
    public bool SubmissionMayHaveSucceeded { get; } = submissionMayHaveSucceeded;
}

public sealed class ApiKeyCredential
{
    internal string Value { get; }
    public string ConnectionId { get; }
    public ApiKeyCredential(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 8192 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl) || key.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Enter the original international API Key.", nameof(key));
        Value = key;
        ConnectionId = "international:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }
    public override string ToString() => "Aholo API Key (redacted)";
}

public sealed record WorldResult(string WorldId, string Name, string Status, double? Progress, string? UpAxis, string? PlyUrl, string? SpzUrl, string? ImageUrl);
public sealed record MeshResult(string TaskId, int Status, string? MeshId);
public sealed record RenderOperation(string OperationId, bool Done, string? ResultUrl, string? Error);
