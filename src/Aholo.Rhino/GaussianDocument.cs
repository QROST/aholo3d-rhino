using System.Globalization;
using System.Security.Cryptography;
using Aholo.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
namespace Aholo.Rhino;

/// <summary>GS data stays in an immutable PLY; a normal Rhino block stores placement and undo state.</summary>
internal static partial class GaussianDocument
{
    internal const string Marker = "Aholo.GS.Version";
    internal const string HashKey = "Aholo.GS.Sha256";
    internal const string PathKey = "Aholo.GS.AssetPath";
    internal const string ProxyKey = "Aholo.GS.Proxy";
    private static readonly Dictionary<uint, Dictionary<string, AssetEntry>> Documents = new();
    private static DateTime _lastRefresh;
    private static bool _initialized;
    internal sealed record AssetData(GaussianSplat[] Splats, GaussianPreparedAsset Prepared, BoundingBox Bounds, string Hash, string Path);
    internal sealed class AssetEntry
    {
        internal AssetData? Data;
        internal string? Error;
        internal bool Loading;
        internal CancellationTokenSource? Cancellation;
        internal HashSet<string> AttemptedSources = new(StringComparer.Ordinal);
    }
    internal sealed record Placement(InstanceObject Object, AssetData Asset, GaussianTransform Transform);
    private static string CacheRoot => System.IO.Path.Combine(AholoPlugin.Session.Store.Root, "assets", "gaussians");

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        global::Rhino.RhinoApp.Idle += OnIdle;
        RhinoDoc.CloseDocument += OnClose;
    }
    internal static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        global::Rhino.RhinoApp.Idle -= OnIdle;
        RhinoDoc.CloseDocument -= OnClose;
        foreach (var assets in Documents.Values) foreach (var entry in assets.Values) entry.Cancellation?.Cancel();
        Documents.Clear();
        foreach (var pending in Imports.Values) foreach (var operation in pending) operation.Cancel();
        Imports.Clear();
    }
    private static void OnClose(object? sender, global::Rhino.DocumentEventArgs e)
    {
        if (Documents.Remove(e.DocumentSerialNumber, out var assets))
            foreach (var entry in assets.Values) entry.Cancellation?.Cancel();
        if (Imports.Remove(e.DocumentSerialNumber, out var pending))
            foreach (var operation in pending) operation.Cancel();
        GaussianViewport.DocumentClosed(e.DocumentSerialNumber);
    }
    private static void OnIdle(object? sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromMilliseconds(500)) return;
        _lastRefresh = DateTime.UtcNow;
        foreach (RhinoDoc doc in RhinoDoc.OpenDocuments())
        {
            if (doc.IsClosing) continue;
            GaussianViewport.PruneViews(doc);
            InstanceObject[] instances = Instances(doc, null, includeHidden: true).ToArray();
            // Undo restores the native reference and reloads the immutable asset on the next idle pass.
            // Deleted assets need not retain all splats in managed memory until document close.
            if (Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets))
            {
                var hashes = instances.Select(o => Reference(o)?.Hash).ToHashSet();
                string[] unused = assets.Keys.Where(hash => !hashes.Contains(hash)).ToArray();
                foreach (string hash in unused) { assets[hash].Cancellation?.Cancel(); assets.Remove(hash); }
                if (unused.Length > 0) GaussianViewport.ForgetDocument(doc.RuntimeSerialNumber);
            }
            foreach (InstanceObject obj in instances) EnsureLoaded(doc, obj);
        }
    }
    internal static IEnumerable<InstanceObject> Instances(RhinoDoc doc, global::Rhino.Display.RhinoViewport? viewport, bool includeHidden = false)
    {
        var settings = new ObjectEnumeratorSettings
        {
            ObjectTypeFilter = ObjectType.InstanceReference,
            HiddenObjects = includeHidden, NormalObjects = true, LockedObjects = true,
            IdefObjects = false, DeletedObjects = false, ActiveObjects = true,
            ReferenceObjects = false, VisibleFilter = !includeHidden
        };
        if (viewport is not null) settings.ViewportFilter = viewport;
        foreach (RhinoObject obj in doc.Objects.GetObjectList(settings))
            if (obj is InstanceObject instance && IsGaussian(instance) && (includeHidden || instance.Visible)) yield return instance;
    }
    private static GaussianReferenceMetadata? Reference(RhinoObject obj) => GaussianReferenceMetadata.Resolve(
        obj.Attributes.GetUserString, () => obj.Attributes.GetUserStrings()?.AllKeys.OfType<string>() ?? Array.Empty<string>());
    private static bool HasGaussianMarker(RhinoObject obj) => GaussianReferenceMetadata.HasMarker(
        obj.Attributes.GetUserString, () => obj.Attributes.GetUserStrings()?.AllKeys.OfType<string>() ?? Array.Empty<string>());
    internal static bool IsGaussian(RhinoObject obj) => Reference(obj) is not null;
    internal static AssetData? Ready(RhinoDoc doc, RhinoObject obj)
    {
        string? hash = Reference(obj)?.Hash;
        return hash is not null && Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets) && assets.TryGetValue(hash, out var entry) ? entry.Data : null;
    }
    internal static List<Placement> Placements(RhinoDoc doc, global::Rhino.Display.RhinoViewport viewport)
    {
        var result = new List<Placement>();
        foreach (InstanceObject obj in Instances(doc, viewport))
        {
            AssetData? asset = Ready(doc, obj);
            if (asset is null) continue;
            Transform t = obj.InstanceXform;
            var linear = new GaussianMatrix(t.M00,t.M01,t.M02,t.M10,t.M11,t.M12,t.M20,t.M21,t.M22);
            // Singular placement cannot represent an ellipsoid; keep its native proxy visible.
            try { _ = linear.Inverse(); }
            catch (ArgumentException) { continue; }
            result.Add(new Placement(obj, asset, new GaussianTransform(linear, new GaussianVector(t.M03,t.M13,t.M23))));
        }
        // Rhino's object enumeration order is not a stable scene identity or transparency tie-break order.
        result.Sort((a,b)=>a.Object.Id.CompareTo(b.Object.Id));
        return result;
    }
    internal static void Reload(RhinoDoc doc)
    {
        if (Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets))
            foreach (string key in assets.Where(p => p.Value.Error is not null).Select(p => p.Key).ToArray()) assets.Remove(key);
        foreach (InstanceObject obj in Instances(doc, null, includeHidden: true)) EnsureLoaded(doc, obj);
        GaussianViewport.ForgetDocument(doc.RuntimeSerialNumber);
    }
    private static void EnsureLoaded(RhinoDoc doc, InstanceObject obj)
    {
        GaussianReferenceMetadata? reference = Reference(obj);
        if (reference is null) return;
        string hash = reference.Hash;
        if (!Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets)) Documents.Add(doc.RuntimeSerialNumber, assets = new());
        string source = reference.AssetPath ?? "";
        assets.TryGetValue(hash, out var previous);
        if (previous is not null && (previous.Data is not null || previous.Loading || previous.Error is null || previous.AttemptedSources.Contains(source))) return;
        // A second instance may reference the same bytes at a surviving local path.
        // Each path is tried once until explicit reload, avoiding an idle retry loop.
        var entry = new AssetEntry { Loading = true, Cancellation = new CancellationTokenSource(),
            AttemptedSources = previous is null ? new(StringComparer.Ordinal) : new(previous.AttemptedSources, StringComparer.Ordinal) };
        entry.AttemptedSources.Add(source); assets[hash] = entry;
        uint serial = doc.RuntimeSerialNumber;
        _ = LoadAsync(serial, reference, entry);
    }
    private static async Task LoadAsync(uint serial, GaussianReferenceMetadata reference, AssetEntry entry)
    {
        string hash = reference.Hash;
        CancellationTokenSource cancellation = entry.Cancellation!;
        CancellationToken ct = cancellation.Token;
        try
        {
            AssetData data = await Task.Run(() =>
            {
                GaussianReferenceData loaded = GaussianReferenceAsset.Load(reference, CacheRoot, ct);
                return PrepareAsset(loaded.Splats, loaded.Path, hash, ct);
            }, ct).ConfigureAwait(false);
            await AholoHost.OnUi(() =>
            {
                if (!IsCurrent(serial, hash, entry)) return false;
                entry.Data = data; entry.Loading = false;
                GaussianViewport.ForgetDocument(serial);
                RhinoDoc.FromRuntimeSerialNumber(serial)?.Views.Redraw();
                return true;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            await AholoHost.OnUi(() =>
            {
                if (!IsCurrent(serial, hash, entry)) return false;
                entry.Error = AholoHost.FriendlyError(ex); entry.Loading = false;
                global::Rhino.RhinoApp.WriteLine(global::Aholo.Core.UiText.Get("Aholo GS: asset not loaded; bounds are retained. ") + entry.Error);
                RhinoDoc.FromRuntimeSerialNumber(serial)?.Views.Redraw(); return true;
            }).ConfigureAwait(false);
        }
        finally
        {
            // Cancel and Dispose are both serialized on Rhino's UI thread.
            await AholoHost.OnUi(() =>
            {
                if (ReferenceEquals(entry.Cancellation, cancellation)) entry.Cancellation = null;
                cancellation.Dispose(); return true;
            }).ConfigureAwait(false);
        }
    }
    private static bool IsCurrent(uint serial, string hash, AssetEntry entry) => _initialized && Documents.TryGetValue(serial, out var assets) && assets.TryGetValue(hash, out var current) && ReferenceEquals(entry, current);
    internal static Task<string> ImportAsync(string path, uint targetSerial, double scale, string upAxis, Guid? taskId, CancellationToken ct)
        => ImportCoreAsync(path, targetSerial, null, scale, upAxis, taskId, ct);
    internal static Task<string> ImportAsync(string path, ImportTarget target, double scale, string upAxis, Guid? taskId, CancellationToken ct)
        => ImportCoreAsync(path, target.Serial, target.Units, scale, upAxis, taskId, ct);
    private static async Task<string> ImportCoreAsync(string path, uint targetSerial, global::Rhino.UnitSystem? expectedUnits, double scale, string upAxis, Guid? taskId, CancellationToken ct)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentException(global::Aholo.Core.UiText.Get("GS scale must be finite and greater than zero."));
        if (upAxis is not ("Y" or "Z")) throw new ArgumentException(global::Aholo.Core.UiText.Get("The GS up axis must be Y or Z."));
        var units = await AholoHost.OnUi(() => RequireTarget(targetSerial).ModelUnitSystem).ConfigureAwait(false);
        if (expectedUnits.HasValue && units != expectedUnits.Value) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Document units changed during download. Confirm the GS scale again."));
        AssetData data = await Task.Run(() => Snapshot(path, ct), ct).ConfigureAwait(false);
        return await AholoHost.OnUi(() =>
        {
            ct.ThrowIfCancellationRequested(); RhinoDoc doc = RequireTarget(targetSerial);
            if (doc.ModelUnitSystem != units) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Document units changed while reading. Confirm the GS scale again."));
            if (doc.IsCommandRunning || doc.UndoRecordingIsActive) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Finish the current Rhino command before importing GS."));
            if (taskId.HasValue)
            {
                InstanceObject? existing = Instances(doc, null, true).FirstOrDefault(o => o.Attributes.GetUserString("Aholo.TaskId") == taskId.Value.ToString("D"));
                if (existing is not null)
                {
                    doc.Objects.Select(existing.Id);
                    doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(existing.Geometry.GetBoundingBox(true)); GaussianViewport.Activate(doc);
                    return global::Aholo.Core.UiText.Get("This task already has a GS instance; the view is focused on it. Use Rhino Move/Rotate/Scale to adjust it.");
                }
            }
            Transform transform = Transform.Scale(Point3d.Origin, scale);
            if (upAxis == "Y") transform = transform * Transform.Rotation(Math.PI / 2, Vector3d.XAxis, Point3d.Origin);
            BoundingBox box = data.Bounds; box.Transform(transform);
            if (!box.IsValid) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Transformed GS bounds exceed the valid numeric range. The document has not been modified."));
            uint undo = doc.BeginUndoRecord(global::Aholo.Core.UiText.Get("Import Aholo Gaussian Splat"));
            if (undo == 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot create the GS Undo record. The document has not been modified."));
            Guid added;
            try { added = AddBlock(doc, data, transform, path, scale, upAxis, taskId); }
            finally { doc.EndUndoRecord(undo); }
            if (added == Guid.Empty) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Rhino could not create the GS block instance."));
            if (!Documents.TryGetValue(targetSerial, out var assets)) Documents.Add(targetSerial, assets = new());
            assets[data.Hash] = new AssetEntry { Data = data };
            GaussianViewport.ForgetDocument(targetSerial);
            doc.Objects.UnselectAll(); doc.Objects.Select(added);
            doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(box); GaussianViewport.Activate(doc);
            return global::Aholo.Core.UiText.Format("Placed a block instance with {0:N0} complete GS. The original PLY is retained and Undo is available. Bounds appear during preparation and batch upload; GS appears when fully ready. Transparency interleaving and real-time performance are not yet accepted.", data.Splats.Length);
        }).ConfigureAwait(false);
    }
    private static RhinoDoc RequireTarget(uint serial)
    {
        RhinoDoc doc = RhinoDoc.FromRuntimeSerialNumber(serial) ?? throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The original target document has closed."));
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != serial || doc.IsClosing || doc.IsReadOnly) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Activate a writable target Rhino document."));
        if (doc.Views.ActiveView is global::Rhino.Display.RhinoPageView { PageIsActive: true }) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Activate a model viewport first."));
        return doc;
    }
    private static AssetData Snapshot(string path, CancellationToken ct)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!string.Equals(System.IO.Path.GetExtension(path), ".ply", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(global::Aholo.Core.UiText.Get("The experimental GS viewport currently requires a PLY with complete Gaussian fields."));
        Directory.CreateDirectory(CacheRoot);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(CacheRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string temp = System.IO.Path.Combine(CacheRoot, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var buffer = new byte[131072]; int read;
                while ((read = source.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); target.Write(buffer,0,read); }
                target.Flush(true);
            }
            string hash = Hash(temp, ct);
            AssetData data = ReadAsset(temp, hash, ct);
            string destination = System.IO.Path.Combine(CacheRoot, hash + ".ply");
            GaussianCachePublisher.Publish(temp, destination, hash, ct);
            return data with { Path = destination };
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static string Hash(string path, CancellationToken ct)
    {
        using var input = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[131072]; int read;
        while ((read = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); hash.AppendData(buffer, 0, read); }
        ct.ThrowIfCancellationRequested();
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
    private static AssetData ReadAsset(string path, string hash, CancellationToken ct)
    {
        var splats = new List<GaussianSplat>(); BoundingBox bounds = BoundingBox.Empty;
        foreach (GaussianSplat splat in PlyReader.ReadGaussians(path, ct))
        {
            ct.ThrowIfCancellationRequested();
            GaussianMatrix covariance = GaussianMath.Covariance(splat, GaussianTransform.Identity);
            double x=3*Math.Sqrt(covariance.M11), y=3*Math.Sqrt(covariance.M22), z=3*Math.Sqrt(covariance.M33);
            bounds.Union(new BoundingBox(new Point3d(splat.Center.X-x,splat.Center.Y-y,splat.Center.Z-z), new Point3d(splat.Center.X+x,splat.Center.Y+y,splat.Center.Z+z)));
            splats.Add(splat);
        }
        if (splats.Count == 0 || !bounds.IsValid) throw new InvalidDataException(global::Aholo.Core.UiText.Get("The PLY contains no valid complete Gaussian Splat data."));
        if (bounds.Diagonal.Length == 0) bounds.Inflate(1);
        var source = splats.ToArray();
        return new(source, GaussianPreparedAsset.Prepare(source, ct), bounds, hash, path);
    }
    private static Guid AddBlock(RhinoDoc doc, AssetData data, Transform transform, string sourcePath, double scale, string upAxis, Guid? taskId)
    {
        int layer = VisibleLayer(doc);
        var geometry = data.Bounds.GetEdges().Select(line => (GeometryBase)new LineCurve(line)).ToArray();
        var proxyAttributes = geometry.Select(_ => new ObjectAttributes { LayerIndex = layer, ObjectColor = Color.FromArgb(105,170,200), ColorSource = ObjectColorSource.ColorFromObject }).ToArray();
        int definition = -1;
        Guid instance = Guid.Empty;
        try
        {
            foreach (var a in proxyAttributes) a.SetUserString(ProxyKey, data.Hash);
            definition = doc.InstanceDefinitions.Add("AholoGS_" + data.Hash[..10] + "_" + Guid.NewGuid().ToString("N")[..6], "Aholo GS reference; bounding box is a placeholder, not converted geometry.", Point3d.Origin, geometry, proxyAttributes);
            if (definition < 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot create the GS block definition."));
            using var attrs = new ObjectAttributes { Name = "Aholo GS · " + System.IO.Path.GetFileNameWithoutExtension(sourcePath), LayerIndex = layer };
            attrs.SetUserString(Marker,"1"); attrs.SetUserString(HashKey,data.Hash); attrs.SetUserString(PathKey,data.Path);
            attrs.SetUserString("Aholo.GS.SourceName",System.IO.Path.GetFileName(sourcePath));
            attrs.SetUserString("Aholo.GS.SourceUpAxis",upAxis); attrs.SetUserString("Aholo.GS.InitialScale",scale.ToString("R",CultureInfo.InvariantCulture));
            attrs.SetUserString("Aholo.GS.Count",data.Splats.Length.ToString(CultureInfo.InvariantCulture));
            if (taskId.HasValue) attrs.SetUserString("Aholo.TaskId",taskId.Value.ToString("D"));
            instance = doc.Objects.AddInstanceObject(definition,transform,attrs);
            return instance;
        }
        finally
        {
            if (definition >= 0 && instance == Guid.Empty) doc.InstanceDefinitions.Delete(definition, false, true);
            foreach(var item in geometry) item.Dispose(); foreach(var item in proxyAttributes) item.Dispose();
        }
    }
    internal static int VisibleLayer(RhinoDoc doc)
    {
        var current=doc.Layers.CurrentLayer;
        if(current.IsVisible && !current.IsLocked) return current.Index;
        int index=doc.Layers.Add("Aholo GS "+Guid.NewGuid().ToString("N")[..6],Color.FromArgb(105,170,200));
        if(index<0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot create a visible GS layer."));
        return index;
    }
    internal sealed record DocumentStatus(int Instances, int Loaded, int Loading, int Errors);
    internal static DocumentStatus Status(RhinoDoc doc)
    {
        Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets);
        return new(Instances(doc, null, true).Count(), assets?.Values.Count(a => a.Data is not null) ?? 0,
            assets?.Values.Count(a => a.Loading) ?? 0, assets?.Values.Count(a => a.Error is not null) ?? 0);
    }
    internal static string Diagnostics(RhinoDoc doc)
    {
        var objects=Instances(doc,null,true).ToArray();
        int compatible=objects.Count(obj=>Reference(obj)?.IsNative==false);
        string locations=UiText.Format("Compatible GS references: {0}. Source metadata is retained.\nGS cache: {1}\nOriginal files: {2}", compatible, CacheRoot, System.IO.Path.Combine(AholoPlugin.Session.Store.Root,"sources"));
        if(!Documents.TryGetValue(doc.RuntimeSerialNumber,out var assets)) return UiText.Format("GS block instances: {0}; assets are not loaded yet.", objects.Length)+"\n"+locations;
        return UiText.Format("GS block instances: {0}; loaded assets: {1}; loading: {2}; asset errors: {3}.", objects.Length, assets.Values.Count(a=>a.Data is not null), assets.Values.Count(a=>a.Loading), assets.Values.Count(a=>a.Error is not null))+"\n"+locations;
    }
}
