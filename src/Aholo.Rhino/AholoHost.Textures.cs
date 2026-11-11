using Aholo.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.Render;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal static partial class AholoHost
{
    private sealed record TextureSnapshot(MeshTextureSource Source,TextureUvTransform Uvw,int Channel,bool SurfaceParameters);
    private static InvalidOperationException TextureUnsupported()=>new(UiText.Get("This bitmap uses unsupported mapping, blending or color space. Use supported object mapping or mesh surface UVs, or disable bitmap export."));
    private static TextureSnapshot? CaptureTexture(Material material,RhinoDoc doc)
    {
        using var texture=material.IsPhysicallyBased?material.PhysicallyBased.GetTexture(TextureType.PBR_BaseColor):material.GetBitmapTexture();
        if(texture is null||!texture.Enabled)return null;
        if(texture.TextureCombineMode!=TextureCombineMode.Modulate||texture.TreatAsLinear||texture.WcsProjected||texture.WcsBoxProjected||
            texture.ProjectionMode is not (TextureProjectionModes.MappingChannel or TextureProjectionModes.SurfaceParameterization))throw TextureUnsupported();
        var uvw=texture.ApplyUvwTransform?texture.UvwTransform:Transform.Identity;
        var bitmapTransform=ReadTextureUvTransform(uvw);
        string path=texture.FileName;
        if(string.IsNullOrWhiteSpace(path)||path.Contains("://",StringComparison.Ordinal)||path.StartsWith("\\\\",StringComparison.Ordinal)||path.StartsWith("//",StringComparison.Ordinal))throw TextureUnsupported();
        if(!Path.IsPathFullyQualified(path)||!File.Exists(path))
        {
            _=doc.Bitmaps.Find(texture.FileName,true,out string embeddedPath);
            if(!string.IsNullOrEmpty(embeddedPath))path=embeddedPath;
        }
        if(!Path.IsPathFullyQualified(path))
        {
            if(string.IsNullOrEmpty(doc.Path))throw new InvalidOperationException(UiText.Get("The bitmap path is unresolved. Save the document with local bitmap files or disable bitmap export."));
            path=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(doc.Path)!,path));
        }
        MeshTextureSource.RequireLocalPath(path);
        var file=new FileInfo(path);
        if(!file.Exists)throw new InvalidOperationException(UiText.Get("A base-color bitmap file is missing. Relink the local bitmap in Rhino or disable bitmap export."));
        MeshTextureWrap Wrap(TextureUvwWrapping value)=>value switch{TextureUvwWrapping.Repeat=>MeshTextureWrap.Repeat,TextureUvwWrapping.Clamp=>MeshTextureWrap.Clamp,_=>throw TextureUnsupported()};
        MeshTextureFilter Filter(TextureFilter value)=>value switch{TextureFilter.Nearest=>MeshTextureFilter.Nearest,TextureFilter.Linear=>MeshTextureFilter.Linear,_=>throw TextureUnsupported()};
        bool alpha=material.IsPhysicallyBased&&material.PhysicallyBased.UseBaseColorTextureAlphaForObjectAlphaTransparencyTexture;
        return new(new(file.FullName,file.Length,file.LastWriteTimeUtc.Ticks,Wrap(texture.WrapU),Wrap(texture.WrapV),Filter(texture.MinFilter),Filter(texture.MagFilter),alpha),bitmapTransform,texture.MappingChannelId,texture.ProjectionMode==TextureProjectionModes.SurfaceParameterization);
    }
    private static TextureUvTransform ReadTextureUvTransform(Transform m)
    {
        if(!m.IsValid)throw TextureUnsupported();
        try{return TextureUvTransform.FromMatrix(new[]{m.M00,m.M01,m.M02,m.M03,m.M10,m.M11,m.M12,m.M13,m.M20,m.M21,m.M22,m.M23,m.M30,m.M31,m.M32,m.M33});}
        catch(ArgumentException){throw TextureUnsupported();}
    }
    private static float[] CaptureTextureCoordinates(RhinoObject obj,Mesh mesh,TextureSnapshot bitmap,IReadOnlyCollection<int> objectChannels,IReadOnlyCollection<int> parentChannels)
    {
        int? channel=TextureMappingSelection.Select(bitmap.Channel,bitmap.SurfaceParameters,objectChannels,parentChannels);
        Transform mappingTransform=Transform.Identity;
        using var mapping=channel is { } selected?obj.GetTextureMapping(selected,out mappingTransform):TextureMapping.CreateSurfaceParameterMapping();
        if(mapping is null||!mapping.IsValid)throw TextureUnsupported();
        _=ReadTextureUvTransform(mapping.UvwTransform); // Native mapping applies it; validation must not apply it again.
        if(mapping.MappingType==TextureMappingType.SurfaceParameters)
        {
            // Freshly remeshed individual Brep faces do not retain the original packed surface domains.
            if(obj.Geometry is not Mesh)throw new InvalidOperationException(UiText.Get("Surface-parameter textures on surfaces need their original packed UVs. Use an explicit object mapping or a textured mesh, or disable bitmap export."));
            // Preserve existing surface parameters. If absent, derive them from authored mesh UVs when possible.
            _=mesh.SetSurfaceParametersFromTextureCoordinates();
        }
        else if(mapping.MappingType is not (TextureMappingType.PlaneMapping or TextureMappingType.BoxMapping or TextureMappingType.CylinderMapping or TextureMappingType.SphereMapping))throw TextureUnsupported();
        if(!mappingTransform.IsValid||mappingTransform.M30!=0||mappingTransform.M31!=0||mappingTransform.M32!=0||mappingTransform.M33!=1||!mappingTransform.TryGetInverse(out _))throw TextureUnsupported();
        mesh.InvalidateCachedTextureCoordinates(false);
        mesh.TextureCoordinates.Destroy();
        if(mapping.MappingType==TextureMappingType.SurfaceParameters)
        {
            // Surface failures can leave count-correct zero UVs. Use the bool API after clearing both caches.
            if(!mesh.TextureCoordinates.SetTextureCoordinates(mapping))throw new InvalidOperationException(UiText.Get("Rhino could not compute texture coordinates for this mapping. Repair the mapping or disable bitmap export."));
        }
        else mesh.SetTextureCoordinates(mapping,mappingTransform,false,true);
        if(mesh.Vertices.Count==0||mesh.TextureCoordinates.Count!=mesh.Vertices.Count)throw new InvalidOperationException(UiText.Get("Rhino could not compute texture coordinates for this mapping. Repair the mapping or disable bitmap export."));
        var source=new float[checked(mesh.Vertices.Count*2)];
        for(int i=0;i<mesh.Vertices.Count;i++)
        {
            var uv=mesh.TextureCoordinates[i];source[i*2]=uv.X;source[i*2+1]=uv.Y;
        }
        return bitmap.Uvw.ToGltf(source);
    }
}
