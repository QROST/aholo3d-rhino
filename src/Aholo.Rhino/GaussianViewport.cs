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
    private static PlugIn? _plugin;
    private static bool _startupScheduled,_initializing;
    private static int _startupAttempts;
    private static Eto.Forms.UITimer? _startupTimer;
    private static string? _startupError;
    private readonly Dictionary<Guid,ViewState> _views=new();
    private readonly Dictionary<Guid,ViewCandidate> _candidates=new();
    private readonly Dictionary<Guid,ViewDiagnostics> _diagnostics=new();
    private readonly Dictionary<Guid,NativeView> _nativeViews=new();
    private readonly Dictionary<Guid,NativeSizeRetry> _nativeSizeRetries=new();
    private bool _hasNativeInventory;
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
    private string _backend=global::Aholo.Core.UiText.Get("Not drawn yet; backend not detected");
    private int _skippedPreviews;
    private DateTime _lastViewRefresh;
    private sealed record NativeView(uint Document,Size ViewportSize,Size ClientSize,Size WindowSize,GaussianNativeSampling? Sampling);
    private sealed record NativeSizeRetry(Size FrameSize,double Dpi);
    private sealed record ViewCandidate(GaussianNativeRequest Request,DateTime ChangedAt,string Reason);
    private sealed class ViewDiagnostics
    {
        internal long Restarts,DrawRejects,SuppressedReplacementLogs;
        internal string LastReplacementReason="none",LastRejectReason="none";
        internal DateTime LastReplacementLog,LastRejectLog;
    }
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
        // Set once by PreDrawObjects for this actual pipeline invocation. Never suppress proxies for an old camera.
        internal bool DrawAccepted;
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
        _plugin=plugin;
        if(_instance is not null||_startupScheduled)return;
        // AtStartup plug-ins may load before the display-mode manager is ready. The panel must load independently.
        _startupAttempts=0;_startupError=null;_startupScheduled=true;
        global::Rhino.RhinoApp.Idle+=OnStartupIdle;
        GaussianLog.Write("viewport_startup_scheduled",$"version={AholoPlugin.DisplayVersion}; waiting for UI idle");
    }
    private static void OnStartupIdle(object? sender,EventArgs e)
    {
        if(!_startupScheduled||_initializing)return;
        // Idle starts the first attempt only. A UI timer guarantees subsequent attempts without mouse/key input.
        global::Rhino.RhinoApp.Idle-=OnStartupIdle;
        _startupAttempts++;
        if(TryInitializeRenderer(_startupAttempts==1?"idle":"retry_timer")){CancelStartup();return;}
        if(_startupAttempts>=3)
        {
            CancelStartup();
            global::Rhino.RhinoApp.WriteLine(_startupError+global::Aholo.Core.UiText.Get(" AholoPanel remains available; run AholoGsMode to retry."));
        }
        else if(_startupTimer is null)
        {
            try
            {
                _startupTimer=new Eto.Forms.UITimer{Interval=.25};
                _startupTimer.Elapsed+=OnStartupIdle;_startupTimer.Start();
            }
            catch(Exception ex)
            {
                CancelStartup();
                GaussianLog.Write("viewport_startup_retry_unavailable",$"error={ex.GetType().Name}");
                global::Rhino.RhinoApp.WriteLine(_startupError+global::Aholo.Core.UiText.Get(" Run AholoGsMode to retry."));
            }
        }
    }
    private static void CancelStartup()
    {
        _startupScheduled=false;
        global::Rhino.RhinoApp.Idle-=OnStartupIdle;
        var timer=_startupTimer;_startupTimer=null;
        if(timer is null)return;
        timer.Elapsed-=OnStartupIdle;
        ReleaseNative(()=>timer.Stop());ReleaseNative(()=>timer.Dispose());
    }
    private static bool TryInitializeRenderer(string trigger)
    {
        if(_initializing||_plugin is null)return false;
        if(_instance is not null)return true;
        _initializing=true;string stage="mode_registration";
        try
        {
            string saved=_plugin.Settings.GetString(ModeSetting,string.Empty);
            Guid.TryParse(saved,out var previous);
            _modeId=GaussianModeRegistration.Ensure(new RhinoGaussianModeStore(_plugin,ModeSetting),previous,ModeName);
            stage="material";
            var conduit=new GaussianViewport();_instance=conduit;
            string texture=Path.Combine(Path.GetDirectoryName(typeof(GaussianViewport).Assembly.Location)!,"assets","gaussian-opacity-atlas.png");
            if(File.Exists(texture))conduit._material=CreateMaterial(texture);
            else conduit._lastError=global::Aholo.Core.UiText.Get("Gaussian opacity atlas is missing; visible bounds are retained.");
            stage="document_and_viewport";
            GaussianDocument.Initialize();conduit.RefreshNativeViews();
            conduit._timer=new Eto.Forms.UITimer{Interval=.016};conduit._timer.Elapsed+=conduit.OnIdle;conduit._timer.Start();
            RhinoDoc.BeginSaveDocument+=conduit.OnBeginSave;RhinoDoc.EndSaveDocument+=conduit.OnEndSave;
            DisplayPipeline.DisplayModeChanged+=conduit.OnDisplayModeChanged;
            conduit.Enabled=true;_startupError=null;
            GaussianLog.Write("viewport_initialize",$"version={AholoPlugin.DisplayVersion}; trigger={trigger}; mode={_modeId:D}; batch=2048; shared opacity atlas; native model view/save guards enabled");
            return true;
        }
        catch(Exception ex)
        {
            // Keep core commands/panel available, and release partial native state before a bounded or explicit retry.
            string action=stage switch { "mode_registration"=>global::Aholo.Core.UiText.Get("display mode registration"), "material"=>global::Aholo.Core.UiText.Get("material preparation"), _=>global::Aholo.Core.UiText.Get("viewport initialization") };
            _startupError=global::Aholo.Core.UiText.Get("Aholo GS initialization did not finish: ")+action+".";
            GaussianLog.Write("viewport_startup_failed",$"version={AholoPlugin.DisplayVersion}; trigger={trigger}; attempt={_startupAttempts}; stage={stage}; error={ex.GetType().Name}");
            try { StopRenderer(); }
            catch(Exception cleanup){GaussianLog.Write("viewport_startup_cleanup_failed",$"error={cleanup.GetType().Name}");}
            return false;
        }
        finally{_initializing=false;}
    }
    internal static void Shutdown()
    {
        CancelStartup();_plugin=null;
        StopRenderer();
    }
    private static void StopRenderer()
    {
        var conduit=_instance;_instance=null;if(conduit is null)return;
        // Detach first: even a native disposal failure must never make the next attempt see a ready instance.
        conduit._stopping=true;ReleaseNative(()=>conduit.Enabled=false);
        var timer=conduit._timer;conduit._timer=null;
        if(timer is not null)
        {
            timer.Elapsed-=conduit.OnIdle;
            ReleaseNative(()=>timer.Stop());ReleaseNative(()=>timer.Dispose());
        }
        RhinoDoc.BeginSaveDocument-=conduit.OnBeginSave;RhinoDoc.EndSaveDocument-=conduit.OnEndSave;
        DisplayPipeline.DisplayModeChanged-=conduit.OnDisplayModeChanged;
        ReleaseNative(()=>conduit._activeWork?.Cancellation.Cancel());
        foreach(var state in conduit._views.Values)conduit.Retire(state);
        conduit._views.Clear();conduit._candidates.Clear();conduit._diagnostics.Clear();conduit._nativeViews.Clear();conduit._nativeSizeRetries.Clear();
        while(conduit._retired.TryDequeue(out var mesh))ReleaseNative(()=>mesh.Dispose());
        var material=conduit._material;conduit._material=null;ReleaseNative(()=>material?.Dispose());
        while(conduit._results.TryDequeue(out _)) { }
        ReleaseNative(GaussianDocument.Shutdown);GaussianLog.Write("viewport_shutdown","workers cancelled; native resource cleanup attempted");
    }
    private static void ReleaseNative(Action release)
    {
        try{release();}
        catch(Exception ex){GaussianLog.Write("viewport_cleanup_failed",$"error={ex.GetType().Name}");}
    }
    internal static void ForgetDocument(uint serial)
    {
        var conduit=_instance;if(conduit is null)return;
        if(conduit._activeWork?.Request.Document==serial)conduit._activeWork.Cancellation.Cancel();
        foreach(Guid id in conduit._views.Where(p=>p.Value.Request.Document==serial).Select(p=>p.Key)
            .Concat(conduit._candidates.Where(p=>p.Value.Request.Document==serial).Select(p=>p.Key)).Distinct().ToArray())conduit.RemoveView(id,"document_invalidated");
    }
    internal static void DocumentClosed(uint serial)
    {
        _instance?._saving.Remove(serial);ForgetDocument(serial);
        var conduit=_instance;if(conduit is null)return;
        foreach(Guid id in conduit._nativeViews.Where(p=>p.Value.Document==serial).Select(p=>p.Key).ToArray())
        {conduit._nativeViews.Remove(id);conduit._nativeSizeRetries.Remove(id);conduit._diagnostics.Remove(id);}
    }
    internal static void PruneViews(RhinoDoc doc)
    {
        var conduit=_instance;if(conduit is null)return;
        var live=doc.Views.GetViewList(ViewTypeFilter.Model).Select(v=>v.MainViewport.Id).ToHashSet();
        foreach(Guid id in conduit._views.Where(p=>p.Value.Request.Document==doc.RuntimeSerialNumber).Select(p=>p.Key)
            .Concat(conduit._candidates.Where(p=>p.Value.Request.Document==doc.RuntimeSerialNumber).Select(p=>p.Key)).Distinct().Where(id=>!live.Contains(id)).ToArray())
        {conduit.RemoveView(id,"view_closed");conduit._diagnostics.Remove(id);}
    }
    internal static void Activate(RhinoDoc doc)
    {
        if(!TryInitializeRenderer("command")||_modeId==Guid.Empty)
            throw new InvalidOperationException((_startupError??global::Aholo.Core.UiText.Get("The Aholo GS display mode is not initialized yet."))+global::Aholo.Core.UiText.Get("Run AholoGsDiagnostics for status, or retry AholoGsMode later."));
        CancelStartup();
        var view=doc.Views.ActiveView??throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Activate a model viewport first."));
        if(view is RhinoPageView)throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Switch to a native model viewport. GS rendering in layout details is not supported in this version."));
        using var mode=DisplayModeDescription.GetDisplayMode(_modeId)??throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The Aholo GS display mode was deleted. Reload the plugin."));
        view.ActiveViewport.DisplayMode=mode;GaussianDocument.Reload(doc);_instance!.RefreshNativeViews();view.Redraw();
    }
    private static bool IsMode(RhinoViewport viewport)=>viewport.DisplayMode?.Id==_modeId;
    private bool Supported(DrawEventArgs e)=>SupportFailure(e) is null;
    private string? SupportFailure(DrawEventArgs e)
    {
        if(!IsMode(e.Viewport))return "other_mode";
        if(_stopping)return "shutdown";
        if(_material is null)return "missing_material";
        if(e.Display.IsPrinting)return "printing";
        if(e.Display.IsInViewCapture)return "view_capture";
        if(e.Display.IsStereoMode)return "stereo";
        if(_saving.ContainsKey(e.RhinoDoc.RuntimeSerialNumber))return "save_preview";
        if(e.Display.IsInTiledDraw(out _,out _))return "tiled_draw";
        // Only sizes captured from real model views OUTSIDE display callbacks are trusted. Thumbnails and GetPreviewBitmap
        // may reuse a viewport ID, so matching that ID alone must never trigger a full rebuild/render.
        if(!_nativeViews.TryGetValue(e.Viewport.Id,out var native)||native.Document!=e.RhinoDoc.RuntimeSerialNumber)return "not_native_model_view";
        Size size=e.Display.FrameSize;
        if(size.Width<=0||size.Height<=0)return "empty_frame";
        return Matches(size,native.ViewportSize,e.Display.DpiScale)||Matches(size,native.ClientSize,e.Display.DpiScale)?null:"offscreen_size";
    }
    private void OnDisplayModeChanged(object? sender,DisplayModeChangedEventArgs e)
    {
        // Polling MainViewport.DisplayMode can disagree with an in-progress pipeline. Only an explicit mode-change
        // event confirmed by the native model view is allowed to retire an otherwise valid frame.
        if(e.OldDisplayModeId!=_modeId||e.ChangedDisplayModeId==_modeId)return;
        if(!_nativeViews.TryGetValue(e.Viewport.Id,out var native)||native.Document!=e.RhinoDoc.RuntimeSerialNumber)return;
        RhinoView? view=e.RhinoDoc.Views.GetViewList(ViewTypeFilter.Model).FirstOrDefault(v=>v.MainViewport.Id==e.Viewport.Id);
        if(view is not null&&!IsMode(view.MainViewport))RemoveView(e.Viewport.Id,"display_mode_exit");
    }
    private static bool Matches(Size actual,Size native,double dpi)=>
        GaussianFramePolicy.MatchesNativeSampling(actual.Width,actual.Height,native.Width,native.Height,dpi);
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
    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
        // Dynamic geometry must contribute its bounds even when its block proxy is suppressed. This stabilizes the
        // host's near/far calculation; clipping planes and native depth testing remain completely enabled.
        if(_stopping||!IsMode(e.Viewport)||!_nativeViews.TryGetValue(e.Viewport.Id,out var native)||native.Document!=e.RhinoDoc.RuntimeSerialNumber)return;
        try
        {
            foreach(var placement in GaussianDocument.Placements(e.RhinoDoc,e.Viewport))
            {
                BoundingBox box=placement.Asset.Bounds;box.Transform(placement.WorldTransform);
                if(box.IsValid)e.IncludeBoundingBox(box);
            }
        }
        catch(Exception ex){_lastError=global::Aholo.Core.UiText.Get("GS bounds calculation failed: ")+AholoHost.FriendlyError(ex);}
    }
    protected override void PreDrawObjects(DrawEventArgs e)
    {
        if(_views.TryGetValue(e.Viewport.Id,out var previous))previous.DrawAccepted=false;
        string? failure=SupportFailure(e);
        if(failure is not null)
        {
            if(failure!="other_mode"){_skippedPreviews++;LogDrawRejected(e.Viewport.Id,failure,e.Display.FrameSize);}
            if(failure=="offscreen_size")_nativeSizeRetries[e.Viewport.Id]=new NativeSizeRetry(e.Display.FrameSize,e.Display.DpiScale);
            return;
        }
        _nativeSizeRetries.Remove(e.Viewport.Id);
        _backend=e.Display.IsOpenGL?global::Aholo.Core.UiText.Get("OpenGL (current viewport)"):global::Aholo.Core.UiText.Get("Non-OpenGL (this alone does not confirm Metal)");
        try
        {
            var placements=GaussianDocument.Placements(e.RhinoDoc,e.Viewport);
            if(placements.Count==0)
            {
                // A loading, hidden or unrecognized scene has no frame to prepare. Asset completion and
                // native object changes will redraw again; camera motion must not enqueue empty generations.
                RemoveView(e.Viewport.Id,"no_ready_placements");
                return;
            }
            NativeView native=_nativeViews[e.Viewport.Id];
            // SupportFailure already authenticated FrameSize against real native inventory. Preserve the first grid
            // across rounded DPI representations; only independent window bounds can start a resized scene identity.
            GaussianNativeSampling sampling=GaussianFramePolicy.ObserveNativeWindow(native.Sampling,e.Display.FrameSize.Width,e.Display.FrameSize.Height,native.WindowSize.Width,native.WindowSize.Height);
            _nativeViews[e.Viewport.Id]=native with{Sampling=sampling};
            GaussianCamera camera=GaussianFramePolicy.NormalizeSampling(Camera(e.Viewport,e.Display.FrameSize.Width,e.Display.FrameSize.Height),sampling);
            string windowKey=FormattableString.Invariant($"window={sampling.WindowWidth}x{sampling.WindowHeight}|");
            string key=windowKey+PlacementKey(placements);
            bool sameScene=previous is not null&&previous.Request.Key==key;
            if(sameScene&&GaussianFramePolicy.CanReuse(previous!.Request.Camera,camera))
            {
                _candidates.Remove(e.Viewport.Id);
                previous.DrawAccepted=previous.Complete&&previous.Error is null;
                if(!previous.DrawAccepted)LogDrawRejected(e.Viewport.Id,previous.Error is not null?"frame_error":previous.Pending is not null?"uploading":"preparing",e.Display.FrameSize);
                return;
            }
            string reason=previous is null?"first_frame":!sameScene?
                (previous.Request.Key.StartsWith(windowKey,StringComparison.Ordinal)?"placements_changed":"native_window_resized"):
                GaussianFramePolicy.SameProjection(previous.Request.Camera,camera)?"depth_coverage":"projection_changed";
            if(sameScene&&GaussianFramePolicy.SameProjection(previous!.Request.Camera,camera))
                camera=GaussianFramePolicy.CoverDepthRange(previous.Request.Camera,camera);
            if(_candidates.TryGetValue(e.Viewport.Id,out var candidate)&&candidate.Request.Key==key&&GaussianFramePolicy.SameProjection(candidate.Request.Camera,camera))
            {
                // Depth observations accumulate coverage without restarting the debounce. A transient DPI callback
                // cannot destroy the request that is already being prepared or uploaded.
                var covered=GaussianFramePolicy.CoverDepthRange(candidate.Request.Camera,camera);
                if(covered!=candidate.Request.Camera)_candidates[e.Viewport.Id]=candidate with{Request=candidate.Request with{Camera=covered}};
            }
            else
            {
                // Capture only immutable data and value transforms. The worker never reads RhinoObject/RhinoDoc state.
                var snapshot=placements.Select(p=>new GaussianNativePlacement(p.Instance,p.Asset.Hash,p.Asset.Prepared)).ToArray();
                var request=new GaussianNativeRequest(e.RhinoDoc.RuntimeSerialNumber,e.Viewport.Id,0,camera,key,snapshot);
                _candidates[e.Viewport.Id]=new ViewCandidate(request,DateTime.UtcNow,reason);
            }
            LogDrawRejected(e.Viewport.Id,reason,e.Display.FrameSize);
            _lastError=null;
        }
        catch(Exception ex){_lastError=global::Aholo.Core.UiText.Get("GS preparation failed; bounds are retained: ")+AholoHost.FriendlyError(ex);RemoveView(e.Viewport.Id,"observation_failed");}
    }
    protected override void PreDrawObject(DrawObjectEventArgs e)
    {
        if(!Supported(e)||!_views.TryGetValue(e.Viewport.Id,out var state)||!state.DrawAccepted||!state.Complete||state.Error is not null)return;
        RhinoObject? top=e.Display.ActiveTopLevelObject;
        foreach(var placement in state.Request.Placements)
            if(GaussianInstanceGraph.CanSuppressWholeRoot(placement.Instance,e.RhinoObject.Id,top?.Id)){e.DrawObject=false;return;}
    }
    protected override void PostDrawObjects(DrawEventArgs e)
    {
        if(!Supported(e)||!_views.TryGetValue(e.Viewport.Id,out var state)||!state.DrawAccepted||!state.Complete||state.Error is not null)return;
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
        catch(Exception ex){state.Error=global::Aholo.Core.UiText.Get("GS batch drawing failed; bounds are retained. Run AholoGsMode to retry: ")+AholoHost.FriendlyError(ex);_lastError=state.Error;GaussianLog.Write("draw_failed",$"generation={state.Request.Generation}; {state.Error}");}
        finally
        {
            try{if(writing)e.Display.PopDepthWriting();}finally{if(testing)e.Display.PopDepthTesting();}
            state.LastDrawMilliseconds=clock.Elapsed.TotalMilliseconds;
        }
    }
    protected override void DrawForeground(DrawEventArgs e)
    {
        if(!Supported(e))return;
        _views.TryGetValue(e.Viewport.Id,out var state);
        bool candidate=_candidates.ContainsKey(e.Viewport.Id);
        GaussianDisplayPhase phase=DisplayPhase(e.RhinoDoc,e.Viewport,state,candidate,true,true);
        if(!GaussianDisplayStatus.ShouldShowOverlay(phase,state?.DrawAccepted==true))return;
        string progress="Aholo GS: "+DisplayText(phase,state);
        e.Display.Draw2dText(progress,Color.FromArgb(45,135,175),new Point2d(18,38),false,14);
    }
    private void AcceptStableCandidates()
    {
        DateTime now=DateTime.UtcNow;
        foreach(var pair in _candidates.Where(p=>now-p.Value.ChangedAt>=TimeSpan.FromMilliseconds(75)).ToArray())
        {
            Guid id=pair.Key;ViewCandidate candidate=pair.Value;
            _views.TryGetValue(id,out var previous);
            if(_activeWork?.Request.Viewport==id)_activeWork.Cancellation.Cancel();
            if(previous is not null)Retire(previous);
            var request=candidate.Request with{Generation=++_generation};
            var state=new ViewState{Request=request,ChangedAt=candidate.ChangedAt,InputCount=request.Placements.Sum(p=>p.Asset.Count)};
            _views[id]=state;_candidates.Remove(id);
            LogReplacement(previous,request,candidate.Reason);
        }
    }
    private void OnIdle(object? sender,EventArgs e)
    {
        if(_stopping||_pumping)return;
        _pumping=true;
        try
        {
            if(DateTime.UtcNow-_lastViewRefresh>TimeSpan.FromMilliseconds(250))RefreshNativeViews();
            AcceptStableCandidates();
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
                if(state.Pending is null||state.Error is not null||_candidates.ContainsKey(state.Request.Viewport))continue;
                while(state.Batches.Count<state.TotalBatches&&slice.Elapsed.TotalMilliseconds<UploadBudgetMilliseconds)
                {
                    int index=state.Batches.Count;var batch=state.Pending.Batches[index]!;
                    var upload=Stopwatch.StartNew();Mesh? mesh=null;
                    try
                    {
                        mesh=new Mesh();mesh.Vertices.UseDoublePrecisionVertices=true;
                        mesh.Vertices.AddVertices(batch.Vertices);mesh.Faces.AddFaces(batch.Faces);
                        if(!mesh.Normals.SetNormals(batch.Normals)||!mesh.TextureCoordinates.SetTextureCoordinates(batch.Uv)||!mesh.VertexColors.SetColors(batch.Colors))throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Rhino could not accept GS normals, UVs or vertex colors."));
                        mesh.SetSurfaceParametersFromTextureCoordinates();
                        state.Batches.Add(new NativeBatch(mesh,batch.FaceIndices));mesh=null;state.Pending.Batches[index]=null;
                        state.MaxUploadMilliseconds=Math.Max(state.MaxUploadMilliseconds,upload.Elapsed.TotalMilliseconds);
                        if(state.Batches.Count%16==0)GaussianLog.Write("upload_progress",$"generation={state.Request.Generation}; batches={state.Batches.Count}/{state.TotalBatches}");
                    }
                    catch(Exception ex){mesh?.Dispose();state.Error=global::Aholo.Core.UiText.Get("GS upload failed; bounds are retained: ")+AholoHost.FriendlyError(ex);_lastError=state.Error;Retire(state);break;}
                }
                if(state.Pending is not null&&state.Batches.Count==state.TotalBatches)
                {state.Pending=null;state.Complete=true;GaussianLog.Write("frame_ready",$"generation={state.Request.Generation}; visible={state.VisibleCount}; batches={state.Batches.Count}; maxUploadMs={state.MaxUploadMilliseconds:F1}");Redraw(state.Request);}
                // Redraw only after atomic publication. Progress redraws used to produce transient clip/DPI callbacks
                // which cancelled the very upload whose progress they were displaying.
                if(slice.Elapsed.TotalMilliseconds>=UploadBudgetMilliseconds)break;
            }
            if(_worker is null||_worker.IsCompleted)
            {
                _worker=null;_activeWork=null;
                var next=_views.Values.Where(s=>!s.Started&&s.Error is null&&!_candidates.ContainsKey(s.Request.Viewport)&&DateTime.UtcNow-s.ChangedAt>=TimeSpan.FromMilliseconds(75)).OrderBy(s=>s.ChangedAt).FirstOrDefault();
                if(next is not null)Start(next);
            }
        }
        catch(Exception ex){_lastError=global::Aholo.Core.UiText.Get("GS scheduling did not finish: ")+AholoHost.FriendlyError(ex);}
        finally{_pumping=false;}
    }
    private void Start(ViewState state)
    {
        var diagnostics=GetDiagnostics(state.Request.Viewport);GaussianCamera camera=state.Request.Camera;
        GaussianLog.Write("prepare_start",FormattableString.Invariant($"generation={state.Request.Generation}; input={state.InputCount}; size={camera.Width}x{camera.Height}; near={camera.Near:R}; far={camera.Far:R}; fx={camera.Fx:R}; fy={camera.Fy:R}; restarts={diagnostics.Restarts}; replaced_reason={diagnostics.LastReplacementReason}"));
        state.Started=true;var cancellation=new WorkCancellation();var token=cancellation.Token;
        var work=new ActiveWork(state.Request,cancellation);_activeWork=work;
        _worker=Task.Run(()=>
        {
            WorkResult result;
            try{var prepared=GaussianNativeBatchBuilder.Prepare(work.Request,token);result=new(work.Request,prepared,null,false);}
            catch(OperationCanceledException)when(token.IsCancellationRequested){result=new(work.Request,null,null,true);GaussianLog.Write("prepare_cancelled",$"generation={work.Request.Generation}");}
            catch(Exception ex){result=new(work.Request,null,global::Aholo.Core.UiText.Get("GS background preparation failed; bounds are retained: ")+AholoHost.FriendlyError(ex),false);}
            finally{cancellation.Dispose();}
            if(!_stopping)_results.Enqueue(result);
        });
    }
    private void RefreshNativeViews()
    {
        _lastViewRefresh=DateTime.UtcNow;var live=new HashSet<Guid>();var redraws=new List<RhinoView>();
        foreach(RhinoDoc doc in RhinoDoc.OpenDocuments())
        {
            if(doc.IsClosing)continue;
            foreach(RhinoView view in doc.Views.GetViewList(ViewTypeFilter.Model))
            {
                Guid id=view.MainViewport.Id;live.Add(id);
                // Inventory only: native viewport/client sizes may change representation across DPI callbacks.
                // Window bounds are observed independently outside rendering; preserve the accepted grid until a real
                // window resize. Never invalidate or redraw solely because polling observed another size representation.
                bool discovered=!_nativeViews.TryGetValue(id,out var previous);
                var native=new NativeView(doc.RuntimeSerialNumber,view.MainViewport.Size,view.ClientRectangle.Size,view.Bounds.Size,previous?.Sampling);
                _nativeViews[id]=native;
                bool retry=_nativeSizeRetries.TryGetValue(id,out var rejected)&&
                    (Matches(rejected.FrameSize,native.ViewportSize,rejected.Dpi)||Matches(rejected.FrameSize,native.ClientSize,rejected.Dpi));
                // A native resize can draw before this inventory catches up. Retry only that explicitly rejected
                // size once it is independently confirmed; clear before Redraw to prevent recursive retry loops.
                if(retry)_nativeSizeRetries.Remove(id);
                if((retry||discovered&&_hasNativeInventory)&&IsMode(view.MainViewport))
                {
                    redraws.Add(view);
                    GaussianLog.Write("native_redraw_once",$"viewport={id:D}; reason={(retry?"confirmed_native_size":"new_native_view")}");
                }
            }
        }
        foreach(Guid id in _nativeViews.Keys.Where(id=>!live.Contains(id)).ToArray())
        {_nativeViews.Remove(id);_nativeSizeRetries.Remove(id);RemoveView(id,"view_closed");_diagnostics.Remove(id);}
        _hasNativeInventory=true;
        foreach(RhinoView view in redraws)view.Redraw();
    }
    private void RemoveView(Guid id,string reason)
    {
        _candidates.Remove(id);_nativeSizeRetries.Remove(id);
        if(_activeWork?.Request.Viewport==id)_activeWork.Cancellation.Cancel();
        if(_views.Remove(id,out var state))
        {
            GaussianLog.Write("frame_removed",$"generation={state.Request.Generation}; replaced_reason={reason}; uploaded={state.Batches.Count}/{state.TotalBatches}");
            Retire(state);
        }
    }
    private ViewDiagnostics GetDiagnostics(Guid id)
    {
        if(!_diagnostics.TryGetValue(id,out var result))_diagnostics[id]=result=new ViewDiagnostics();
        return result;
    }
    private void LogDrawRejected(Guid id,string reason,Size size)
    {
        // Do not retain arbitrary preview viewport IDs, or log ordinary non-GS view redraws.
        if(!_nativeViews.ContainsKey(id))return;
        var diagnostics=GetDiagnostics(id);diagnostics.DrawRejects++;
        DateTime now=DateTime.UtcNow;
        bool report=now-diagnostics.LastRejectLog>=TimeSpan.FromSeconds(5)||diagnostics.LastRejectReason!=reason&&now-diagnostics.LastRejectLog>=TimeSpan.FromSeconds(1);
        diagnostics.LastRejectReason=reason;
        if(!report)return;
        diagnostics.LastRejectLog=now;
        GaussianLog.Write("draw_rejected",$"viewport={id:D}; reason={reason}; size={size.Width}x{size.Height}; count={diagnostics.DrawRejects}");
    }
    private void LogReplacement(ViewState? previous,GaussianNativeRequest request,string reason)
    {
        var diagnostics=GetDiagnostics(request.Viewport);if(previous is not null)diagnostics.Restarts++;
        DateTime now=DateTime.UtcNow;
        bool report=now-diagnostics.LastReplacementLog>=TimeSpan.FromSeconds(1);
        diagnostics.LastReplacementReason=reason;
        if(!report){diagnostics.SuppressedReplacementLogs++;return;}
        diagnostics.LastReplacementLog=now;
        GaussianCamera camera=request.Camera;GaussianCamera? old=previous?.Request.Camera;
        GaussianLog.Write("frame_replaced",FormattableString.Invariant($"viewport={request.Viewport:D}; generation={request.Generation}; replaced_reason={reason}; restarts={diagnostics.Restarts}; suppressed={diagnostics.SuppressedReplacementLogs}; oldSize={old?.Width??0}x{old?.Height??0}; size={camera.Width}x{camera.Height}; oldNear={old?.Near??0:R}; near={camera.Near:R}; oldFar={old?.Far??0:R}; far={camera.Far:R}; deltaFxNormalized={(old is null?0:camera.Fx/camera.Width-old.Fx/old.Width):R}; deltaFyNormalized={(old is null?0:camera.Fy/camera.Height-old.Fy/old.Height):R}; deltaCxNormalized={(old is null?0:(camera.Cx+.5)/camera.Width-(old.Cx+.5)/old.Width):R}; deltaCyNormalized={(old is null?0:(camera.Cy+.5)/camera.Height-(old.Cy+.5)/old.Height):R}; deltaEye={(old is null?0:(camera.Eye-old.Eye).Length):R}; deltaBasisMax={(old is null?0:Math.Max(BasisDelta(camera.Right,old.Right),Math.Max(BasisDelta(camera.Up,old.Up),BasisDelta(camera.Forward,old.Forward)))):R}"));
        diagnostics.SuppressedReplacementLogs=0;
    }
    private static double BasisDelta(GaussianVector a,GaussianVector b)=>Math.Max(Math.Abs(a.X-b.X),Math.Max(Math.Abs(a.Y-b.Y),Math.Abs(a.Z-b.Z)));
    private void Retire(ViewState state)
    {
        state.Pending=null;state.Complete=false;state.DrawAccepted=false;
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
        if(!source.SetBitmapTexture(texture))throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot set the Gaussian opacity atlas."));
        return new DisplayMaterial(source){IsTwoSided=false};
    }
    private static GaussianVector Vector(Vector3d v)=>new(v.X,v.Y,v.Z);
    private static GaussianCamera Camera(RhinoViewport viewport,int width,int height)
    {
        if(width<=0||height<=0||!viewport.GetFrustum(out double left,out double right,out double bottom,out double top,out double near,out double far))throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Cannot read viewport dimensions or camera frustum."));
        var eye=viewport.CameraLocation;double factor=viewport.IsPerspectiveProjection?near:1;
        return new(new(eye.X,eye.Y,eye.Z),Vector(viewport.CameraX),Vector(viewport.CameraY),Vector(viewport.CameraDirection).Normalized(),viewport.IsPerspectiveProjection,width,height,
            factor*width/(right-left),factor*height/(top-bottom),-left*width/(right-left)-.5,top*height/(top-bottom)-.5,near,far);
    }
    private static string PlacementKey(IEnumerable<GaussianDocument.Placement> placements)
    {
        var key=new StringBuilder();
        foreach(var p in placements)
        {
            key.Append(p.Instance.Path).Append(':').Append(p.Asset.Hash).Append(':');Transform t=p.WorldTransform;
            for(int row=0;row<3;row++)for(int col=0;col<4;col++)key.Append(t[row,col].ToString("R",CultureInfo.InvariantCulture)).Append(',');key.Append(';');
        }
        return key.ToString();
    }
    private static GaussianDisplayPhase DisplayPhase(RhinoDoc doc,RhinoViewport? viewport,ViewState? frame,bool candidate,bool modelView,bool gsMode)
    {
        GaussianDocument.DocumentStatus document=GaussianDocument.Status(doc);
        return GaussianDisplayStatus.Evaluate(new GaussianDisplayFacts
        {
            DocumentInstances=document.Instances,LoadedResources=document.Loaded,LoadingResources=document.Loading,ResourceErrors=document.Errors,
            VisibleInstances=viewport is null?0:GaussianDocument.Occurrences(doc,viewport).Count,
            ReadyPlacements=viewport is null?0:GaussianDocument.Placements(doc,viewport).Count,
            IsModelView=modelView,IsGsMode=gsMode,HasCandidate=candidate,HasFrame=frame is not null,
            FrameComplete=frame?.Complete==true,UploadPending=frame?.Pending is not null,FrameFailed=frame?.Error is not null,
            InputCount=frame?.InputCount??0,ProjectedCount=frame?.VisibleCount??0
        });
    }
    private static string DisplayText(GaussianDisplayPhase phase,ViewState? frame)=>phase switch
    {
        GaussianDisplayPhase.NoRecognizedInstances=>UiText.Get("No GS references recognized in the document. Import an original GS file."),
        GaussianDisplayPhase.WaitingForModelView=>UiText.Get("Activate a native modeling viewport."),
        GaussianDisplayPhase.NoVisibleInstances=>UiText.Get("No visible GS objects in this view. Check visibility and layers."),
        GaussianDisplayPhase.WaitingForResources=>UiText.Get("GS recognized; waiting for local resources. Bounding boxes retained."),
        GaussianDisplayPhase.LoadingResources=>UiText.Get("GS recognized; loading resources. Bounding boxes retained."),
        GaussianDisplayPhase.ResourceLoadFailed=>UiText.Get("GS resource loading failed. Bounding boxes retained; check resource errors or restore the original file."),
        GaussianDisplayPhase.NoRenderablePlacements=>UiText.Get("Visible GS cannot be rendered yet. Check instance transforms and resources."),
        GaussianDisplayPhase.WaitingForGsMode=>UiText.Get("Resources loaded. Switch to the Aholo GS display mode."),
        GaussianDisplayPhase.WaitingForStableView=>UiText.Get("View changed; waiting for a stable view before preparation."),
        GaussianDisplayPhase.WaitingForFrame=>UiText.Get("Resources loaded; waiting for the modeling viewport."),
        GaussianDisplayPhase.Preparing=>UiText.Get("Projecting and sorting in the background; bounding boxes retained."),
        GaussianDisplayPhase.Uploading=>UiText.Format("Uploading batches {0:N0}/{1:N0}; display starts when fully ready.", frame?.Batches.Count??0, frame?.TotalBatches??0),
        GaussianDisplayPhase.PreparationFailed=>UiText.Get("Display preparation failed. Bounding boxes retained; run AholoGsDiagnostics for errors."),
        GaussianDisplayPhase.NoProjectedGaussians=>UiText.Get("GS loaded, but no splats project into this view. Check the camera and clipping range."),
        GaussianDisplayPhase.PartiallyReady=>UiText.Get("Some GS are ready; other visible instances are loading or have errors."),
        GaussianDisplayPhase.Ready=>UiText.Get("Visible GS in the current view are ready."),
        _=>UiText.Get("Unknown display status.")
    };
    internal static string Diagnostics(RhinoDoc doc)
    {
        var conduit=_instance;
        if(conduit is null)return global::Aholo.Core.UiText.Format("Aholo {0}; GS initialization: {1}; automatic attempt {2}/3.\n", AholoPlugin.DisplayVersion, (_startupScheduled?global::Aholo.Core.UiText.Get("waiting for automatic retry"):global::Aholo.Core.UiText.Get("not ready")), _startupAttempts)+
            (_startupError??global::Aholo.Core.UiText.Get("Preparing display mode."))+global::Aholo.Core.UiText.Get(" AholoPanel is available; run AholoGsMode to retry.\nLog: ")+GaussianLog.FilePath;
        var active=doc.Views.ActiveView?.ActiveViewport;ViewState? state=null;
        if(active is not null)conduit?._views.TryGetValue(active.Id,out state);
        ViewDiagnostics? diagnostics=null;
        if(active is not null)conduit?._diagnostics.TryGetValue(active.Id,out diagnostics);
        bool candidate=active is not null&&conduit?._candidates.ContainsKey(active.Id)==true;
        bool modelView=active is not null&&doc.Views.ActiveView is not RhinoPageView;
        string status=DisplayText(DisplayPhase(doc,active,state,candidate,modelView,active is not null&&IsMode(active)),state);
        return global::Aholo.Core.UiText.Get("Aholo GS batch experiment; ")+(conduit?._backend??global::Aholo.Core.UiText.Get("Not initialized"))+".\n"+GaussianDocument.Diagnostics(doc)+"\n"+
            global::Aholo.Core.UiText.Format("Status: {0}; input: {1:N0}; projected visible: {2:N0}; batches this frame: {3:N0}; up to {4} GS per batch (no total-count cap).\n", status, state?.InputCount??0, state?.VisibleCount??0, state?.Batches.Count??0, GaussianNativeBatchBuilder.SplatsPerBatch)+
            global::Aholo.Core.UiText.Format("Background preparation {0:F1} ms; longest batch upload {1:F1} ms; latest draw {2:F1} ms; skipped thumbnail/non-native-size draws {3:N0}.\n", state?.PreparationMilliseconds??0, state?.MaxUploadMilliseconds??0, state?.LastDrawMilliseconds??0, conduit?._skippedPreviews??0)+
            global::Aholo.Core.UiText.Format("Preparation restarts {0:N0}; latest reason: {1}; deferred draws {2:N0}, latest reason: {3}.\n", diagnostics?.Restarts??0, diagnostics?.LastReplacementReason??"none", diagnostics?.DrawRejects??0, diagnostics?.LastRejectReason??"none")+
            global::Aholo.Core.UiText.Get("Original Gaussians are fully preserved; 8-bit RGB/HDR clipping and the 256-level opacity atlas limit display precision. Transparency interleaving, Raytraced, layout/print/capture/thumbnail support and large-scene real-time speed remain pending. Vertex RGB, atlas alpha and native occlusion require manual host validation.\n")+(state?.Error??conduit?._lastError??global::Aholo.Core.UiText.Get("Visual acceptance is not determined automatically."))+global::Aholo.Core.UiText.Get("\nLog: ")+GaussianLog.FilePath;
    }
}
