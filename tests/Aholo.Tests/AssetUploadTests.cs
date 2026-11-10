using System.Net;
using System.Text;
using System.Text.Json;
using Aholo.Core;
using Aholo.Provider;

namespace Aholo.Tests;

public sealed class AssetUploadTests : IDisposable
{
    private readonly string file = Path.GetTempFileName();
    private static readonly ApiKeyCredential Key = new("fake-upload-test-key");
    public AssetUploadTests() => File.WriteAllText(file, "abcdefghij");
    public void Dispose() => File.Delete(file);
    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void ResumeRangesAreMergedWithoutExpandingEveryPart()
    {
        var merged=UploadBlockRanges.Parse(Json("[\"4-8\",\"1-2\",\"2-5\",\"10-10\"]"),10);
        Assert.Equal(new[]{new UploadBlockRanges.Range(1,8),new UploadBlockRanges.Range(10,10)},merged);
        Assert.Empty(UploadBlockRanges.Parse(Json("[]"),10));
        Assert.Equal(new UploadBlockRanges.Range(1,int.MaxValue),Assert.Single(UploadBlockRanges.Parse(default,int.MaxValue)));
    }
    [Theory]
    [InlineData("[\"0-1\"]")]
    [InlineData("[\"3-2\"]")]
    [InlineData("[\"1-11\"]")]
    [InlineData("[\"1\"]")]
    [InlineData("[\"1-2-3\"]")]
    [InlineData("[\"1-9223372036854775808\"]")]
    [InlineData("[1]")]
    [InlineData("{}")]
    public void InvalidResumeRangesFailBeforeSendingParts(string value)
        => Assert.Throws<InvalidDataException>(()=>UploadBlockRanges.Parse(Json(value),10));

    [Theory]
    [InlineData("null",3)]
    [InlineData("[]",0)]
    [InlineData("[\"2-2\",\"2-3\"]",2)]
    public async Task SendsOnlyServerRequestedParts(string ranges,int expected)
    {
        var handler=new UploadHandler(ranges);using var api=Api();using var http=new HttpClient(handler);
        await new AholoApiClient(api,http).UploadAsync(Key,file);
        Assert.Equal(expected,handler.Blocks.Count);Assert.Equal(handler.Blocks.Count,handler.Blocks.Distinct().Count());
        if(expected==2){Assert.Equal(new long[]{2,3},handler.Blocks);Assert.Contains("efgh",handler.Bodies[0]);Assert.Contains("ij",handler.Bodies[1]);}
        Assert.All(handler.Bodies,body=>Assert.Contains("application/octet-stream",body));
    }
    [Fact]
    public async Task OptionalMultipartFieldsRoundTripAndDoNotChangeContent()
    {
        var handler=new UploadHandler("[]");using var api=Api();using var http=new HttpClient(handler);
        await new AholoApiClient(api,http).UploadAsync(Key,file,new AssetUploadOptions("元数据 & +", "folder/name", ""));
        string query=Uri.UnescapeDataString(handler.Init!.Query);
        Assert.Contains("&metadata=元数据 & +&customPrefix=folder/name&customFilename=",query);
        Assert.Empty(handler.Blocks);
    }
    [Fact]
    public async Task SmallFileOptionsFailBeforeAnyUpload()
    {
        var handler=new UploadHandler("[]");using var api=Api(blockSize:100);using var http=new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(()=>new AholoApiClient(api,http).UploadAsync(Key,file,new AssetUploadOptions("metadata")));
        Assert.Null(handler.Init);Assert.Equal(0,handler.Requests);
    }
    [Theory]
    [InlineData("https://ous-sg.kujiale.com:444/")]
    [InlineData("https://ous-sg.kujiale.com/#fragment")]
    [InlineData("https://person@ous-sg.kujiale.com/")]
    public async Task TokenNeverGoesToAlteredUploadOrigins(string origin)
    {
        var handler=new UploadHandler("[]");using var api=Api(origin:origin);using var http=new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderException>(()=>new AholoApiClient(api,http).UploadAsync(Key,file));
        Assert.Equal(0,handler.Requests);
    }
    [Fact]
    public async Task TimeoutIncludesPendingStatusHttpAndCallerCancellationStaysCancellation()
    {
        var handler=new UploadHandler("[]"){StallStatus=true};using var api=Api();using var http=new HttpClient(handler);
        var client=new AholoApiClient(api,http){UploadCompletionTimeout=TimeSpan.FromMilliseconds(80)};
        var error=await Assert.ThrowsAsync<ProviderException>(()=>client.UploadAsync(Key,file));
        Assert.Equal("upload_timeout",error.Code);
        using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>client.UploadAsync(Key,file,cancellation.Token));
        Assert.Empty(handler.Blocks);
    }
    [Fact]
    public async Task LostPartResponseIsNotBlindlyReplayed()
    {
        var handler=new UploadHandler("null"){FailPart=true};using var api=Api();using var http=new HttpClient(handler);
        await Assert.ThrowsAsync<ProviderException>(()=>new AholoApiClient(api,http).UploadAsync(Key,file));
        Assert.Equal(new long[]{1},handler.Blocks);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("\"4\"")]
    [InlineData("{}")]
    public async Task MalformedTokenBlockSizeHasHandledProviderFailure(string value)
    {
        using var api=new HttpClient(new FixedHandler("{\"ousToken\":\"fake-ous-token\",\"globalDomain\":\"https://ous-sg.kujiale.com/\",\"blockSize\":"+value+"}"));
        var handler=new UploadHandler("[]");using var http=new HttpClient(handler);
        var error=await Assert.ThrowsAsync<ProviderException>(()=>new AholoApiClient(api,http).UploadAsync(Key,file));
        Assert.Equal("invalid_response",error.Code);Assert.Equal(0,handler.Requests);
    }
    [Theory]
    [InlineData("{\"taskId\":\"1\"}",null)]
    [InlineData("{\"taskId\":null}",null)]
    [InlineData("{\"taskId\":1,\"lackBlocks\":[\"bad\"]}",null)]
    [InlineData("{\"taskId\":1,\"lackBlocks\":[]}","[]")]
    [InlineData("{\"taskId\":1,\"lackBlocks\":[]}","{\"status\":\"5\"}")]
    public async Task MalformedInitAndStatusHaveHandledProviderFailure(string init,string? status)
    {
        using var api=Api();var handler=new UploadHandler("[]"){InitResult=init,StatusResult=status};using var http=new HttpClient(handler);
        var error=await Assert.ThrowsAsync<ProviderException>(()=>new AholoApiClient(api,http).UploadAsync(Key,file));
        Assert.Equal("invalid_response",error.Code);Assert.Empty(handler.Blocks);
    }
    private static HttpClient Api(long blockSize=4,string origin="https://ous-sg.kujiale.com/")
        => new(new FixedHandler(JsonSerializer.Serialize(new{ousToken="fake-ous-token",globalDomain=origin,blockSize})));
    private static HttpResponseMessage Response(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")};
    private sealed class FixedHandler(string body):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(Response(body));
    }
    private sealed class UploadHandler(string ranges):HttpMessageHandler
    {
        public string? InitResult,StatusResult;
        public Uri? Init;public int Requests;public bool StallStatus,FailPart;
        public List<long> Blocks=new();public List<string> Bodies=new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Requests++;Assert.False(request.Headers.Contains("Authorization"));Assert.Equal("fake-ous-token",Assert.Single(request.Headers.GetValues("ous-token-v2")));
            if(request.RequestUri!.AbsolutePath.EndsWith("/init")){Init=request.RequestUri;return Response("{\"c\":\"0\",\"d\":"+(InitResult??("{\"taskId\":1,\"lackBlocks\":"+ranges+"}"))+"}");}
            if(request.RequestUri.AbsolutePath.EndsWith("/part"))
            {
                var multipart=Assert.IsType<MultipartFormDataContent>(request.Content);
                var block=multipart.First(p=>p.Headers.ContentDisposition!.Name!.Trim('"')=="block");
                Blocks.Add(long.Parse(await block.ReadAsStringAsync(ct)));Bodies.Add(await multipart.ReadAsStringAsync(ct));
                if(FailPart)throw new HttpRequestException("simulated lost reply");
                return Response("{\"c\":\"0\",\"d\":null}");
            }
            if(StatusResult is not null)return Response("{\"c\":\"0\",\"d\":"+StatusResult+"}");
            if(StallStatus)await Task.Delay(Timeout.Infinite,ct);
            return Response("{\"c\":\"0\",\"d\":{\"status\":5,\"url\":\"https://assets.example.test/result.glb\"}}");
        }
    }
}
