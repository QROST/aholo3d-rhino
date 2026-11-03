using Aholo.Core;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
namespace Aholo.Rhino;

internal static partial class GaussianDocument
{
    internal sealed record ExportPart(string Name, GaussianReferenceMetadata Reference,
        GaussianTransform Transform, bool Antialiased, GaussianSplat[]? Loaded);
    internal sealed record ExportSnapshot(uint Serial, double MetersPerUnit, ExportPart[] Parts, bool SelectedOnly);

    // Only called on Rhino's UI thread. Background work retains no live Rhino objects.
    internal static ExportSnapshot CaptureExport(RhinoDoc doc, bool selectedOnly)
    {
        if (global::Rhino.RhinoApp.InvokeRequired) throw new InvalidOperationException("请从 Rhino 界面发起导出。");
        if (doc.IsClosing) throw new InvalidOperationException("目标文档已关闭。");
        if (doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits)
            throw new InvalidOperationException("请先为文档设置明确的标准长度单位，再导出 USD；不会猜测单位。");
        double meters = global::Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem, global::Rhino.UnitSystem.Meters);
        if (!double.IsFinite(meters) || meters <= 0) throw new InvalidOperationException("文档单位不能转换为米。");
        InstanceObject[] objects;
        if (selectedOnly)
        {
            RhinoObject[] selected = doc.Objects.GetSelectedObjects(true, true).ToArray();
            if (selected.Length == 0) throw new InvalidOperationException("请先选择需要导出的顶层 GS 块，或在面板选择全部顶层 GS。");
            if (selected.Any(o => o is not InstanceObject || !IsGaussian(o)))
                throw new InvalidOperationException("选中内容包含非 GS 对象或嵌套块；此导出仅支持顶层 GS 块，请调整选择。");
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
                .Where(obj => obj.Attributes.GetUserString(Marker) is not null).ToArray();
        }
        if (objects.Length == 0) throw new InvalidOperationException("当前文档没有可导出的顶层 GS 块。");
        var parts = objects.OrderBy(o => o.Id).Select(obj =>
        {
            GaussianReferenceMetadata reference = GaussianReferenceMetadata.Resolve(obj.Attributes.GetUserString, () => Array.Empty<string>()) ?? throw new InvalidDataException("GS 引用无效。");
            Transform t = obj.InstanceXform;
            if (!t.IsValid || t.M30 != 0 || t.M31 != 0 || t.M32 != 0 || t.M33 != 1)
                throw new InvalidDataException("GS 块包含无效或非仿射变换，导出已停止。");
            var linear = new GaussianMatrix(t.M00,t.M01,t.M02,t.M10,t.M11,t.M12,t.M20,t.M21,t.M22);
            _ = linear.Inverse();
            var transform = new GaussianTransform(linear, new GaussianVector(t.M03,t.M13,t.M23));
            bool antialiased = obj.Attributes.GetUserString(reference.Prefix + ".Antialiased") == "true";
            return new ExportPart(obj.Name ?? "", reference, transform, antialiased, Ready(doc, obj)?.Splats);
        }).ToArray();
        return new(doc.RuntimeSerialNumber, meters, parts, selectedOnly);
    }

    internal static async Task<string> ExportAsync(string path, ExportSnapshot snapshot, CancellationToken ct,
        IProgress<string>? progress = null)
    {
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken token = cancellation.Token;
        try
        {
            await AholoHost.OnUi(() =>
            {
                RhinoDoc doc = RhinoDoc.FromRuntimeSerialNumber(snapshot.Serial) ?? throw new InvalidOperationException("目标文档已关闭。");
                if (doc.IsClosing) throw new InvalidOperationException("目标文档已关闭。");
                Initialize();
                if (!Imports.TryGetValue(snapshot.Serial, out var pending)) Imports.Add(snapshot.Serial, pending = new());
                pending.Add(cancellation);
                return true;
            }).ConfigureAwait(false);
            return await Task.Run(() =>
            {
                var loaded = new Dictionary<string, GaussianSplat[]>(StringComparer.Ordinal);
                var parts = new GaussianPart[snapshot.Parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    token.ThrowIfCancellationRequested();
                    ExportPart part = snapshot.Parts[i];
                    progress?.Report($"准备完整 GS 资源 {i + 1:N0} / {parts.Length:N0}…");
                    if (!loaded.TryGetValue(part.Reference.Hash, out var splats))
                    {
                        splats = part.Loaded ?? GaussianReferenceAsset.Load(part.Reference, CacheRoot, token).Splats;
                        loaded.Add(part.Reference.Hash, splats);
                    }
                    parts[i] = new(part.Name, splats, part.Transform, part.Antialiased);
                }
                progress?.Report("正在生成 USD 场景并封装；大场景可能需要几分钟…");
                UsdGaussianWriter.Write(path, new GaussianScene(parts, "Z", snapshot.MetersPerUnit, "USD"), token);
                long count = parts.Sum(p => (long)p.Splats.Length);
                return $"已导出 {parts.Length:N0} 个 GS 块、{count:N0} 个高斯。\n{path}\n" +
                    "使用点击导出时的文档坐标与单位；保留完整 SH 和对象变换。USD 几何字段采用标准 float32 精度。" +
                    (snapshot.SelectedOnly ? "" : " 隐藏/锁定的顶层 GS 也作为可见资产导出。");
            }, token).ConfigureAwait(false);
        }
        finally
        {
            await AholoHost.OnUi(() =>
            {
                if (Imports.TryGetValue(snapshot.Serial, out var pending))
                {
                    pending.Remove(cancellation);
                    if (pending.Count == 0) Imports.Remove(snapshot.Serial);
                }
                cancellation.Dispose();
                return true;
            }).ConfigureAwait(false);
        }
    }
}
