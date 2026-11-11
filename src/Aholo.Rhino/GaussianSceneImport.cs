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
        bool useSourceUnits, CancellationToken ct, IProgress<string>? progress = null, Guid? taskId = null)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new ArgumentException(global::Aholo.Core.UiText.Get("GS scale must be finite and greater than zero."));
        if (upAxis is not ("Auto" or "Y" or "Z")) throw new ArgumentException(global::Aholo.Core.UiText.Get("The up axis must be Auto, Y or Z."));
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken token = cancellation.Token;
        try
        {
            await AholoHost.OnUi(() =>
            {
                RhinoDoc doc = RequireTarget(target.Serial);
                if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Document units changed. Confirm the import settings again."));
                Initialize();
                if (!Imports.TryGetValue(target.Serial, out var pending)) Imports.Add(target.Serial, pending = new());
                pending.Add(cancellation);
                return true;
            }).ConfigureAwait(false);
            PreparedImport prepared = await Task.Run(() => PrepareImport(path, token, progress), token).ConfigureAwait(false);
            progress?.Report(global::Aholo.Core.UiText.Get("Complete assets are ready; writing to the target document…"));
            return await AholoHost.OnUi(() => CommitImport(prepared, target, scale, upAxis, useSourceUnits, taskId, token)).ConfigureAwait(false);
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
        progress?.Report(global::Aholo.Core.UiText.Get("Archiving the original file…"));
        SourceArchive original = ArchiveSource(path, ct);
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        bool usd = extension is ".usd" or ".usda" or ".usdc" or ".usdz";
        progress?.Report(global::Aholo.Core.UiText.Get("Reading the complete Gaussian Splat scene…"));
        // USD composition uses the original resolver context; its root bytes must remain unchanged.
        GaussianScene scene = GaussianSceneReader.Read(usd ? path : original.Path, ct);
        if (usd && Hash(path, ct) != original.Hash) throw new IOException(global::Aholo.Core.UiText.Get("The source scene changed while being read. Import stopped; save the file and retry."));
        return PrepareFromScene(original,scene,ct,progress);
    }
    private static PreparedImport PrepareFromScene(SourceArchive original, GaussianScene scene, CancellationToken ct, IProgress<string>? progress)
    {
        if (scene.Parts.Count == 0) throw new InvalidDataException(global::Aholo.Core.UiText.Get("The file contains no Gaussian Splat data."));
        GaussianPart[] nonempty = scene.Parts.Where(part => part.Splats.Length > 0).ToArray();
        if (nonempty.Length == 0) throw new InvalidDataException(global::Aholo.Core.UiText.Get("The file contains no Gaussian Splat data."));
        if (nonempty.Length != scene.Parts.Count)
            scene = scene with { Warnings = (scene.Warnings ?? Array.Empty<string>()).Concat(new[] { global::Aholo.Core.UiText.Format("Skipped {0:N0} empty parts; no GS data was removed.", scene.Parts.Count - nonempty.Length) }).ToArray() };
        var parts = new PreparedPart[nonempty.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            GaussianPart part = nonempty[index];
            _ = part.Transform.Linear.Inverse();
            progress?.Report(global::Aholo.Core.UiText.Format("Preparing part {0:N0}/{1:N0} · {2:N0} GS…", index + 1, parts.Length, part.Splats.Length));
            string sourceName = System.IO.Path.GetFileNameWithoutExtension(original.Name);
            string name = scene.Format.StartsWith("OpenUSD", StringComparison.Ordinal) ? part.Name : scene.Parts.Count == 1 ? sourceName : $"{sourceName} [{index + 1}]";
            parts[index] = new(name, PublishPart(part.Splats, ct), part.Transform, part.Antialiased);
        }
        return new(original, scene, parts);
    }
    private static string CommitImport(PreparedImport prepared, ImportTarget target, double scale, string axis,
        bool useSourceUnits, Guid? taskId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RhinoDoc doc = RequireTarget(target.Serial);
        if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Document units changed while reading. Confirm the import scale again."));
        if (doc.IsCommandRunning || doc.UndoRecordingIsActive) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Finish the current Rhino command before importing GS."));
        if (taskId.HasValue)
        {
            var existing = Instances(doc, null, true).Where(o => o.Attributes.GetUserString("Aholo.TaskId") == taskId.Value.ToString("D")).ToArray();
            if (existing.Length > 0)
            {
                BoundingBox bounds = BoundingBox.Empty;
                doc.Objects.UnselectAll();
                foreach (var instance in existing) { doc.Objects.Select(instance.Id); bounds.Union(instance.Geometry.GetBoundingBox(true)); }
                doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(bounds); GaussianViewport.Activate(doc);
                return global::Aholo.Core.UiText.Get("This task already has GS instances; the view is focused on them. Use Rhino Move/Rotate/Scale to adjust them.");
            }
        }
        var placement=ValidateGsPlacement(prepared,doc,scale,axis,useSourceUnits);
        var transforms=placement.Transforms;var overall=placement.Bounds;
        string upAxis=placement.UpAxis;double effectiveScale=placement.Scale;
        uint undo = doc.BeginUndoRecord(global::Aholo.Core.UiText.Get("Import Aholo Gaussian Splat"));
        if (undo == 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot create the import Undo record. The document has not been modified."));
        var added = new List<(Guid Id, int Definition)>();
        bool undoEnded;
        int layer = -1;
        bool createdLayer = !(doc.Layers.CurrentLayer.IsVisible && !doc.Layers.CurrentLayer.IsLocked);
        try
        {
            layer = VisibleLayer(doc);
            for (int i = 0; i < prepared.Parts.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                added.Add(AddBlock(doc, prepared.Parts[i], transforms[i], prepared.Source, prepared.Scene,
                    effectiveScale, upAxis, layer, taskId));
            }
            ct.ThrowIfCancellationRequested();
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
            if (rollbackFailed) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Import did not finish and Rhino could not fully roll back the new objects. Use Undo to reverse this import. ") + AholoHost.FriendlyError(failure), failure);
            throw;
        }
        finally { undoEnded=doc.EndUndoRecord(undo); }
        if(!undoEnded)throw new InvalidOperationException(UiText.Get("The imported object exists, but its undo record could not be confirmed. Inspect the document; do not import again."));
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
        messages.Add(global::Aholo.Core.UiText.Format("Imported {0:N0} GS in {1:N0} parts; one Undo reverses the import. Up axis {2}, effective scale {3:G8}.", count, added.Count, upAxis, effectiveScale));
        messages.Add(global::Aholo.Core.UiText.Get("The original file was archived and the complete local GS cache retained. Bounds are shown during preparation; full GS returns when the view is stationary."));
        if (prepared.Parts.Any(part => part.Antialiased))
            messages.Add(global::Aholo.Core.UiText.Get("This scene declares antialiased mode. The viewport does not yet implement its compensation formula, so appearance may differ."));
        if (prepared.Scene.Warnings is not null) messages.AddRange(prepared.Scene.Warnings.Select(w=>UiText.TranslateKnownMessage(w)));
        try
        {
            doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(overall);
            GaussianViewport.Activate(doc);
        }
        catch (Exception ex) { messages.Add(global::Aholo.Core.UiText.Get("The model is in the document, but the display mode is not ready. Bounds are retained. ") + AholoHost.FriendlyError(ex)); }
        doc.Views.Redraw();
        return string.Join(System.Environment.NewLine, messages);
    }
    private sealed record GsPlacement(Transform[] Transforms,BoundingBox Bounds,string UpAxis,double Scale);
    private static GsPlacement ValidateGsPlacement(PreparedImport prepared,RhinoDoc doc,double scale,string axis,bool useSourceUnits)
    {
        string upAxis = axis == "Auto" ? prepared.Scene.UpAxis ?? "Z" : axis;
        if (upAxis is not ("Y" or "Z")) throw new InvalidDataException(global::Aholo.Core.UiText.Get("The scene up axis is not supported Y or Z. Choose an up axis and retry."));
        double unitScale = 1;
        if (useSourceUnits && prepared.Scene.MetersPerUnit is double meters)
        {
            if (doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
                throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The target document has no convertible standard length units. Set standard length units and retry."));
            unitScale = meters * global::Rhino.RhinoMath.UnitScale(global::Rhino.UnitSystem.Meters, doc.ModelUnitSystem);
        }
        double effectiveScale = scale * unitScale;
        if (!double.IsFinite(effectiveScale) || effectiveScale <= 0) throw new InvalidDataException(global::Aholo.Core.UiText.Get("The source units and scale do not produce a valid positive scale."));
        Transform import = Transform.Scale(Point3d.Origin, effectiveScale);
        if (upAxis == "Y") import *= Transform.Rotation(Math.PI / 2, Vector3d.XAxis, Point3d.Origin);
        var transforms = new Transform[prepared.Parts.Length];
        BoundingBox overall = BoundingBox.Empty;
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i] = import * ToRhino(prepared.Parts[i].Transform);
            if (!transforms[i].IsValid) throw new InvalidDataException(global::Aholo.Core.UiText.Get("A part transform is invalid. The document has not been modified."));
            BoundingBox box = prepared.Parts[i].Asset.Bounds; box.Transform(transforms[i]);
            if (!box.IsValid) throw new InvalidDataException(global::Aholo.Core.UiText.Get("The transformed GS bounds exceed the valid numeric range. The document has not been modified."));
            overall.Union(box);
        }
        return new(transforms,overall,upAxis,effectiveScale);
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
        if (!GaussianSceneReader.Extensions.Contains(extension))
            throw new ArgumentException(global::Aholo.Core.UiText.Get("Choose a PLY, SPLAT, SPZ, USD, USDA, USDC or USDZ file."));
        string directory = System.IO.Path.Combine(AholoPlugin.Session.Store.Root, "sources");
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
        => GaussianCachePublisher.Publish(temporary, destination, hash, ct);
    private static AssetData PrepareAsset(GaussianSplat[] source, string path, string hash, CancellationToken ct)
    {
        BoundingBox bounds = BoundingBox.Empty;
        foreach (GaussianSplat splat in source)
        {
            ct.ThrowIfCancellationRequested();
            GaussianMatrix covariance = GaussianMath.Covariance(splat, GaussianTransform.Identity);
            double x=3*Math.Sqrt(covariance.M11), y=3*Math.Sqrt(covariance.M22), z=3*Math.Sqrt(covariance.M33);
            var extent = new BoundingBox(new Point3d(splat.Center.X-x,splat.Center.Y-y,splat.Center.Z-z), new Point3d(splat.Center.X+x,splat.Center.Y+y,splat.Center.Z+z));
            if (!extent.IsValid) throw new InvalidDataException(global::Aholo.Core.UiText.Get("GS bounds exceed the valid numeric range."));
            bounds.Union(extent);
        }
        if (source.Length == 0 || !bounds.IsValid) throw new InvalidDataException(global::Aholo.Core.UiText.Get("This part has no valid complete Gaussian Splat data."));
        if (bounds.Diagonal.Length == 0) bounds.Inflate(1);
        return new(source, GaussianPreparedAsset.Prepare(source, ct), bounds, hash, path);
    }
    private static (Guid Id, int Definition) AddBlock(RhinoDoc doc, PreparedPart part, Transform transform,
        SourceArchive source, GaussianScene scene, double scale, string upAxis, int layer, Guid? taskId, Guid? sceneImportId = null)
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
            if (definition < 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot create the GS block definition."));
            using var attrs = new ObjectAttributes { Name = scene.Format.StartsWith("OpenUSD", StringComparison.Ordinal) ? part.Name : "Aholo · " + System.IO.Path.GetFileNameWithoutExtension(source.Name) + " · " + part.Name, LayerIndex = layer };
            attrs.SetUserString(Marker,"1"); attrs.SetUserString(HashKey,data.Hash); attrs.SetUserString(PathKey,data.Path);
            if (taskId.HasValue) attrs.SetUserString("Aholo.TaskId",taskId.Value.ToString("D"));
            if (sceneImportId.HasValue) attrs.SetUserString("Aholo.Scene.ImportId",sceneImportId.Value.ToString("D"));
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
            if (instance == Guid.Empty) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Rhino could not create the GS block instance."));
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
