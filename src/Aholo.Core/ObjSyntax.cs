using System.Globalization;
using System.Text;

namespace Aholo.Core;

public readonly record struct ObjCorner(int Vertex,int? TexCoord,int? Normal);
public sealed record ObjFace(ObjCorner[] Corners,string ObjectName,int ObjectOrdinal,string[] Groups,string? Material,int SmoothingGroup,int SourceLine);
public sealed record ObjGeometry(GaussianVector[] Positions,GaussianVector[] Normals,(double U,double V)[] TexCoords,ObjFace[] Faces,ObjSourceFile[] MaterialLibraries);

/// <summary>Static polygon OBJ syntax. Preserves source corners, smoothing and material scope before any splitting.</summary>
public static class ObjSyntax
{
    public static ObjGeometry Read(ObjSourceSnapshot source,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();var root=source.Root(ct);
        var positions=new List<GaussianVector>();var normals=new List<GaussianVector>();var texCoords=new List<(double,double)>();var faces=new List<ObjFace>();
        var libraries=new Dictionary<string,ObjSourceFile>(StringComparer.OrdinalIgnoreCase);
        string name="";string[] groups=[];string? material=null;int smoothing=0,objectOrdinal=0;
        foreach(var statement in Statements(root,ct))
        {
            var args=statement.Tokens;string op=args[0];
            try
            {
                switch(op)
                {
                    case "v":
                        if(args.Length is not (4 or 5))throw Invalid("OBJ vertices require XYZ and an optional rational weight; vertex-color extensions are not yet supported.");
                        var p=new GaussianVector(Number(args[1]),Number(args[2]),Number(args[3]));
                        if(args.Length==5)
                        {Require(Number(args[4])==1,"Non-unit rational OBJ weights require a freeform surface and cannot be interpreted as polygon coordinates.");}
                        if(!p.IsFinite)throw Invalid("OBJ vertex exceeds numeric precision.");positions.Add(p);break;
                    case "vn":
                        Require(args.Length==4,"OBJ normals require three coordinates.");
                        var n=new GaussianVector(Number(args[1]),Number(args[2]),Number(args[3]));
                        _=n.Normalized();normals.Add(n);break;
                    case "vt":
                        Require(args.Length is >=2 and <=4,"OBJ texture coordinates require one to three coordinates.");
                        double u=Number(args[1]),v=args.Length>=3?Number(args[2]):0;
                        if(args.Length==4)Require(Number(args[3])==0,"Three-dimensional OBJ texture coordinates are not supported.");
                        texCoords.Add((u,v));break;
                    case "f":
                        Require(args.Length>=4,"OBJ faces need at least three corners.");
                        var corners=new ObjCorner[args.Length-1];
                        for(int i=0;i<corners.Length;i++)
                        {
                            if((i&4095)==0)ct.ThrowIfCancellationRequested();
                            string[] indices=args[i+1].Split('/');Require(indices.Length is >=1 and <=3,"Invalid OBJ face corner.");
                            int vertex=Index(indices[0],positions.Count);
                            int? uv=indices.Length>=2&&indices[1].Length>0?Index(indices[1],texCoords.Count):null;
                            int? normal=indices.Length==3&&indices[2].Length>0?Index(indices[2],normals.Count):null;
                            Require(indices.Length!=2||uv.HasValue,"OBJ v/ is not a complete corner.");
                            Require(indices.Length!=3||normal.HasValue,"OBJ v/vt/ is not a complete corner.");
                            corners[i]=new(vertex,uv,normal);
                        }
                        Require(corners.All(c=>c.TexCoord.HasValue==corners[0].TexCoord.HasValue)&&corners.All(c=>c.Normal.HasValue==corners[0].Normal.HasValue),"Each OBJ face must use a consistent corner attribute form.");
                        faces.Add(new(corners,name,objectOrdinal,(string[])groups.Clone(),material,smoothing,statement.Line));break;
                    case "o":name=string.Join(' ',args.Skip(1));objectOrdinal=checked(objectOrdinal+1);break;
                    case "g":groups=args.Skip(1).Distinct(StringComparer.Ordinal).ToArray();break;
                    case "usemtl":Require(args.Length>=2,"OBJ usemtl requires a material name.");material=string.Join(' ',args.Skip(1));break;
                    case "mtllib":
                        Require(args.Length>=2,"OBJ mtllib requires a relative library path.");
                        foreach(string reference in args.Skip(1))
                        {var file=source.Resolve(root.Name,reference,ct);if(!Path.GetExtension(file.Name).Equals(".mtl",StringComparison.OrdinalIgnoreCase))throw Invalid("OBJ material libraries require an MTL extension.");libraries.TryAdd(file.Name,file);}break;
                    case "s":
                        Require(args.Length==2,"OBJ smoothing requires off, on or a non-negative group number.");
                        smoothing=args[1] switch{"off"=>0,"on"=>1,_=>NonNegative(args[1])};break;
                    default:throw Invalid("Unsupported OBJ statement: "+op+". Only static polygon meshes are accepted; no geometry or shading statements are silently omitted.");
                }
            }
            catch(Exception error) when(error is InvalidDataException or ArgumentException or OverflowException)
            {throw new InvalidDataException($"{root.Name}:{statement.Line}: {error.Message}",error);}
        }
        Require(faces.Count>0&&positions.Count>0,"OBJ has no polygon mesh geometry.");ct.ThrowIfCancellationRequested();
        return new(positions.ToArray(),normals.ToArray(),texCoords.ToArray(),faces.ToArray(),libraries.Values.ToArray());
    }
    internal sealed record Statement(int Line,string[] Tokens);
    internal static IEnumerable<Statement> Statements(ObjSourceFile file,CancellationToken ct)
    {
        using var input=file.OpenText();int line=0,start=0;var logical=new StringBuilder();bool pending=false;
        foreach(string physical in Lines(input,ct))
        {
            ct.ThrowIfCancellationRequested();line++;if(!pending)start=line;
            string value=StripComment(physical,ct).TrimEnd();
            bool continuation=value.EndsWith('\\');if(continuation)value=value[..^1];
            logical.Append(value);logical.Append(' ');pending=continuation;
            if(continuation)continue;
            string[] tokens=Tokens(logical.ToString(),ct);logical.Clear();if(tokens.Length>0)yield return new(start,tokens);
        }
        Require(!pending,"Unterminated OBJ/MTL continuation.");
    }
    private static IEnumerable<string> Lines(TextReader input,CancellationToken ct)
    {
        var line=new StringBuilder();char[] buffer=new char[4096];bool carriage=false;int count;
        while(true)
        {
            ct.ThrowIfCancellationRequested();count=input.Read(buffer,0,buffer.Length);if(count==0)break;
            for(int i=0;i<count;i++)
            {
                char c=buffer[i];if(c=='\n'&&carriage){carriage=false;continue;}
                carriage=c=='\r';
                if(c is '\r' or '\n'){yield return line.ToString();line.Clear();}
                else line.Append(c);
            }
        }
        ct.ThrowIfCancellationRequested();if(line.Length>0)yield return line.ToString();
    }
    private static string StripComment(string value,CancellationToken ct)
    {
        bool quoted=false,tokenStart=true;
        for(int i=0;i<value.Length;i++)
        {
            if((i&4095)==0)ct.ThrowIfCancellationRequested();char c=value[i];
            if(c=='"'&&(quoted||tokenStart))quoted=!quoted;
            else if(c=='#'&&!quoted)return value[..i];
            tokenStart=char.IsWhiteSpace(c)&&!quoted;
        }
        return value;
    }
    // Double quotes delimit paths/names; apostrophes and Windows separators remain literal.
    internal static string[] Tokens(string value,CancellationToken ct=default)
    {
        var result=new List<string>();var token=new StringBuilder();bool quote=false;
        for(int i=0;i<value.Length;i++)
        {
            if((i&4095)==0)ct.ThrowIfCancellationRequested();char c=value[i];
            if(c=='"'&&(quote||token.Length==0))quote=!quote;
            else if(char.IsWhiteSpace(c)&&!quote){if(token.Length>0){result.Add(token.ToString());token.Clear();}}
            else {if(char.IsControl(c))throw Invalid("Control characters are not valid OBJ/MTL text.");token.Append(c);}
        }
        Require(!quote,"Unterminated OBJ/MTL quoted name.");if(token.Length>0)result.Add(token.ToString());return result.ToArray();
    }
    internal static double Number(string value)
    {if(!double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out double result)||!double.IsFinite(result))throw Invalid("Expected a finite OBJ/MTL number.");return result;}
    private static int NonNegative(string value)
    {if(!int.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out int result)||result<0)throw Invalid("Invalid OBJ smoothing group.");return result;}
    private static int Index(string value,int count)
    {
        if(!int.TryParse(value,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int index)||index==0)throw Invalid("OBJ indices are nonzero integers.");
        long actual=index>0?(long)index-1:(long)count+index;
        if(actual<0||actual>=count)throw Invalid("OBJ index is outside the values declared before this face.");return (int)actual;
    }
    internal static void Require(bool condition,string message){if(!condition)throw Invalid(message);}
    internal static InvalidDataException Invalid(string message)=>new(message);
}
