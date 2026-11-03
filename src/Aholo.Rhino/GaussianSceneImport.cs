using System.Globalization;
using Aholo.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
namespace Aholo.Rhino;

internal static partial class GaussianDocument
{
    private static readonly Dictionary<uint, List<CancellationTokenSource>> Imports = new();
    private sealed record SourceArchive(string Hash, string Path, string Name);
    private sealed record PreparedPart(string Name, AssetData Asset, GaussianTransform Transform, bool Antialiased);
    private sealed record PreparedImport(SourceArchive Source, GaussianScene Scene, PreparedPart[] Parts);

    internal static async Task<string> ImportSceneAsync(string path, ImportTarget target, double scale, string upAxis,
        bool useSourceUnits, CancellationToken ct, IProgress<string>? progress = null)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentException("GS 比例必须大于零且有限。");
        if (upAxis is not ("Auto" or "Y" or "Z")) throw new ArgumentException("向上轴必须是 Auto、Y 或 Z。");
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken token = cancellation.Token;
        try
        {
            await AholoHost.OnUi(() =>
            {
                RhinoDoc doc = RequireTarget(target.Serial);
                if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException("文档单位已改变，请重新确认导入设置。");
                Initialize();
                if (!Imports.TryGetValue(target.Serial, out var pending)) Imports.Add(target.Serial, pending = new());
                pending.Add(cancellation);
                return true;
            }).ConfigureAwait(false);
            PreparedImport prepared = await Task.Run(() => PrepareImport(path, token, progress), token).ConfigureAwait(false);
            progress?.Report("完整资源已准备；正在写入目标文档…");
            return await AholoHost.OnUi(() => CommitImport(prepared, target, scale, upAxis, useSourceUnits, token)).ConfigureAwait(false);
        }
        finally
        {
            await AholoHost.OnUi(() =>
            {
                if (Imports.TryGetValue(target.Serial, out var pending))
                {
                    pending.Remove(cancellation);
                    if (pending.Count == 0) Imports.Remove(target.Serial);
                }
                cancellation.Dispose();
                return true;
            }).ConfigureAwait(false);
        }
    }
    private static PreparedImport PrepareImport(string path, CancellationToken ct, IProgress<string>? progress)
    {
        path = System.IO.Path.GetFullPath(path);
        progress?.Report("正在存档原始文件…");
        SourceArchive original = ArchiveSource(path, ct);
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        bool usd = extension is ".usd" or ".usda" or ".usdc" or ".usdz";
        progress?.Report("正在读取完整 Gaussian Splat 场景…");
        // USD composition uses the original resolver context; its root bytes must remain unchanged.
        GaussianScene scene = GaussianSceneReader.Read(usd ? path : original.Path, ct);
        if (usd && Hash(path, ct) != original.Hash) throw new IOException("读取期间源场景文件改变，导入已停止；请保存文件后重试。");
        if (scene.Parts.Count == 0) throw new InvalidDataException("文件中没有 Gaussian Splat 数据。");
        GaussianPart[] nonempty = scene.Parts.Where(part => part.Splats.Length > 0).ToArray();
        if (nonempty.Length == 0) throw new InvalidDataException("文件中没有 Gaussian Splat 数据。");
        if (nonempty.Length != scene.Parts.Count)
            scene = scene with { Warnings = (scene.Warnings ?? Array.Empty<string>()).Concat(new[] { $"已略过 {scene.Parts.Count - nonempty.Length:N0} 个空部分；没有删除任何 GS。" }).ToArray() };
        var parts = new PreparedPart[nonempty.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            GaussianPart part = nonempty[index];
            _ = part.Transform.Linear.Inverse();
            progress?.Report($"准备部分 {index + 1:N0}/{parts.Length:N0} · {part.Splats.Length:N0} 个 GS…");
            string sourceName = System.IO.Path.GetFileNameWithoutExtension(original.Name);
            string name = usd ? part.Name : scene.Parts.Count == 1 ? sourceName : $"{sourceName} [{index + 1}]";
            parts[index] = new(name, PublishPart(part.Splats, ct), part.Transform, part.Antialiased);
        }
        return new(original, scene, parts);
    }
    private static string CommitImport(PreparedImport prepared, ImportTarget target, double scale, string axis,
        bool useSourceUnits, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RhinoDoc doc = RequireTarget(target.Serial);
        if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException("读取期间文档单位改变，请重新确认导入比例。");
        if (doc.IsCommandRunning || doc.UndoRecordingIsActive) throw new InvalidOperationException("请先结束当前 Rhino 命令再导入 GS。");
        string upAxis = axis == "Auto" ? prepared.Scene.UpAxis ?? "Z" : axis;
        if (upAxis is not ("Y" or "Z")) throw new InvalidDataException("场景向上轴不是受支持的 Y 或 Z；请选择轴向后重试。");
        double unitScale = 1;
        if (useSourceUnits && prepared.Scene.MetersPerUnit is double meters)
        {
            if (doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
                throw new InvalidOperationException("目标文档没有可换算的标准长度单位；请先将文档设为标准长度单位后重试。");
            unitScale = meters * global::Rhino.RhinoMath.UnitScale(global::Rhino.UnitSystem.Meters, doc.ModelUnitSystem);
        }
        double effectiveScale = scale * unitScale;
        if (!double.IsFinite(effectiveScale) || effectiveScale <= 0) throw new InvalidDataException("源单位与比例组合后不是有效的正数。");
        Transform import = Transform.Scale(Point3d.Origin, effectiveScale);
        if (upAxis == "Y") import *= Transform.Rotation(Math.PI / 2, Vector3d.XAxis, Point3d.Origin);
        var transforms = new Transform[prepared.Parts.Length];
        BoundingBox overall = BoundingBox.Empty;
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i] = import * ToRhino(prepared.Parts[i].Transform);
            if (!transforms[i].IsValid) throw new InvalidDataException("部分的变换无效，尚未修改文档。");
            BoundingBox box = prepared.Parts[i].Asset.Bounds; box.Transform(transforms[i]);
            if (!box.IsValid) throw new InvalidDataException("变换后的 GS 包围盒超出有效数值范围，尚未修改文档。");
            overall.Union(box);
        }
        uint undo = doc.BeginUndoRecord("导入 Aholo Gaussian Splat");
        if (undo == 0) throw new InvalidOperationException("无法建立导入撤销记录，尚未修改文档。");
        var added = new List<(Guid Id, int Definition)>();
        int layer = -1;
        bool createdLayer = !(doc.Layers.CurrentLayer.IsVisible && !doc.Layers.CurrentLayer.IsLocked);
        try
        {
            layer = VisibleLayer(doc);
            for (int i = 0; i < prepared.Parts.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                added.Add(AddBlock(doc, prepared.Parts[i], transforms[i], prepared.Source, prepared.Scene,
                    effectiveScale, upAxis, layer));
            }
        }
        catch (Exception failure)
        {
            bool rollbackFailed = false;
            foreach (var item in added.AsEnumerable().Reverse())
            {
                try
                {
                    if (!doc.Objects.Delete(item.Id, true)) rollbackFailed = true;
                    if (!doc.InstanceDefinitions.Delete(item.Definition, false, true)) rollbackFailed = true;
                }
                catch (Exception) { rollbackFailed = true; }
            }
            if (createdLayer && layer >= 0)
            {
                try { if (!doc.Layers.Delete(layer, true)) rollbackFailed = true; }
                catch (Exception) { rollbackFailed = true; }
            }
            if (rollbackFailed) throw new InvalidOperationException("导入未完成，Rhino 未能完全撤回本次对象；请使用 Undo 撤销本次导入。" + AholoHost.FriendlyError(failure), failure);
            throw;
        }
        finally { doc.EndUndoRecord(undo); }
        if (!Documents.TryGetValue(target.Serial, out var assets)) Documents.Add(target.Serial, assets = new());
        foreach (PreparedPart part in prepared.Parts)
        {
            if (assets.TryGetValue(part.Asset.Hash, out var existing)) existing.Cancellation?.Cancel();
            assets[part.Asset.Hash] = new AssetEntry { Data = part.Asset };
        }
        GaussianViewport.ForgetDocument(target.Serial);
        doc.Objects.UnselectAll(); foreach (var item in added) doc.Objects.Select(item.Id);
        var messages = new List<string>();
        long count = prepared.Parts.Sum(part => (long)part.Asset.Splats.Length);
        messages.Add($"已导入 {count:N0} 个 GS，共 {added.Count:N0} 个部分，可一次 Undo 撤销。轴向 {upAxis}，有效比例 {effectiveScale:G8}。");
        messages.Add("原始文件已存档；完整本地 GS 缓存保留。显示准备期间显示包围盒，静止后恢复完整 GS。");
        if (prepared.Parts.Any(part => part.Antialiased))
            messages.Add("此场景声明了 antialiased 模式；当前视口尚未实现该模式的补偿公式，视觉结果可能不同。");
        if (prepared.Scene.Warnings is not null) messages.AddRange(prepared.Scene.Warnings);
        try
        {
            doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(overall);
            GaussianViewport.Activate(doc);
        }
        catch (Exception ex) { messages.Add("模型已写入，显示模式尚未就绪；包围盒保留。" + AholoHost.FriendlyError(ex)); }
        doc.Views.Redraw();
        return string.Join(System.Environment.NewLine, messages);
    }
    private static Transform ToRhino(GaussianTransform source)
    {
        var m = source.Linear; var t = source.Translation;
        var value = Transform.Identity;
        value.M00=m.M11; value.M01=m.M12; value.M02=m.M13; value.M03=t.X;
        value.M10=m.M21; value.M11=m.M22; value.M12=m.M23; value.M13=t.Y;
        value.M20=m.M31; value.M21=m.M32; value.M22=m.M33; value.M23=t.Z;
        return value;
    }
    private static SourceArchive ArchiveSource(string path, CancellationToken ct)
    {
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".ply" or ".usd" or ".usda" or ".usdc" or ".usdz"))
            throw new ArgumentException("请选择 PLY、USD、USDA、USDC 或 USDZ 文件。");
        string directory = System.IO.Path.Combine(TaskStore.DefaultRoot, "sources");
        CreatePrivateDirectory(directory);
        string temporary = System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                MakePrivate(temporary);
                var buffer = new byte[131072]; int read;
                while ((read = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, read); }
                output.Flush(true);
            }
            string hash = Hash(temporary, ct);
            string destination = System.IO.Path.Combine(directory, hash + extension);
            Publish(temporary, destination, hash, ct);
            return new(hash, destination, System.IO.Path.GetFileName(path));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static AssetData PublishPart(GaussianSplat[] splats, CancellationToken ct)
    {
        CreatePrivateDirectory(CacheRoot);
        string temporary = System.IO.Path.Combine(CacheRoot, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            GaussianPlyWriter.Write(temporary, splats, ct);
            MakePrivate(temporary);
            string hash = Hash(temporary, ct);
            AssetData asset = PrepareAsset(splats, temporary, hash, ct);
            string destination = System.IO.Path.Combine(CacheRoot, hash + ".ply");
            Publish(temporary, destination, hash, ct);
            return asset with { Path = destination };
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void Publish(string temporary, string destination, string hash, CancellationToken ct)
    {
        if (!File.Exists(destination))
        {
            try { File.Move(temporary, destination); }
            catch (IOException) when (File.Exists(destination)) { }
        }
        if (Hash(destination, ct) != hash) throw new IOException("本地缓存校验失败，原始文件未修改。");
    }
    private static AssetData PrepareAsset(GaussianSplat[] source, string path, string hash, CancellationToken ct)
    {
        BoundingBox bounds = BoundingBox.Empty;
        foreach (GaussianSplat splat in source)
        {
            ct.ThrowIfCancellationRequested();
            GaussianMatrix covariance = GaussianMath.Covariance(splat, GaussianTransform.Identity);
            double x=3*Math.Sqrt(covariance.M11), y=3*Math.Sqrt(covariance.M22), z=3*Math.Sqrt(covariance.M33);
            var extent = new BoundingBox(new Point3d(splat.Center.X-x,splat.Center.Y-y,splat.Center.Z-z), new Point3d(splat.Center.X+x,splat.Center.Y+y,splat.Center.Z+z));
            if (!extent.IsValid) throw new InvalidDataException("GS 包围盒超出有效数值范围。");
            bounds.Union(extent);
        }
        if (source.Length == 0 || !bounds.IsValid) throw new InvalidDataException("此部分没有有效的完整 Gaussian Splat 数据。");
        if (bounds.Diagonal.Length == 0) bounds.Inflate(1);
        return new(source, GaussianPreparedAsset.Prepare(source, ct), bounds, hash, path);
    }
    private static (Guid Id, int Definition) AddBlock(RhinoDoc doc, PreparedPart part, Transform transform,
        SourceArchive source, GaussianScene scene, double scale, string upAxis, int layer)
    {
        AssetData data = part.Asset;
        var geometry = data.Bounds.GetEdges().Select(line => (GeometryBase)new LineCurve(line)).ToArray();
        var proxyAttributes = geometry.Select(_ => new ObjectAttributes { LayerIndex = layer, ObjectColor = Color.FromArgb(105,170,200), ColorSource = ObjectColorSource.ColorFromObject }).ToArray();
        int definition = -1;
        Guid instance = Guid.Empty;
        try
        {
            foreach (var a in proxyAttributes) a.SetUserString(ProxyKey, data.Hash);
            definition = doc.InstanceDefinitions.Add("AholoGS_" + data.Hash[..10] + "_" + Guid.NewGuid().ToString("N")[..6], "Aholo Gaussian reference; bounding box placeholder.", Point3d.Origin, geometry, proxyAttributes);
            if (definition < 0) throw new InvalidOperationException("无法建立 GS 块定义。");
            using var attrs = new ObjectAttributes { Name = scene.Format.StartsWith("OpenUSD", StringComparison.Ordinal) ? part.Name : "Aholo · " + System.IO.Path.GetFileNameWithoutExtension(source.Name) + " · " + part.Name, LayerIndex = layer };
            attrs.SetUserString(Marker,"1"); attrs.SetUserString(HashKey,data.Hash); attrs.SetUserString(PathKey,data.Path);
            attrs.SetUserString("Aholo.GS.SourceName",source.Name);
            attrs.SetUserString("Aholo.GS.SourceSha256",source.Hash);
            attrs.SetUserString("Aholo.GS.SourceArchivePath",source.Path);
            attrs.SetUserString("Aholo.GS.SourceFormat",scene.Format);
            attrs.SetUserString("Aholo.GS.PartName",part.Name);
            attrs.SetUserString("Aholo.GS.SourceUpAxis",scene.UpAxis ?? "unspecified");
            attrs.SetUserString("Aholo.GS.ImportUpAxis",upAxis);
            var linear = part.Transform.Linear; var offset = part.Transform.Translation;
            attrs.SetUserString("Aholo.GS.SourceTransform",string.Join(",",new[] { linear.M11,linear.M12,linear.M13,linear.M21,linear.M22,linear.M23,linear.M31,linear.M32,linear.M33,offset.X,offset.Y,offset.Z }.Select(value => value.ToString("R",CultureInfo.InvariantCulture))));
            attrs.SetUserString("Aholo.GS.InitialScale",scale.ToString("R",CultureInfo.InvariantCulture));
            if (scene.MetersPerUnit.HasValue) attrs.SetUserString("Aholo.GS.SourceMetersPerUnit",scene.MetersPerUnit.Value.ToString("R",CultureInfo.InvariantCulture));
            attrs.SetUserString("Aholo.GS.Antialiased",part.Antialiased ? "true" : "false");
            attrs.SetUserString("Aholo.GS.Count",data.Splats.Length.ToString(CultureInfo.InvariantCulture));
            instance = doc.Objects.AddInstanceObject(definition,transform,attrs);
            if (instance == Guid.Empty) throw new InvalidOperationException("Rhino 未能建立 GS 块实例。");
            return (instance, definition);
        }
        finally
        {
            if (definition >= 0 && instance == Guid.Empty) doc.InstanceDefinitions.Delete(definition, false, true);
            foreach(var item in geometry) item.Dispose(); foreach(var item in proxyAttributes) item.Dispose();
        }
    }
    private static void CreatePrivateDirectory(string path)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private static void MakePrivate(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
