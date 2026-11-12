namespace Aholo3D.Core;

public enum PlyContentKind { Gaussian, Mesh, Points, Unsupported }
public sealed record PlyContent(PlyContentKind Kind, long Vertices, long Faces);

public static partial class PlyReader
{
    /// <summary>Classification only; the selected decoder must still validate the full captured payload.</summary>
    public static PlyContent Inspect(string path,CancellationToken ct=default)
    {
        using var stream=new BufferedStream(File.OpenRead(path),65536);
        return Classify(ReadHeader(stream,ct));
    }
    private static PlyContent Classify((string Format,List<Element> Elements,bool SphericalHarmonics) header)
    {
        var vertex=header.Elements.Single(x=>x.Name=="vertex");
        var face=header.Elements.SingleOrDefault(x=>x.Name=="face");
        var names=vertex.Properties.Where(p=>p.CountType is null).Select(p=>p.Name).ToHashSet(StringComparer.Ordinal);
        string[] required={"opacity","scale_0","scale_1","scale_2","rot_0","rot_1","rot_2","rot_3","f_dc_0","f_dc_1","f_dc_2"};
        bool gaussianMarker=vertex.Properties.Any(p=>p.Name=="opacity"||p.Name.StartsWith("scale_",StringComparison.Ordinal)||p.Name.StartsWith("rot_",StringComparison.Ordinal)||p.Name.StartsWith("f_dc_",StringComparison.Ordinal)||p.Name.StartsWith("f_rest_",StringComparison.Ordinal));
        bool extraElements=header.Elements.Any(e=>e.Count>0&&e.Name is not ("vertex" or "face"));
        PlyContentKind kind;
        if(vertex.Count==0||extraElements||vertex.Properties.Any(p=>p.CountType is not null))kind=PlyContentKind.Unsupported;
        else if(gaussianMarker)kind=required.All(names.Contains)&&(face?.Count??0)==0?PlyContentKind.Gaussian:PlyContentKind.Unsupported;
        else if(face?.Count>0&&face.Properties.Any(p=>p.Name is "vertex_indices" or "vertex_index"&&p.CountType is not null&&p.Type is not (Scalar.Float32 or Scalar.Float64)))kind=PlyContentKind.Mesh;
        else kind=(face?.Count??0)==0?PlyContentKind.Points:PlyContentKind.Unsupported;
        return new(kind,vertex.Count,face?.Count??0);
    }
    /// <summary>Validate mesh topology without allocating vertices/faces or imposing a geometry quota.</summary>
    public static PlyContent ValidateMesh(string path,CancellationToken ct=default)
    {
        using var stream=new BufferedStream(File.OpenRead(path),65536);
        var header=ReadHeader(stream,ct);var content=Classify(header);
        if(content.Kind!=PlyContentKind.Mesh)throw new InvalidDataException(UiText.Get("This PLY is not an ordinary mesh. Use Import GS for complete Gaussian data; point-only or ambiguous files are not converted."));
        var reader=new Values(stream,header.Format,ct);
        foreach(var element in header.Elements)
        for(long row=0;row<element.Count;row++)
        {
            ct.ThrowIfCancellationRequested();
            foreach(var property in element.Properties)
            {
                bool indices=element.Name=="face"&&property.Name is "vertex_indices" or "vertex_index";
                if(property.CountType is{} countType)
                {
                    double n=reader.Read(countType);
                    if(n<0||n>uint.MaxValue||n!=Math.Truncate(n)||(indices&&n<3))throw Invalid("Invalid mesh list count.");
                    for(long i=0;i<(long)n;i++)
                    {
                        if((i&4095)==0)ct.ThrowIfCancellationRequested();
                        double value=reader.Read(property.Type);
                        if(!double.IsFinite(value)||indices&&(value<0||value>=content.Vertices||value!=Math.Truncate(value)))throw Invalid("Invalid mesh face index or property.");
                    }
                }
                else if(!double.IsFinite(reader.Read(property.Type)))throw Invalid("Non-finite mesh property.");
            }
        }
        reader.RequireEnd();return content;
    }
}
