using System.Net;
using System.Text;
using System.Text.Json;
using Aholo.Core;
using Aholo.Provider;
using Aholo.Sidecar;
namespace Aholo.Tests;

public sealed class CloudRecoveryTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-cloud-tests-"+Guid.NewGuid().ToString("N"));
    private readonly ApiKeyCredential key=new("fake-cloud-recovery-key");
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    private (TaskStore Store,Workflows Flow,Handler Http) Create(Func<HttpRequestMessage,string> reply)
    {
        var store=new TaskStore(root);var handler=new Handler(reply);var http=new HttpClient(handler);
        return(store,new Workflows(new AholoApiClient(http,http),store,new AssetDownloader(http,store)),handler);
    }
    private const string World="{\"worldId\":\"WORLD1\",\"name\":\"Room\",\"status\":\"SUCCEEDED\",\"assets\":{\"splats\":{\"urls\":{\"plyPath\":\"https://asset.example/world.ply?sig=secret\",\"lodMetaPath\":\"https://asset.example/lod?sig=secret\"}}}}";
    [Theory]
    [InlineData(TaskService.World,0)]
    [InlineData(TaskService.SpatialUnderstanding,1)]
    [InlineData(TaskService.Lux3D,1)]
    public async Task CloudPagesMapConventionsAndDoNotCreateLocalRecords(TaskService service,int expectedPage)
    {
        var env=Create(request=>service==TaskService.Lux3D
            ? "{\"c\":\"0\",\"d\":{\"items\":[{\"taskId\":9007199254740993,\"status\":3}],\"total\":1,\"page\":1,\"pageSize\":20}}"
            : "{\"pageNum\":"+expectedPage+",\"pageSize\":20,\"count\":1,\"totalCount\":1,\"hasMore\":false,\"result\":["+(service==TaskService.World?World:"{\"taskId\":\"SPACE1\",\"status\":\"SUCCEEDED\"}")+"]}");
        var result=await env.Flow.CloudHistoryAsync(key,new(service),default);
        Assert.Equal(key.ConnectionId,result.ConnectionId);Assert.Single(result.Items);Assert.Equal(1,result.Page);Assert.Empty(env.Store.List());
        Assert.DoesNotContain("secret",JsonSerializer.Serialize(result));
        if(service==TaskService.Lux3D){Assert.Equal("9007199254740993",result.Items[0].RemoteId);Assert.Contains("page=1",env.Http.Requests[0].Url);}
        else Assert.Contains("\"pageNum\":"+expectedPage,env.Http.Requests[0].Body);
    }
    [Fact]
    public async Task RepeatedRestoreUsesOriginalIdentityBeyondFirstLocalPage()
    {
        var env=Create(_=>World);
        var original=new LocalTask{Kind="world",WorldId="WORLD1",ConnectionId=key.ConnectionId,Status="SUCCEEDED",PlyPath="retained.ply",CreatedAt=DateTimeOffset.UtcNow.AddYears(-1)};
        env.Store.Save(original);
        for(int i=0;i<110;i++)env.Store.Save(new(){Status="FAILED",ConnectionId=key.ConnectionId});
        var restored=await env.Flow.RestoreRemoteAsync(key,new(TaskService.World,"WORLD1"),default);
        Assert.Equal(original.Id,restored.Id);Assert.Equal("retained.ply",restored.PlyPath);
        var again=await env.Flow.RestoreRemoteAsync(key,new(TaskService.World,"WORLD1"),default);
        Assert.Equal(original.Id,again.Id);Assert.Equal(111,env.Store.Page(new(Limit:100)).Total);
        Assert.Null(env.Store.Get(original.Id).Artifacts.Single().Url);
        Assert.DoesNotContain("secret",File.ReadAllText(Path.Combine(root,"tasks",original.Id.ToString("N")+".json")));
    }
    [Fact]
    public async Task LuxRestoreRequiresExplicitOperationAndRetainsDownloadedSlots()
    {
        var env=Create(_=>"{\"c\":\"0\",\"d\":{\"taskId\":9007199254740993,\"status\":3,\"outputs\":[{\"content\":\"https://asset.example/model.glb?token=secret\"}]}}");
        await Assert.ThrowsAsync<ArgumentException>(()=>env.Flow.RestoreRemoteAsync(key,new(TaskService.Lux3D,"9007199254740993"),default));Assert.Empty(env.Http.Requests);
        var task=await env.Flow.RestoreRemoteAsync(key,new(TaskService.Lux3D,"9007199254740993","lux.text-to-3d"),default);
        env.Store.Save(task with{Artifacts=new[]{task.Artifacts[0] with{LocalPath="original.glb",Sha256="savedhash"}}});
        var again=await env.Flow.RestoreRemoteAsync(key,new(TaskService.Lux3D,"9007199254740993","lux.text-to-3d"),default);
        Assert.Equal(task.Id,again.Id);Assert.Equal("original.glb",again.Artifacts[0].LocalPath);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>env.Flow.RestoreRemoteAsync(key,new(TaskService.Lux3D,"9007199254740993","lux.rig"),default));
        Assert.Equal("lux.text-to-3d",env.Store.Get(task.Id).ServiceOperation);
        Assert.All(env.Http.Requests,r=>Assert.Equal(HttpMethod.Get,r.Method));
    }
    [Fact]
    public async Task RestoringRenderOnlyPollsAndCannotAuthorizeContinuation()
    {
        var env=Create(_=>"{\"operationId\":\"render1\",\"done\":false}");
        var task=await env.Flow.RestoreRemoteAsync(key,new(TaskService.RenderCloud,"render1"),default);
        Assert.False(TaskCapabilities.For(task).CanContinueRender);Assert.Null(task.Render);
        Assert.Contains("\"operationId\":\"render1\"",Assert.Single(env.Http.Requests).Body);
        await env.Flow.ContinueRenderAsync(key,task.Id,default);
        Assert.Equal(2,env.Http.Requests.Count);Assert.All(env.Http.Requests,r=>Assert.Contains("operationId",r.Body));
    }
    [Fact]
    public async Task StaleAccountIsRejectedBeforeAnyNetworkOrJournalChange()
    {
        var env=Create(_=>throw new Exception("No request expected"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>env.Flow.RestoreRemoteAsync(key,new(TaskService.World,"WORLD1",ExpectedConnectionId:"another-account"),default));
        Assert.Empty(env.Store.List());Assert.Empty(env.Http.Requests);
    }
    [Fact]
    public async Task UnknownSubmissionIsNotGuessedOrOverwrittenByRecovery()
    {
        var env=Create(_=>World);var uncertain=new LocalTask{Kind="world",ConnectionId=key.ConnectionId,Status="SubmissionUnknown"};env.Store.Save(uncertain);
        var restored=await env.Flow.RestoreRemoteAsync(key,new(TaskService.World,"WORLD1"),default);
        Assert.NotEqual(uncertain.Id,restored.Id);Assert.Equal("SubmissionUnknown",env.Store.Get(uncertain.Id).Status);
    }
    [Fact]
    public async Task UnsupportedJournalIsPreservedByteForByte()
    {
        var env=Create(_=>World);var original=new LocalTask{Kind="world",WorldId="WORLD1",ConnectionId=key.ConnectionId,Status="SUCCEEDED"};env.Store.Save(original);
        string path=Path.Combine(root,"tasks",original.Id.ToString("N")+".json");string bytes=File.ReadAllText(path).Replace("\"schemaVersion\":1","\"schemaVersion\":999");File.WriteAllText(path,bytes);
        await Assert.ThrowsAsync<IOException>(()=>env.Flow.RestoreRemoteAsync(key,new(TaskService.World,"WORLD1"),default));Assert.Equal(bytes,File.ReadAllText(path));
    }
    [Fact]
    public async Task WorldLodMetadataIsRetainedWithoutGuessingItsSchema()
    {
        var env=Create(request=>request.RequestUri!.Host=="asset.example"?"opaque future lod data":World);
        var task=await env.Flow.RestoreRemoteAsync(key,new(TaskService.World,"WORLD1"),default);
        var downloaded=await env.Flow.DownloadAsync(key,new(task.Id,"artifact:world/lod"),default);
        var artifact=Assert.Single(downloaded.Artifacts);Assert.Equal("metadata",artifact.Format);Assert.Equal("opaque future lod data",File.ReadAllText(artifact.LocalPath!));
        Assert.Null(env.Store.Get(task.Id).Artifacts[0].Url);
    }
    private sealed class Handler(Func<HttpRequestMessage,string> reply):HttpMessageHandler
    {
        public List<(HttpMethod Method,string Url,string Body)> Requests=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests.Add((request.Method,request.RequestUri!.AbsoluteUri,request.Content is null?"":await request.Content.ReadAsStringAsync(ct)));
            return new(HttpStatusCode.OK){Content=new StringContent(reply(request),Encoding.UTF8,"application/json")};
        }
    }
}
