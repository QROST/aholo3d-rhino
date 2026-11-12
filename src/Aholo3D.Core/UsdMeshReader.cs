using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aholo3D.Core;

/// <summary>Static polygon mesh. Positions and normals remain in the source prim's local frame; UVs use USD's lower-left origin.</summary>
public sealed record UsdMeshPrimitive(string PrimPath, string Name, float[] Positions, int[] FaceCounts, int[] Indices,
    float[]? Normals, float[]? TexCoords, GaussianTransform Transform, bool LeftHanded, bool DoubleSided, MeshMaterial Material, MeshTexture? Texture)
{
    public string SourcePrimPath { get; init; } = PrimPath;
    public bool GeneratedFacetNormals { get; init; }
    public IReadOnlyList<string> SourceGroups { get; init; } = Array.Empty<string>();
}
public sealed record UsdMeshScene(IReadOnlyList<UsdMeshPrimitive> Parts, string UpAxis, double MetersPerUnit,
    string SourceSha256, IReadOnlyList<string> DependencyHashes, IReadOnlyList<string> Warnings)
{
    public string? SourceEntry { get; init; }
    /// <summary>Proper axis rotation followed by physical-unit conversion, independent of placement height.</summary>
    public GaussianTransform ToMillimetres(GaussianTransform transform)
    {
        var basis = UpAxis == "Z" ? GaussianMatrix.Identity : new GaussianMatrix(1,0,0,0,0,-1,0,1,0);
        double scale = MetersPerUnit * 1000;
        var scaled = new GaussianMatrix(scale,0,0,0,scale,0,0,0,scale) * basis;
        var result = new GaussianTransform(scaled * transform.Linear, scaled.Transform(transform.Translation));
        if (!result.Linear.IsFinite || !result.Translation.IsFinite) throw new InvalidDataException("Source physical coordinates exceed numeric precision.");
        _ = result.Linear.Inverse(); return result;
    }
}

/// <summary>Strict static mesh decoder through pinned, isolated OpenUSD. Does not invoke Rhino or a cloud service.</summary>
public static class UsdMeshReader
{
    public static UsdMeshScene Read(string path, string? expectedSha256 = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string input = Path.GetFullPath(path);
        if (Path.GetExtension(input).ToLowerInvariant() is not (".usd" or ".usda" or ".usdc" or ".usdz"))
            throw new NotSupportedException("Choose a USD, USDA, USDC or USDZ file.");
        string workspace = UsdBridgeProcess.CreateWorkspace();
        try
        {
            UsdBridgeProcess.Run(input, workspace, ct, mesh: true);
            var scene = ReadBridgeOutput(workspace, ct);
            if (expectedSha256 is not null && !scene.SourceSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(UiText.Get("The mesh hash does not match the task record."));
            return scene;
        }
        finally { UsdBridgeProcess.RemoveWorkspace(workspace); }
    }

    public static UsdMeshScene ReadBridgeOutput(string directory, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(Path.Combine(directory, "mesh-import.json"));
        using var json = JsonDocument.Parse(stream); var root = json.RootElement;
        if (root.GetProperty("protocol").GetInt32() != 1 || root.GetProperty("openUsdVersion").GetString() != "26.11") throw Invalid("Unsupported USD mesh transport.");
        string axis = root.GetProperty("upAxis").GetString() ?? "";
        double units = root.GetProperty("metersPerUnit").GetDouble();
        if (axis is not ("Y" or "Z") || !double.IsFinite(units) || units <= 0) throw Invalid("Invalid USD physical units or axis.");
        string hash = root.GetProperty("sourceSha256").GetString() ?? "";
        if (!Regex.IsMatch(hash, @"\A[a-fA-F0-9]{64}\z")) throw Invalid("Missing USD source hash.");
        string[] dependencies = root.GetProperty("dependencyHashes").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        if (dependencies.Length == 0 || dependencies.Any(x => !Regex.IsMatch(x,@"\A[a-fA-F0-9]{64}\z")) || !dependencies.Contains(hash,StringComparer.OrdinalIgnoreCase)) throw Invalid("Invalid USD dependency hashes.");
        var parts = new List<UsdMeshPrimitive>(); var names = new HashSet<string>(StringComparer.Ordinal); var files = new HashSet<string>(StringComparer.Ordinal);
        var images = new Dictionary<string,byte[]>(StringComparer.Ordinal);
        foreach (var part in root.GetProperty("parts").EnumerateArray())
        {
            ct.ThrowIfCancellationRequested();
            string name = part.GetProperty("primPath").GetString() ?? "", display = part.GetProperty("name").GetString() ?? "";
            if (!name.StartsWith('/') || !names.Add(name)) throw Invalid("USD prim paths must be distinct absolute paths.");
            string sourcePrim = part.TryGetProperty("sourcePrimPath",out var sourcePath) ? sourcePath.GetString() ?? "" : name;
            if (!sourcePrim.StartsWith('/') || (sourcePrim != name && name[..name.LastIndexOf('/')] != sourcePrim))
                throw Invalid("USD material parts must identify their source mesh prim.");
            string file = part.GetProperty("file").GetString() ?? "";
            if (!Regex.IsMatch(file, @"\Amesh-[0-9]{6,}\.bin\z") || !files.Add(file)) throw Invalid("Invalid USD mesh payload path.");
            int vertices = part.GetProperty("vertices").GetInt32(), faces = part.GetProperty("faces").GetInt32(), count = part.GetProperty("indices").GetInt32();
            bool hasNormals = part.GetProperty("normals").GetBoolean(), hasUv = part.GetProperty("uv").GetBoolean();
            bool generatedNormals = part.TryGetProperty("generatedNormals",out var generated) && generated.GetBoolean();
            if(generatedNormals && !hasNormals)throw Invalid("Generated USD facet normals are missing from the payload.");
            if (vertices <= 0 || faces <= 0 || count <= 0) throw Invalid("Empty USD mesh.");
            long scalars = checked((long)vertices * (3 + (hasNormals ? 3 : 0) + (hasUv ? 2 : 0)) + faces + count);
            string payload = Path.Combine(directory, file); var info = new FileInfo(payload);
            if (info.LinkTarget is not null || info.Length != checked(scalars * 4)) throw Invalid("Incomplete or linked USD mesh payload.");
            float[] positions, normals, uv; int[] counts, indices;
            using (var reader = new BinaryReader(File.OpenRead(payload)))
            {
                positions = Floats(reader, checked(vertices * 3), ct); counts = Integers(reader, faces, ct); indices = Integers(reader, count, ct);
                normals = hasNormals ? Floats(reader, checked(vertices * 3), ct) : Array.Empty<float>();
                uv = hasUv ? Floats(reader, checked(vertices * 2), ct) : Array.Empty<float>();
            }
            if (counts.Any(x => x is not (3 or 4)) || counts.Sum(x => (long)x) != count || indices.Any(x => x < 0 || x >= vertices))
                throw Invalid("USD mesh payload topology does not match its triangulated vertex array.");
            for (int i = 0; i < normals.Length; i += 3) _ = new GaussianVector(normals[i], normals[i+1], normals[i+2]).Normalized();
            double[] matrix = Tuple(part.GetProperty("transform"),12);
            var transform = new GaussianTransform(new(matrix[0],matrix[1],matrix[2],matrix[3],matrix[4],matrix[5],matrix[6],matrix[7],matrix[8]),new(matrix[9],matrix[10],matrix[11]));
            _ = transform.Linear.Inverse();
            double[] color = Tuple(part.GetProperty("color"),4);
            var material = new MeshMaterial(color[0],color[1],color[2],color[3],part.GetProperty("metallic").GetDouble(),part.GetProperty("roughness").GetDouble()); material.Validate();
            MeshTexture? texture = null;
            if (part.TryGetProperty("texture",out var image) && image.ValueKind != JsonValueKind.Null)
            {
                if (!hasUv) throw Invalid("USD image requires UVs.");
                string imageFile = image.GetProperty("file").GetString() ?? "";
                if (!Regex.IsMatch(imageFile,@"\A[a-f0-9]{64}\.(png|jpg)\z")) throw Invalid("Invalid USD image payload path.");
                if (!images.TryGetValue(imageFile,out var bytes))
                {
                    var imageInfo = new FileInfo(Path.Combine(directory,imageFile));
                    if (imageInfo.LinkTarget is not null) throw Invalid("USD image payload cannot be linked.");
                    bytes = File.ReadAllBytes(imageInfo.FullName); ct.ThrowIfCancellationRequested(); images.Add(imageFile,bytes);
                }
                texture = new MeshTexture(bytes,(MeshTextureWrap)image.GetProperty("wrapS").GetInt32(),(MeshTextureWrap)image.GetProperty("wrapT").GetInt32(),
                    (MeshTextureFilter)image.GetProperty("minFilter").GetInt32(),(MeshTextureFilter)image.GetProperty("magFilter").GetInt32(),image.GetProperty("useAlpha").GetBoolean(),ct);
                if (!texture.Sha256.Equals(Path.GetFileNameWithoutExtension(imageFile),StringComparison.OrdinalIgnoreCase) ||
                    (texture.MimeType == "image/png" ? ".png" : ".jpg") != Path.GetExtension(imageFile)) throw Invalid("USD image snapshot hash or media type does not match.");
            }
            parts.Add(new(name,display,positions,counts,indices,hasNormals ? normals : null,hasUv ? uv : null,transform,part.GetProperty("leftHanded").GetBoolean(),part.GetProperty("doubleSided").GetBoolean(),material,texture){SourcePrimPath=sourcePrim,GeneratedFacetNormals=generatedNormals});
        }
        if (parts.Count == 0) throw Invalid("The USD contains no supported meshes.");
        var result = new UsdMeshScene(parts,axis,units,hash,dependencies,root.GetProperty("warnings").EnumerateArray().Select(x => x.GetString() ?? "").ToArray());
        foreach (var part in parts) _ = result.ToMillimetres(part.Transform);
        return result;
    }
    private static double[] Tuple(JsonElement value,int length)
    {
        var values = value.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        if (values.Length != length || values.Any(x => !double.IsFinite(x))) throw Invalid("Invalid USD numeric tuple."); return values;
    }
    private static float[] Floats(BinaryReader reader,int count,CancellationToken ct)
    {
        var values = new float[count];
        for (int i=0;i<count;i++) { if ((i&4095)==0) ct.ThrowIfCancellationRequested(); values[i]=reader.ReadSingle(); if (!float.IsFinite(values[i])) throw Invalid("Non-finite USD attribute."); }
        return values;
    }
    private static int[] Integers(BinaryReader reader,int count,CancellationToken ct)
    {
        var values = new int[count]; for(int i=0;i<count;i++){if((i&4095)==0)ct.ThrowIfCancellationRequested();values[i]=reader.ReadInt32();}return values;
    }
    private static InvalidDataException Invalid(string message) => new(message);
}
