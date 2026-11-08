using System.Net;
using System.Text;
using System.Text.Json;
using Aholo.Core;
using Aholo.Provider;
namespace Aholo.Tests;

public sealed class ServiceApiTests
{
    private static readonly ApiKeyCredential Key = new("unit-test-public-placeholder");
    public static IEnumerable<object[]> Creates()
    {
        yield return new object[]{"world.generate","""{"prompt":"a room"}"""};
        yield return new object[]{"world.reconstruct","""{"scene":"space","taskQuality":"normal","resources":[{"url":"https://example.com/room.mp4","type":"video"}]}"""};
        yield return new object[]{"lux.text-to-3d","""{"prompt":"chair","version":"G1-Turbo","outputFormat":["glb"],"faceCount":300000}"""};
        yield return new object[]{"lux.img-to-3d","""{"imgs":["https://example.com/a.png","https://example.com/b.png"],"version":"G1-Turbo","outputFormat":["glb"]}"""};
        yield return new object[]{"lux.multimodal-to-image","""{"prompt":"chair"}"""};
        yield return new object[]{"lux.image-to-four-view","""{"img":"https://example.com/a.png"}"""};
        yield return new object[]{"lux.material-transfer","""{"img":"https://example.com/a.png","meshUrl":"https://example.com/a.glb","version":"v3.0-standard","customSize":1200,"outputFormat":["glb","usdz"]}"""};
        yield return new object[]{"lux.part-split","""{"glbUrl":"https://example.com/a.glb"}"""};
        yield return new object[]{"lux.multi-format-export","""{"modelUrl":"https://example.com/a.glb","outputFormat":["usdz","stl"]}"""};
        yield return new object[]{"lux.articulation-animation","""{"glbUrl":"https://example.com/a.glb","prompt":"open doors"}"""};
        yield return new object[]{"lux.rig","""{"modelUrl":"https://example.com/a.glb"}"""};
        yield return new object[]{"lux.retarget","""{"rigModelUrl":"https://example.com/a.glb","animationIds":["Walk_Loop"],"outFormat":"glb"}"""};
        yield return new object[]{"spatial.understand","""{"inputFileUrl":"https://example.com/room.ply","transformationMatrix":[[1,0,0,2],[0,1,0,3],[0,0,1,4],[0,0,0,1]]}"""};
    }
    [Theory,MemberData(nameof(Creates))]
    public async Task EveryPublicCreateHasExplicitContractAndSubmitsOnce(string operation,string json)
    {
        int calls=0;var def=ServiceOperations.Get(operation);
        using var handler=new Handler(async request=>
        {
            calls++;Assert.Equal(HttpMethod.Post,request.Method);
            Assert.Equal("/global/"+def.Path,request.RequestUri!.AbsolutePath);
            Assert.Single(request.Headers.GetValues("Authorization"));
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(JsonSerializer.Serialize(Element(json)),JsonSerializer.Serialize(body.RootElement));
            return Json(def.Service switch{TaskService.Lux3D=>"""{"c":"0","d":9007199254740993,"f":null,"m":""}""",TaskService.World=>"""{"worldId":"world-a"}""",_=>"""{"taskId":"spatial-a"}"""});
        });
        using var http=new HttpClient(handler);
        var result=await new AholoApiClient(http,http).CreateServiceTaskAsync(Key,operation,Element(json));
        Assert.Equal(def.Service,result.Service);Assert.Equal(1,calls);
        if(def.Service==TaskService.Lux3D)Assert.Equal("9007199254740993",result.Id);
    }
    [Theory]
    [InlineData("lux.img-to-3d","{\"img\":\"https://example.com/a.png\",\"imgs\":[\"https://example.com/b.png\"]}")]
    [InlineData("lux.img-to-3d","{\"imgs\":[\"https://example.com/a.png\"]}")]
    [InlineData("lux.text-to-3d","{\"prompt\":\"chair\",\"faceCount\":300001}")]
    [InlineData("lux.text-to-3d","{\"prompt\":\"chair\",\"outputFormat\":[\"glb\",\"glb\"]}")]
    [InlineData("lux.retarget","{\"rigModelUrl\":\"https://example.com/a.glb\",\"animationIds\":[\"InventedAnimation\"]}")]
    [InlineData("lux.material-transfer","{\"img\":\"https://example.com/a.png\",\"meshUrl\":\"https://example.com/a.glb\",\"version\":\"v3.0-standard\",\"customSize\":0}")]
    [InlineData("lux.multi-format-export","{\"modelUrl\":\"https://example.com/a.glb\"}")]
    [InlineData("lux.multi-format-export","{\"modelUrl\":\"https://example.com/a.glb\",\"outputFormat\":[\"glb\"]}")]
    [InlineData("spatial.understand","{\"inputFileUrl\":\"https://example.com/a.glb\"}")]
    [InlineData("spatial.understand","{\"inputFileUrl\":\"https://example.com/a.ply\",\"transformationMatrix\":[[1,0],[0,1]]}")]
    [InlineData("world.generate","{\"name\":\"empty world\"}")]
    [InlineData("world.reconstruct","{\"scene\":\"space\",\"taskQuality\":\"normal\",\"resources\":[{\"url\":\"https://example.com/a.jpg\"}]}")]
    public async Task InvalidInputNeverReachesNetwork(string operation,string json)
    {
        using var handler=new Handler(_=>throw new Exception("No network call permitted"));using var http=new HttpClient(handler);
        await Assert.ThrowsAnyAsync<ArgumentException>(()=>new AholoApiClient(http,http).CreateServiceTaskAsync(Key,operation,Element(json)));
    }
    [Fact]
    public void ImageReconstructionUsesServiceMinimumAndKeepsMixedTypes()
    {
        var images=Enumerable.Range(0,20).Select(i=>new{url="https://example.com/"+i+".png",type="image"}).ToArray();
        ServiceOperations.Validate("world.reconstruct",AholoJson.Element(new{scene="model",taskQuality="high",resources=images}));
        Assert.Throws<ArgumentException>(()=>ServiceOperations.Validate("world.reconstruct",AholoJson.Element(new{scene="model",taskQuality="high",resources=images.Take(19)})));
    }
    [Fact]
    public async Task LostOrMalformedCreateReplyRemainsUnknownAndIsNeverRetried()
    {
        int calls=0;using var handler=new Handler(_=>{calls++;return Task.FromResult(Json("""{"c":"0","d":null}"""));});using var http=new HttpClient(handler);
        var ex=await Assert.ThrowsAsync<ProviderException>(()=>new AholoApiClient(http,http).CreateServiceTaskAsync(Key,"lux.text-to-3d",Element("""{"prompt":"chair"}""")));
        Assert.True(ex.SubmissionMayHaveSucceeded);Assert.Equal(1,calls);
    }
    [Fact]
    public async Task LuxQueryDoesNotRequireUndocumentedBizIdOrLoseOutputOrder()
    {
        using var handler=new Handler(request=>
        {
            Assert.Equal(HttpMethod.Get,request.Method);Assert.EndsWith("?taskid=9007199254740993",request.RequestUri!.ToString());
            return Task.FromResult(Json("""{"c":"0","d":{"taskId":9007199254740993,"status":91,"outputs":[{"content":"NOT_REQUESTED"},{"content":"[\"https://example.com/front.png\"]"},{"content":null}]}}"""));
        });using var http=new HttpClient(handler);
        var task=await new AholoApiClient(http,http).GetLuxTaskAsync(Key,"9007199254740993");
        Assert.Equal(91,task.Status);Assert.Equal(3,task.Outputs.Count);Assert.Equal("NOT_REQUESTED",task.Outputs[0]);Assert.StartsWith("[",task.Outputs[1]);Assert.Null(task.Outputs[2]);
    }
    [Fact]
    public async Task LuxListPreservesInt64AndServicePagination()
    {
        using var handler=new Handler(request=>
        {
            Assert.Equal("?page=2&pagesize=20&status=3&starttime=1&endtime=2",request.RequestUri!.Query);
            return Task.FromResult(Json("""{"c":"0","d":{"items":[{"taskId":9223372036854775807,"status":6,"created":1,"lastModified":2}],"total":21,"page":2,"pageSize":20}}"""));
        });using var http=new HttpClient(handler);
        var page=await new AholoApiClient(http,http).ListLuxTasksAsync(Key,2,20,3,1,2);Assert.Equal("9223372036854775807",page.Items[0].TaskId);Assert.Equal(21,page.Total);Assert.Equal(6,page.Items[0].Status);
    }
    [Fact]
    public async Task WorldAndSpatialQueriesUseDifferentPageOriginsAndResultShapes()
    {
        using var handler=new Handler(async request=>
        {
            using var input=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            bool world=request.RequestUri!.AbsolutePath=="/global/world/v1/list";
            Assert.Equal(world?0:1,input.RootElement.GetProperty("pageNum").GetInt32());
            return Json(world?"""{"pageNum":0,"pageSize":20,"count":1,"totalCount":1,"hasMore":false,"result":[{"worldId":"w","status":"RUNNING"}]}""":"""{"pageNum":1,"pageSize":20,"count":1,"totalCount":1,"hasMore":false,"result":[{"taskId":"s","status":"SUCCEEDED","result":{"resultSslUrl":"https://example.com/scene.txt"}}]}""");
        });using var http=new HttpClient(handler);var api=new AholoApiClient(http,http);
        Assert.Equal("w",(await api.ListWorldPageAsync(Key)).Items[0].WorldId);
        Assert.Equal("https://example.com/scene.txt",(await api.ListSpatialTasksAsync(Key)).Items[0].ResultUrl);
    }
    [Fact]
    public async Task SpatialDetailChecksIdentityAndDoesNotParseDslAsJson()
    {
        using var handler=new Handler(request=>
        {
            Assert.Equal("/global/spatial-understanding/v1/tasks/room-a",request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json("""{"taskId":"room-a","status":"SUCCEEDED","result":{"resultSslUrl":"https://example.com/walls.txt"},"modelVersion":"v1"}"""));
        });using var http=new HttpClient(handler);
        var task=await new AholoApiClient(http,http).GetSpatialTaskAsync(Key,"room-a");Assert.Equal("v1",task.ModelVersion);Assert.EndsWith(".txt",task.ResultUrl);
    }
    [Fact]
    public void JsonEscapesUseValueSemanticsForChoicesAndDuplicates()
    {
        ServiceOperations.Validate("lux.text-to-3d",Element("""{"prompt":"chair","version":"\u00471-Turbo"}"""));
        Assert.Throws<ArgumentException>(()=>ServiceOperations.Validate("lux.retarget",Element("""{"rigModelUrl":"https://example.com/a.glb","animationIds":["Walk_Loop","\u0057alk_Loop"]}""")));
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task MalformedLuxListRowsAreProviderErrors(string row)
    {
        using var handler=new Handler(_=>Task.FromResult(Json("{\"c\":\"0\",\"d\":{\"items\":["+row+"],\"page\":1,\"pageSize\":20,\"total\":1}}")));using var http=new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderException>(()=>new AholoApiClient(http,http).ListLuxTasksAsync(Key));
    }
    [Fact]
    public async Task WorldDetailRetainsCoverLodAndTimeMetadata()
    {
        using var handler=new Handler(_=>Task.FromResult(Json("""{"worldId":"w","status":"SUCCEEDED","scene":"space","cover":"https://example.com/a.png","createTime":12,"updateTime":23,"assets":{"splats":{"urls":{"lodMetaPath":"https://example.com/lod.json"}}}}""")));using var http=new HttpClient(handler);
        var world=await new AholoApiClient(http,http).GetWorldAsync(Key,"w");
        Assert.Equal("space",world.Scene);Assert.Equal(12,world.Created);Assert.Equal(23,world.Updated);Assert.EndsWith(".json",world.LodMetaUrl);Assert.EndsWith(".png",world.CoverUrl);
    }
    private static JsonElement Element(string json){using var doc=JsonDocument.Parse(json);return doc.RootElement.Clone();}
    private static HttpResponseMessage Json(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> send):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request);}
}
