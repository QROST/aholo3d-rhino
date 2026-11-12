using Aholo3D.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo3D.Rhino;

internal static partial class GaussianDocument
{
    internal sealed record Occurrence(InstanceObject Object,GaussianInstanceOccurrence Instance);
    private sealed record OccurrenceSnapshot(IReadOnlyList<Occurrence> Items,int CyclicDefinitions,int MissingDefinitions);
    internal static IReadOnlyList<Occurrence> Occurrences(RhinoDoc doc,global::Rhino.Display.RhinoViewport? viewport,bool includeHidden=false)
        =>CaptureOccurrences(doc,viewport,includeHidden).Items;
    private sealed record NativeGraph(GaussianInstanceNode[] Roots,Func<Guid,IReadOnlyList<GaussianInstanceNode>?> Children,Dictionary<Guid,InstanceObject> Objects);
    private static OccurrenceSnapshot CaptureOccurrences(RhinoDoc doc,global::Rhino.Display.RhinoViewport? viewport,bool includeHidden=false)
    {
        var source=CaptureGraph(doc,viewport,includeHidden);
        var graph=GaussianInstanceGraph.Expand(source.Roots,source.Children,includeHidden);
        return new(graph.Occurrences.Select(o=>new Occurrence(source.Objects[o.ObjectId],o)).ToArray(),graph.CyclicDefinitions,graph.MissingDefinitions);
    }
    private sealed record ResourceSnapshot(InstanceObject[] Objects,int CyclicDefinitions,int MissingDefinitions);
    private static InstanceObject[] ResourceInstances(RhinoDoc doc)=>CaptureResources(doc).Objects;
    private static ResourceSnapshot CaptureResources(RhinoDoc doc)
    {
        var source=CaptureGraph(doc,null,true);var graph=GaussianInstanceGraph.FindResources(source.Roots,source.Children);
        return new(graph.ObjectIds.Select(id=>source.Objects[id]).ToArray(),graph.CyclicDefinitions,graph.MissingDefinitions);
    }
    // UI-thread enumeration only. Definitions are read once per observation, while each placement remains distinct.
    private static NativeGraph CaptureGraph(RhinoDoc doc,global::Rhino.Display.RhinoViewport? viewport,bool includeHidden)
    {
        var settings=new ObjectEnumeratorSettings
        {
            ObjectTypeFilter=ObjectType.InstanceReference,HiddenObjects=includeHidden,NormalObjects=true,LockedObjects=true,
            IdefObjects=false,DeletedObjects=false,ActiveObjects=true,ReferenceObjects=false,VisibleFilter=!includeHidden
        };
        if(viewport is not null)settings.ViewportFilter=viewport;
        var objects=new Dictionary<Guid,InstanceObject>();var definitions=new Dictionary<Guid,InstanceDefinition>();
        var members=new Dictionary<Guid,IReadOnlyList<GaussianInstanceNode>>();var layers=new Dictionary<int,bool>();
        bool LayerVisible(int index)
        {
            if(layers.TryGetValue(index,out bool result))return result;
            var layer=index>=0&&index<doc.Layers.Count?doc.Layers[index]:null;
            var visited=new HashSet<Guid>();result=layer is not null;
            while(layer is not null)
            {
                if(!visited.Add(layer.Id)||layer.IsDeleted||!layer.IsVisible||!layer.ModelIsVisible||
                    (viewport is not null&&!layer.PerViewportIsVisible(viewport.Id))){result=false;break;}
                if(layer.ParentLayerId==Guid.Empty)break;
                layer=doc.Layers.FindId(layer.ParentLayerId);if(layer is null)result=false;
            }
            layers[index]=result;return result;
        }
        GaussianInstanceNode Node(InstanceObject obj)
        {
            objects[obj.Id]=obj;var definition=obj.InstanceDefinition;
            if(definition is not null)definitions[definition.Id]=definition;
            Transform t=obj.InstanceXform;
            GaussianTransform? local=t.IsValid&&t.M30==0&&t.M31==0&&t.M32==0&&t.M33==1
                ?new(new(t.M00,t.M01,t.M02,t.M10,t.M11,t.M12,t.M20,t.M21,t.M22),new(t.M03,t.M13,t.M23)):null;
            bool visible=includeHidden||(!obj.IsDeleted&&obj.Attributes.Visible&&obj.Attributes.Space!=ActiveSpace.PageSpace&&LayerVisible(obj.Attributes.LayerIndex)&&
                (obj.Attributes.ViewportId==Guid.Empty||viewport?.Id==obj.Attributes.ViewportId));
            bool marked=HasGaussianMarker(obj),valid=marked&&IsGaussian(obj);
            return new(obj.Id,marked&&!valid?null:definition?.Id,local,valid,visible);
        }
        IReadOnlyList<GaussianInstanceNode>? Children(Guid id)
        {
            if(members.TryGetValue(id,out var cached))return cached;
            if(!definitions.TryGetValue(id,out var definition)||definition.IsDeleted)return null;
            var captured=definition.GetObjects().OfType<InstanceObject>().Where(o=>!o.IsDeleted).Select(Node).ToArray();
            members[id]=captured;return captured;
        }
        var roots=doc.Objects.GetObjectList(settings).OfType<InstanceObject>().Select(Node).ToArray();
        return new(roots,Children,objects);
    }
}
