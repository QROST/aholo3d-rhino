using System.Globalization;
using System.Text;
using Aholo.Core;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.PlugIns;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
namespace Aholo.Rhino;

/// <summary>Public Rhino draw APIs only. This small-scene prototype is not a dedicated GPU splat engine.</summary>
internal sealed class GaussianViewport : DisplayConduit
{
    internal const string ModeName = "Aholo GS (Experimental)";
    private const string ModeSetting = "GaussianViewport.ModeId";
    private static GaussianViewport? _instance;
    private static Guid _modeId;
    private readonly Dictionary<Guid, Frame> _frames = new();
    private string? _texturePath;
    private string? _lastError;
    private string _backend = "尚未绘制；后端未检测";
    private int _lastSplats;
    private int _lastVisible;
    private sealed class DrawSplat : IDisposable
    {
        internal required Mesh Quad;
        internal required DisplayMaterial Material;
        internal required double Depth;
        internal required int Ordinal;
        public void Dispose() { Quad.Dispose(); Material.Dispose(); }
    }
    private sealed class Frame : IDisposable
    {
        internal required uint Document;
        internal required GaussianCamera Camera;
        internal required string PlacementKey;
        internal required List<DrawSplat> Splats;
        internal required HashSet<Guid> InstanceIds;
        internal bool Failed;
        public void Dispose() { foreach(var s in Splats) s.Dispose(); }
    }
    internal static void Initialize(PlugIn plugin)
    {
        if(_instance is not null) return;
        string saved=plugin.Settings.GetString(ModeSetting,string.Empty);
        DisplayModeDescription? mode=null;
        if(Guid.TryParse(saved,out var previous)) mode=DisplayModeDescription.GetDisplayMode(previous);
        if(mode is null || mode.EnglishName!=ModeName)
        {
            mode?.Dispose();
            _modeId=DisplayModeDescription.CopyDisplayMode(DisplayModeDescription.ShadedId,ModeName);
            if(_modeId==Guid.Empty) throw new InvalidOperationException("无法注册 Aholo GS 实验显示模式。");
            mode=DisplayModeDescription.GetDisplayMode(_modeId);
        }
        else _modeId=mode.Id;
        using(mode)
        {
            if(mode is null) throw new InvalidOperationException("无法读取 Aholo GS 显示模式。");
            mode.InMenu=true; mode.AllowObjectAssignment=false; mode.SupportsShading=true;
            mode.ShadedPipelineRequired=true; mode.WireframePipelineRequired=false;
            mode.DisplayAttributes.ShadingEnabled=true; mode.DisplayAttributes.DisableTransparency=false;
            if (!DisplayModeDescription.UpdateDisplayMode(mode)) throw new InvalidOperationException("无法保存 Aholo GS 显示模式。");
        }
        plugin.Settings.SetString(ModeSetting,_modeId.ToString("D"));
        var conduit=new GaussianViewport();
        string texture=Path.Combine(Path.GetDirectoryName(typeof(GaussianViewport).Assembly.Location)!,"assets","gaussian-alpha.png");
        if(File.Exists(texture)) conduit._texturePath=texture;
        else conduit._lastError="缺少 Gaussian alpha 纹理；GS 保留为可见包围盒。";
        _instance=conduit; GaussianDocument.Initialize(); conduit.Enabled=true;
    }
    internal static void Shutdown()
    {
        if(_instance is null) return;
        _instance.Enabled=false;
        foreach(var frame in _instance._frames.Values) frame.Dispose();
        _instance._frames.Clear(); _instance=null; GaussianDocument.Shutdown();
    }
    internal static void ForgetDocument(uint serial)
    {
        if(_instance is null) return;
        foreach(Guid id in _instance._frames.Where(p=>p.Value.Document==serial).Select(p=>p.Key).ToArray())
        { _instance._frames[id].Dispose(); _instance._frames.Remove(id); }
    }
    internal static void PruneViews(RhinoDoc doc)
    {
        if (_instance is null || _instance._frames.Count == 0) return;
        var live = new HashSet<Guid>();
        foreach (RhinoView view in doc.Views.GetViewList(ViewTypeFilter.Model | ViewTypeFilter.Page))
        {
            live.Add(view.MainViewport.Id); live.Add(view.ActiveViewport.Id);
            if (view is RhinoPageView page)
                foreach (var detail in page.GetDetailViews()) live.Add(detail.Viewport.Id);
        }
        foreach (Guid id in _instance._frames.Where(p => p.Value.Document == doc.RuntimeSerialNumber && !live.Contains(p.Key)).Select(p => p.Key).ToArray())
            _instance.RemoveFrame(id);
    }
    internal static void Activate(RhinoDoc doc)
    {
        if(_instance is null || _modeId==Guid.Empty) throw new InvalidOperationException("Aholo GS 显示模式尚未初始化。");
        var view=doc.Views.ActiveView ?? throw new InvalidOperationException("请先激活一个模型视窗。");
        using var mode=DisplayModeDescription.GetDisplayMode(_modeId) ?? throw new InvalidOperationException("Aholo GS 显示模式已被删除，请重新加载插件。");
        view.ActiveViewport.DisplayMode=mode;
        GaussianDocument.Reload(doc); view.Redraw();
    }
    private static bool IsMode(RhinoViewport viewport) => viewport.DisplayMode?.Id==_modeId;
    private bool Supported(DrawEventArgs e) => _texturePath is not null && IsMode(e.Viewport) && !e.Display.IsPrinting && !e.Display.IsInViewCapture && !e.Display.IsStereoMode;
    protected override void PreDrawObjects(DrawEventArgs e)
    {
        if(!Supported(e)) { RemoveFrame(e.Viewport.Id); return; }
        _backend=e.Display.IsOpenGL ? "OpenGL（当前视窗）" : "Non-OpenGL（不能仅据此确认 Metal）";
        try
        {
            if (_frames.TryGetValue(e.Viewport.Id, out var failed) && failed.Failed && failed.Document == e.RhinoDoc.RuntimeSerialNumber) return;
            GaussianCamera camera=Camera(e.Viewport,e.Display.FrameSize.Width,e.Display.FrameSize.Height);
            var placements=GaussianDocument.Placements(e.RhinoDoc,e.Viewport);
            string key=PlacementKey(placements);
            if(_frames.TryGetValue(e.Viewport.Id,out var old) && old.Document==e.RhinoDoc.RuntimeSerialNumber && old.Camera==camera && old.PlacementKey==key) return;
            // A failed rebuild uses the native placeholder, so retaining two full mesh/material sets has no benefit.
            RemoveFrame(e.Viewport.Id);
            Frame next=BuildFrame(e.RhinoDoc.RuntimeSerialNumber,camera,placements,key,e.Display);
            _frames[e.Viewport.Id]=next;
            _lastError=null;
        }
        catch(Exception ex)
        {
            RemoveFrame(e.Viewport.Id);
            _lastError="GS 绘制准备未完成，保留原生包围盒："+(ex is OperationCanceledException ? ex.Message : AholoHost.FriendlyError(ex));
        }
    }
    protected override void PreDrawObject(DrawObjectEventArgs e)
    {
        if(!Supported(e) || !_frames.TryGetValue(e.Viewport.Id,out var frame) || frame.Failed) return;
        // Suppress only our placeholder for an instance included in this frame. Missing/corrupt assets remain visible.
        RhinoObject? top=e.Display.ActiveTopLevelObject;
        if(top is not null && frame.InstanceIds.Contains(top.Id)) e.DrawObject=false;
        else if(frame.InstanceIds.Contains(e.RhinoObject.Id)) e.DrawObject=false;
    }
    protected override void PostDrawObjects(DrawEventArgs e)
    {
        if(!Supported(e) || !_frames.TryGetValue(e.Viewport.Id,out var frame) || frame.Failed) return;
        bool testingPushed=false, writingPushed=false;
        try
        {
            e.Display.PushDepthTesting(true); testingPushed=true;
            e.Display.PushDepthWriting(false); writingPushed=true;
            // All placed assets share one sort; GS never writes hard rectangle depths into Rhino's opaque depth buffer.
            foreach(DrawSplat splat in frame.Splats)
            {
                if(e.Display.InterruptDrawing()) break;
                e.Display.DrawMeshShaded(splat.Quad,splat.Material);
            }
            e.Display.Flush();
        }
        catch(Exception ex)
        {
            frame.Failed=true;
            _lastError="GS 实验绘制失败；后续帧保留包围盒，可运行 AholoGsMode 重试："+AholoHost.FriendlyError(ex);
        }
        finally
        {
            try { if(writingPushed) e.Display.PopDepthWriting(); }
            finally { if(testingPushed) e.Display.PopDepthTesting(); }
        }
    }
    private Frame BuildFrame(uint serial,GaussianCamera camera,List<GaussianDocument.Placement> placements,string key,DisplayPipeline pipeline)
    {
        var draws=new List<DrawSplat>(); var instances=new HashSet<Guid>(); int count=0;
        try
        {
            foreach(var placement in placements)
            {
                instances.Add(placement.Object.Id);
                foreach(GaussianSplat source in placement.Asset.Splats)
                {
                    // Rhino's own interruption is a transient draw budget, never an import or geometry-count limit.
                    if(pipeline.InterruptDrawing()) throw new OperationCanceledException("Rhino 已中断本次绘制；保留包围盒，可停止旋转后重试。");
                    count++;
                    ProjectedGaussian? p=GaussianProjector.Project(source,camera,placement.Transform);
                    if(p is null) continue;
                    var mesh=new Mesh(); mesh.Vertices.UseDoublePrecisionVertices=true;
                    DisplayMaterial? material=null;
                    try
                    {
                        AddCorner(mesh,camera,p,-1,-1); AddCorner(mesh,camera,p,1,-1); AddCorner(mesh,camera,p,1,1); AddCorner(mesh,camera,p,-1,1);
                        // Pixel Y points down; reverse the face so its front faces the camera.
                        mesh.Faces.AddFace(0,3,2,1);
                        mesh.TextureCoordinates.Add(0,0);mesh.TextureCoordinates.Add(1,0);mesh.TextureCoordinates.Add(1,1);mesh.TextureCoordinates.Add(0,1);
                        mesh.Normals.ComputeNormals();
                        material=CreateMaterial(p);
                        draws.Add(new DrawSplat { Quad=mesh, Material=material, Depth=p.Depth, Ordinal=count });
                    }
                    catch { mesh.Dispose(); material?.Dispose(); throw; }
                }
            }
            // The CPU oracle is stable front-to-back; reverse equal-depth insertion order for back-to-front alpha.
            draws.Sort((a,b) => { int depth=b.Depth.CompareTo(a.Depth); return depth != 0 ? depth : b.Ordinal.CompareTo(a.Ordinal); });
            _lastSplats=count; _lastVisible=draws.Count;
            return new Frame { Document=serial,Camera=camera,PlacementKey=key,Splats=draws,InstanceIds=instances };
        }
        catch { foreach(var draw in draws) draw.Dispose(); throw; }
    }
    private DisplayMaterial CreateMaterial(ProjectedGaussian p)
    {
        var color=Color.FromArgb(Byte(p.Color.R),Byte(p.Color.G),Byte(p.Color.B));
        using var source=new Material
        {
            DiffuseColor=color, DisableLighting=true, AlphaTransparency=true,
            Transparency=1-p.Opacity, SpecularColor=Color.Black, Reflectivity=0, Shine=0
        };
        using var texture=new Texture
        {
            FileName=_texturePath!, Enabled=true,
            TextureCombineMode=TextureCombineMode.Modulate,
            WrapU=TextureUvwWrapping.Clamp, WrapV=TextureUvwWrapping.Clamp,
            MinFilter=TextureFilter.Linear, MagFilter=TextureFilter.Linear
        };
        if(!source.SetBitmapTexture(texture)) throw new InvalidOperationException("无法设置 Gaussian alpha 纹理。");
        var result=new DisplayMaterial(source) { IsTwoSided=false };
        return result;
    }
    private static int Byte(double x)=>(int)Math.Round(Math.Clamp(x,0,1)*255);
    private static void AddCorner(Mesh mesh,GaussianCamera camera,ProjectedGaussian p,int a,int b)
    {
        GaussianVector v=camera.WorldAtPixel(p.CenterX+a*p.Axis1X+b*p.Axis2X,p.CenterY+a*p.Axis1Y+b*p.Axis2Y,p.Depth);
        mesh.Vertices.Add(v.X,v.Y,v.Z);
    }
    private static GaussianVector Vector(Vector3d v)=>new(v.X,v.Y,v.Z);
    private static GaussianCamera Camera(RhinoViewport viewport,int width,int height)
    {
        if(width<=0 || height<=0 || !viewport.GetFrustum(out double left,out double right,out double bottom,out double top,out double near,out double far))
            throw new InvalidOperationException("无法读取视窗尺寸或相机视锥。");
        var eye=viewport.CameraLocation;
        double factor=viewport.IsPerspectiveProjection?near:1;
        return new GaussianCamera(new(eye.X,eye.Y,eye.Z),Vector(viewport.CameraX),Vector(viewport.CameraY),Vector(viewport.CameraDirection).Normalized(),viewport.IsPerspectiveProjection,width,height,
            factor*width/(right-left),factor*height/(top-bottom),-left*width/(right-left)-0.5,top*height/(top-bottom)-0.5,near,far);
    }
    private static string PlacementKey(IEnumerable<GaussianDocument.Placement> placements)
    {
        var key=new StringBuilder();
        foreach(var p in placements)
        {
            key.Append(p.Object.Id).Append(':').Append(p.Asset.Hash).Append(':');
            Transform t=p.Object.InstanceXform;
            for(int row=0;row<3;row++)for(int col=0;col<4;col++) key.Append(t[row,col].ToString("R",CultureInfo.InvariantCulture)).Append(',');
            key.Append(';');
        }
        return key.ToString();
    }
    private void RemoveFrame(Guid id) { if(_frames.Remove(id,out var frame)) frame.Dispose(); }
    internal static string Diagnostics(RhinoDoc doc)
    {
        var active=doc.Views.ActiveView?.ActiveViewport;
        return "Aholo GS：实验原型；"+(_instance?._backend??"尚未初始化")+"。\n"+
            GaussianDocument.Diagnostics(doc)+"\n"+
            $"当前模式：{active?.DisplayMode?.EnglishName??"无"}；最近一帧输入：{_instance?._lastSplats??0:N0}；投影可见：{_instance?._lastVisible??0:N0}。\n"+
            "离线数学检查与编译不等于宿主验收。需手动验证不透明 Mesh/Brep/NURBS/点云遮挡及柔和 alpha。透明体交错、Raytraced、打印/视图捕获、立体视图、嵌套在其他块内和大场景 GPU 性能尚不支持。原生材质为 8 位颜色，SH 高光值在混合前截断；不保证与 CPU 的 HDR 混合结果一致。\n"+
            (_instance?._lastError??"宿主视觉结果尚待确认。");
    }
}
