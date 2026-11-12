using System.Xml.Linq;
namespace Aholo3D.Core;

public static partial class ThreeMfReader
{
    private sealed record Face(int[] Vertices,int Group,int[] Properties,GaussianVector Normal);
    private readonly record struct MaterialPart(int Group,int Color);
    private static UsdMeshPrimitive[] ReadMesh(XElement owner,XElement mesh,Dictionary<int,object> defined,CancellationToken ct)
    {
        Attributes(mesh);Children(mesh,Core+"vertices",Core+"triangles");
        var vertexElement=Single(mesh,Core+"vertices");var triangleElement=Single(mesh,Core+"triangles");
        Require(!vertexElement.ElementsBeforeSelf().Any());Attributes(vertexElement);Attributes(triangleElement);Children(vertexElement,Core+"vertex");Children(triangleElement,Core+"triangle");
        var vertices=new List<GaussianVector>();
        foreach(var vertex in vertexElement.Elements())
        {
            ct.ThrowIfCancellationRequested();Attributes(vertex,"x","y","z");Children(vertex);
            var point=new GaussianVector(Number(Text(vertex,"x")),Number(Text(vertex,"y")),Number(Text(vertex,"z")));
            vertices.Add(point);
        }
        Require(vertices.Count>=3);
        bool hasDefault=owner.Attribute("pid") is not null||owner.Attribute("pindex") is not null;
        int defaultGroup=0,defaultProperty=0;
        if(hasDefault)
        {
            defaultGroup=Integer(owner,"pid",true);defaultProperty=Integer(owner,"pindex");
            _=Property(defined,defaultGroup,defaultProperty);
        }
        var faces=new List<Face>();var groups=new Dictionary<MaterialPart,List<int>>();
        GaussianVector min=default,max=default;bool hasBounds=false;
        foreach(var triangle in triangleElement.Elements())
        {
            ct.ThrowIfCancellationRequested();Attributes(triangle,"v1","v2","v3","pid","p1","p2","p3");Children(triangle);
            int[] v={Integer(triangle,"v1"),Integer(triangle,"v2"),Integer(triangle,"v3")};Require(v.All(i=>i<vertices.Count));
            // Only referenced geometry contributes to float rebasing; unused vertices must not reduce precision.
            foreach(int index in v)
            {
                var point=vertices[index];
                if(!hasBounds){min=max=point;hasBounds=true;}
                else{min=new(Math.Min(min.X,point.X),Math.Min(min.Y,point.Y),Math.Min(min.Z,point.Z));max=new(Math.Max(max.X,point.X),Math.Max(max.Y,point.Y),Math.Max(max.Z,point.Z));}
            }
            int group=defaultGroup;int[] properties={defaultProperty,defaultProperty,defaultProperty};
            if(triangle.Attribute("p1") is not null)
            {
                Require(hasDefault,"3MF triangle properties require an object default property.");
                group=triangle.Attribute("pid") is null?defaultGroup:Integer(triangle,"pid",true);
                properties[0]=Integer(triangle,"p1");
                // Missing p2/p3 inherit p1 independently, matching official lib3mf's corner handling.
                properties[1]=triangle.Attribute("p2") is null?properties[0]:Integer(triangle,"p2");
                properties[2]=triangle.Attribute("p3") is null?properties[0]:Integer(triangle,"p3");
            }
            else
            {
                // p1 absent means the entire object default; still reject malformed ignored numeric fields.
                foreach(string name in new[]{"pid","p2","p3"})if(triangle.Attribute(name) is not null)_=Integer(triangle,name,name=="pid");
                if(triangle.Attribute("pid") is not null||triangle.Attribute("p2") is not null||triangle.Attribute("p3") is not null)Require(hasDefault);
            }
            int color=0;
            if(group!=0)
            {
                var property=Property(defined,group,properties[0]);_=Property(defined,group,properties[1]);_=Property(defined,group,properties[2]);
                if(property.Colors is { } colors)
                {
                    if(property.BaseMaterials)Require(properties.All(p=>p==properties[0]),"A 3MF triangle cannot interpolate different base materials.");
                    else Require(colors[properties[0]]==colors[properties[1]]&&colors[properties[0]]==colors[properties[2]],"3MF vertex color gradients are not yet supported. Use a texture or uniform triangle colors.");
                    color=properties[0];
                }
            }
            var normal=PolygonTriangulator.Triangulate(v.Select(i=>vertices[i]).ToArray(),ct).UnitNormal;
            var key=new MaterialPart(group,color);if(!groups.TryGetValue(key,out var list))groups.Add(key,list=new());
            list.Add(faces.Count);faces.Add(new(v,group,properties,normal));
        }
        Require(faces.Count>0);var origin=min*.5+max*.5;var output=new List<UsdMeshPrimitive>();
        foreach(var pair in groups)
        {
            ct.ThrowIfCancellationRequested();PropertyGroup? group=pair.Key.Group==0?null:(PropertyGroup)defined[pair.Key.Group];
            var material=group?.Colors is {} colors?colors[pair.Key.Color]:group?.Texture is not null?new MeshMaterial(1,1,1):new MeshMaterial(.18,.18,.18);
            int count=checked(pair.Value.Count*3);var positions=new float[checked(count*3)];var normals=new float[positions.Length];var indices=new int[count];var uv=group?.Texture is null?null:new float[checked(count*2)];
            int at=0;
            foreach(int faceIndex in pair.Value)
            {
                ct.ThrowIfCancellationRequested();var face=faces[faceIndex];var display=new GaussianVector[3];
                for(int corner=0;corner<3;corner++)
                {
                    int vertex=at+corner;var point=vertices[face.Vertices[corner]]-origin;
                    positions[vertex*3]=Float(point.X);positions[vertex*3+1]=Float(point.Y);positions[vertex*3+2]=Float(point.Z);
                    display[corner]=new(positions[vertex*3],positions[vertex*3+1],positions[vertex*3+2]);
                    normals[vertex*3]=(float)face.Normal.X;normals[vertex*3+1]=(float)face.Normal.Y;normals[vertex*3+2]=(float)face.Normal.Z;indices[vertex]=vertex;
                    if(uv is not null)
                    {var coordinate=group!.Coordinates![face.Properties[corner]];uv[vertex*2]=Float(coordinate.U);uv[vertex*2+1]=Float(coordinate.V);}
                }
                var displayedNormal=PolygonTriangulator.Triangulate(display,ct).UnitNormal;
                Require(GaussianVector.Dot(displayedNormal,face.Normal)>0,"3MF triangles collapse or change orientation at mesh display precision.");at+=3;
            }
            string objectPath="/3mf/objects/"+Integer(owner,"id",true);
            output.Add(new(objectPath+"/part"+output.Count,(string?)owner.Attribute("name")??("Object "+Integer(owner,"id",true)),positions,Enumerable.Repeat(3,pair.Value.Count).ToArray(),indices,normals,uv,new(GaussianMatrix.Identity,origin),false,true,material,group?.Texture)
                {SourcePrimPath=objectPath,GeneratedFacetNormals=true,SourceGroups=new[]{"propertyResource="+pair.Key.Group,"propertyIndex="+pair.Key.Color}});
        }
        return output.ToArray();
    }
    private static PropertyGroup Property(Dictionary<int,object> defined,int group,int index)
    {
        Require(defined.TryGetValue(group,out var resource)&&resource is PropertyGroup);
        var value=(PropertyGroup)resource!;Require(index>=0&&index<(value.Colors?.Length??value.Coordinates!.Length));return value;
    }
    private static float Float(double value)
    {float result=(float)value;Require(float.IsFinite(result),"3MF attributes exceed mesh display precision.");return result;}
}
