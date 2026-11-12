namespace Aholo3D.Core;

/// <summary>OBJ has no standardized length units or up axis; these are explicit import choices.</summary>
public sealed record ObjImportOptions(double MetersPerUnit,string UpAxis,string? EntryName=null)
{
    public void Validate()
    {
        if(!double.IsFinite(MetersPerUnit)||MetersPerUnit<=0||!double.IsFinite(MetersPerUnit*1000)||UpAxis is not ("Y" or "Z"))
            throw new ArgumentException(UiText.Get("Choose positive finite OBJ length units and a Y or Z up axis."));
    }
}

/// <summary>Host-independent OBJ compiler. Splits after smoothing, retaining per-object double origins.</summary>
public static class ObjMeshReader
{
    private readonly record struct SmoothKey(int Vertex,int Group);
    private readonly record struct VertexKey(int Vertex,int? Uv,int? Normal,int Group,int Face);
    private readonly record struct PartKey(int Object,string? Material,bool Uv,string Groups);
    public static UsdMeshScene Read(string path,ObjImportOptions options,string? expectedSha256=null,CancellationToken ct=default)
    {
        options.Validate();using var source=ObjSourceSnapshot.Create(path,expectedSha256,options.EntryName,ct);
        return Read(source,options,ct);
    }
    public static UsdMeshScene Read(ObjSourceSnapshot source,ObjImportOptions options,CancellationToken ct=default)
    {
        options.Validate();ct.ThrowIfCancellationRequested();
        if(options.EntryName is not null&&!source.EntryName.Equals(ObjSourceSnapshot.Normalize(options.EntryName,false),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UiText.Get("The chosen OBJ entry does not match the captured source."));
        var geometry=ObjSyntax.Read(source,ct);var materials=ObjMaterials.Read(source,geometry,ct);
        var polygons=new PolygonTriangles[geometry.Faces.Length];var smooth=new Dictionary<SmoothKey,GaussianVector>();
        var partitions=new Dictionary<PartKey,List<int>>();var bounds=new Dictionary<int,(GaussianVector Min,GaussianVector Max)>();
        for(int f=0;f<geometry.Faces.Length;f++)
        {
            ct.ThrowIfCancellationRequested();var face=geometry.Faces[f];
            try
            {
                var points=new GaussianVector[face.Corners.Length];
                for(int c=0;c<points.Length;c++){if((c&4095)==0)ct.ThrowIfCancellationRequested();points[c]=geometry.Positions[face.Corners[c].Vertex];}
                // All polygons must be simple and planar; nonplanar quads are not silently flattened.
                polygons[f]=PolygonTriangulator.Triangulate(points,ct);
                var normal=polygons[f].UnitNormal;
                for(int c=0;c<points.Length;c++)
                {
                    if((c&4095)==0)ct.ThrowIfCancellationRequested();var p=points[c];
                    if(!bounds.TryGetValue(face.ObjectOrdinal,out var box))box=(p,p);
                    bounds[face.ObjectOrdinal]=(Min(box.Min,p),Max(box.Max,p));
                    if(face.SmoothingGroup==0)continue;
                    var previous=(points[(c+points.Length-1)%points.Length]-p).Normalized();
                    var next=(points[(c+1)%points.Length]-p).Normalized();var cross=Cross(next,previous);
                    double angle=Math.Atan2(cross.Length,GaussianVector.Dot(next,previous));
                    if(GaussianVector.Dot(cross,normal)<0)angle=2*Math.PI-angle;
                    var key=new SmoothKey(face.Corners[c].Vertex,face.SmoothingGroup);
                    smooth.TryGetValue(key,out var accumulated);smooth[key]=accumulated+normal*angle;
                }
                var part=new PartKey(face.ObjectOrdinal,face.Material,face.Corners[0].TexCoord.HasValue,string.Join('\0',face.Groups));
                if(!partitions.TryGetValue(part,out var faces))partitions.Add(part,faces=new());faces.Add(f);
            }
            catch(Exception error) when(error is InvalidDataException or ArgumentException or OverflowException)
            {throw new InvalidDataException($"{source.EntryName}:{face.SourceLine}: {error.Message}",error);}
        }
        var result=new List<UsdMeshPrimitive>();int ordinal=0;
        foreach(var partition in partitions)
        {
            ct.ThrowIfCancellationRequested();var key=partition.Key;var first=geometry.Faces[partition.Value[0]];
            var material=key.Material is null?ObjMaterial.Default:materials[key.Material];
            ObjSyntax.Require(material.Texture is null||key.Uv,"An OBJ face uses a bitmap material without texture coordinates.");
            var box=bounds[key.Object];var origin=box.Min*.5+box.Max*.5;
            var vertices=new Dictionary<VertexKey,int>();var positions=new List<float>();var normals=new List<float>();var uv=new List<float>();var indices=new List<int>();
            foreach(int f in partition.Value)
            {
                ct.ThrowIfCancellationRequested();var face=geometry.Faces[f];var mapping=new int[face.Corners.Length];
                for(int c=0;c<face.Corners.Length;c++)
                {
                    if((c&4095)==0)ct.ThrowIfCancellationRequested();var corner=face.Corners[c];
                    var vertex=new VertexKey(corner.Vertex,corner.TexCoord,corner.Normal,corner.Normal.HasValue?0:face.SmoothingGroup,corner.Normal.HasValue||face.SmoothingGroup!=0?-1:f);
                    if(!vertices.TryGetValue(vertex,out int index))
                    {
                        index=vertices.Count;vertices.Add(vertex,index);var p=geometry.Positions[corner.Vertex]-origin;
                        positions.Add(Float(p.X));positions.Add(Float(p.Y));positions.Add(Float(p.Z));
                        GaussianVector n;
                        try{n=(corner.Normal.HasValue?geometry.Normals[corner.Normal.Value]:face.SmoothingGroup==0?polygons[f].UnitNormal:smooth[new(corner.Vertex,face.SmoothingGroup)]).Normalized();}
                        catch(ArgumentException error){throw new InvalidDataException(UiText.Get("OBJ smoothing cancels to a zero normal; provide explicit normals or separate smoothing groups."),error);}
                        normals.Add((float)n.X);normals.Add((float)n.Y);normals.Add((float)n.Z);
                        if(corner.TexCoord.HasValue)
                        {
                            var original=geometry.TexCoords[corner.TexCoord.Value];double u=original.U*material.ScaleU+material.OffsetU,v=original.V*material.ScaleV+material.OffsetV;
                            ObjSyntax.Require(!material.Decal||u>=0&&u<=1&&v>=0&&v<=1,"MTL -clamp on outside [0,1] requires decal compositing and cannot be represented by edge clamping.");
                            uv.Add(Float(u));uv.Add(Float(v));
                        }
                    }
                    mapping[c]=index;
                }
                var triangles=polygons[f].Corners;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    if((i&4095)==0)ct.ThrowIfCancellationRequested();int a=mapping[triangles[i]],b=mapping[triangles[i+1]],c=mapping[triangles[i+2]];
                    var pa=Point(positions,a);var pb=Point(positions,b);var pc=Point(positions,c);
                    var cross=Cross(pb-pa,pc-pa);
                    ObjSyntax.Require(cross.IsFinite&&GaussianVector.Dot(cross,polygons[f].UnitNormal)>0,"OBJ triangulation loses orientation or collapses at mesh display precision.");
                    indices.Add(a);indices.Add(b);indices.Add(c);
                }
            }
            string objectPath="/obj/"+key.Object.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string name=first.ObjectName.Length==0?System.IO.Path.GetFileNameWithoutExtension(source.EntryName):first.ObjectName;
            if(key.Material is not null)name+=" · "+key.Material;
            result.Add(new(objectPath+"/part"+(ordinal++).ToString(System.Globalization.CultureInfo.InvariantCulture),name,positions.ToArray(),Enumerable.Repeat(3,indices.Count/3).ToArray(),indices.ToArray(),normals.ToArray(),key.Uv?uv.ToArray():null,new(GaussianMatrix.Identity,origin),false,true,material.Surface,material.Texture)
                {SourcePrimPath=objectPath,SourceGroups=first.Groups});
        }
        var warnings=new List<string>{"OBJ units and up axis use the explicit import choices. Numeric material colors are interpreted as linear; image colors are sRGB."};
        if(materials.Values.Any(x=>x.LegacyPreview))warnings.Add("Used legacy OBJ illumination is approximated by a PBR preview; ambient, specular and optical-density terms are not reproduced exactly.");
        var scene=new UsdMeshScene(result,options.UpAxis,options.MetersPerUnit,source.SourceSha256,source.DependencyHashes,warnings){SourceEntry=source.EntryName};
        foreach(var part in result){ct.ThrowIfCancellationRequested();_=scene.ToMillimetres(part.Transform);}return scene;
    }
    private static GaussianVector Min(GaussianVector a,GaussianVector b)=>new(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Min(a.Z,b.Z));
    private static GaussianVector Max(GaussianVector a,GaussianVector b)=>new(Math.Max(a.X,b.X),Math.Max(a.Y,b.Y),Math.Max(a.Z,b.Z));
    private static GaussianVector Cross(GaussianVector a,GaussianVector b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    private static GaussianVector Point(List<float> values,int vertex)=>new(values[vertex*3],values[vertex*3+1],values[vertex*3+2]);
    private static float Float(double value)
    {float result=(float)value;ObjSyntax.Require(float.IsFinite(result),"OBJ attribute exceeds mesh display precision.");return result;}
}
