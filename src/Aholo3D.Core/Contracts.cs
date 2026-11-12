using System.Text.Json;
using System.Text.Json.Serialization;
namespace Aholo3D.Core;
public sealed record RpcRequest(int Version, Guid Id, string Method, JsonElement Payload, string? CredentialEpoch = null);
public sealed record RpcResponse(int Version, Guid Id, JsonElement? Result, string? Error, string? ErrorCode = null);
public static class Aholo3DJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static JsonElement Element<T>(T value) => JsonSerializer.SerializeToElement(value, Options);
}
public sealed record ConnectionState(bool Connected, string? ConnectionId, bool Remembered, string? CredentialEpoch = null, string Region = Aholo3DRegions.International);
public sealed record ConnectRequest(string ApiKey, bool Remember, string ValidationService = "world", string Region = Aholo3DRegions.International);
public sealed record GenerateWorldRequest(string Name, string Prompt, string? ImagePath = null);
// Row-vector pose, translation in row 4, distances in millimetres; camera looks along local -Z.
public sealed record RenderCamera(double[] Transform, double FocalLength, double HorizontalAperture, double VerticalAperture, double Near, double Far);
public sealed record RenderRequest(string Name, string GlbPath, RenderCamera Camera, int Width = 1280, int Height = 720, double EnvironmentIntensity = 1, AssetUploadOptions? Upload = null, RenderLight[]? Lights = null);
public sealed record DownloadRequest(Guid Id, string Kind = "ply");
public sealed record LocalTask
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Region { get; init; } = "international";
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Kind { get; init; } = "world";
    public string Name { get; init; } = "";
    public string ConnectionId { get; init; } = "";
    public string Status { get; init; } = "Preparing";
    public string? WorldId { get; init; }
    public string? LuxTaskId { get; init; }
    public string? ServiceOperation { get; init; }
    public TaskArtifact[] Artifacts { get; init; } = Array.Empty<TaskArtifact>();
    public string? SpatialTaskId { get; init; }
    public DateTimeOffset? LastQueriedAt { get; init; }
    public string? MeshTaskId { get; init; }
    public string? MeshId { get; init; }
    public string? OperationId { get; init; }
    public double? Progress { get; init; }
    public string? UpAxis { get; init; }
    public string? Error { get; init; }
    public string? PlyUrl { get; init; }
    public string? SpzUrl { get; init; }
    public string? ImageUrl { get; init; }
    public string? PlyPath { get; init; }
    public string? SpzPath { get; init; }
    public string? ImagePath { get; init; }
    public RenderRequest? Render { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
    public override string ToString() => $"{Name} · {UiText.Status(Status)}";
}

public sealed record TaskPageRequest(int Offset = 0, int Limit = 50, string? Kind = null, string? Search = null);
public sealed record TaskPage(IReadOnlyList<LocalTask> Items, int Offset, int Total, bool HasMore);
