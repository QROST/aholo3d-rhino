using Aholo.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal static partial class AholoHost
{
    private sealed record TextureSnapshot(MeshTextureSource Source,Transform Uvw);
    private static InvalidOperationException TextureUnsupported()=>new(UiText.Get("This bitmap uses unsupported mapping, blending or color space. Use a directly textured mesh with existing UVs, or disable bitmap export."));
    private static TextureSnapshot? CaptureTexture(Material material,RhinoDoc doc)
    {
        using var texture=material.IsPhysicallyBased?material.PhysicallyBased.GetTexture(TextureType.PBR_BaseColor):material.GetBitmapTexture();
        if(texture is null||!texture.Enabled)return null;
        if(texture.TextureCombineMode!=TextureCombineMode.Modulate||texture.TreatAsLinear||texture.WcsProjected||texture.WcsBoxProjected||
            texture.ProjectionMode is not (TextureProjectionModes.MappingChannel or TextureProjectionModes.SurfaceParameterization))throw TextureUnsupported();
        var uvw=texture.ApplyUvwTransform?texture.UvwTransform:Transform.Identity;
        if(!uvw.IsValid||uvw.M30!=0||uvw.M31!=0||uvw.M32!=0||uvw.M33!=1||uvw.M02!=0||uvw.M12!=0||uvw.M20!=0||uvw.M21!=0||uvw.M23!=0)throw TextureUnsupported();
        string path=texture.FileName;
        if(string.IsNullOrWhiteSpace(path)||path.Contains("://",StringComparison.Ordinal)||path.StartsWith("\\\\",StringComparison.Ordinal)||path.StartsWith("//",StringComparison.Ordinal))throw TextureUnsupported();
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
        return new(new(file.FullName,file.Length,file.LastWriteTimeUtc.Ticks,Wrap(texture.WrapU),Wrap(texture.WrapV),Filter(texture.MinFilter),Filter(texture.MagFilter),alpha),uvw);
    }
    private static float[] CaptureTextureCoordinates(RhinoObject obj,Mesh mesh,TextureSnapshot bitmap,bool parentMapping)
    {
        if(parentMapping||obj.Geometry is not Mesh||obj.GetTextureChannels().Length!=0||mesh.TextureCoordinates.Count!=mesh.Vertices.Count)throw TextureUnsupported();
        var result=new float[checked(mesh.Vertices.Count*2)];
        for(int i=0;i<mesh.Vertices.Count;i++)
        {
            var uv=mesh.TextureCoordinates[i];var p=new Point3d(uv.X,uv.Y,0);p.Transform(bitmap.Uvw);
            result[i*2]=CheckedFloat(p.X);result[i*2+1]=CheckedFloat(1-p.Y);
        }
        return result;
    }
}
