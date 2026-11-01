using System.Globalization;
using Aholo.Core;
using Eto.Forms;
using Rhino.DocObjects;
using Rhino.Geometry;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
namespace Aholo.Rhino;

internal sealed record ImportTarget(uint Serial, global::Rhino.UnitSystem Units);
internal sealed record RenderSnapshot(IReadOnlyList<MeshData> Meshes, RenderCamera Camera, string Name);

/// <summary>Capture and commit occur on the Rhino UI thread; parsing and file serialization are asynchronous.</summary>
internal static class AholoHost
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
        ? "内存不足，未完成预览。原始 PLY 和远程任务仍保留，可释放内存后重试。"
        : e is OperationCanceledException ? "本地等待已停止；远程任务可能仍在运行，可从历史查询，勿重复提交。" : e.Message;
    internal static ImportTarget CaptureTarget(RhinoDoc doc)
    {
        RequireUi(); RequireDocument(doc);
        return new(doc.RuntimeSerialNumber, doc.ModelUnitSystem);
    }
    private static void RequireUi()
    {
        if (global::Rhino.RhinoApp.InvokeRequired) throw new InvalidOperationException("请从 Rhino 界面发起此操作。");
    }
    private static void RequireDocument(RhinoDoc doc)
    {
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != doc.RuntimeSerialNumber) throw new InvalidOperationException("请先激活面板对应的文档。");
        if (doc.IsClosing || doc.IsReadOnly) throw new InvalidOperationException("目标文档当前不可写。");
        if (doc.Views.ActiveView is global::Rhino.Display.RhinoPageView { PageIsActive: true }) throw new InvalidOperationException("请切换到模型视图，或激活布局中的模型细节视口。");
    }
    internal static async Task<string> ImportAsync(string path, ImportTarget target, double scale, string upAxis, Guid? taskId, CancellationToken ct)
    {
        if (!double.IsFinite(scale) || scale <= 0) throw new InvalidOperationException("导入比例必须是大于零的有限数字。");
        if (upAxis is not ("Y" or "Z")) throw new InvalidOperationException("请选择 Y 或 Z 向上。");
        // Keep all source data. This is a native point-cloud preview, not a splat renderer or a reconstructed mesh.
        var data = await Task.Run(() =>
        {
            var points = new List<Point3d>(); var colors = new List<Color>();
            foreach (SplatPoint p in PlyReader.Read(path, ct))
            {
                ct.ThrowIfCancellationRequested();
                var point = upAxis == "Y" ? new Point3d(p.X * scale, -p.Z * scale, p.Y * scale) : new Point3d(p.X * scale, p.Y * scale, p.Z * scale);
                if (!point.IsValid) throw new InvalidOperationException("缩放后的 PLY 坐标无效，请检查比例。");
                points.Add(point); colors.Add(Color.FromArgb(p.R, p.G, p.B));
            }
            if (points.Count == 0) throw new InvalidOperationException("PLY 中没有可导入的点。");
            return (Points: points, Colors: colors);
        }, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return await OnUi(() =>
        {
            ct.ThrowIfCancellationRequested(); RequireUi();
            RhinoDoc doc = RhinoDoc.FromRuntimeSerialNumber(target.Serial) ?? throw new InvalidOperationException("原目标文档已关闭。请在需要的文档重新发起导入。");
            RequireDocument(doc);
            if (doc.ModelUnitSystem != target.Units) throw new InvalidOperationException("文档单位在读取期间改变，请确认比例后重新导入。");
            if (doc.IsCommandRunning || doc.UndoRecordingIsActive) throw new InvalidOperationException("请先结束当前 Rhino 命令，再重试导入。");
            // Repeated clicks on a task locate the existing object; deliberate local-file imports create new objects.
            if (taskId.HasValue)
            {
                RhinoObject? existing = doc.Objects.GetObjectList(ObjectType.PointSet).FirstOrDefault(o => !o.IsDeleted && o.Attributes.GetUserString("Aholo.TaskId") == taskId.Value.ToString("D"));
                if (existing is not null)
                {
                    doc.Objects.Select(existing.Id); doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(existing.Geometry.GetBoundingBox(true)); doc.Views.Redraw();
                    return "此任务已在文档中，已定位现有点云。若需改变比例，可使用 Rhino 的缩放命令。";
                }
            }
            using var cloud = new PointCloud(); cloud.AddRange(data.Points, data.Colors);
            if (!cloud.IsValid) throw new InvalidOperationException("Rhino 无法接受此点云；文档尚未修改。");
            var bounds = cloud.GetBoundingBox(true);
            if (!bounds.IsValid) throw new InvalidOperationException("点云包围盒无效；文档尚未修改。");
            using var attributes = new ObjectAttributes { Name = "Aholo · " + Path.GetFileNameWithoutExtension(path) };
            attributes.SetUserString("Aholo.Preview", "3DGS centers / DC color; not a splat renderer");
            attributes.SetUserString("Aholo.SourcePath", Path.GetFullPath(path));
            attributes.SetUserString("Aholo.Scale", scale.ToString("R", CultureInfo.InvariantCulture));
            attributes.SetUserString("Aholo.SourceUpAxis", upAxis);
            if (taskId.HasValue) attributes.SetUserString("Aholo.TaskId", taskId.Value.ToString("D"));
            uint undo = doc.BeginUndoRecord("导入 Aholo 点云预览");
            if (undo == 0) throw new InvalidOperationException("无法创建撤销记录，尚未导入。");
            Guid added;
            try { attributes.LayerIndex = VisibleLayer(doc); added = doc.Objects.AddPointCloud(cloud, attributes); }
            finally { doc.EndUndoRecord(undo); }
            if (added == Guid.Empty) throw new InvalidOperationException("Rhino 未能添加点云。");
            doc.Objects.Select(added);
            if (bounds.Diagonal.Length == 0) bounds.Inflate(Math.Max(scale, 1));
            doc.Views.ActiveView?.ActiveViewport.ZoomBoundingBox(bounds); doc.Views.Redraw();
            return $"已放入 {data.Points.Count:N0} 个彩色中心点并定位视图。可用一次 Undo 撤销。原始 PLY 保留；此预览不包含高斯透明度、尺寸和视角相关效果。";
        }).ConfigureAwait(false);
    }
    private static int VisibleLayer(RhinoDoc doc)
    {
        // A hidden/locked current layer must not make a successful import appear missing.
        Layer current = doc.Layers.CurrentLayer;
        if (current.IsVisible && !current.IsLocked) return current.Index;
        int index = doc.Layers.Add("Aholo preview " + Guid.NewGuid().ToString("N")[..8], Color.FromArgb(100, 180, 220));
        if (index < 0) throw new InvalidOperationException("无法创建可见的预览图层。");
        return index;
    }
    internal static RenderSnapshot CaptureRender(RhinoDoc doc, int width, int height)
    {
        RequireUi(); RequireDocument(doc);
        var viewport = doc.Views.ActiveView?.ActiveViewport ?? throw new InvalidOperationException("请先激活一个透视视图。");
        if (!viewport.IsPerspectiveProjection) throw new InvalidOperationException("当前版本 RenderCloud 需要透视视图，请切换到 Perspective 后重试。");
        if (!viewport.GetFrustum(out double left, out double right, out double bottom, out double top, out double near, out double far)) throw new InvalidOperationException("无法读取当前视图的相机。");
        if (Math.Abs(left + right) > Math.Max(1e-9, (right - left) * 1e-5) || Math.Abs(bottom + top) > Math.Max(1e-9, (top - bottom) * 1e-5)) throw new InvalidOperationException("当前相机使用偏移视锥。请切换到标准透视视图；本版暂不导出镜头偏移。");
        if (doc.ModelUnitSystem is global::Rhino.UnitSystem.None or global::Rhino.UnitSystem.Unset or global::Rhino.UnitSystem.CustomUnits) throw new InvalidOperationException("请先为 Rhino 文档设置明确的长度单位。");
        double mm = global::Rhino.RhinoMath.UnitScale(doc.ModelUnitSystem, global::Rhino.UnitSystem.Millimeters);
        if (!double.IsFinite(mm) || mm <= 0) throw new InvalidOperationException("无法换算文档单位。");
        RhinoObject[] objects = doc.Objects.GetSelectedObjects(false, false).ToArray();
        if (objects.Length == 0) throw new InvalidOperationException("请先在 Rhino 中选中要渲染的网格、曲面或实体。");
        if (objects.Any(o => o.Geometry is not (Mesh or Brep or Extrusion))) throw new InvalidOperationException("选区包含暂不支持的对象。本版支持网格、曲面/多重曲面和挤出；请先取消其他对象的选择，块可先 Explode。");
        var meshes = new List<MeshData>();
        foreach (RhinoObject obj in objects)
        {
            Color color = obj.Attributes.DrawColor(doc);
            // Export diffuse color only; Rhino textures and procedural materials are not translated in this version.
            if (obj.Attributes.MaterialSource == ObjectMaterialSource.MaterialFromObject && obj.Attributes.MaterialIndex >= 0)
                color = doc.Materials[obj.Attributes.MaterialIndex].DiffuseColor;
            Mesh[] generated = obj.Geometry switch
            {
                Mesh mesh => new[] { mesh.DuplicateMesh() },
                Brep brep => Mesh.CreateFromBrep(brep, MeshingParameters.Default) ?? Array.Empty<Mesh>(),
                Extrusion extrusion => MeshExtrusion(extrusion),
                _ => Array.Empty<Mesh>()
            };
            try
            {
                if (generated.Length == 0) throw new InvalidOperationException("选中的对象无法转换为网格，尚未上传。");
                foreach (Mesh mesh in generated)
                {
                    if (mesh.Vertices.Count == 0 || mesh.Faces.Count == 0 || !mesh.IsValid) throw new InvalidOperationException("选区包含空网格或无效网格，请先在 Rhino 中修复。");
                    var positions = new float[checked(mesh.Vertices.Count * 3)];
                    for (int i = 0; i < mesh.Vertices.Count; i++)
                    {
                        Point3d p = mesh.Vertices.Point3dAt(i);
                        positions[i * 3] = CheckedFloat(p.X * mm); positions[i * 3 + 1] = CheckedFloat(p.Y * mm); positions[i * 3 + 2] = CheckedFloat(p.Z * mm);
                    }
                    var indices = new List<int>();
                    foreach (MeshFace face in mesh.Faces)
                    {
                        indices.Add(face.A); indices.Add(face.B); indices.Add(face.C);
                        if (face.IsQuad) { indices.Add(face.A); indices.Add(face.C); indices.Add(face.D); }
                    }
                    meshes.Add(new MeshData(positions, indices.ToArray(), color.R, color.G, color.B));
                }
            }
            finally { foreach (Mesh mesh in generated) mesh.Dispose(); }
        }
        Vector3d x = viewport.CameraX, y = viewport.CameraY, z = -viewport.CameraDirection;
        if (!x.Unitize() || !y.Unitize() || !z.Unitize()) throw new InvalidOperationException("相机方向无效。");
        Point3d location = viewport.CameraLocation;
        double focal = viewport.Camera35mmLensLength;
        if (!double.IsFinite(focal) || focal <= 0) throw new InvalidOperationException("相机焦距无效。");
        double horizontalAperture = (right - left) / near * focal;
        // Preserve horizontal field of view; use the chosen output aspect ratio for the vertical aperture.
        double verticalAperture = horizontalAperture * height / width;
        var camera = new RenderCamera(new[] { x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, location.X * mm, location.Y * mm, location.Z * mm, 1 }, focal, horizontalAperture, verticalAperture, near * mm, far * mm);
        return new RenderSnapshot(meshes, camera, string.IsNullOrWhiteSpace(doc.Name) ? "Rhino 选区渲染" : Path.GetFileNameWithoutExtension(doc.Name) + " · 选区渲染");
    }
    private static Mesh[] MeshExtrusion(Extrusion extrusion)
    {
        using Brep brep = extrusion.ToBrep();
        return Mesh.CreateFromBrep(brep, MeshingParameters.Default) ?? Array.Empty<Mesh>();
    }
    private static float CheckedFloat(double value)
    {
        float result = (float)value;
        if (!float.IsFinite(result)) throw new InvalidOperationException("选区坐标超出 GLB 可表示范围，请先检查模型单位或坐标。");
        return result;
    }
}
