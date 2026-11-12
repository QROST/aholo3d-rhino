using System.Drawing;
using Aholo3D.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc=global::Rhino.RhinoDoc;

namespace Aholo3D.Rhino;

internal static partial class Aholo3DHost
{
    // Borrowed document material references live only inside synchronous UI capture.
    private sealed record MaterialBinding(Material? Material,Color FallbackColor);
    private enum SurfaceUvSource { AuthoredMesh, OriginalRenderMesh, Remeshed }
    private sealed record CapturedMeshPart(Mesh Mesh,MaterialSnapshot Appearance,SurfaceUvSource SurfaceUv);

    private static MaterialBinding CaptureMaterialBinding(RhinoObject obj,RhinoDoc doc,Lazy<MaterialBinding>? parent)
    {
        Color fallback=obj.Attributes.DrawColor(doc);var source=obj.Attributes.MaterialSource;
        if(source==ObjectMaterialSource.MaterialFromParent)
        {
            if(parent is not null)return parent.Value.Material is not null?parent.Value:new(null,fallback);
            source=ObjectMaterialSource.MaterialFromLayer;
        }
        int index=source switch
        {
            ObjectMaterialSource.MaterialFromObject=>obj.Attributes.MaterialIndex,
            ObjectMaterialSource.MaterialFromLayer=>doc.Layers[obj.Attributes.LayerIndex].RenderMaterialIndex,
            _=>throw UnsupportedFaceMaterial()
        };
        if(index<0)return new(null,fallback);
        if(index>=doc.Materials.Count)throw MissingFaceMaterial();
        // Preserve Rhino's effective front material, including renderer-specific references.
        var material=obj.GetMaterial(true);
        if(material is null||material.IsDeleted)throw MissingFaceMaterial();
        return new(material,fallback);
    }

    private static InvalidOperationException MissingFaceMaterial()=>new(UiText.Get("A face material is missing. Repair its material assignment before export."));
    private static InvalidOperationException UnsupportedFaceMaterial()=>new(UiText.Get("A sub-object material cannot be resolved. Convert the object to separate material parts before export."));

    private static CapturedMeshPart[] CaptureMeshParts(RhinoObject obj,MaterialBinding basis,RhinoDoc doc,bool includeTextures,IReadOnlyCollection<int> objectChannels,IReadOnlyCollection<int> parentChannels)
    {
        var result=new List<CapturedMeshPart>();
        var prepared=new Dictionary<MaterialBinding,(MaterialSnapshot Appearance,bool Surface)>();
        (MaterialSnapshot Appearance,bool Surface) Prepare(MaterialBinding binding)
        {
            if(prepared.TryGetValue(binding,out var existing))return existing;
            var appearance=CaptureMaterial(binding,doc,includeTextures);
            bool surface=false;
            if(appearance.Texture is { } bitmap)
            {
                using var mapping=ResolveTextureMapping(obj,bitmap,objectChannels,parentChannels,out _);
                surface=mapping.MappingType==global::Rhino.Render.TextureMappingType.SurfaceParameters;
            }
            var value=(appearance,surface);prepared.Add(binding,value);return value;
        }
        void Add(Mesh[]? meshes,MaterialSnapshot appearance,SurfaceUvSource source=SurfaceUvSource.Remeshed)
        {
            if(meshes is null||meshes.Length==0)throw new InvalidOperationException(UiText.Get("A selected object cannot be converted to a mesh. Nothing has been uploaded."));
            foreach(var mesh in meshes)result.Add(new(mesh,appearance,source));
        }
        MaterialBinding[] FaceBindings(Brep brep)=>FaceMaterialBindings.Resolve(brep.Faces.Select(f=>f.MaterialChannelIndex).ToArray(),basis,
            (material,channel)=>material.Material?.MaterialChannelIdFromIndex(channel)??Guid.Empty,
            id=>{int index=doc.Materials.Find(id,true);return index<0?null:new(doc.Materials[index],basis.FallbackColor);});
        void ComponentBindings(MaterialBinding[] mapped,Func<ComponentIndex,ComponentIndex> toBrep)
        {
            var components=obj.SubobjectMaterialComponents;var seen=new HashSet<int>();
            if(components.Length==0)
            {
                if(obj.HasSubobjectMaterials)throw UnsupportedFaceMaterial();
                return;
            }
            using var attributes=obj.Attributes.Duplicate();
            if(basis.Material is { } baseMaterial)
            {
                if(baseMaterial.MaterialIndex<0)throw UnsupportedFaceMaterial();
                attributes.MaterialRefs.Clear();
                attributes.MaterialSource=ObjectMaterialSource.MaterialFromObject;attributes.MaterialIndex=baseMaterial.MaterialIndex;
            }

            foreach(var component in components)
            {
                var face=toBrep(component);
                if(face.ComponentIndexType!=ComponentIndexType.BrepFace||face.Index<0||face.Index>=mapped.Length||!seen.Add(face.Index))throw UnsupportedFaceMaterial();
                // No assigned effective base means no channel table. Never substitute the leaf layer.
                if(basis.Material is null)continue;
                var material=obj.GetMaterial(component,Guid.Empty,attributes);
                if(material is null||material.IsDeleted)throw MissingFaceMaterial();
                mapped[face.Index]=new(material,basis.FallbackColor);
            }
        }
        void BrepParts(Brep brep,MaterialBinding[] bindings,bool separate,bool originalBrep)
        {
            if(bindings.Length==0)throw UnsupportedFaceMaterial();
            var appearances=bindings.Select(Prepare).ToArray();
            if(appearances.Any(a=>a.Surface))
            {
                // Keep both source material identity and the entire supported appearance when merging an extrusion.
                if(!originalBrep&&(bindings.Any(b=>b.Material?.Id!=bindings[0].Material?.Id)||appearances.Any(a=>a.Appearance!=appearances[0].Appearance)))
                    throw new InvalidOperationException(UiText.Get("This extrusion has different face materials and surface UVs. Convert it to a polysurface, use explicit object mapping, or disable bitmap export."));
                // Generate a missing whole-object cache once; never repack individual faces.
                EnsureOriginalRenderMeshes(obj);
                if(originalBrep)
                {
                    if(obj.Geometry is not Brep current||current.Faces.Count!=bindings.Length)throw OriginalRenderMeshMissing();
                    for(int face=0;face<current.Faces.Count;face++)
                    {
                        using var cached=current.Faces[face].GetMesh(MeshType.Render);
                        if(cached is null)throw OriginalRenderMeshMissing();
                        Add(new[]{cached.DuplicateMesh()},appearances[face].Appearance,SurfaceUvSource.OriginalRenderMesh);
                    }
                }
                else
                {
                    // An extrusion cache has no public original-component-to-mesh-face correspondence.
                    var appearance=appearances[0].Appearance;
                    if(obj.Geometry is not Extrusion current)throw OriginalRenderMeshMissing();
                    using var cached=current.GetMesh(MeshType.Render);
                    if(cached is null)throw OriginalRenderMeshMissing();
                    Add(new[]{cached.DuplicateMesh()},appearance,SurfaceUvSource.OriginalRenderMesh);
                }
                return;
            }
            if(!separate){Add(Mesh.CreateFromBrep(brep,MeshingParameters.Default),appearances[0].Appearance);return;}
            // A whole-brep mesh array has no documented face correspondence. Keep each source face explicit.
            for(int face=0;face<brep.Faces.Count;face++)
            {
                using var single=brep.Faces[face].DuplicateFace(false);
                if(single is null)throw UnsupportedFaceMaterial();
                Add(Mesh.CreateFromBrep(single,MeshingParameters.Default),appearances[face].Appearance);
            }
        }
        try
        {
            switch(obj.Geometry)
            {
                case Mesh mesh:
                    if(obj.HasSubobjectMaterials)throw UnsupportedFaceMaterial();
                    var appearance=Prepare(basis).Appearance;
                    Add(new[]{mesh.DuplicateMesh()},appearance,SurfaceUvSource.AuthoredMesh);break;
                case Brep brep:
                    var bindings=FaceBindings(brep);
                    ComponentBindings(bindings,component=>component);
                    BrepParts(brep,bindings,brep.Faces.Any(f=>f.MaterialChannelIndex!=0)||obj.SubobjectMaterialComponents.Length!=0||obj.HasSubobjectMaterials,true);break;
                case Extrusion extrusion:
                    // Component mapping is defined only for the unsplit Brep form.
                    using(var converted=extrusion.ToBrep(false))
                    {
                        if(converted is null)throw UnsupportedFaceMaterial();
                        var mapped=FaceBindings(converted);var components=obj.SubobjectMaterialComponents;
                        ComponentBindings(mapped,component=>
                        {
                            if(component.ComponentIndexType is not (ComponentIndexType.ExtrusionWallSurface or ComponentIndexType.ExtrusionCapSurface))throw UnsupportedFaceMaterial();
                            return extrusion.GetBrepFormComponentIndex(component);
                        });
                        BrepParts(converted,mapped,components.Length!=0||converted.Faces.Any(f=>f.MaterialChannelIndex!=0),false);
                    }
                    break;
                default:throw UnsupportedFaceMaterial();
            }
            return result.ToArray();
        }
        catch{foreach(var part in result)part.Mesh.Dispose();throw;}
    }

    private static InvalidOperationException OriginalRenderMeshMissing()=>new(UiText.Get("Rhino could not prepare complete original render meshes. Repair the object or regenerate its render meshes before exporting surface textures."));
    private static void EnsureOriginalRenderMeshes(RhinoObject obj)
    {
        var document=obj.Document;
        if(document is null||obj.IsDeleted)throw OriginalRenderMeshMissing();
        uint documentSerial=document.RuntimeSerialNumber;
        bool Complete()
        {
            if(obj.IsDeleted||obj.Document?.RuntimeSerialNumber!=documentSerial)return false;
            switch(obj.Geometry)
            {
                case Brep brep:
                    if(brep.Faces.Count==0)return false;
                    foreach(var face in brep.Faces)
                    {
                        using var mesh=face.GetMesh(MeshType.Render);
                        if(mesh is null||mesh.Vertices.Count==0||mesh.Faces.Count==0)return false;
                    }
                    return true;
                case Extrusion extrusion:
                    using(var mesh=extrusion.GetMesh(MeshType.Render))
                        return mesh is not null&&mesh.Vertices.Count>0&&mesh.Faces.Count>0;
                default:return false;
            }
        }
        if(Complete())return;
        using var parameters=obj.GetRenderMeshParameters();
        if(parameters is null)throw OriginalRenderMeshMissing();
        _=obj.CreateMeshes(MeshType.Render,parameters,false);
        if(!Complete())throw OriginalRenderMeshMissing();
    }
}
