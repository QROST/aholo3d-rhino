namespace Aholo3D.Core;

public sealed record ObjMaterial(MeshMaterial Surface,MeshTexture? Texture,double ScaleU=1,double ScaleV=1,double OffsetU=0,double OffsetV=0,bool Decal=false,bool LegacyPreview=false)
{
    public static ObjMaterial Default { get; }=new(new(.18,.18,.18),null);
}

/// <summary>Used OBJ materials only. Legacy illumination is an explicit PBR preview; unsupported effective maps fail.</summary>
public static class ObjMaterials
{
    private sealed record Definition(string Name,ObjSourceFile Source,List<ObjSyntax.Statement> Statements);
    public static IReadOnlyDictionary<string,ObjMaterial> Read(ObjSourceSnapshot source,ObjGeometry geometry,CancellationToken ct=default)
    {
        var used=geometry.Faces.Where(f=>f.Material is not null).Select(f=>f.Material!).ToHashSet(StringComparer.Ordinal);
        var definitions=new Dictionary<string,Definition>(StringComparer.Ordinal);
        foreach(var file in geometry.MaterialLibraries)
        {
            Definition? current=null;bool named=false;
            foreach(var statement in ObjSyntax.Statements(file,ct))
            {
                var args=statement.Tokens;
                if(args[0]=="newmtl")
                {
                    ObjSyntax.Require(args.Length>=2,"MTL newmtl requires a name.");string name=string.Join(' ',args.Skip(1));current=null;named=true;
                    if(used.Contains(name))
                    {current=new(name,file,new());ObjSyntax.Require(definitions.TryAdd(name,current),UiText.Format("A used OBJ material is defined more than once: {0}",name));}
                }
                else if(current is not null)current.Statements.Add(statement);
                else if(!named)throw ObjSyntax.Invalid("MTL statements must follow newmtl.");
            }
        }
        var result=new Dictionary<string,ObjMaterial>(StringComparer.Ordinal);
        var textures=new Dictionary<(string Hash,MeshTextureWrap Wrap),MeshTexture>();
        foreach(string name in used)
        {
            ct.ThrowIfCancellationRequested();ObjSyntax.Require(definitions.TryGetValue(name,out var definition),UiText.Format("OBJ references an undefined material: {0}",name));
            try{result.Add(name,Decode(source,definition!,textures,ct));}
            catch(Exception error) when(error is InvalidDataException or ArgumentException or OverflowException)
            {throw new InvalidDataException(UiText.Format("{0}: material {1}: {2}",definition!.Source.Name,name,error.Message),error);}
        }
        return result;
    }
    private static ObjMaterial Decode(ObjSourceSnapshot source,Definition definition,Dictionary<(string Hash,MeshTextureWrap Wrap),MeshTexture> textures,CancellationToken ct)
    {
        double[] color=[.18,.18,.18];double opacity=1,metallic=0,roughness=.75;double? dissolve=null,transparency=null,ns=null,pr=null;
        string[]? map=null;bool legacy=false;var seen=new Dictionary<string,string[]>(StringComparer.Ordinal);
        foreach(var statement in definition.Statements)
        {
            ct.ThrowIfCancellationRequested();var args=statement.Tokens;string op=args[0];
            if(seen.TryGetValue(op,out var previous))
            {ObjSyntax.Require(previous.SequenceEqual(args),UiText.Format("Conflicting MTL declarations: {0}",op));continue;}
            seen.Add(op,args);
            switch(op)
            {
                case "Kd":color=Color(args);break;
                case "d":dissolve=Unit(Scalar(args));break;
                case "Tr":transparency=Unit(Scalar(args));break;
                case "Pm":metallic=Unit(Scalar(args));break;
                case "Pr":pr=Unit(Scalar(args));break;
                case "Ns":ns=Scalar(args);ObjSyntax.Require(ns>=0,"MTL Ns cannot be negative.");legacy=true;break;
                case "Ka":case "Ks":_=Color(args);legacy=true;break;
                case "Ni":ObjSyntax.Require(Scalar(args)>0,"MTL optical density must be positive.");legacy=true;break;
                case "illum":double mode=Scalar(args);ObjSyntax.Require(mode is 0 or 1 or 2,"This OBJ illumination model requires unsupported reflection/refraction.");legacy=true;break;
                case "Ke":ObjSyntax.Require(Color(args).All(x=>x==0),"Emissive OBJ materials are not yet supported.");break;
                case "Tf":ObjSyntax.Require(Color(args).All(x=>x==1),"Colored OBJ transmission is not yet supported.");break;
                case "map_Kd":map=args;break;
                default:throw ObjSyntax.Invalid(UiText.Format("Unsupported used MTL statement: {0}",op));
            }
        }
        if(dissolve.HasValue&&transparency.HasValue)ObjSyntax.Require(Math.Abs(dissolve.Value+transparency.Value-1)<=1e-12,"MTL d and Tr disagree.");
        opacity=dissolve??(transparency.HasValue?1-transparency.Value:1);
        roughness=pr??(ns.HasValue?Math.Sqrt(2/(ns.Value+2)):.75);
        var material=new MeshMaterial(color[0],color[1],color[2],opacity,metallic,roughness);material.Validate();
        if(map is null)return new(material,null,LegacyPreview:legacy);
        double su=1,sv=1,ou=0,ov=0;bool decal=false;int index=1;var options=new HashSet<string>(StringComparer.Ordinal);
        while(index<map.Length&&map[index].StartsWith('-'))
        {
            string option=map[index++];ObjSyntax.Require(options.Add(option),UiText.Format("Duplicate OBJ texture option: {0}",option));
            switch(option)
            {
                case "-s":case "-o":case "-t":
                    var values=new List<double>();
                    while(index<map.Length&&values.Count<3&&double.TryParse(map[index],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out _))values.Add(ObjSyntax.Number(map[index++]));
                    ObjSyntax.Require(values.Count>0,"OBJ texture transform is missing a number.");
                    if(option=="-s")
                    {su=values[0];sv=values.Count>1?values[1]:1;ObjSyntax.Require(values.Count<3||values[2]==1,"Three-dimensional OBJ texture scale is unsupported.");}
                    else if(option=="-o")
                    {ou=values[0];ov=values.Count>1?values[1]:0;ObjSyntax.Require(values.Count<3||values[2]==0,"Three-dimensional OBJ texture offset is unsupported.");}
                    else ObjSyntax.Require(values.All(x=>x==0),"OBJ texture turbulence is unsupported.");break;
                case "-clamp":
                    ObjSyntax.Require(index<map.Length&&map[index] is "on" or "off","OBJ clamp requires on or off.");decal=map[index++]=="on";break;
                case "-blendu":case "-blendv":ObjSyntax.Require(index<map.Length&&map[index++]=="on","Non-default directional OBJ filtering is unsupported.");break;
                case "-cc":ObjSyntax.Require(index<map.Length&&map[index++]=="off","OBJ texture color correction is unsupported; provide an sRGB image.");break;
                case "-mm":ObjSyntax.Require(index+1<map.Length&&ObjSyntax.Number(map[index++])==0&&ObjSyntax.Number(map[index++])==1,"Non-default OBJ texture gain/bias is unsupported.");break;
                default:throw ObjSyntax.Invalid(UiText.Format("Unsupported OBJ texture option: {0}",option));
            }
        }
        ObjSyntax.Require(index==map.Length-1,"OBJ texture requires one relative filename; quote filenames containing spaces.");
        var image=source.Resolve(definition.Source.Name,map[index],ct);
        var wrap=decal?MeshTextureWrap.Clamp:MeshTextureWrap.Repeat;
        // map_Kd modulates RGB. Dissolve is d/map_d, never inferred from a PNG alpha channel.
        if(!textures.TryGetValue((image.Sha256,wrap),out var texture))
        {texture=new MeshTexture(ReadImage(image.Path,ct),wrap,wrap,useAlpha:false,ct:ct);textures.Add((image.Sha256,wrap),texture);}
        return new(material,texture,su,sv,ou,ov,decal,legacy);
    }
    private static byte[] ReadImage(string path,CancellationToken ct)
    {
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
        ObjSyntax.Require(input.Length<=Array.MaxLength,"The OBJ bitmap exceeds the runtime byte-array capacity.");
        byte[] bytes=new byte[(int)input.Length];int offset=0;
        while(offset<bytes.Length){ct.ThrowIfCancellationRequested();int read=input.Read(bytes,offset,Math.Min(131072,bytes.Length-offset));if(read==0)throw new EndOfStreamException();offset+=read;}
        ct.ThrowIfCancellationRequested();return bytes;
    }
    private static double Scalar(string[] args){ObjSyntax.Require(args.Length==2,"MTL scalar requires one number.");return ObjSyntax.Number(args[1]);}
    private static double Unit(double value){ObjSyntax.Require(value>=0&&value<=1,"MTL color, opacity and PBR factors must be between zero and one.");return value;}
    private static double[] Color(string[] args)
    {
        ObjSyntax.Require(args.Length is 2 or 4,"Only scalar or RGB MTL colors are supported.");
        double r=Unit(ObjSyntax.Number(args[1]));return args.Length==2?[r,r,r]:[r,Unit(ObjSyntax.Number(args[2])),Unit(ObjSyntax.Number(args[3]))];
    }
}
