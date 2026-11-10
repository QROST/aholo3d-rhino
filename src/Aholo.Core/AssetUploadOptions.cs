using System.Text.Json.Serialization;
namespace Aholo.Core;

/// <summary>Optional storage fields documented for multipart initialization only.</summary>
public sealed record AssetUploadOptions(string? Metadata = null, string? CustomPrefix = null, string? CustomFilename = null)
{
    [JsonIgnore] public bool HasValues => Metadata is not null || CustomPrefix is not null || CustomFilename is not null;
}
