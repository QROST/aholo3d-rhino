using Aholo3D.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace Aholo3D.Rhino;

internal static partial class GaussianDocument
{
    internal sealed record ExportPart(string Name, GaussianReferenceMetadata Reference,
        GaussianTransform Transform, bool Antialiased, GaussianSplat[]? Loaded);
    internal sealed record ExportSnapshot(uint Serial, double MetersPerUnit, ExportPart[] Parts, bool SelectedOnly);

    // Only called on Rhino's UI thread. Background work retains no live Rhino objects.
    internal static ExportSnapshot CaptureExport(RhinoDoc doc, bool selectedOnly)
    {
        if (global::Rhino.RhinoApp.InvokeRequired) throw new InvalidOperationException(global::Aholo3D.Core.UiText.Get("Start export from the Rhino interface."));
        if (doc.IsClosing) throw new InvalidOperationException(global::Aholo3D.Core.UiText.Get("The target document has closed."));
        if (doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
            throw new InvalidOperationException(global::Aholo3D.Core.UiText.Get("Set standard document length units before exporting USD. Units will not be guessed."));
        double meters = global::Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem, global::Rhino.UnitSystem.Meters);
        if (!double.IsFinite(meters) || meters <= 0) throw new InvalidOperationException(global::Aholo3D.Core.UiText.Get("Document units cannot be converted to meters."));
        InstanceObject[] objects;
        if (selectedOnly)
        {
            RhinoObject[] selected = doc.Objects.GetSelectedObjects(true, true).ToArray();
            if (selected.Length == 0) throw new InvalidOperationException(global::Aholo3D.Core.UiText.Get("Select top-level GS blocks to export, or choose all top-level GS in the panel."));
            if (selected.Any(o => o is not InstanceObject || !HasGaussianMarker(o)))
                throw new InvalidOperationException(global::Aholo3D.Core.UiText.Get("The selection contains non-GS objects or nested blocks. This export supports top-level GS blocks only; adjust the selection."));
            objects = selected.Cast<InstanceObject>().ToArray();
        }
        else
        {
            var settings = new ObjectEnumeratorSettings
            {
                ObjectTypeFilter = ObjectType.InstanceReference, HiddenObjects = true,
                NormalObjects = true, LockedObjects = true, IdefObjects = false,
                DeletedObjects = false, ActiveObjects = true, ReferenceObjects = false, VisibleFilter = false
            };
            objects = doc.Objects.GetObjectList(settings).OfType<InstanceObject>()
                .Where(HasGaussianMarker).ToArray();
        }
        if (objects.Length == 0) throw new InvalidOperationException(global::Aholo3D.Core.UiText.Get("The current document has no exportable top-level GS blocks."));
        if (objects.Any(o => o.Attributes.Space == ActiveSpace.PageSpace))
            throw new InvalidOperationException(UiText.Get("Select model-space geometry for USD export. Page-space objects use different units."));
        var parts = objects.OrderBy(o => o.Id).Select(obj => CaptureExportPart(doc, obj, Transform.Identity)).ToArray();
        return new(doc.RuntimeSerialNumber, meters, parts, selectedOnly);
    }

    internal static ExportPart CaptureExportPart(RhinoDoc doc, InstanceObject obj, Transform parent)
    {
        GaussianReferenceMetadata reference = Reference(obj) ?? throw new InvalidDataException(UiText.Get("Invalid GS reference."));
        Transform t = parent * obj.InstanceXform;
        if (!t.IsValid || t.M30 != 0 || t.M31 != 0 || t.M32 != 0 || t.M33 != 1)
            throw new InvalidDataException(UiText.Get("A GS block has an invalid or non-affine transform. Export was stopped."));
        var linear = new GaussianMatrix(t.M00,t.M01,t.M02,t.M10,t.M11,t.M12,t.M20,t.M21,t.M22);
        _ = linear.Inverse();
        var transform = new GaussianTransform(linear, new GaussianVector(t.M03,t.M13,t.M23));
        bool antialiased = reference.Antialiased;
        return new(obj.Name ?? "", reference, transform, antialiased, Ready(doc, obj)?.Splats);
    }

    internal static GaussianScene ResolveExportScene(ExportSnapshot snapshot, CancellationToken ct, IProgress<string>? progress = null)
    {
        var loaded = new Dictionary<string, GaussianSplat[]>(StringComparer.Ordinal);
        var parts = new GaussianPart[snapshot.Parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            ExportPart part = snapshot.Parts[i];
            progress?.Report(UiText.Format("Preparing complete GS assets {0:N0} / {1:N0}…", i + 1, parts.Length));
            if (!loaded.TryGetValue(part.Reference.Hash, out var splats))
            {
                splats = part.Loaded ?? GaussianReferenceAsset.Load(part.Reference, CacheRoot, ct).Splats;
                loaded.Add(part.Reference.Hash, splats);
            }
            parts[i] = new(part.Name, splats, part.Transform, part.Antialiased);
        }
        return new(parts, "Z", snapshot.MetersPerUnit, "USD");
    }

    internal static Task<string> ExportAsync(string path, ExportSnapshot snapshot, CancellationToken ct, IProgress<string>? progress = null)
        => RunExportAsync(snapshot.Serial, ct, token =>
        {
            var scene = ResolveExportScene(snapshot, token, progress);
            progress?.Report(UiText.Get("Building and packaging the USD scene; large scenes may take several minutes…"));
            UsdGaussianWriter.Write(path, scene, token);
            long count = scene.Parts.Sum(p => (long)p.Splats.Length);
            return UiText.Format("Exported {0:N0} GS blocks and {1:N0} Gaussians.\n{2}\n", scene.Parts.Count, count, path) +
                UiText.Get("Used document coordinates and units captured at export start; complete SH and object transforms are preserved. USD geometry fields use standard float32 precision.") +
                (snapshot.SelectedOnly ? "" : UiText.Get(" Hidden/locked top-level GS blocks are also exported as visible assets."));
        });

    // Registration also covers a document closing between snapshot capture and background startup.
    internal static async Task<string> RunExportAsync(uint serial, CancellationToken ct, Func<CancellationToken,string> work)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            await Aholo3DHost.OnUi(() =>
            {
                cancellation.Token.ThrowIfCancellationRequested();
                RhinoDoc doc = RhinoDoc.FromRuntimeSerialNumber(serial) ?? throw new InvalidOperationException(UiText.Get("The target document has closed."));
                if (doc.IsClosing) throw new InvalidOperationException(UiText.Get("The target document has closed."));
                Initialize();
                if (!Imports.TryGetValue(serial, out var pending)) Imports.Add(serial, pending = new());
                pending.Add(cancellation);
                return true;
            }).ConfigureAwait(false);
            return await Task.Run(() => work(cancellation.Token), cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            await Aholo3DHost.OnUi(() =>
            {
                if (Imports.TryGetValue(serial, out var pending))
                {
                    pending.Remove(cancellation);
                    if (pending.Count == 0) Imports.Remove(serial);
                }
                cancellation.Dispose();
                return true;
            }).ConfigureAwait(false);
        }
    }
}
