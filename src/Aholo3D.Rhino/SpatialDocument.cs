using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Aholo3D.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace Aholo3D.Rhino;

internal static class SpatialDocument
{
    internal static async Task<string> PlaceAsync(string path, string? expectedHash, ImportTarget target, Guid taskId, string artifactKey, CancellationToken ct)
    {
        var prepared = await Task.Run(() =>
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920]; int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) != 0) { ct.ThrowIfCancellationRequested(); hasher.AppendData(buffer, 0, read); }
            ct.ThrowIfCancellationRequested();
            string hash = Convert.ToHexString(hasher.GetHashAndReset());
            if (expectedHash is not null && !hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(UiText.Get("The downloaded spatial result changed. Download the original result again."));
            stream.Position = 0;
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            var layout = SpatialDsl.Read(reader, ct);
            return (Layout: layout, Shapes: SpatialDsl.Shapes(layout, ct), Hash: hash);
        }, ct).ConfigureAwait(false);
        return await Aholo3DHost.OnUi(() =>
        {
            ct.ThrowIfCancellationRequested();
            var doc = RhinoDoc.FromRuntimeSerialNumber(target.Serial) ?? throw new InvalidOperationException(UiText.Get("The original target document has closed."));
            _ = Aholo3DHost.CaptureTarget(doc);
            if (doc.ModelUnitSystem != target.Units || doc.IsCommandRunning || doc.UndoRecordingIsActive)
                throw new InvalidOperationException(UiText.Get("The target document changed. Retry placement in the intended document."));
            if (doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
                throw new InvalidOperationException(UiText.Get("Set standard document units before placing spatial geometry."));
            string identity = taskId.ToString("D") + ":" + artifactKey;
            var existing = doc.Objects.GetObjectList(ObjectType.InstanceReference).OfType<InstanceObject>()
                .FirstOrDefault(o => o.Attributes.GetUserString("Aholo.Spatial.Result") == identity);
            if (existing is not null)
            {
                if (!string.Equals(existing.Attributes.GetUserString("Aholo.Spatial.Sha256"), prepared.Hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(UiText.Get("The spatial result differs from the existing block. The existing model is retained."));
                doc.Objects.UnselectAll(); existing.Select(true); doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(existing.Geometry.GetBoundingBox(true)); doc.Views.Redraw();
                return UiText.Get("This spatial result is already in the document.");
            }
            double factor = global::Rhino.RhinoMath.UnitScale(global::Rhino.UnitSystem.Meters, doc.ModelUnitSystem);
            if (!double.IsFinite(factor) || factor <= 0) throw new InvalidOperationException(UiText.Get("Document units cannot be converted to meters."));
            var geometry = new List<GeometryBase>(); var attributes = new List<ObjectAttributes>();
            try
            {
                foreach (var shape in prepared.Shapes)
                {
                    ct.ThrowIfCancellationRequested();
                    var points = shape.Points.Select(p => new Point3d(p.X * factor, p.Y * factor, p.Z * factor)).ToArray();
                    if (points.Any(p => !p.IsValid)) throw new InvalidDataException(UiText.Get("Spatial geometry exceeds the numeric range."));
                    GeometryBase? item = shape.Kind switch
                    {
                        "Wall" => Brep.CreateFromCornerPoints(points[0], points[1], points[2], points[3], doc.ModelAbsoluteTolerance),
                        "Bbox" => Brep.CreateFromBox(points),
                        _ => new PolylineCurve(points.Concat(new[] { points[0] }))
                    };
                    if (item is null) throw new InvalidDataException(UiText.Get("Rhino could not construct a spatial entity."));
                    geometry.Add(item);
                    if (!item.IsValid) throw new InvalidDataException(UiText.Get("Rhino could not construct a spatial entity."));
                    var attrs = new ObjectAttributes { Name = shape.Entity.Id };
                    attributes.Add(attrs);
                    attrs.SetUserString("Aholo.Spatial.EntityId", shape.Entity.Id);
                    attrs.SetUserString("Aholo.Spatial.Type", shape.Kind);
                    attrs.SetUserString("Aholo.Spatial.Line", shape.Entity.Line.ToString(CultureInfo.InvariantCulture));
                    if (shape.Entity is SpatialBox box) attrs.SetUserString("Aholo.Spatial.Class", box.Class);
                    if (shape.Entity is SpatialOpening opening) attrs.SetUserString("Aholo.Spatial.WallId", opening.WallId);
                    if (shape.Entity is SpatialWall wall) attrs.SetUserString("Aholo.Spatial.ThicknessMeters", wall.Thickness.ToString("R", CultureInfo.InvariantCulture));
                }
                ct.ThrowIfCancellationRequested();
                uint undo = doc.BeginUndoRecord(UiText.Get("Place spatial geometry"));
                if (undo == 0) throw new InvalidOperationException(UiText.Get("Cannot create the import Undo record. The document has not been modified."));
                int definition = -1, layer = -1; Guid added = Guid.Empty;
                bool createdLayer = !(doc.Layers.CurrentLayer.IsVisible && !doc.Layers.CurrentLayer.IsLocked);
                try
                {
                    layer = GaussianDocument.VisibleLayer(doc);
                    foreach (var attribute in attributes) attribute.LayerIndex = layer;
                    definition = doc.InstanceDefinitions.Add("Aholo3D_Spatial_" + Guid.NewGuid().ToString("N"), "Spatial result · metres converted to document units", Point3d.Origin, geometry, attributes);
                    if (definition < 0) throw new InvalidOperationException(UiText.Get("Rhino could not construct a spatial entity."));
                    using var root = new ObjectAttributes { Name = "Aholo3D spatial result", LayerIndex = layer };
                    root.SetUserString("Aholo.Spatial.Result", identity); root.SetUserString("Aholo.TaskId", taskId.ToString("D"));
                    root.SetUserString("Aholo.Spatial.Sha256", prepared.Hash);
                    ct.ThrowIfCancellationRequested(); _ = Aholo3DHost.CaptureTarget(doc);
                    if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException(UiText.Get("The target document changed. Retry placement in the intended document."));
                    added = doc.Objects.AddInstanceObject(definition, Transform.Identity, root);
                    if (added == Guid.Empty) throw new InvalidOperationException(UiText.Get("Rhino could not construct a spatial entity."));
                }
                catch (Exception error)
                {
                    bool rollbackFailed = false;
                    try { if (added != Guid.Empty && !doc.Objects.Delete(added, true)) rollbackFailed = true; } catch { rollbackFailed = true; }
                    try { if (definition >= 0 && !doc.InstanceDefinitions.Delete(definition, false, true)) rollbackFailed = true; } catch { rollbackFailed = true; }
                    try { if (createdLayer && layer >= 0 && !doc.Layers.Delete(layer, true)) rollbackFailed = true; } catch { rollbackFailed = true; }
                    if (rollbackFailed) throw new InvalidOperationException(UiText.Get("Spatial placement stopped, but cleanup was incomplete. Use Undo to remove the new geometry."), error);
                    throw;
                }
                finally { doc.EndUndoRecord(undo); }
                try
                {
                    var obj = doc.Objects.FindId(added); doc.Objects.UnselectAll(); obj?.Select(true);
                    if (obj is not null) doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(obj.Geometry.GetBoundingBox(true));
                    doc.Views.Redraw();
                }
                catch { /* The committed geometry remains successful if framing fails. */ }
                string result = UiText.Format("Placed {0:N0} spatial entities as a block. Walls are surfaces, openings are outlines, and boxes retain their orientation. Use BlockEdit or Explode to edit; one Undo removes the placement.", prepared.Shapes.Length);
                if (prepared.Layout.Entities.OfType<SpatialWall>().Any(w => w.Thickness > 0))
                    result += "\n" + UiText.Get("Detected wall thickness is retained as metadata; this version creates wall surfaces without thickness or automatic openings.");
                return result + (prepared.Layout.Warnings.Count == 0 ? "" : "\n" + string.Join("\n", prepared.Layout.Warnings));
            }
            finally { foreach (var item in geometry) item.Dispose(); foreach (var attrs in attributes) attrs.Dispose(); }
        }).ConfigureAwait(false);
    }
}
