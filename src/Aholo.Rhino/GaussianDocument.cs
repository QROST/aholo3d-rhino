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
    }
    internal sealed record Placement(InstanceObject Object, AssetData Asset, GaussianTransform Transform);
    private static string CacheRoot => System.IO.Path.Combine(TaskStore.DefaultRoot, "assets", "gaussians");

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
                var hashes = instances.Select(o => o.Attributes.GetUserString(HashKey)).ToHashSet();
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
    internal static bool IsGaussian(RhinoObject obj) => obj.Attributes.GetUserString(Marker) == "1";
    internal static AssetData? Ready(RhinoDoc doc, RhinoObject obj)
    {
        string? hash = obj.Attributes.GetUserString(HashKey);
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
        string? hash = obj.Attributes.GetUserString(HashKey);
        if (hash is null || hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c))) return;
        if (!Documents.TryGetValue(doc.RuntimeSerialNumber, out var assets)) Documents.Add(doc.RuntimeSerialNumber, assets = new());
        if (assets.ContainsKey(hash)) return;
        var entry = new AssetEntry { Loading = true, Cancellation = new CancellationTokenSource() }; assets.Add(hash, entry);
        uint serial = doc.RuntimeSerialNumber;
        string? savedPath = obj.Attributes.GetUserString(PathKey);
        _ = LoadAsync(serial, hash, savedPath, entry);
    }
    private static async Task LoadAsync(uint serial, string hash, string? savedPath, AssetEntry entry)
    {
        CancellationTokenSource cancellation = entry.Cancellation!;
        CancellationToken ct = cancellation.Token;
        try
        {
            AssetData data = await Task.Run(() =>
            {
                string path = System.IO.Path.Combine(CacheRoot, hash + ".ply");
                if (!File.Exists(path))
                {
                    if (string.IsNullOrWhiteSpace(savedPath) || !System.IO.Path.IsPathFullyQualified(savedPath) || savedPath.StartsWith("\\\\", StringComparison.Ordinal))
                        throw new IOException("找不到此 GS 的原始资源；请重新导入原始 PLY，包围盒仍保留。");
                    path = savedPath;
                }
                if (!string.Equals(Hash(path, ct), hash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("GS 资源内容与文档记录的哈希不一致；保留包围盒，请重新导入。");
                return ReadAsset(path, hash, ct);
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
                global::Rhino.RhinoApp.WriteLine("Aholo GS：资源未加载，保留包围盒。" + entry.Error);
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
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentException("GS 比例必须大于零且有限。");
        if (upAxis is not ("Y" or "Z")) throw new ArgumentException("GS 向上轴必须是 Y 或 Z。");
        var units = await AholoHost.OnUi(() => RequireTarget(targetSerial).ModelUnitSystem).ConfigureAwait(false);
        if (expectedUnits.HasValue && units != expectedUnits.Value) throw new InvalidOperationException("下载期间文档单位改变，请重新确认 GS 比例。");
        AssetData data = await Task.Run(() => Snapshot(path, ct), ct).ConfigureAwait(false);
        return await AholoHost.OnUi(() =>
        {
            ct.ThrowIfCancellationRequested(); RhinoDoc doc = RequireTarget(targetSerial);
            if (doc.ModelUnitSystem != units) throw new InvalidOperationException("读取期间文档单位改变，请重新确认 GS 比例。");
            if (doc.IsCommandRunning || doc.UndoRecordingIsActive) throw new InvalidOperationException("请先结束当前 Rhino 命令再导入 GS。");
            if (taskId.HasValue)
            {
                InstanceObject? existing = Instances(doc, null, true).FirstOrDefault(o => o.Attributes.GetUserString("Aholo.TaskId") == taskId.Value.ToString("D"));
                if (existing is not null)
                {
                    doc.Objects.Select(existing.Id);
                    doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(existing.Geometry.GetBoundingBox(true)); GaussianViewport.Activate(doc);
                    return "此任务已有 GS 实例，已定位。可使用 Rhino 的 Move/Rotate/Scale 调整。";
                }
            }
            Transform transform = Transform.Scale(Point3d.Origin, scale);
            if (upAxis == "Y") transform = transform * Transform.Rotation(Math.PI / 2, Vector3d.XAxis, Point3d.Origin);
            BoundingBox box = data.Bounds; box.Transform(transform);
            if (!box.IsValid) throw new InvalidOperationException("GS 变换后的包围盒超出有效数值范围，尚未修改文档。");
            uint undo = doc.BeginUndoRecord("导入 Aholo Gaussian Splat");
            if (undo == 0) throw new InvalidOperationException("无法建立 GS 撤销记录，尚未修改文档。");
            Guid added;
            try { added = AddBlock(doc, data, transform, path, scale, upAxis, taskId); }
            finally { doc.EndUndoRecord(undo); }
            if (added == Guid.Empty) throw new InvalidOperationException("Rhino 未能建立 GS 块实例。");
            if (!Documents.TryGetValue(targetSerial, out var assets)) Documents.Add(targetSerial, assets = new());
            assets[data.Hash] = new AssetEntry { Data = data };
            GaussianViewport.ForgetDocument(targetSerial);
            doc.Objects.UnselectAll(); doc.Objects.Select(added);
            doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(box); GaussianViewport.Activate(doc);
            return $"已放入 {data.Splats.Length:N0} 个完整 GS 的块实例。原始 PLY 已保留，可 Undo；后台准备和分批上传期间显示包围盒，完整就绪后显示 GS。透明物体交错和实时速度尚未验收。";
        }).ConfigureAwait(false);
    }
    private static RhinoDoc RequireTarget(uint serial)
    {
        RhinoDoc doc = RhinoDoc.FromRuntimeSerialNumber(serial) ?? throw new InvalidOperationException("原目标文档已关闭。");
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != serial || doc.IsClosing || doc.IsReadOnly) throw new InvalidOperationException("请激活可写的目标 Rhino 文档。");
        if (doc.Views.ActiveView is global::Rhino.Display.RhinoPageView { PageIsActive: true }) throw new InvalidOperationException("请先激活模型视口。");
        return doc;
    }
    private static AssetData Snapshot(string path, CancellationToken ct)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!string.Equals(System.IO.Path.GetExtension(path), ".ply", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("实验 GS 视窗当前需要保留完整高斯字段的 PLY。");
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
            if (!File.Exists(destination))
            {
                try { File.Move(temp, destination); }
                catch (IOException) when (File.Exists(destination)) { } // Another import can publish the identical snapshot.
            }
            if (Hash(destination, ct) != hash) throw new IOException("本地 GS 缓存校验失败，原始文件未修改。");
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
        if (splats.Count == 0 || !bounds.IsValid) throw new InvalidDataException("PLY 没有有效的完整 Gaussian Splat 数据。");
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
            if (definition < 0) throw new InvalidOperationException("无法建立 GS 块定义。");
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
        if(index<0) throw new InvalidOperationException("无法创建可见 GS 图层。");
        return index;
    }
    internal static string Diagnostics(RhinoDoc doc)
    {
        int instances=Instances(doc,null,true).Count();
        if(!Documents.TryGetValue(doc.RuntimeSerialNumber,out var assets)) return $"GS 块实例：{instances}；资源尚未加载。";
        return $"GS 块实例：{instances}；已加载资源：{assets.Values.Count(a=>a.Data is not null)}；加载中：{assets.Values.Count(a=>a.Loading)}；资源错误：{assets.Values.Count(a=>a.Error is not null)}。";
    }
}
