using System.Net;
using System.Text.Json;
using Aholo3D.Core;
using Aholo3D.Provider;
using Aholo3D.Sidecar;
namespace Aholo3D.Tests;
public sealed class ServiceWorkflowTests:IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo3d-service-tests-"+Guid.NewGuid().ToString("N"));
    private readonly ApiKeyCredential key=new("test-key-do-not-use");
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    private (TaskStore,Workflows,Handler) Create(Func<HttpRequestMessage,HttpResponseMessage> respond)
    {var store=new TaskStore(root);var handler=new Handler(respond);var http=new HttpClient(handler);return(store,new Workflows(new Aholo3DApiClient(http,http),store,new AssetDownloader(http,store)),handler);}
    private static HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK){Content=new StringContent(value)};
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;return Task.FromResult(respond(request));}}
    [Theory]
    [InlineData("lux.text-to-3d","{\"prompt\":\"A chair\"}","object","{\"c\":\"0\",\"d\":9007199254740993}")]
    [InlineData("world.generate","{\"prompt\":\"An indoor room\"}","world","{\"worldId\":\"WORLD1\"}")]
    public async Task CreatesTypedTaskAndRetainsExactRemoteIdentity(string operation,string body,string kind,string reply)
    {
        var(store,flow,handler)=Create(r=>{Assert.Equal(HttpMethod.Post,r.Method);Assert.Equal("Submitting",Assert.Single(new TaskStore(root).List()).Status);return Json(reply);});
        var task=await flow.CreateServiceAsync(key,new(operation,"Test",JsonDocument.Parse(body).RootElement),default);
        Assert.Equal("PENDING",task.Status);Assert.Equal(kind,task.Kind);Assert.Equal(operation,store.Get(task.Id).ServiceOperation);
        Assert.Equal(kind=="object"?"9007199254740993":kind=="world"?"WORLD1":"SPACE1",TaskIdentity.From(task).RemoteId);Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task SpatialWorkflowRequiresLocalUploadProvenance()
    {
        var(store,flow,handler)=Create(_=>throw new Exception("No request expected"));
        await Assert.ThrowsAsync<ArgumentException>(()=>flow.CreateServiceAsync(key,new("spatial.understand","Room",Aholo3DJson.Element(new{inputFileUrl="https://example.com/arbitrary.ply"})),default));Assert.Equal(0,handler.Calls);Assert.Empty(store.List());
    }
    [Fact] public async Task BadInputsAreRejectedBeforeNetworkOrTaskCreation()
    {
        var(store,flow,handler)=Create(_=>throw new Exception("No request expected"));
        await Assert.ThrowsAsync<ArgumentException>(()=>flow.CreateServiceAsync(key,new("lux.img-to-3d","Bad",Aholo3DJson.Element(new{version="G1"}),new[]{new ServiceFileInput("imgs",new[]{"a.png"}),new ServiceFileInput("img",new[]{"b.png"})}),default));
        Assert.Equal(0,handler.Calls);Assert.Empty(store.List());
    }
    [Fact] public async Task UnknownSubmissionIsNeverReplayedByRefresh()
    {
        var(store,flow,handler)=Create(_=>throw new HttpRequestException("lost reply"));
        var task=await flow.CreateServiceAsync(key,new("lux.text-to-3d","Chair",Aholo3DJson.Element(new{prompt="chair"})),default);
        Assert.Equal("SubmissionUnknown",task.Status);await flow.RefreshAsync(key,task.Id,default);Assert.Equal(1,handler.Calls);
    }
    [Fact] public async Task QueryAndDownloadKeepSignedUrlsOffDiskAndDifferentOutputFilesSeparate()
    {
        byte[] glb=GlbContainerValidatorTests.CreateTexturedTriangleGlb();
        var(store,flow,handler)=Create(r=>
        {
            if(r.RequestUri!.Host=="api.aholo.ai")return Json("""{"c":"0","d":{"taskId":7,"status":3,"outputs":[{"content":"https://example.com/a.zip?token=private"},{"content":"https://example.com/b.glb?token=private"}]}}""");
            Assert.Null(r.Headers.Authorization);return new(HttpStatusCode.OK){Content=new ByteArrayContent(r.RequestUri.AbsolutePath.EndsWith(".glb")?glb:new byte[]{80,75,3,4,0})};
        });
        var task=new LocalTask{Kind="object",Status="RUNNING",LuxTaskId="7",ConnectionId=key.ConnectionId,ServiceOperation="lux.material-transfer"};store.Save(task);
        var first=await flow.DownloadAsync(key,new(task.Id,"artifact:0"),default);var second=await flow.DownloadAsync(key,new(task.Id,"artifact:1"),default);
        Assert.NotEqual(first.Artifacts[0].LocalPath,second.Artifacts[1].LocalPath);Assert.All(second.Artifacts,a=>Assert.True(File.Exists(a.LocalPath)));
        Assert.All(store.Get(task.Id).Artifacts,a=>Assert.Null(a.Url));Assert.DoesNotContain("private",File.ReadAllText(Path.Combine(root,"tasks",task.Id.ToString("N")+".json")));Assert.Equal(4,handler.Calls);
    }
    [Fact] public void DecoderPreservesSlotsAndDoesNotLeakMalformedContent()
    {
        var assets=LuxArtifacts.Decode("lux.material-transfer",new[]{"https://example.com/a.zip","NOT_REQUESTED",null,"https://example.com/b.zip"});
        Assert.Equal(new[]{"0","3"},assets.Select(a=>a.Key));Assert.Equal("obj_zip",assets[1].Format);
        var errors=LuxArtifacts.Decode("lux.rig",new[]{"{\"error\":\"https://example.com/?token=private\"}"});Assert.Null(Assert.Single(errors).Url);Assert.DoesNotContain("private",JsonSerializer.Serialize(errors));
    }
    [Fact] public void FourViewAndRetargetKeepExplicitOutputIdentities()
    {
        var views=LuxArtifacts.Decode("lux.image-to-four-view",new[]{"[\"https://example.com/a.png\",\"https://example.com/b.png\",\"https://example.com/c.png\",\"https://example.com/d.png\"]"});Assert.Equal(4,views.Length);Assert.Equal("Back",views[3].Label);
        var actions=LuxArtifacts.Decode("lux.retarget",new[]{"""{"actions":[{"animation_id":"Idle","status":"success","animationGlbUrl":"https://example.com/a.glb"},{"animation_id":"Idle","status":"success","fbxUrl":"https://example.com/a.fbx"},{"status":"failed","error":{"message":"secret"}}]}"""});
        Assert.Equal(3,actions.Length);Assert.Equal(3,actions.Select(a=>a.Key).Distinct().Count());Assert.Equal("fbx",actions[1].Format);Assert.Null(actions[2].Url);
    }
    [Fact] public void ReconstructionPrevalidationPreservesImageOrderAndMinimum()
    {
        var inputs=Enumerable.Range(0,20).Select(i=>"image"+i+".png").ToArray();
        var request=new CreateServiceRequest("world.reconstruct","Room",Aholo3DJson.Element(new{scene="space",taskQuality="normal"}),new[]{new ServiceFileInput("resources",inputs)});
        var body=ServiceRequest.Bind(request,(input,i)=>"https://example.com/"+input.Paths[i]);Assert.Equal("https://example.com/image19.png",body.GetProperty("resources")[19].GetProperty("url").GetString());
        Assert.Throws<ArgumentException>(()=>ServiceRequest.Bind(request with{Files=new[]{new ServiceFileInput("resources",inputs.Take(19).ToArray())}},ServiceRequest.Placeholder));
    }
    [Fact] public async Task MissingOutputsRetainDownloadedAssetsAndNoStaleUrl()
    {
        var(store,flow,_)=Create(_=>Json("{\"c\":\"0\",\"d\":{\"taskId\":7,\"status\":3,\"outputs\":[]}}"));
        var task=new LocalTask{Kind="object",Status="SUCCEEDED",LuxTaskId="7",ConnectionId=key.ConnectionId,ServiceOperation="lux.material-transfer",Artifacts=new[]{new TaskArtifact("1","GLB","glb",LocalPath:"original.glb",Sha256:"hash")}};store.Save(task);
        var result=await flow.RefreshAsync(key,task.Id,default);var asset=Assert.Single(result.Artifacts);Assert.Equal("original.glb",asset.LocalPath);Assert.Equal("hash",asset.Sha256);Assert.Null(asset.Url);
    }
    [Fact] public async Task RedownloadPublishesNewBytesWithoutOverwritingPreviouslyRecordedFile()
    {
        byte[] bytes=System.Text.Encoding.ASCII.GetBytes("ply\nfirst");var(store,_,handler)=Create(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});var downloader=new AssetDownloader(new HttpClient(handler),store);var id=Guid.NewGuid();
        string a=await downloader.DownloadAsync(id,"https://example.com/a.ply","ply",default);bytes=System.Text.Encoding.ASCII.GetBytes("ply\nsecond");string b=await downloader.DownloadAsync(id,"https://example.com/a.ply","ply",default);
        Assert.NotEqual(a,b);Assert.Equal("ply\nfirst",File.ReadAllText(a));Assert.Equal("ply\nsecond",File.ReadAllText(b));
    }
    [Fact] public void RestoredTaskCanBeLocatedBeyondFirstPage()
    {
        var store=new TaskStore(root);var task=new LocalTask{Status="SUCCEEDED",CreatedAt=DateTimeOffset.UtcNow.AddDays(-1)};store.Save(task);
        for(int i=0;i<110;i++)store.Save(new LocalTask{Status="SUCCEEDED"});var page=store.PageContaining(task.Id);Assert.Equal(100,page.Offset);Assert.Contains(page.Items,t=>t.Id==task.Id);
    }
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void InvalidGlbInputFailsBeforeUpload(int defect)
    {
        Directory.CreateDirectory(root);byte[] bytes=GlbContainerValidatorTests.CreateTexturedTriangleGlb();
        if(defect==0)bytes[4]=1;else if(defect==1)bytes[8]^=1;else if(defect==2)bytes=bytes[..12];else bytes[12]=1;
        string path=Path.Combine(root,"invalid.glb");File.WriteAllBytes(path,bytes);Assert.Throws<ArgumentException>(()=>GlbEnvelope.Validate(path));
    }
    [Fact] public void ValidAnimatedContainerIsAcceptedForUploadButNotStaticPlacement()
    {
        Directory.CreateDirectory(root);byte[] bytes=GlbContainerValidatorTests.BuildGlb("{\"asset\":{\"version\":\"2.0\"},\"animations\":[{}]}");string path=Path.Combine(root,"animation.glb");File.WriteAllBytes(path,bytes);GlbEnvelope.Validate(path);Assert.Throws<AssetValidationException>(()=>GlbContainerValidator.Validate(bytes));
    }
    [Fact] public async Task AnimationGlbDownloadDoesNotApplyStaticPlacementRestrictions()
    {
        byte[] glb=GlbContainerValidatorTests.CreateTexturedTriangleGlb();var(store,_,handler)=Create(_=>new(HttpStatusCode.OK){Content=new ByteArrayContent(glb)});
        string file=await new AssetDownloader(new HttpClient(handler),store).DownloadAsync(Guid.NewGuid(),"https://example.com/action.glb","glb",default,"0123456789ABCDEF");Assert.Equal(glb,File.ReadAllBytes(file));
    }
}
