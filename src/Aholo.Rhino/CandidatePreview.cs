using Rhino.Display;
using Rhino.Geometry;
using RhinoDoc=global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

public sealed class CandidatePreview : DisplayConduit, IDisposable
{
    private readonly object _sync = new();
    private PreviewGeometry? _geometry;
    private Transform _modelTransform = Transform.Identity;
    private BoundingBox _bounds = BoundingBox.Empty;
    private uint _documentSerial;
    private bool _visible;
    private bool _disposed;

    public void Show(PreparedAsset asset, PlacementIntent placement)
    {
        RequireUiThread();
        ArgumentNullException.ThrowIfNull(asset);
        lock (_sync)
            if (_disposed) throw new ObjectDisposedException(nameof(CandidatePreview));
        Transform placementTransform = placement.ToTransform(asset.Audit.Bounds);
        PreviewGeometry? current;
        lock (_sync) current = _geometry;
        PreviewGeometry next = current is not null && ReferenceEquals(current.Asset, asset)
            ? current : PreviewGeometry.Create(asset);
        BoundingBox bounds = next.SourceBounds;
        if (!bounds.Transform(placementTransform) || !bounds.IsValid)
        {
            if (!ReferenceEquals(next, current)) next.Dispose();
            throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Invalid transformed preview bounding box."));
        }
        uint previousDocument;
        lock (_sync)
        {
            previousDocument = _documentSerial;
            _geometry = next;
            _modelTransform = placementTransform;
            _bounds = bounds;
            _documentSerial = asset.DocumentSerial;
            _visible = true;
        }
        // Draw callbacks hold _sync while using a cache. Once it has been swapped
        // out here, no callback can still be drawing its meshes or materials.
        if (!ReferenceEquals(current, next)) current?.Dispose();
        if (!Enabled) Enabled = true;
        if (previousDocument != 0 && previousDocument != asset.DocumentSerial)
            Redraw(previousDocument);
        Redraw(asset.DocumentSerial);
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        lock (_sync)
            if (_visible && e.RhinoDoc?.RuntimeSerialNumber == _documentSerial)
                e.IncludeBoundingBox(_bounds);
    }

    protected override void CalculateBoundingBoxZoomExtents(CalculateBoundingBoxEventArgs e) => CalculateBoundingBox(e);

    protected override void PostDrawObjects(DrawEventArgs e)
    {
        lock (_sync)
        {
            if (!_visible || _geometry is null || e.RhinoDoc?.RuntimeSerialNumber != _documentSerial) return;
            // PushModelTransform sets, rather than multiplies, the current matrix.
            // Keep any enclosing display transform and always restore its stack.
            e.Display.PushModelTransform(e.Display.ModelTransform * _modelTransform);
            try
            {
                foreach (var item in _geometry.Items)
                    e.Display.DrawMeshShaded(item.Mesh, item.Material);
            }
            finally { e.Display.PopModelTransform(); }
        }
    }

    public void Clear()
    {
        RequireUiThread();
        lock (_sync)
            if (_disposed || (!_visible && _geometry is null)) return;
        Enabled = false;
        PreviewGeometry? previous;
        uint previousDocument;
        lock (_sync)
        {
            previous = _geometry;
            previousDocument = _documentSerial;
            _geometry = null;
            _bounds = BoundingBox.Empty;
            _modelTransform = Transform.Identity;
            _documentSerial = 0;
            _visible = false;
        }
        try { previous?.Dispose(); }
        finally { Redraw(previousDocument); }
    }

    public void Dispose()
    {
        RequireUiThread();
        lock (_sync)
            if (_disposed) return;
        try { Clear(); }
        finally
        {
            lock (_sync) _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    // Read-only lifecycle evidence for the in-Rhino self-test; never used on the draw path.
    internal PreviewState CaptureState()
    {
        lock (_sync)
            return new PreviewState(_geometry, _modelTransform, _geometry?.SourceBounds ?? BoundingBox.Empty,
                _bounds, _documentSerial, _geometry?.Items.Length ?? 0, _visible, _disposed);
    }

    internal readonly record struct PreviewState(object? GeometryCache, Transform ModelTransform,
        BoundingBox SourceBounds, BoundingBox Bounds, uint DocumentSerial, int MeshCount, bool Visible, bool Disposed);

    private static void RequireUiThread()
    {
        if (global::Rhino.RhinoApp.InvokeRequired)
            throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Candidate previews must be updated or released on Rhino's UI thread."));
    }

    private static void Redraw(uint documentSerial)
    {
        if (documentSerial != 0) RhinoDoc.FromRuntimeSerialNumber(documentSerial)?.Views.Redraw();
    }

    private sealed class PreviewGeometry : IDisposable
    {
        private PreviewGeometry(PreparedAsset asset, (Mesh Mesh, DisplayMaterial Material)[] items, BoundingBox bounds)
        {
            Asset = asset; Items = items; SourceBounds = bounds;
        }
        public PreparedAsset Asset { get; }
        public (Mesh Mesh, DisplayMaterial Material)[] Items { get; private set; }
        public BoundingBox SourceBounds { get; }

        public static PreviewGeometry Create(PreparedAsset asset)
        {
            // Verify on first load/switch only. Commit independently verifies the
            // snapshot again; slider changes use this immutable geometry cache.
            asset.Verify();
            var items = new List<(Mesh Mesh, DisplayMaterial Material)>();
            BoundingBox bounds = BoundingBox.Empty;
            try
            {
                foreach (var (source, transform, obj) in MeshHostService.Meshes(MeshHostService.Objects(asset.Staging), Transform.Identity))
                {
                    Mesh? mesh = source.DuplicateMesh();
                    DisplayMaterial? material = null;
                    try
                    {
                        // Bake only the asset's node transform, once. Placement
                        // scale/rotation/translation stays on the display stack.
                        if (!mesh.Transform(transform)) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The candidate mesh node transform failed."));
                        bounds.Union(mesh.GetBoundingBox(true));
                        material = new DisplayMaterial(obj.GetMaterial(true));
                        items.Add((mesh, material));
                        mesh = null; material = null;
                    }
                    finally { try { mesh?.Dispose(); } finally { material?.Dispose(); } }
                }
                if (items.Count == 0 || !bounds.IsValid) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The candidate contains no previewable mesh."));
                return new PreviewGeometry(asset, items.ToArray(), bounds);
            }
            catch
            {
                DisposeItems(items);
                throw;
            }
        }

        public void Dispose()
        {
            var items = Items;
            Items = Array.Empty<(Mesh, DisplayMaterial)>();
            DisposeItems(items);
        }

        private static void DisposeItems(IEnumerable<(Mesh Mesh, DisplayMaterial Material)> items)
        {
            Exception? failure = null;
            foreach (var item in items)
            {
                try { item.Mesh.Dispose(); } catch (Exception ex) { failure ??= ex; }
                try { item.Material.Dispose(); } catch (Exception ex) { failure ??= ex; }
            }
            if (failure is not null) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Candidate preview resources could not be fully released."), failure);
        }
    }
}
