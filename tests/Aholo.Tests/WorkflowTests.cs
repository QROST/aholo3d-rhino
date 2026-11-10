using System.Net;
using System.Text;
using System.Text.Json;
using Aholo.Core;
using Aholo.Provider;
using Aholo.Sidecar;

namespace Aholo.Tests;
public sealed class WorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "aholo-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly ApiKeyCredential Key = new("test-key-do-not-use");
    private static readonly RenderCamera Camera = new(new double[] {1,0,0,0,0,1,0,0,0,0,1,0,0,0,1000,1},35,36,24,1,100000);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private (TaskStore Store, Workflows Flow, Stub Handler) Create(Func<HttpRequestMessage,Task<HttpResponseMessage>> responder)
    {
        var store = new TaskStore(root); var handler = new Stub(responder);
        var http = new HttpClient(handler);
        return (store, new Workflows(new AholoApiClient(http, http), store, new AssetDownloader(http, store)), handler);
    }
    [Fact]
    public void JournalRecoversUncertainSubmissionAndDropsSignedUrls()
    {
        var store = new TaskStore(root);
        var task = new LocalTask { Status = "RenderSubmitting", PlyUrl = "https://example.com/a?token=secret", ImageUrl = "https://example.com/b?sig=secret" };
        store.Save(task);
        Assert.Equal("SubmissionUnknown", store.Get(task.Id).Status);
        Assert.Null(store.Get(task.Id).PlyUrl);
        Assert.DoesNotContain("secret", File.ReadAllText(Directory.GetFiles(Path.Combine(root,"tasks"))[0]));
    }
    [Fact]
    public void HistoryDisplayMetadataCannotExhaustPipeEnvelope()
    {
        var store = new TaskStore(root);
        var task = new LocalTask { Status = "FAILED", Name = new string('x', 2 * 1024 * 1024), Error = new string('e', 10000) };
        store.Save(task);
        var loaded = Assert.Single(store.List());
        Assert.Equal(513, loaded.Name.Length);
        Assert.Equal(1025, loaded.Error!.Length);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(loaded).Length < 10000);
    }
    [Fact]
    public void DamagedHistoryRemainsVisibleInsteadOfHidingPotentialPaidTask()
    {
        var store = new TaskStore(root); var id = Guid.NewGuid();
        File.WriteAllText(Path.Combine(root, "tasks", id.ToString("N") + ".json"), "truncated");
        var record = Assert.Single(store.List());
        Assert.Equal(id, record.Id); Assert.Equal("RecordError", record.Status);
        Assert.True(File.Exists(Path.Combine(root, "tasks", id.ToString("N") + ".json")));
    }
    [Fact]
    public async Task LostGenerationReplyIsNotAutomaticallySubmittedAgain()
    {
        var env = Create(_ => throw new HttpRequestException("sensitive arbitrary server exception"));
        var result = await env.Flow.GenerateAsync(Key, new("Test","a bright indoor world"),default);
        Assert.Equal("SubmissionUnknown", result.Status);
        Assert.Equal(1,env.Handler.Count);
        var refresh = await env.Flow.RefreshAsync(Key,result.Id,default);
        Assert.Equal("SubmissionUnknown",refresh.Status);
        Assert.Equal(1,env.Handler.Count);
        Assert.DoesNotContain("sensitive",JsonSerializer.Serialize(refresh));
    }
    [Fact]
    public async Task RenderContinuationOnlySubmitsOnceThenPollsByOperationId()
    {
        var bodies = new List<string>();
        var env = Create(async request =>
        {
            if (request.Method == HttpMethod.Get) return Json("""{"success":true,"data":{"taskId":9007199254740993,"status":3,"outputs":[{"content":"TESTMESH"}]}}""");
            var body = await request.Content!.ReadAsStringAsync(); bodies.Add(body);
            return body.Contains("operationId") ? Json("""{"operationId":"op-1","done":true,"result":{"resultUrl":"https://example.com/render.png"},"error":null}""") : Json("""{"operationId":"op-1","done":false,"result":null,"error":null}""");
        });
        var task = new LocalTask { Kind="render",ConnectionId=Key.ConnectionId,Status="MeshProcessing",MeshTaskId="9007199254740993",Render=new("Test","unused.glb",Camera) };
        env.Store.Save(task);
        var ready = await env.Flow.RefreshAsync(Key,task.Id,default);
        Assert.Equal("MeshReady", ready.Status); Assert.Empty(bodies);
        var submitted = await env.Flow.ContinueRenderAsync(Key,task.Id,default);
        Assert.Equal("op-1",submitted.OperationId);
        Assert.Equal("Running",submitted.Status);
        var complete = await env.Flow.RefreshAsync(Key,task.Id,default);
        Assert.Equal("SUCCEEDED",complete.Status);
        Assert.Equal(2,bodies.Count);
        using var first = JsonDocument.Parse(bodies[0]);
        Assert.True(first.RootElement.TryGetProperty("usdContent",out _));
        using var second = JsonDocument.Parse(bodies[1]);
        Assert.Single(second.RootElement.EnumerateObject());
        Assert.Equal("op-1",second.RootElement.GetProperty("operationId").GetString());
        Assert.Null(env.Store.Get(task.Id).ImageUrl);
    }
    [Fact]
    public async Task RecoveredRenderIntentDoesNotRepeatPaidSubmission()
    {
        var env = Create(_ => throw new Exception("Must not call network"));
        var task = new LocalTask {Kind="render",ConnectionId=Key.ConnectionId,Status="RenderSubmitting",MeshTaskId="123",Render=new("Test","unused.glb",Camera)};
        env.Store.Save(task);
        Assert.Equal("SubmissionUnknown",(await env.Flow.RefreshAsync(Key,task.Id,default)).Status);
        Assert.Equal(0,env.Handler.Count);
    }
    [Fact]
    public async Task DifferentKeyCannotPollOrDownloadEarlierAccountTask()
    {
        var env = Create(_ => throw new Exception("Must not call network"));
        var task = new LocalTask { ConnectionId="another-key", WorldId="a-world",Status="PENDING" };
        env.Store.Save(task);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>env.Flow.RefreshAsync(Key,task.Id,default));
        Assert.Equal(0,env.Handler.Count);
    }
    [Fact]
    public async Task AssetDownloadsRejectHtmlAndRemovePartialFiles()
    {
        var env = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("<html>bad</html>")}));
        var downloader = new AssetDownloader(new HttpClient(env.Handler),env.Store);
        await Assert.ThrowsAsync<IOException>(()=>downloader.DownloadAsync(Guid.NewGuid(),"https://example.com/result","image",default));
        Assert.Empty(Directory.GetFiles(Path.Combine(root,"assets")));
        await Assert.ThrowsAsync<IOException>(()=>downloader.DownloadAsync(Guid.NewGuid(),"http://example.com/result","ply",default));
        Assert.Equal(1,env.Handler.Count);
    }
    [Fact]
    public async Task AssetDownloadSendsNoApiKeyAndPreservesOriginalPly()
    {
        const string ply = "ply\nformat ascii 1.0\nelement vertex 0\nend_header\n";
        var env = Create(request =>
        {
            Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("ous-token-v2"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(ply)});
        });
        string path = await new AssetDownloader(new HttpClient(env.Handler),env.Store).DownloadAsync(Guid.NewGuid(),"https://example.com/result.ply","ply",default);
        Assert.Equal(ply,File.ReadAllText(path));
    }
    [Fact]
    public async Task DownloadDoesNotContinuePaidRender()
    {
        var env = Create(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json("""{"success":true,"data":{"taskId":123,"status":3,"outputs":[{"content":"MESH"}]}}"""));
        });
        var task = new LocalTask { Kind="render",ConnectionId=Key.ConnectionId,Status="MeshProcessing",MeshTaskId="123",Render=new("Test","unused.glb",Camera) };
        env.Store.Save(task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => env.Flow.DownloadAsync(Key,new(task.Id,"image"),default));
        Assert.Equal(1, env.Handler.Count); Assert.Equal("MeshReady",env.Store.Get(task.Id).Status);
    }
    [Fact]
    public async Task WorldRestoreFindsTaskBeyondFirstPage()
    {
        var env = Create(_ => Task.FromResult(Json("""{"worldId":"older-world","status":"RUNNING"}""")));
        var older = new LocalTask { ConnectionId=Key.ConnectionId,WorldId="older-world",CreatedAt=DateTimeOffset.UtcNow.AddDays(-1), Status="PENDING" };
        env.Store.Save(older);
        for(int n=0;n<110;n++) env.Store.Save(new LocalTask {Status="PENDING"});
        Assert.DoesNotContain(env.Store.List(),t=>t.Id==older.Id);
        Assert.Equal(older.Id,(await env.Flow.ImportWorldAsync(Key,"older-world",default)).Id);
        Assert.Equal(111,env.Store.Page(new()).Total);
    }
    [Fact]
    public async Task UnknownContinuationCannotResubmitRender()
    {
        var env = Create(_ => throw new HttpRequestException());
        var task = new LocalTask { Kind="render",ConnectionId=Key.ConnectionId,Status="MeshReady",MeshTaskId="123",MeshId="MESH",Render=new("Test","unused.glb",Camera) };
        env.Store.Save(task);
        Assert.Equal("SubmissionUnknown",(await env.Flow.ContinueRenderAsync(Key,task.Id,default)).Status);
        Assert.Equal("SubmissionUnknown",(await env.Flow.ContinueRenderAsync(Key,task.Id,default)).Status);
        Assert.Equal(1,env.Handler.Count);
    }
    [Fact]
    public async Task SeparateTasksCanSubmitWhileHistorySeesTheLiveOwner()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls=0;
        var env=Create(async _=>
        {
            int current=Interlocked.Increment(ref calls);
            if(current==1) { entered.SetResult(); await release.Task; }
            return Json("{\"worldId\":\"world-"+current+"\"}");
        });
        var first=env.Flow.GenerateAsync(Key,new("First","room"),default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal("Submitting",Assert.Single(env.Store.List()).Status);
            var second=await env.Flow.GenerateAsync(Key,new("Second","room"),default);
            Assert.Equal("PENDING",second.Status);
            Assert.Equal(2,env.Store.Page(new()).Total);
        }
        finally { release.TrySetResult(); }
        Assert.Equal("PENDING",(await first).Status);
    }
    [Fact]
    public async Task RenderSnapshotOwnsCameraAndLightsBeforeUploadCanObserveCallerMutation()
    {
        var light=RenderLight.PointApproximation(new(1,2,3),GaussianTransform.Identity,1,50,new[]{1d,.5,.2},2);
        var input=new RenderRequest("Test",Path.Combine(root,"source.glb"),Camera with{Transform=Camera.Transform.ToArray()},Lights:new[]{light});
        var env=Create(_=>
        {
            input.Camera.Transform[12]=999;light.Transform[12]=999;light.Color[0]=0;input.Lights![0]=light with{Radius=999};
            throw new HttpRequestException("Upload deliberately stopped after snapshot");
        });
        GlbWriter.Write(input.GlbPath,new[]{new MeshData(new float[9],new[]{0,1,2},0,0,0)});
        var result=await env.Flow.RenderAsync(Key,input,default);
        var recorded=env.Store.Get(result.Id).Render!;
        Assert.Equal(0,recorded.Camera.Transform[12]);Assert.Equal(1,recorded.Lights![0].Transform[12]);Assert.Equal(1,recorded.Lights[0].Color[0]);Assert.Equal(50,recorded.Lights[0].Radius);
        Assert.Equal(1,env.Handler.Count);
    }
    [Fact]
    public async Task InvalidLightFailsBeforeCreatingJournalOrUploading()
    {
        var env=Create(_=>throw new Exception("No network expected"));
        var light=RenderLight.PointApproximation(new(),GaussianTransform.Identity,1,50,new[]{1d,1,1},2) with{Radius=-1};
        await Assert.ThrowsAsync<ArgumentException>(()=>env.Flow.RenderAsync(Key,new("Test","unused",Camera,Lights:new[]{light}),default));
        Assert.Empty(env.Store.List());Assert.Equal(0,env.Handler.Count);
    }
    [Fact]
    public async Task RecoveredRenderContinuationUsesOriginalSavedLightSnapshot()
    {
        string? scene=null;var env=Create(async request=>
        {
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            scene=Encoding.UTF8.GetString(Convert.FromBase64String(body.RootElement.GetProperty("usdContent").GetString()!));
            return Json("{\"operationId\":\"original-light-render\",\"done\":false}");
        });
        var light=RenderLight.PointApproximation(new(1,2,3),GaussianTransform.Identity,1000,50,new[]{1d,.5,.2},2);
        var task=new LocalTask{Kind="render",ConnectionId=Key.ConnectionId,Status="MeshReady",MeshId="MESH",MeshTaskId="123",Render=new("Test","unused",Camera,Lights:new[]{light})};
        env.Store.Save(task);light.Transform[12]=999;
        await env.Flow.ContinueRenderAsync(Key,task.Id,default);
        Assert.Contains("(1000, 2000, 3000, 1)",scene);Assert.Contains("def SphereLight",scene);Assert.Equal(1,env.Handler.Count);
    }
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK){Content=new StringContent(text,Encoding.UTF8,"application/json")};
    private sealed class Stub(Func<HttpRequestMessage,Task<HttpResponseMessage>> responder):HttpMessageHandler
    {
        public int Count {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) {Count++;return responder(request);}
    }
}
