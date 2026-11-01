using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Aholo.Core;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Rhino.PlugIns;
using RhinoDoc = global::Rhino.RhinoDoc;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;
namespace Aholo.Rhino;

/// <summary>Native GS batches: pure background projection, bounded UI transfers, one shared display material.</summary>
internal sealed class GaussianViewport : DisplayConduit
{
    internal const string ModeName="Aholo GS (Experimental)";
    private const string ModeSetting="GaussianViewport.ModeId";
    private const double UploadBudgetMilliseconds=4;
    private static GaussianViewport? _instance;
    private static Guid _modeId;
    private readonly Dictionary<Guid,ViewState> _views=new();
    private readonly Dictionary<Guid,NativeView> _nativeViews=new();
    private readonly Queue<Mesh> _retired=new();
    private readonly Dictionary<uint,int> _saving=new();
    private readonly ConcurrentQueue<WorkResult> _results=new();
    private DisplayMaterial? _material;
    private Task? _worker;
    private Eto.Forms.UITimer? _timer;
    private bool _pumping;
    private ActiveWork? _activeWork;
    private long _generation;
    private volatile bool _stopping;
    private string? _lastError;
    private string _backend="尚未绘制；后端未检测";
    private int _skippedPreviews;
    private DateTime _lastViewRefresh;
    private sealed record NativeView(uint Document,Size ViewportSize,Size ClientSize);
    private sealed record NativeBatch(Mesh Mesh,int[] FaceIndices);
    private sealed record WorkResult(GaussianNativeRequest Request,GaussianNativePreparation? Prepared,string? Error,bool Cancelled);
    private sealed record ActiveWork(GaussianNativeRequest Request,WorkCancellation Cancellation);
    private sealed class WorkCancellation : IDisposable
    {
        private readonly object _gate=new();
        private CancellationTokenSource? _source=new();
        internal CancellationToken Token { get { lock(_gate) return _source!.Token; } }
        internal void Cancel() { lock(_gate) _source?.Cancel(); }
        public void Dispose() { lock(_gate) { _source?.Dispose();_source=null; } }
    }
    private sealed class ViewState
    {
        internal required GaussianNativeRequest Request;
        internal DateTime ChangedAt=DateTime.UtcNow;
        internal bool Started;
        internal bool Complete;
        internal string? Error;
        internal GaussianNativePreparation? Pending;
        internal readonly List<NativeBatch> Batches=new();
        internal int InputCount;
        internal int VisibleCount;
        internal int TotalBatches;
        internal double PreparationMilliseconds;
        internal double MaxUploadMilliseconds;
        internal double LastDrawMilliseconds;
    }
    internal static void Initialize(PlugIn plugin)
    {
        if(_instance is not null)return;
        string saved=plugin.Settings.GetString(ModeSetting,string.Empty);
        DisplayModeDescription? mode=null;
        if(Guid.TryParse(saved,out var previous))mode=DisplayModeDescription.GetDisplayMode(previous);
        if(mode is null||mode.EnglishName!=ModeName)
        {
            mode?.Dispose();_modeId=DisplayModeDescription.CopyDisplayMode(DisplayModeDescription.ShadedId,ModeName);
            if(_modeId==Guid.Empty)throw new InvalidOperationException("无法注册 Aholo GS 实验显示模式。");
            mode=DisplayModeDescription.GetDisplayMode(_modeId);
        }
        else _modeId=mode.Id;
        using(mode)
        {
            if(mode is null)throw new InvalidOperationException("无法读取 Aholo GS 显示模式。");
            mode.InMenu=true;mode.AllowObjectAssignment=false;mode.SupportsShading=true;mode.ShadedPipelineRequired=true;mode.WireframePipelineRequired=false;
            mode.DisplayAttributes.ShadingEnabled=true;mode.DisplayAttributes.DisableTransparency=false;mode.DisplayAttributes.ShadeVertexColors=true;mode.DisplayAttributes.FrontFlatShaded=false;
            if(!DisplayModeDescription.UpdateDisplayMode(mode))throw new InvalidOperationException("无法保存 Aholo GS 显示模式。");
        }
        plugin.Settings.SetString(ModeSetting,_modeId.ToString("D"));
        var conduit=new GaussianViewport();
        string texture=Path.Combine(Path.GetDirectoryName(typeof(GaussianViewport).Assembly.Location)!,"assets","gaussian-opacity-atlas.png");
        if(File.Exists(texture))conduit._material=CreateMaterial(texture);
        else conduit._lastError="缺少 Gaussian opacity atlas；保留可见包围盒。";
        _instance=conduit;GaussianDocument.Initialize();conduit.RefreshNativeViews();
        conduit._timer=new Eto.Forms.UITimer{Interval=.016};conduit._timer.Elapsed+=conduit.OnIdle;conduit._timer.Start();
        RhinoDoc.BeginSaveDocument+=conduit.OnBeginSave;RhinoDoc.EndSaveDocument+=conduit.OnEndSave;
        conduit.Enabled=true;
        GaussianLog.Write("viewport_initialize","batch=2048; shared opacity atlas; native model view/save guards enabled");
    }
    internal static void Shutdown()
    {
        var conduit=_instance;if(conduit is null)return;
        conduit._stopping=true;conduit.Enabled=false;
        if(conduit._timer is not null){conduit._timer.Stop();conduit._timer.Elapsed-=conduit.OnIdle;conduit._timer.Dispose();conduit._timer=null;}
        RhinoDoc.BeginSaveDocument-=conduit.OnBeginSave;RhinoDoc.EndSaveDocument-=conduit.OnEndSave;
        conduit._activeWork?.Cancellation.Cancel();
        foreach(var state in conduit._views.Values)conduit.Retire(state);
        conduit._views.Clear();conduit._nativeViews.Clear();
        while(conduit._retired.TryDequeue(out var mesh))mesh.Dispose();
        conduit._material?.Dispose();conduit._material=null;
        while(conduit._results.TryDequeue(out _)) { }
        _instance=null;GaussianDocument.Shutdown();GaussianLog.Write("viewport_shutdown","workers cancelled; native batches released");
    }
    internal static void ForgetDocument(uint serial)
    {
        var conduit=_instance;if(conduit is null)return;
        if(conduit._activeWork?.Request.Document==serial)conduit._activeWork.Cancellation.Cancel();
        foreach(Guid id in conduit._views.Where(p=>p.Value.Request.Document==serial).Select(p=>p.Key).ToArray())conduit.RemoveView(id);
    }
    internal static void DocumentClosed(uint serial)
    {
        _instance?._saving.Remove(serial);ForgetDocument(serial);
    }
    internal static void PruneViews(RhinoDoc doc)
    {
        var conduit=_instance;if(conduit is null)return;
        var live=doc.Views.GetViewList(ViewTypeFilter.Model).Select(v=>v.MainViewport.Id).ToHashSet();
        foreach(Guid id in conduit._views.Where(p=>p.Value.Request.Document==doc.RuntimeSerialNumber&&!live.Contains(p.Key)).Select(p=>p.Key).ToArray())conduit.RemoveView(id);
    }
    internal static void Activate(RhinoDoc doc)
    {
        if(_instance is null||_modeId==Guid.Empty)throw new InvalidOperationException("Aholo GS 显示模式尚未初始化。");
        var view=doc.Views.ActiveView??throw new InvalidOperationException("请先激活一个模型视窗。");
        if(view is RhinoPageView)throw new InvalidOperationException("请先切换到原生模型视窗；本版不在布局细节中绘制 GS。");
        using var mode=DisplayModeDescription.GetDisplayMode(_modeId)??throw new InvalidOperationException("Aholo GS 显示模式已被删除，请重新加载插件。");
        view.ActiveViewport.DisplayMode=mode;GaussianDocument.Reload(doc);_instance.RefreshNativeViews();view.Redraw();
    }
    private static bool IsMode(RhinoViewport viewport)=>viewport.DisplayMode?.Id==_modeId;
    private bool Supported(DrawEventArgs e)
    {
        if(_stopping||_material is null||!IsMode(e.Viewport)||e.Display.IsPrinting||e.Display.IsInViewCapture||e.Display.IsStereoMode||_saving.ContainsKey(e.RhinoDoc.RuntimeSerialNumber))return false;
        if(e.Display.IsInTiledDraw(out _,out _))return false;
        // Only sizes captured from real model views OUTSIDE display callbacks are trusted. Thumbnails and GetPreviewBitmap
        // may reuse a viewport ID, so matching that ID alone must never trigger a full rebuild/render.
        if(!_nativeViews.TryGetValue(e.Viewport.Id,out var native)||native.Document!=e.RhinoDoc.RuntimeSerialNumber)return false;
        Size size=e.Display.FrameSize;
        return size.Width>0&&size.Height>0&&(Matches(size,native.ViewportSize,e.Display.DpiScale)||Matches(size,native.ClientSize,e.Display.DpiScale));
    }
    private static bool Matches(Size actual,Size logical,double dpi)
    {
        if(actual==logical)return true;
        return double.IsFinite(dpi)&&dpi>0&&Math.Abs(actual.Width-Math.Round(logical.Width*dpi))<=1&&Math.Abs(actual.Height-Math.Round(logical.Height*dpi))<=1;
    }
    private void OnBeginSave(object? sender,global::Rhino.DocumentSaveEventArgs e)
    {
        _saving.TryGetValue(e.DocumentSerialNumber,out int depth);_saving[e.DocumentSerialNumber]=depth+1;
        GaussianLog.Write("save_preview_suppressed",$"doc={e.DocumentSerialNumber}; depth={depth+1}");
    }
    private void OnEndSave(object? sender,global::Rhino.DocumentSaveEventArgs e)
    {
        if(_saving.TryGetValue(e.DocumentSerialNumber,out int depth))
        {if(depth<=1)_saving.Remove(e.DocumentSerialNumber);else _saving[e.DocumentSerialNumber]=depth-1;}
    }
    protected override void PreDrawObjects(DrawEventArgs e)
    {
        if(!Supported(e)){if(IsMode(e.Viewport))_skippedPreviews++;return;}
        _backend=e.Display.IsOpenGL?"OpenGL（当前视窗）":"Non-OpenGL（不能仅据此确认 Metal）";
        try
        {
            GaussianCamera camera=Camera(e.Viewport,e.Display.FrameSize.Width,e.Display.FrameSize.Height);
            var placements=GaussianDocument.Placements(e.RhinoDoc,e.Viewport);
            string key=PlacementKey(placements);
            if(_views.TryGetValue(e.Viewport.Id,out var previous)&&previous.Request.Camera==camera&&previous.Request.Key==key)return;
            RemoveView(e.Viewport.Id);
            // Capture only immutable data and value transforms. The worker never reads RhinoObject/RhinoDoc state.
            var snapshot=placements.Select(p=>new GaussianNativePlacement(p.Object.Id,p.Asset.Hash,p.Asset.Prepared,p.Transform)).ToArray();
            var request=new GaussianNativeRequest(e.RhinoDoc.RuntimeSerialNumber,e.Viewport.Id,++_generation,camera,key,snapshot);
            _views[e.Viewport.Id]=new ViewState{Request=request,InputCount=snapshot.Sum(p=>p.Asset.Count)};
            _lastError=null;
        }
        catch(Exception ex){_lastError="GS 准备失败，保留包围盒："+AholoHost.FriendlyError(ex);RemoveView(e.Viewport.Id);}
    }
    protected override void PreDrawObject(DrawObjectEventArgs e)
    {
        if(!Supported(e)||!_views.TryGetValue(e.Viewport.Id,out var state)||!state.Complete||state.Error is not null)return;
        RhinoObject? top=e.Display.ActiveTopLevelObject;
        foreach(var placement in state.Request.Placements)
            if(placement.Id==e.RhinoObject.Id||placement.Id==top?.Id){e.DrawObject=false;return;}
    }
    protected override void PostDrawObjects(DrawEventArgs e)
    {
        if(!Supported(e)||!_views.TryGetValue(e.Viewport.Id,out var state)||!state.Complete||state.Error is not null)return;
        bool testing=false,writing=false;var clock=Stopwatch.StartNew();
        try
        {
            e.Display.PushDepthTesting(true);testing=true;e.Display.PushDepthWriting(false);writing=true;
            foreach(var batch in state.Batches)
            {
                if(e.Display.InterruptDrawing())break;
                // The face-index overload preserves vertex RGB in Rhino 8. The two-argument overload strips it.
                e.Display.DrawMeshShaded(batch.Mesh,_material!,batch.FaceIndices);
            }
            e.Display.Flush();
        }
        catch(Exception ex){state.Error="GS 批量绘制失败；保留包围盒，运行 AholoGsMode 重试："+AholoHost.FriendlyError(ex);_lastError=state.Error;GaussianLog.Write("draw_failed",$"generation={state.Request.Generation}; {state.Error}");}
        finally
        {
            try{if(writing)e.Display.PopDepthWriting();}finally{if(testing)e.Display.PopDepthTesting();}
            state.LastDrawMilliseconds=clock.Elapsed.TotalMilliseconds;
        }
    }
    protected override void DrawForeground(DrawEventArgs e)
    {
        if(!Supported(e)||!_views.TryGetValue(e.Viewport.Id,out var state)||state.Complete&&state.Error is null)return;
        string progress=state.Error is not null?"Aholo GS：准备失败，已保留包围盒；运行 AholoGsDiagnostics":state.Pending is not null?$"Aholo GS：上传 {state.Batches.Count:N0}/{state.TotalBatches:N0} 批；完整就绪后显示":state.Started?"Aholo GS：后台投影与排序中；原始资源完整保留":"Aholo GS：视图稳定后准备显示";
        e.Display.Draw2dText(progress,Color.FromArgb(45,135,175),new Point2d(18,38),false,14);
    }
    private void OnIdle(object? sender,EventArgs e)
    {
        if(_stopping||_pumping)return;
        _pumping=true;
        try
        {
            if(DateTime.UtcNow-_lastViewRefresh>TimeSpan.FromMilliseconds(250))RefreshNativeViews();
            while(_results.TryDequeue(out var result))
            {
                if(!_views.TryGetValue(result.Request.Viewport,out var state)||state.Request.Generation!=result.Request.Generation)continue;
                if(result.Cancelled)continue;
                if(result.Error is not null){state.Error=result.Error;_lastError=result.Error;GaussianLog.Write("prepare_failed",$"generation={state.Request.Generation}; {result.Error}");Redraw(state.Request);continue;}
                state.Pending=result.Prepared!;state.InputCount=result.Prepared!.InputCount;state.VisibleCount=result.Prepared.VisibleCount;
                state.TotalBatches=result.Prepared.Batches.Length;state.PreparationMilliseconds=result.Prepared.Milliseconds;
                GaussianLog.Write("prepare_ready",$"generation={state.Request.Generation}; input={state.InputCount}; visible={state.VisibleCount}; batches={state.TotalBatches}; ms={state.PreparationMilliseconds:F1}");
            }
            var slice=Stopwatch.StartNew();
            while(_retired.Count>0&&slice.Elapsed.TotalMilliseconds<UploadBudgetMilliseconds)_retired.Dequeue().Dispose();
            foreach(var state in _views.Values.ToArray())
            {
                if(state.Pending is null||state.Error is not null)continue;
                int previousBatchCount=state.Batches.Count;
                while(state.Batches.Count<state.TotalBatches&&slice.Elapsed.TotalMilliseconds<UploadBudgetMilliseconds)
                {
                    int index=state.Batches.Count;var batch=state.Pending.Batches[index]!;
                    var upload=Stopwatch.StartNew();Mesh? mesh=null;
                    try
                    {
                        mesh=new Mesh();mesh.Vertices.UseDoublePrecisionVertices=true;
                        mesh.Vertices.AddVertices(batch.Vertices);mesh.Faces.AddFaces(batch.Faces);
                        if(!mesh.Normals.SetNormals(batch.Normals)||!mesh.TextureCoordinates.SetTextureCoordinates(batch.Uv)||!mesh.VertexColors.SetColors(batch.Colors))throw new InvalidOperationException("Rhino 未能接受 GS 法线、UV 或顶点色。");
                        mesh.SetSurfaceParametersFromTextureCoordinates();
                        state.Batches.Add(new NativeBatch(mesh,batch.FaceIndices));mesh=null;state.Pending.Batches[index]=null;
                        state.MaxUploadMilliseconds=Math.Max(state.MaxUploadMilliseconds,upload.Elapsed.TotalMilliseconds);
                        if(state.Batches.Count%16==0)GaussianLog.Write("upload_progress",$"generation={state.Request.Generation}; batches={state.Batches.Count}/{state.TotalBatches}");
                    }
                    catch(Exception ex){mesh?.Dispose();state.Error="GS 上传失败，保留包围盒："+AholoHost.FriendlyError(ex);_lastError=state.Error;Retire(state);break;}
                }
                if(state.Pending is not null&&state.Batches.Count==state.TotalBatches)
                {state.Pending=null;state.Complete=true;GaussianLog.Write("frame_ready",$"generation={state.Request.Generation}; visible={state.VisibleCount}; batches={state.Batches.Count}; maxUploadMs={state.MaxUploadMilliseconds:F1}");Redraw(state.Request);}
                else if(state.Batches.Count/16!=previousBatchCount/16)Redraw(state.Request);
                if(slice.Elapsed.TotalMilliseconds>=UploadBudgetMilliseconds)break;
            }
            if(_worker is null||_worker.IsCompleted)
            {
                _worker=null;_activeWork=null;
                var next=_views.Values.Where(s=>!s.Started&&s.Error is null&&DateTime.UtcNow-s.ChangedAt>=TimeSpan.FromMilliseconds(75)).OrderBy(s=>s.ChangedAt).FirstOrDefault();
                if(next is not null)Start(next);
            }
        }
        catch(Exception ex){_lastError="GS 调度未完成："+AholoHost.FriendlyError(ex);}
        finally{_pumping=false;}
    }
    private void Start(ViewState state)
    {
        GaussianLog.Write("prepare_start",$"generation={state.Request.Generation}; input={state.InputCount}; size={state.Request.Camera.Width}x{state.Request.Camera.Height}");
        state.Started=true;var cancellation=new WorkCancellation();var token=cancellation.Token;
        var work=new ActiveWork(state.Request,cancellation);_activeWork=work;
        _worker=Task.Run(()=>
        {
            WorkResult result;
            try{var prepared=GaussianNativeBatchBuilder.Prepare(work.Request,token);result=new(work.Request,prepared,null,false);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){result=new(work.Request,null,null,true);GaussianLog.Write("prepare_cancelled",$"generation={work.Request.Generation}");}
            catch(Exception ex){result=new(work.Request,null,"GS 后台准备失败，保留包围盒："+AholoHost.FriendlyError(ex),false);}
            finally{cancellation.Dispose();}
            if(!_stopping)_results.Enqueue(result);
        });
    }
    private void RefreshNativeViews()
    {
        _lastViewRefresh=DateTime.UtcNow;var live=new HashSet<Guid>();
        foreach(RhinoDoc doc in RhinoDoc.OpenDocuments())
        {
            if(doc.IsClosing)continue;
            foreach(RhinoView view in doc.Views.GetViewList(ViewTypeFilter.Model))
            {
                Guid id=view.MainViewport.Id;live.Add(id);
                var observed=new NativeView(doc.RuntimeSerialNumber,view.MainViewport.Size,view.ClientRectangle.Size);
                bool changed=_nativeViews.TryGetValue(id,out var previous)&&previous!=observed;
                _nativeViews[id]=observed;
                if(!IsMode(view.MainViewport))RemoveView(id);
                else if(changed)view.Redraw();
            }
        }
        foreach(Guid id in _nativeViews.Keys.Where(id=>!live.Contains(id)).ToArray()){_nativeViews.Remove(id);RemoveView(id);}
    }
    private void RemoveView(Guid id)
    {
        if(_activeWork?.Request.Viewport==id)_activeWork.Cancellation.Cancel();
        if(_views.Remove(id,out var state))Retire(state);
    }
    private void Retire(ViewState state)
    {
        state.Pending=null;state.Complete=false;
        foreach(var batch in state.Batches)_retired.Enqueue(batch.Mesh);
        state.Batches.Clear();
    }
    private static void Redraw(GaussianNativeRequest request)
    {
        RhinoDoc? doc=RhinoDoc.FromRuntimeSerialNumber(request.Document);
        if(doc is null||doc.IsClosing)return;
        doc.Views.GetViewList(ViewTypeFilter.Model).FirstOrDefault(v=>v.MainViewport.Id==request.Viewport)?.Redraw();
    }
    private static DisplayMaterial CreateMaterial(string path)
    {
        using var source=new Material{DiffuseColor=Color.White,DisableLighting=true,AlphaTransparency=true,Transparency=0,SpecularColor=Color.Black,Reflectivity=0,Shine=0};
        using var texture=new Texture{FileName=path,Enabled=true,TreatAsLinear=true,TextureCombineMode=TextureCombineMode.Blend,WrapU=TextureUvwWrapping.Clamp,WrapV=TextureUvwWrapping.Clamp,MinFilter=TextureFilter.Linear,MagFilter=TextureFilter.Linear};
        texture.SetRGBBlendValues(Color.Black,1,0,0,0);texture.SetAlphaBlendValues(0,0,1,0,0);
        if(!source.SetBitmapTexture(texture))throw new InvalidOperationException("无法设置 Gaussian opacity atlas。");
        return new DisplayMaterial(source){IsTwoSided=false};
    }
    private static GaussianVector Vector(Vector3d v)=>new(v.X,v.Y,v.Z);
    private static GaussianCamera Camera(RhinoViewport viewport,int width,int height)
    {
        if(width<=0||height<=0||!viewport.GetFrustum(out double left,out double right,out double bottom,out double top,out double near,out double far))throw new InvalidOperationException("无法读取视窗尺寸或相机视锥。");
        var eye=viewport.CameraLocation;double factor=viewport.IsPerspectiveProjection?near:1;
        return new(new(eye.X,eye.Y,eye.Z),Vector(viewport.CameraX),Vector(viewport.CameraY),Vector(viewport.CameraDirection).Normalized(),viewport.IsPerspectiveProjection,width,height,
            factor*width/(right-left),factor*height/(top-bottom),-left*width/(right-left)-.5,top*height/(top-bottom)-.5,near,far);
    }
    private static string PlacementKey(IEnumerable<GaussianDocument.Placement> placements)
    {
        var key=new StringBuilder();
        foreach(var p in placements)
        {
            key.Append(p.Object.Id).Append(':').Append(p.Asset.Hash).Append(':');Transform t=p.Object.InstanceXform;
            for(int row=0;row<3;row++)for(int col=0;col<4;col++)key.Append(t[row,col].ToString("R",CultureInfo.InvariantCulture)).Append(',');key.Append(';');
        }
        return key.ToString();
    }
    internal static string Diagnostics(RhinoDoc doc)
    {
        var conduit=_instance;var active=doc.Views.ActiveView?.ActiveViewport;ViewState? state=null;
        if(active is not null)conduit?._views.TryGetValue(active.Id,out state);
        string status=state is null?"等待原生模型视窗":state.Error is not null?"准备失败，包围盒保留":state.Complete?"全量可见 GS 已就绪":state.Pending is not null?$"上传 {state.Batches.Count:N0}/{state.TotalBatches:N0} 批":"后台投影/排序中";
        return "Aholo GS 批量实验版；"+(conduit?._backend??"未初始化")+"。\n"+GaussianDocument.Diagnostics(doc)+"\n"+
            $"状态：{status}；输入：{state?.InputCount??0:N0}；投影可见：{state?.VisibleCount??0:N0}；本帧批数：{state?.Batches.Count??0:N0}；每批最多 {GaussianNativeBatchBuilder.SplatsPerBatch} 个 GS（无总数上限）。\n"+
            $"后台准备 {state?.PreparationMilliseconds??0:F1} ms；最长单批上传 {state?.MaxUploadMilliseconds??0:F1} ms；最近绘制 {state?.LastDrawMilliseconds??0:F1} ms；已跳过缩略图/非原生尺寸绘制 {conduit?._skippedPreviews??0:N0} 次。\n"+
            "原始高斯完整保留；8 位 RGB/HDR 截断与 256 级 opacity atlas 为显示精度限制。透明体交错、Raytraced、布局/打印/捕获/缩略图和大场景实时速度仍待后续支持或验收。顶点 RGB+atlas alpha 及原生遮挡需手动宿主验证。\n"+(state?.Error??conduit?._lastError??"未自动判定视觉通过。")+"\n日志："+GaussianLog.FilePath;
    }
}
