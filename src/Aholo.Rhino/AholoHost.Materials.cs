using System.Drawing;
using Aholo.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc=global::Rhino.RhinoDoc;

namespace Aholo.Rhino;

internal static partial class AholoHost
{
    // Borrowed document material references live only inside synchronous UI capture.
    private sealed record MaterialBinding(Material? Material,Color FallbackColor);
    private sealed record CapturedMeshPart(Mesh Mesh,MaterialBinding Binding);

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

    private static CapturedMeshPart[] CaptureMeshParts(RhinoObject obj,MaterialBinding basis,RhinoDoc doc)
    {
        var result=new List<CapturedMeshPart>();
        void Add(Mesh[]? meshes,MaterialBinding material)
        {
            if(meshes is null||meshes.Length==0)throw new InvalidOperationException(UiText.Get("A selected object cannot be converted to a mesh. Nothing has been uploaded."));
            foreach(var mesh in meshes)result.Add(new(mesh,material));
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
        void BrepParts(Brep brep,MaterialBinding[] bindings,bool separate)
        {
            if(!separate){Add(Mesh.CreateFromBrep(brep,MeshingParameters.Default),basis);return;}
            // A whole-brep mesh array has no documented face correspondence. Keep each source face explicit.
            for(int face=0;face<brep.Faces.Count;face++)
            {
                using var single=brep.Faces[face].DuplicateFace(false);
                if(single is null)throw UnsupportedFaceMaterial();
                Add(Mesh.CreateFromBrep(single,MeshingParameters.Default),bindings[face]);
            }
        }
        try
        {
            switch(obj.Geometry)
            {
                case Mesh mesh:
                    if(obj.HasSubobjectMaterials)throw UnsupportedFaceMaterial();
                    Add(new[]{mesh.DuplicateMesh()},basis);break;
                case Brep brep:
                    var bindings=FaceBindings(brep);
                    ComponentBindings(bindings,component=>component);
                    BrepParts(brep,bindings,brep.Faces.Any(f=>f.MaterialChannelIndex!=0)||obj.SubobjectMaterialComponents.Length!=0||obj.HasSubobjectMaterials);break;
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
                        BrepParts(converted,mapped,components.Length!=0||converted.Faces.Any(f=>f.MaterialChannelIndex!=0));
                    }
                    break;
                default:throw UnsupportedFaceMaterial();
            }
            return result.ToArray();
        }
        catch{foreach(var part in result)part.Mesh.Dispose();throw;}
    }
}
