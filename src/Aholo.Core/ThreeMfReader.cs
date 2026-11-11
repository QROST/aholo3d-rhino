using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
namespace Aholo.Core;

/// <summary>Static 3MF preview subset. Uses a private package copy and never invokes a native importer.</summary>
public static partial class ThreeMfReader
{
    private static readonly XNamespace Core="http://schemas.microsoft.com/3dmanufacturing/core/2015/02",Materials=ThreeMfPreflight.Materials;
    private const string TextureRelationship="http://schemas.microsoft.com/3dmanufacturing/2013/01/3dtexture";
    private sealed record PropertyGroup(MeshMaterial[]? Colors,MeshTexture? Texture,(double U,double V)[]? Coordinates,bool BaseMaterials);
    private sealed record ObjectResource(int Id,string Name,string Type,UsdMeshPrimitive[] Parts,Component[] Components);
    private sealed record Component(ObjectResource Object,GaussianTransform Transform);
    private sealed record ImageResource(MeshTexture Texture);
    private static InvalidDataException Invalid(string message)=>new(UiText.Get(message));
    private static void Require(bool valid,string message="The 3MF model has invalid structure, values or resource references.")
    {if(!valid)throw Invalid(message);}
    private static InvalidDataException Unsupported()=>Invalid("This 3MF feature is not supported by the static mesh reader. Export a supported static mesh instead.");

    public static UsdMeshScene Read(string path,string? expectedSha256=null,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        Require(Path.GetExtension(path).Equals(".3mf",StringComparison.OrdinalIgnoreCase),"Choose a 3MF file.");
        string directory=Path.Combine(Path.GetTempPath(),"Aholo-Rhino",Guid.NewGuid().ToString("N"));
        try
        {
            if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);
            else Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
            string copy=Path.Combine(directory,"source.3mf"),hash;
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            using(var output=new FileStream(copy,options))
            using(var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer=new byte[131072];int read;
                while((read=input.Read(buffer))>0){ct.ThrowIfCancellationRequested();digest.AppendData(buffer,0,read);output.Write(buffer,0,read);}
                ct.ThrowIfCancellationRequested();output.Flush(true);hash=Convert.ToHexString(digest.GetHashAndReset());
            }
            if(expectedSha256 is not null&&!hash.Equals(expectedSha256,StringComparison.OrdinalIgnoreCase))throw Invalid("The mesh hash does not match the task record.");
            var index=ThreeMfPreflight.Inspect(copy,true,ct);
            using var archive=ZipFile.OpenRead(copy);
            var entry=archive.GetEntry(index.PrimaryModel)??throw Invalid("The 3MF model has invalid structure, values or resource references.");
            var document=ThreeMfPreflight.ReadDocument(entry,ct);
            return Decode(document,index,archive,hash,ct);
        }
        catch(Exception error) when(error is XmlException or OverflowException or FormatException or ArgumentException)
        {throw new InvalidDataException(UiText.Get("The 3MF model has invalid structure, values or resource references."),error);}
        finally{try{if(Directory.Exists(directory))Directory.Delete(directory,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }

    private static UsdMeshScene Decode(XDocument document,ThreeMfPackageIndex package,ZipArchive archive,string hash,CancellationToken ct)
    {
        var root=document.Root;Require(root?.Name==Core+"model");
        Attributes(root!,"unit","requiredextensions","recommendedextensions","thumbnail");Children(root!,Core+"metadata",Core+"resources",Core+"build");
        double units=(string?)root!.Attribute("unit") switch{null or "millimeter"=>.001,"micron"=>.000001,"centimeter"=>.01,"inch"=>.0254,"foot"=>.3048,"meter"=>1,_=>throw Unsupported()};
        Metadata(root);
        var required=((string?)root.Attribute("requiredextensions")??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        foreach(string prefix in ((string?)root.Attribute("recommendedextensions")??"").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))
        {
            Require(!required.Contains(prefix));
            if(root.GetNamespaceOfPrefix(prefix)!=Materials)throw Unsupported();
        }
        var resources=Single(root,Core+"resources");var build=Single(root,Core+"build");Attributes(resources);Attributes(build);
        Require(resources.ElementsBeforeSelf().All(x=>x.Name==Core+"metadata")&&build.ElementsAfterSelf().Any()==false);
        var defined=new Dictionary<int,object>();
        foreach(var resource in resources.Elements())
        {
            ct.ThrowIfCancellationRequested();int id=Integer(resource,"id",true);Require(!defined.ContainsKey(id));
            object decoded;
            if(resource.Name==Core+"basematerials"||resource.Name==Materials+"colorgroup")
            {
                bool basis=resource.Name==Core+"basematerials";Attributes(resource,"id");XName item=basis?Core+"base":Materials+"color";Children(resource,item);
                var colors=new List<MeshMaterial>();
                foreach(var color in resource.Elements())
                {
                    ct.ThrowIfCancellationRequested();Attributes(color,basis?new[]{"name","displaycolor"}:new[]{"color"});Children(color);
                    if(basis)_=Text(color,"name");colors.Add(Color(Text(color,basis?"displaycolor":"color"),!basis));
                }
                Require(colors.Count>0);decoded=new PropertyGroup(colors.ToArray(),null,null,basis);
            }
            else if(resource.Name==Materials+"texture2d")decoded=ReadImage(resource,package,archive,ct);
            else if(resource.Name==Materials+"texture2dgroup")
            {
                Attributes(resource,"id","texid");Children(resource,Materials+"tex2coord");int imageId=Integer(resource,"texid",true);
                Require(defined.TryGetValue(imageId,out var value)&&value is ImageResource);
                var coordinates=new List<(double,double)>();
                foreach(var uv in resource.Elements())
                {ct.ThrowIfCancellationRequested();Attributes(uv,"u","v");Children(uv);coordinates.Add((Number(Text(uv,"u")),Number(Text(uv,"v"))));}
                Require(coordinates.Count>0);decoded=new PropertyGroup(null,((ImageResource)value!).Texture,coordinates.ToArray(),false);
            }
            else if(resource.Name==Core+"object")decoded=ReadObject(resource,defined,ct);
            else throw Unsupported();
            defined.Add(id,decoded);
        }
        Children(build,Core+"item");var parts=new List<UsdMeshPrimitive>();long occurrence=0;int itemNumber=0;
        foreach(var item in build.Elements())
        {
            ct.ThrowIfCancellationRequested();Attributes(item,"objectid","transform","partnumber");Children(item,Core+"metadatagroup");
            Metadata(item);
            int id=Integer(item,"objectid",true);Require(defined.TryGetValue(id,out var resource)&&resource is ObjectResource);
            var stack=new Stack<(ObjectResource Object,GaussianTransform Transform)>();stack.Push(((ObjectResource)resource!,Matrix((string?)item.Attribute("transform"))));
            while(stack.Count>0)
            {
                ct.ThrowIfCancellationRequested();var current=stack.Pop();
                if(current.Object.Components.Length!=0)
                {
                    for(int i=current.Object.Components.Length-1;i>=0;i--)
                    {ct.ThrowIfCancellationRequested();var child=current.Object.Components[i];stack.Push((child.Object,Compose(current.Transform,child.Transform)));}
                    continue;
                }
                Require(current.Object.Type!="other","A built 3MF object has an unsupported object type.");
                string source=$"/3mf/build{itemNumber}/instance{occurrence++}/object{current.Object.Id}";
                for(int i=0;i<current.Object.Parts.Length;i++)
                {
                    ct.ThrowIfCancellationRequested();var part=current.Object.Parts[i];
                    parts.Add(part with{PrimPath=source+"/part"+i,SourcePrimPath=source,Transform=Compose(current.Transform,part.Transform)});
                }
            }
            itemNumber++;
        }
        Require(parts.Count>0,"The 3MF build contains no supported meshes.");
        var scene=new UsdMeshScene(parts,"Z",units,hash,new[]{hash},new[]{"3MF is imported as static mesh instances with facet normals. Manufacturing fill/overlap semantics and editable hierarchy are not reconstructed."}){SourceEntry=package.PrimaryModel};
        foreach(var part in parts){ct.ThrowIfCancellationRequested();_=scene.ToMillimetres(part.Transform);}
        return scene;
    }

    private static ObjectResource ReadObject(XElement element,Dictionary<int,object> defined,CancellationToken ct)
    {
        Attributes(element,"id","type","thumbnail","partnumber","name","pid","pindex");Children(element,Core+"metadatagroup",Core+"mesh",Core+"components");
        Metadata(element);
        Require(element.Elements(Core+"metadatagroup").All(g=>!g.ElementsBeforeSelf().Any()));
        int id=Integer(element,"id",true);string name=(string?)element.Attribute("name")??("Object "+id),type=(string?)element.Attribute("type")??"model";
        Require(type is "model" or "solidsupport" or "support" or "surface" or "other");
        var mesh=element.Element(Core+"mesh");var components=element.Element(Core+"components");
        Require((mesh is null)!=(components is null));
        if(components is not null)
        {
            components=Single(element,Core+"components");
            Require(element.Attribute("pid") is null&&element.Attribute("pindex") is null);Attributes(components);Children(components,Core+"component");
            var children=new List<Component>();
            foreach(var child in components.Elements())
            {
                ct.ThrowIfCancellationRequested();Attributes(child,"objectid","transform");Children(child);
                int childId=Integer(child,"objectid",true);Require(defined.TryGetValue(childId,out var value)&&value is ObjectResource);
                children.Add(new((ObjectResource)value!,Matrix((string?)child.Attribute("transform"))));
            }
            Require(children.Count>0);return new(id,name,type,Array.Empty<UsdMeshPrimitive>(),children.ToArray());
        }
        return new(id,name,type,ReadMesh(element,Single(element,Core+"mesh"),defined,ct),Array.Empty<Component>());
    }

    private static ImageResource ReadImage(XElement element,ThreeMfPackageIndex package,ZipArchive archive,CancellationToken ct)
    {
        Attributes(element,"id","path","contenttype","tilestyleu","tilestylev","filter","box");Children(element);
        string reference=Text(element,"path"),mime=Text(element,"contenttype");Require(reference.StartsWith('/'));
        string key=ThreeMfPreflight.ResolvePartName(package.PrimaryModel,reference);
        Require(package.Parts.TryGetValue(key,out var part)&&(part.ContentType=="application/vnd.ms-package.3dmanufacturing-3dmodeltexture"||part.ContentType==mime));
        Require(package.Relationships.Contains((ThreeMfPreflight.NormalizePartName(package.PrimaryModel),TextureRelationship,key)));
        Require(mime is "image/png" or "image/jpeg");
        var entry=archive.GetEntry(part!.EntryName)!;Require(entry.Length<=Array.MaxLength,"The 3MF bitmap exceeds the runtime byte-array capacity.");
        byte[] data=new byte[(int)entry.Length];using(var stream=entry.Open())
        {
            int offset=0;while(offset<data.Length){ct.ThrowIfCancellationRequested();int read=stream.Read(data,offset,Math.Min(131072,data.Length-offset));Require(read>0);offset+=read;}Require(stream.ReadByte()<0);
        }
        MeshTextureWrap Wrap(string name)=>(string?)element.Attribute(name) switch{null or "wrap"=>MeshTextureWrap.Repeat,"clamp" or "none"=>MeshTextureWrap.Clamp,_=>throw Unsupported()};
        var filter=(string?)element.Attribute("filter") switch{null or "auto" or "linear"=>MeshTextureFilter.Linear,"nearest"=>MeshTextureFilter.Nearest,_=>throw Unsupported()};
        // Direct texture2dgroup is an opaque base surface. Alpha is meaningful only for layered multiproperties.
        var image=new MeshTexture(data,Wrap("tilestyleu"),Wrap("tilestylev"),filter,filter,false,ct);Require(image.MimeType==mime);
        return new(image);
    }

    private static MeshMaterial Color(string text,bool opaque)
    {
        Require(text.Length is 7 or 9&&text[0]=='#'&&text[1..].All(Uri.IsHexDigit));
        double Channel(int at)=>byte.Parse(text.AsSpan(at,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture)/255d;
        return new(Channel(1),Channel(3),Channel(5),opaque||text.Length==7?1:Channel(7),ColorEncoding:MeshColorEncoding.Srgb);
    }
    private static GaussianTransform Matrix(string? text)
    {
        if(text is null)return GaussianTransform.Identity;
        var values=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();Require(values.Length==12);
        // 3MF supplies a row-vector matrix; our representation multiplies column vectors.
        return new(new(values[0],values[3],values[6],values[1],values[4],values[7],values[2],values[5],values[8]),new(values[9],values[10],values[11]));
    }
    private static GaussianTransform Compose(GaussianTransform a,GaussianTransform b)
    {
        var result=new GaussianTransform(a.Linear*b.Linear,a.TransformPoint(b.Translation));Require(result.Linear.IsFinite&&result.Translation.IsFinite);return result;
    }
    private static string Text(XElement element,string name)=>(string?)element.Attribute(name)??throw Invalid("The 3MF model has invalid structure, values or resource references.");
    private static int Integer(XElement element,string name,bool positive=false)
    {
        string text=Text(element,name);Require(int.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int value)&&(positive?value>0:value>=0));return value;
    }
    private static double Number(string text)
    {Require(double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)&&double.IsFinite(value));return value;}
    private static XElement Single(XElement element,XName name)
    {var found=element.Elements(name).ToArray();Require(found.Length==1);return found[0];}
    private static void Attributes(XElement element,params string[] allowed)
    {
        foreach(var attribute in element.Attributes())
            if(!attribute.IsNamespaceDeclaration&&attribute.Name!=XNamespace.Xml+"lang"&&(attribute.Name.NamespaceName.Length!=0||!allowed.Contains(attribute.Name.LocalName,StringComparer.Ordinal)))throw Unsupported();
    }
    private static void Metadata(XElement owner)
    {
        var groups=owner.Elements(Core+"metadatagroup").ToArray();Require(groups.Length<=1);
        foreach(var group in groups){Attributes(group);Children(group,Core+"metadata");Require(group.HasElements);}
        foreach(var item in owner.Elements(Core+"metadata").Concat(groups.SelectMany(g=>g.Elements())))
        {
            Attributes(item,"name","preserve","type");Require(!item.HasElements&&Text(item,"name").Length>0);
            Require((string?)item.Attribute("preserve") is null or "0" or "1" or "false" or "true");
        }
    }
    private static void Children(XElement element,params XName[] allowed)
    {
        foreach(var child in element.Elements())if(!allowed.Contains(child.Name))throw Unsupported();
        if(element.Nodes().OfType<XText>().Any(t=>!string.IsNullOrWhiteSpace(t.Value)))throw Invalid("The 3MF model has invalid structure, values or resource references.");
    }
}
