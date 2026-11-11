using System.Globalization;
using Aholo.Core;
using Eto.Forms;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
namespace Aholo.Rhino;

internal sealed record ImportTarget(uint Serial, global::Rhino.UnitSystem Units);
internal sealed record GeometrySnapshot(IReadOnlyList<MeshData> Meshes, RenderLight[] Lights, IReadOnlyDictionary<int,MeshTextureSource> Textures, GaussianVector[] Origins, string[] Names);
internal sealed record RenderSnapshot(IReadOnlyList<MeshData> Meshes, RenderCamera Camera, string Name, RenderLight[] Lights, IReadOnlyDictionary<int,MeshTextureSource> Textures);

/// <summary>Capture and commit occur on the Rhino UI thread; parsing and file serialization are asynchronous.</summary>
internal static partial class AholoHost
{
    internal static void Ui(Action action)
    {
        if (global::Rhino.RhinoApp.InvokeRequired) Application.Instance.Invoke(action); else action();
    }
    internal static Task<T> OnUi<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Ui(() => { try { result.SetResult(action()); } catch (Exception e) { result.SetException(e); } });
        return result.Task;
    }
    internal static string FriendlyError(Exception e) => e is OutOfMemoryException
        ? global::Aholo.Core.UiText.Get("Not enough memory to finish the preview. The original PLY and remote task are retained. Free memory and retry.")
        : e is OperationCanceledException ? global::Aholo.Core.UiText.Get("Local waiting stopped; the remote task may still be running. Query history instead of submitting again.") : UiText.TranslateKnownMessage(e.Message);
    internal static ImportTarget CaptureTarget(RhinoDoc doc)
    {
        RequireUi(); RequireDocument(doc);
        return new(doc.RuntimeSerialNumber, doc.ModelUnitSystem);
    }
    private static void RequireUi()
    {
        if (global::Rhino.RhinoApp.InvokeRequired) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Start this operation from the Rhino interface."));
    }
    private static void RequireDocument(RhinoDoc doc, bool forExport=false)
    {
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != doc.RuntimeSerialNumber) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Activate the document associated with this panel first."));
        if (doc.IsClosing || !forExport && doc.IsReadOnly) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The target document is not writable."));
        if (!forExport && doc.Views.ActiveView is global::Rhino.Display.RhinoPageView { PageIsActive: true }) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Switch to a model view or activate a model detail viewport in the layout."));
    }
    internal static async Task<string> ImportAsync(string path, ImportTarget target, double scale, string upAxis, Guid? taskId, CancellationToken ct)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Import scale must be finite and greater than zero."));
        if (upAxis is not ("Y" or "Z")) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Choose Y-up or Z-up."));
        // Keep all source data. This is a native point-cloud preview, not a splat renderer or a reconstructed mesh.
        var data = await Task.Run(() =>
        {
            var points = new List<Point3d>(); var colors = new List<Color>();
            foreach (SplatPoint p in PlyReader.Read(path, ct))
            {
                ct.ThrowIfCancellationRequested();
                var point = upAxis == "Y" ? new Point3d(p.X * scale, -p.Z * scale, p.Y * scale) : new Point3d(p.X * scale, p.Y * scale, p.Z * scale);
                if (!point.IsValid) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Scaled PLY coordinates are invalid. Check the scale."));
                points.Add(point); colors.Add(Color.FromArgb(p.R, p.G, p.B));
            }
            if (points.Count == 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The PLY contains no importable points."));
            return (Points: points, Colors: colors);
        }, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return await OnUi(() =>
        {
            ct.ThrowIfCancellationRequested(); RequireUi();
            RhinoDoc doc = RhinoDoc.FromRuntimeSerialNumber(target.Serial) ?? throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The original target document has closed. Restart import in the intended document."));
            RequireDocument(doc);
            if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Document units changed while reading. Confirm the scale and import again."));
            if (doc.IsCommandRunning || doc.UndoRecordingIsActive) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Finish the current Rhino command before retrying import."));
            // Repeated clicks on a task locate the existing object; deliberate local-file imports create new objects.
            if (taskId.HasValue)
            {
                RhinoObject? existing = doc.Objects.GetObjectList(ObjectType.PointSet).FirstOrDefault(o => !o.IsDeleted && o.Attributes.GetUserString("Aholo.TaskId") == taskId.Value.ToString("D"));
                if (existing is not null)
                {
                    doc.Objects.Select(existing.Id); doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(existing.Geometry.GetBoundingBox(true)); doc.Views.Redraw();
                    return global::Aholo.Core.UiText.Get("This task is already in the document; the view is focused on its point cloud. Use Rhino Scale to change its scale.");
                }
            }
            using var cloud = new PointCloud(); cloud.AddRange(data.Points, data.Colors);
            if (!cloud.IsValid) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Rhino cannot accept this point cloud. The document has not been modified."));
            var bounds = cloud.GetBoundingBox(true);
            if (!bounds.IsValid) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Point-cloud bounds are invalid. The document has not been modified."));
            using var attributes = new ObjectAttributes { Name = "Aholo · " + Path.GetFileNameWithoutExtension(path) };
            attributes.SetUserString("Aholo.Preview", "3DGS centers / DC color; not a splat renderer");
            attributes.SetUserString("Aholo.SourcePath", Path.GetFullPath(path));
            attributes.SetUserString("Aholo.Scale", scale.ToString("R", CultureInfo.InvariantCulture));
            attributes.SetUserString("Aholo.SourceUpAxis", upAxis);
            if (taskId.HasValue) attributes.SetUserString("Aholo.TaskId", taskId.Value.ToString("D"));
            uint undo = doc.BeginUndoRecord(global::Aholo.Core.UiText.Get("Import Aholo point-cloud preview"));
            if (undo == 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot create an Undo record; nothing was imported."));
            Guid added;
            try { attributes.LayerIndex = VisibleLayer(doc); added = doc.Objects.AddPointCloud(cloud, attributes); }
            finally { doc.EndUndoRecord(undo); }
            if (added == Guid.Empty) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Rhino could not add the point cloud."));
            doc.Objects.Select(added);
            if (bounds.Diagonal.Length == 0) bounds.Inflate(Math.Max(scale, 1));
            doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(bounds); doc.Views.Redraw();
            return global::Aholo.Core.UiText.Format("Placed {0:N0} colored centers and focused the view. One Undo reverses the import. The original PLY is retained; this preview has no Gaussian opacity, scale or view-dependent effects.", data.Points.Count);
        }).ConfigureAwait(false);
    }
    private static int VisibleLayer(RhinoDoc doc)
    {
        // A hidden/locked current layer must not make a successful import appear missing.
        Layer current = doc.Layers.CurrentLayer;
        if (current.IsVisible && !current.IsLocked) return current.Index;
        int index = doc.Layers.Add("Aholo preview " + Guid.NewGuid().ToString("N")[..8], Color.FromArgb(100, 180, 220));
        if (index < 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot create a visible preview layer."));
        return index;
    }
    internal static RenderCamera CaptureCamera(RhinoDoc doc, int width, int height)
    {
        RequireUi(); RequireDocument(doc);
        var viewport = doc.Views.ActiveView?.ActiveViewport ?? throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Activate a perspective view first."));
        if (!viewport.IsPerspectiveProjection) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("RenderCloud requires a perspective view in this version. Switch to Perspective and retry."));
        if (!viewport.GetFrustum(out double left, out double right, out double bottom, out double top, out double near, out double far)) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot read the current view camera."));
        if (Math.Abs(left + right) > Math.Max(1e-9, (right - left) * 1e-5) || Math.Abs(bottom + top) > Math.Max(1e-9, (top - bottom) * 1e-5)) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The camera uses an offset frustum. Switch to a standard perspective view; lens shift is not exported in this version."));
        if (doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Set explicit length units for the Rhino document first."));
        double mm = global::Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem, global::Rhino.UnitSystem.Millimeters);
        if (!double.IsFinite(mm) || mm <= 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot convert document units."));
        Vector3d x = viewport.CameraX, y = viewport.CameraY, z = -viewport.CameraDirection;
        if (!x.Unitize() || !y.Unitize() || !z.Unitize()) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Invalid camera direction."));
        Point3d location = viewport.CameraLocation;
        double focal = viewport.Camera35mmLensLength;
        if (!double.IsFinite(focal) || focal <= 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Invalid camera focal length."));
        double horizontalAperture = (right - left) / near * focal;
        // Preserve horizontal field of view; use the chosen output aspect ratio for the vertical aperture.
        double verticalAperture = horizontalAperture * height / width;
        var camera = new RenderCamera(new[] { x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, location.X * mm, location.Y * mm, location.Z * mm, 1 }, focal, horizontalAperture, verticalAperture, near * mm, far * mm);
        return camera;
    }
    internal static RenderSnapshot CaptureRender(RhinoDoc doc, int width, int height, bool includeLights=false, double pointRadiusMillimetres=50, double lightIntensityMultiplier=1, bool includeTextures=true)
    {
        var camera=CaptureCamera(doc,width,height);
        var geometry=CaptureGeometry(doc,includeLights,pointRadiusMillimetres,lightIntensityMultiplier,includeTextures,false);
        return new RenderSnapshot(geometry.Meshes,camera,string.IsNullOrWhiteSpace(doc.Name) ? UiText.Get("Rhino selection render") : Path.GetFileNameWithoutExtension(doc.Name) + UiText.Get(" · Selection render"),geometry.Lights,geometry.Textures);
    }
    internal static GeometrySnapshot CaptureMeshExport(RhinoDoc doc, bool includeTextures=true)
        => CaptureGeometry(doc,false,50,1,includeTextures,true);
    internal static SceneExportSnapshot CaptureSceneExport(RhinoDoc doc, bool includeTextures=true)
    {
        var parts = new List<GaussianDocument.ExportPart>();
        var meshes = CaptureGeometry(doc,false,50,1,includeTextures,true,parts);
        double meters = global::Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem,global::Rhino.UnitSystem.Meters);
        if (!double.IsFinite(meters) || meters <= 0) throw new InvalidOperationException(UiText.Get("Cannot convert document units."));
        return new(new(doc.RuntimeSerialNumber,meters,parts.ToArray(),true),meshes);
    }
    private static GeometrySnapshot CaptureGeometry(RhinoDoc doc,bool includeLights,double pointRadiusMillimetres,double lightIntensityMultiplier,bool includeTextures,bool rebase,List<GaussianDocument.ExportPart>? gaussianParts=null)
    {
        RequireUi();RequireDocument(doc,rebase);
        if(doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
            throw new InvalidOperationException(UiText.Get("Set explicit length units for the Rhino document first."));
        double mm=global::Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem,global::Rhino.UnitSystem.Millimeters);
        if(!double.IsFinite(mm)||mm<=0)throw new InvalidOperationException(UiText.Get("Cannot convert document units."));
        RhinoObject[] objects = doc.Objects.GetSelectedObjects(false, false)
            .Concat(includeLights?doc.Lights.Where(l=>!l.IsDeleted&&l.IsSelected(false)!=0).Cast<RhinoObject>():Enumerable.Empty<RhinoObject>()).DistinctBy(o=>o.Id).ToArray();
        if (objects.Length == 0) throw new InvalidOperationException(UiText.Get("Select meshes, surfaces, extrusions or ordinary blocks first."));
        if(objects.Any(o=>o.Attributes.Space==ActiveSpace.PageSpace))throw new InvalidOperationException(UiText.Get("Select model-space geometry. Page-space objects use different units and cannot enter this mesh workflow."));
        var meshes = new List<MeshData>();var lights=new List<RenderLight>();var textures=new Dictionary<int,MeshTextureSource>();var origins=new List<GaussianVector>();var names=new List<string>();
        foreach (var (obj,transform,binding,parentMapping) in RenderObjects(objects,doc,includeLights,gaussianParts))
        {
            if(obj.Geometry is Light light)
            {
                if(light.IsEnabled)lights.Add(CaptureLight(light,transform,mm,pointRadiusMillimetres,lightIntensityMultiplier,doc));
                continue;
            }
            var generated = CaptureMeshParts(obj,binding,doc);
            try
            {
                if (generated.Length == 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("A selected object cannot be converted to a mesh. Nothing has been uploaded."));
                foreach (var part in generated)
                {
                    Mesh mesh=part.Mesh;
                    var appearance=CaptureMaterial(part.Binding,doc,includeTextures);
                    float[]? uv = appearance.Texture is { } bitmap ? CaptureTextureCoordinates(obj, mesh, bitmap, parentMapping) : null;
                    if (appearance.Texture is not null) textures.Add(meshes.Count, appearance.Texture.Source);
                    // Preserve custom/welded normals before Rhino transforms its private mesh copy.
                    // Face winding is corrected separately below; inverse-transpose directions are not negated.
                    if (mesh.Normals.Count != mesh.Vertices.Count && !mesh.Normals.ComputeNormals())
                        throw new InvalidOperationException(UiText.Get("Cannot compute valid mesh normals. Repair the selected mesh before rendering."));
                    var sourceNormals = new float[checked(mesh.Vertices.Count * 3)];
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        var n = mesh.Normals[i]; sourceNormals[i * 3] = n.X; sourceNormals[i * 3 + 1] = n.Y; sourceNormals[i * 3 + 2] = n.Z;
                    }
                    var normals = MeshAttributes.TransformNormals(sourceNormals, new(transform.M00, transform.M01, transform.M02, transform.M10, transform.M11, transform.M12, transform.M20, transform.M21, transform.M22));
                    // Both routes transform and triangulate a double-precision copy before final transport conversion.
                    mesh.Vertices.UseDoublePrecisionVertices=true;
                    if (!mesh.Transform(transform)) throw new InvalidOperationException(UiText.Get("A block transform could not be applied."));
                    if(transform.Determinant < 0) mesh.Flip(true,true,true);
                    if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0 || !mesh.IsValid) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The selection contains empty or invalid meshes. Repair them in Rhino first."));
                    Point3d anchor=rebase?mesh.Vertices.Point3dAt(0):Point3d.Origin;
                    origins.Add(new(anchor.X*mm,anchor.Y*mm,anchor.Z*mm));names.Add(obj.Name??"");
                    int vertexCount=mesh.Vertices.Count;
                    long triangles=(long)mesh.Faces.TriangleCount+2L*mesh.Faces.QuadCount;
                    mesh.Faces.ConvertQuadsToTriangles();
                    if(mesh.Faces.QuadCount!=0||mesh.Vertices.Count!=vertexCount||mesh.Faces.Count!=triangles)throw new InvalidOperationException(UiText.Get("The mesh could not be triangulated. Repair it before export."));
                    var positions = new float[checked(mesh.Vertices.Count * 3)];
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        Point3d p = mesh.Vertices.Point3dAt(i);
                        positions[i * 3] = CheckedFloat((p.X-anchor.X) * mm); positions[i * 3 + 1] = CheckedFloat((p.Y-anchor.Y) * mm); positions[i * 3 + 2] = CheckedFloat((p.Z-anchor.Z) * mm);
                    }
                    var indices = new List<int>();
                    foreach (MeshFace face in mesh.Faces)
                    {
                        indices.Add(face.A); indices.Add(face.B); indices.Add(face.C);
                    }
                    meshes.Add(new MeshData(positions, indices.ToArray(), appearance.Color.R, appearance.Color.G, appearance.Color.B) { Material = appearance.Factors, Normals = normals, TexCoords = uv });
                }
            }
            finally { foreach (var part in generated) part.Mesh.Dispose(); }
        }
        if(meshes.Count==0 && (gaussianParts is null || gaussianParts.Count==0))throw new InvalidOperationException(UiText.Get("Select at least one supported mesh or surface together with the lights."));
        return new GeometrySnapshot(meshes,lights.ToArray(),textures,origins.ToArray(),names.ToArray());
    }
    private sealed record MaterialSnapshot(Color Color,MeshMaterial? Factors,TextureSnapshot? Texture=null);
    private static IEnumerable<(RhinoObject Object,Transform Transform,MaterialBinding Binding,bool ParentMapping)> RenderObjects(IEnumerable<RhinoObject> roots,RhinoDoc doc,bool includeLights,List<GaussianDocument.ExportPart>? gaussianParts=null)
    {
        var ancestry=new HashSet<Guid>();
        var stack=new Stack<(IEnumerator<RhinoObject> Objects,Transform Transform,Guid? Definition,Lazy<MaterialBinding>? ParentMaterial,bool ParentMapping)>();
        stack.Push((roots.GetEnumerator(),Transform.Identity,null,null,false));
        try
        {
            while(stack.Count>0)
            {
                var frame=stack.Peek();
                if(!frame.Objects.MoveNext()){frame.Objects.Dispose();stack.Pop();if(frame.Definition.HasValue)ancestry.Remove(frame.Definition.Value);continue;}
                var obj=frame.Objects.Current;
                if(GaussianDocument.HasGaussianMarker(obj)||obj.Attributes.GetUserString(GaussianDocument.ProxyKey) is not null)
                {
                    if(gaussianParts is null)throw new InvalidOperationException(UiText.Get("Gaussian scenes cannot be exported as mesh bounds. Deselect GS objects before mesh rendering."));
                    if(obj is not InstanceObject gaussian || !GaussianDocument.HasGaussianMarker(obj))throw new InvalidDataException(UiText.Get("Invalid GS reference."));
                    gaussianParts.Add(GaussianDocument.CaptureExportPart(doc,gaussian,frame.Transform));
                    continue;
                }
                // Resolve inherited material only when a mesh leaf actually consumes it. GS ignores proxy/parent materials.
                var appearance = new Lazy<MaterialBinding>(()=>CaptureMaterialBinding(obj,doc,frame.ParentMaterial));
                if(obj is InstanceObject instance)
                {
                    var definition=instance.InstanceDefinition??throw new InvalidOperationException(UiText.Get("A selected block definition is missing."));
                    if(!ancestry.Add(definition.Id))throw new InvalidOperationException(UiText.Get("A selected block has a cyclic reference."));
                    var transform=frame.Transform*instance.InstanceXform;
                    if(!transform.IsValid||Math.Abs(transform.Determinant)<1e-20)throw new InvalidOperationException(UiText.Get("A selected block transform is invalid."));
                    stack.Push((((IEnumerable<RhinoObject>)definition.GetObjects()).GetEnumerator(),transform,definition.Id,appearance,frame.ParentMapping||obj.GetTextureChannels().Length!=0));
                }
                else if(obj.Geometry is Mesh or Brep or Extrusion || includeLights&&obj.Geometry is Light)yield return(obj,frame.Transform,appearance.Value,frame.ParentMapping);
                else throw new InvalidOperationException(UiText.Get("The selection contains unsupported geometry. Select meshes, surfaces, extrusions or blocks containing them."));
            }
        }
        finally{while(stack.Count>0)stack.Pop().Objects.Dispose();}
    }
    internal static RenderLight[] CaptureSelectedLights(RhinoDoc doc,double radius)
    {
        RequireUi();RequireDocument(doc);
        if(doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
            throw new InvalidOperationException(UiText.Get("Set explicit length units for the Rhino document first."));
        double mm=global::Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem,global::Rhino.UnitSystem.Millimeters);
        var selected=doc.Objects.GetSelectedObjects(false,false).Concat(doc.Lights.Where(l=>!l.IsDeleted&&l.IsSelected(false)!=0).Cast<RhinoObject>()).DistinctBy(o=>o.Id);
        var lights=new List<RenderLight>();
        foreach(var (obj,transform,_,_) in RenderObjects(selected,doc,true))
            if(obj.Geometry is Light {IsEnabled:true} light)lights.Add(CaptureLight(light,transform,mm,radius,1,doc));
        if(lights.Count==0)throw new InvalidOperationException(UiText.Get("Select enabled rectangular or point lights, or turn off light capture in scene settings."));
        return lights.ToArray();
    }
    private static RenderLight CaptureLight(Light light,Transform t,double mm,double radius,double multiplier,RhinoDoc doc)
    {
        var transform=new GaussianTransform(new(t.M00,t.M01,t.M02,t.M10,t.M11,t.M12,t.M20,t.M21,t.M22),new(t.M03,t.M13,t.M23));
        var raw=new global::Rhino.Display.Color4f(light.Diffuse);
        using var workflow=doc.RenderSettings.LinearWorkflow;
        if(workflow.PreProcessColors)
        {
            float gamma=workflow.PreProcessGamma;
            if(!float.IsFinite(gamma)||gamma<=0)throw new InvalidOperationException(UiText.Get("The document color preprocessing gamma is invalid."));
            raw=global::Rhino.Display.Color4f.ApplyGamma(raw,gamma);
        }
        var color=new[]{(double)raw.R,raw.G,raw.B};double intensity=light.Intensity*multiplier;
        var location=new GaussianVector(light.Location.X,light.Location.Y,light.Location.Z);
        return light.LightStyle switch
        {
            LightStyle.WorldRectangular=>RenderLight.Rectangle(location,new(light.Length.X,light.Length.Y,light.Length.Z),new(light.Width.X,light.Width.Y,light.Width.Z),new(light.Direction.X,light.Direction.Y,light.Direction.Z),transform,mm,color,intensity),
            LightStyle.WorldPoint=>RenderLight.PointApproximation(location,transform,mm,radius,color,intensity),
            _=>throw new InvalidOperationException(UiText.Get("Selected lights include an unsupported type. Only world rectangular and point lights are supported."))
        };
    }
    private static MaterialSnapshot CaptureMaterial(MaterialBinding binding,RhinoDoc doc,bool includeTextures)
    {
        Color color=binding.FallbackColor;var material=binding.Material;
        if(material is null)return new(color,null);
        color=material.DiffuseColor;
        var bitmap=includeTextures?CaptureTexture(material,doc):null;
        if(!material.IsPhysicallyBased)return new(color,null,bitmap);
        var pbr=material.PhysicallyBased;var raw=pbr.BaseColor;
        var factors=new MeshMaterial(raw.R,raw.G,raw.B,pbr.Alpha,pbr.Metallic,pbr.Roughness);
        factors.Validate();
        // Follow the host's input-color workflow once; the writer receives explicitly linear RGB.
        using var workflow=doc.RenderSettings.LinearWorkflow;
        if(workflow.PreProcessColors)
        {
            float gamma=workflow.PreProcessGamma;
            if(!float.IsFinite(gamma)||gamma<=0)throw new InvalidOperationException(UiText.Get("The document color preprocessing gamma is invalid."));
            var linear=global::Rhino.Display.Color4f.ApplyGamma(raw,gamma);
            factors=factors with{R=linear.R,G=linear.G,B=linear.B};factors.Validate();
        }
        return new(color,factors,bitmap);
    }
    private static float CheckedFloat(double value)
    {
        float result = (float)value;
        if (!float.IsFinite(result)) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Selection coordinates exceed the GLB numeric range. Check model units or coordinates first."));
        return result;
    }
}
