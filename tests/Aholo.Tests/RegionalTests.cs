using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aholo.Core;
using Aholo.Provider;
using Aholo.Sidecar;
namespace Aholo.Tests;

public sealed class RegionalTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-regions-"+Guid.NewGuid().ToString("N"));
    private readonly Vault vault=new();
    private CredentialCoordinator Credentials()=>new(vault,Path.Combine(root,"credentials"));
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    private static OAuthToken Token(string region,string text="synthetic-token")=>new(){Region=region,AccessToken=text,RefreshToken=text,ClientId="fixture-client",Scope="get_user_info",ExpiresAt=DateTimeOffset.UtcNow.AddHours(1)};

    [Theory][InlineData("")][InlineData("global")][InlineData("China")][InlineData("https://example.com")][InlineData(null)]
    public void UnknownExplicitRegionIsNeverGuessed(string? region)
    {Assert.Throws<ArgumentException>(()=>AholoRegions.Get(region!));Assert.Throws<ArgumentException>(()=>new ApiKeyCredential("fixture",region!));}

    [Fact]public void ExistingIdentityAndMissingRegionKeepInternationalMeaning()
    {
        var old=new ApiKeyCredential("fixture");var china=new ApiKeyCredential("fixture","china");
        string digest=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture"))).ToLowerInvariant();
        Assert.Equal("international:"+digest,old.ConnectionId);Assert.Equal("china:"+digest,china.ConnectionId);
        Assert.Equal("international",JsonSerializer.Deserialize<LocalTask>("{}",AholoJson.Options)!.Region);
        Assert.Equal("international",JsonSerializer.Deserialize<RealtimePreviewRecord>("{}",AholoJson.Options)!.Region);
        Assert.Equal("international",JsonSerializer.Deserialize<OAuthToken>("{}")!.Region);
        Assert.Equal("international",JsonSerializer.Deserialize<ConnectionState>("{}",AholoJson.Options)!.Region);
    }

    [Fact]public async Task SavedSlotsAndDisconnectedSelectionAreIndependent()
    {
        var c=Credentials();var intl=await c.ConnectAsync(new("intl-fixture"),true);
        var empty=await c.SwitchRegionAsync("china");Assert.Null(empty.Key);Assert.Equal("china",empty.Region);
        var china=await c.ConnectAsync(new("cn-fixture","china"),true);
        Assert.NotEqual(intl.Epoch,china.Epoch);Assert.Equal(2,vault.Values.Count);
        Assert.Equal(intl.Key!.ConnectionId,(await c.SwitchRegionAsync("international")).Key!.ConnectionId);
        Assert.Equal(china.Key!.ConnectionId,(await Credentials().SwitchRegionAsync("china")).Key!.ConnectionId);
        var cleared=await c.DisconnectAsync();Assert.Null(cleared.Key);Assert.Equal("china",cleared.Region);
        Assert.False(vault.Values.ContainsKey("china-api-key"));Assert.True(vault.Values.ContainsKey(CredentialCoordinator.Account));
        Assert.Equal(intl.Key.ConnectionId,(await c.SwitchRegionAsync("international")).Key!.ConnectionId);
    }

    [Fact]public async Task SameRegionPreservesSessionOnlyKeyButLeavingDiscardsIt()
    {
        var c=Credentials();var before=await c.ConnectAsync(new("session-fixture","china"),false);
        var same=await c.SwitchRegionAsync("china");Assert.Equal(before,same);
        await c.SwitchRegionAsync("international");var returned=await c.SwitchRegionAsync("china");Assert.Null(returned.Key);
        Assert.Equal("china",(await Credentials().ReadAsync()).Region);Assert.Empty(vault.Values);
    }

    [Fact]public async Task LegacyStateMigratesInPlaceAndNewFormatRejectsUnknownRegion()
    {
        var c=Credentials();var before=await c.ConnectAsync(new("fixture"),true);
        string path=Assert.Single(Directory.GetFiles(Path.Combine(root,"credentials"),"*.json"));
        File.WriteAllText(path,JsonSerializer.Serialize(new{SchemaVersion=1,before.Epoch,Ready=true}));
        Assert.Equal(before.Key!.ConnectionId,(await Credentials().ReadAsync()).Key!.ConnectionId);
        await c.SwitchRegionAsync("china");using(var state=JsonDocument.Parse(File.ReadAllText(path)))Assert.Equal(2,state.RootElement.GetProperty("SchemaVersion").GetInt32());
        Assert.Single(Directory.GetFiles(Path.Combine(root,"credentials"),"*.json"));
        File.WriteAllText(path,JsonSerializer.Serialize(new{SchemaVersion=2,Epoch=Guid.NewGuid().ToString("N"),Ready=true,Region="invalid"}));
        await Assert.ThrowsAsync<ProviderException>(()=>Credentials().ReadAsync());
        string stamp=await c.MutationStampAsync();var repaired=await c.ConnectAsync(new("repaired","china"),false,expectedStamp:stamp);Assert.Equal("china",repaired.Region);
    }

    [Fact]public async Task StaleValidationAndQueuedRequestCannotCrossRegionSwitch()
    {
        var a=Credentials();var initial=await a.ConnectAsync(new("fixture"),true);
        string stamp=await a.MutationStampAsync();var b=Credentials();await b.SwitchRegionAsync("china");
        var current=await b.ConnectAsync(new("new-session","china"),false);
        await Assert.ThrowsAsync<ProviderException>(()=>a.ConnectAsync(new("late-fixture"),true,expectedStamp:stamp));
        await Assert.ThrowsAsync<ProviderException>(()=>a.SwitchRegionAsync("international",expectedEpoch:initial.Epoch));
        await Assert.ThrowsAsync<ProviderException>(()=>a.DisconnectAsync(expectedEpoch:initial.Epoch));
        using var http=new HttpClient(new Stub(_=>throw new Exception("HTTP must not run")));
        var api=new AholoApiClient(http,http){RequestAdmission=ct=>a.AcquireRequestAsync(initial.Epoch,ct)};
        await Assert.ThrowsAsync<ProviderException>(()=>api.SubmitRenderAsync(initial.Key!,"request","usda"));
        Assert.Equal(current.Key!.ConnectionId,(await b.RequireAsync(current.Epoch)).ConnectionId);
    }

    [Fact]public async Task RegionSwitchWaitsForAdmittedRequest()
    {
        var a=Credentials();var initial=await a.ConnectAsync(new("fixture"),true);var b=Credentials();
        using(var admission=await a.AcquireRequestAsync(initial.Epoch))
        {
            using var ct=new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>b.SwitchRegionAsync("china",ct.Token));
        }
        Assert.Equal(initial.Epoch,(await a.ReadAsync()).Epoch);
        await b.SwitchRegionAsync("china");await Assert.ThrowsAsync<ProviderException>(()=>a.RequireAsync(initial.Epoch));
    }

    [Fact]public async Task FailedTargetReadCannotRestoreOldRegionOrEraseItsSavedKey()
    {
        var c=Credentials();var before=await c.ConnectAsync(new("fixture"),true);vault.FailRead=true;
        await Assert.ThrowsAsync<IOException>(()=>c.SwitchRegionAsync("china"));
        await Assert.ThrowsAsync<ProviderException>(()=>c.RequireAsync(before.Epoch));
        await Assert.ThrowsAsync<ProviderException>(()=>Credentials().ReadAsync());
        vault.FailRead=false;var repaired=await c.SwitchRegionAsync("china");Assert.Null(repaired.Key);
        Assert.Equal(before.Key!.ConnectionId,(await c.SwitchRegionAsync("international")).Key!.ConnectionId);
    }

    [Theory][InlineData("international","https://api.aholo.ai/global/")][InlineData("china","https://api.aholo3d.cn/")]
    public async Task EveryServiceUsesCredentialBoundGateway(string region,string origin)
    {
        var responses=new Queue<string>(new[]{"{\"worldId\":\"world\"}","{\"c\":0,\"d\":{\"taskId\":123,\"status\":1}}","{\"taskId\":\"spatial\",\"status\":\"RUNNING\"}","{\"success\":true,\"data\":123}","{\"operationId\":\"render\",\"done\":false}","{\"operationId\":\"stream\",\"done\":false,\"result\":null,\"error\":null}","{\"sessionId\":\"stream\",\"status\":\"ACTIVE\"}","{\"sessionId\":\"stream\",\"closed\":true}"});
        var seen=new List<Uri>();using var http=new HttpClient(new Stub(r=>
        {Assert.Equal("fixture",r.Headers.GetValues("Authorization").Single());seen.Add(r.RequestUri!);return responses.Dequeue();}));
        var api=new AholoApiClient(http,http);var key=new ApiKeyCredential("fixture",region);
        await api.GenerateWorldAsync(key,"world","room",null);await api.GetLuxTaskAsync(key,"123");await api.GetSpatialTaskAsync(key,"spatial");
        await api.CreateMeshAsync(key,"https://example.com/mesh.glb");await api.SubmitRenderAsync(key,"request","usda");await api.CreateRealtimeAsync(key,"request","usda");
        await api.PushRealtimeAsync(key,"stream","edit","patch");await api.CloseRealtimeAsync(key,"stream");
        Assert.Equal(new[]{"world/v1/generations","lux3d/v1/generate/task/get?taskid=123","spatial-understanding/v1/tasks/spatial","rendercloud/v1/mesh-upload-process/task/create","rendercloud/v1/jobs","rendercloud/v1/streams","rendercloud/v1/streams/stream:push","rendercloud/v1/streams/stream"}.Select(p=>origin+p),seen.Select(u=>u.AbsoluteUri));
    }

    [Theory][InlineData("international","ous-sg.kujiale.com",false)][InlineData("china","ous-cos.kujiale.com",false)][InlineData("china","ous-cos.kujiale.com",true)]
    public async Task UploadUsesOnlyMatchingOusAuthorityAndQueryBasedMultipart(string region,string host,bool multipart)
    {
        Directory.CreateDirectory(root);string path=Path.Combine(root,"input.bin");File.WriteAllBytes(path,new byte[]{1,2});
        using var gateway=new HttpClient(new Stub(r=>{Assert.Equal(AholoRegions.Get(region).ApiBase.Host,r.RequestUri!.Host);Assert.True(r.Headers.Contains("Authorization"));return JsonSerializer.Serialize(new{ousToken="fixture-upload",globalDomain="https://"+host,blockSize=multipart?1:4});}));
        int count=0;using var upload=new HttpClient(new Stub(r=>
        {
            count++;Assert.Equal(host,r.RequestUri!.Host);Assert.False(r.Headers.Contains("Authorization"));Assert.Equal("fixture-upload",r.Headers.GetValues("ous-token-v2").Single());
            if(r.RequestUri.AbsolutePath.EndsWith("/init")){Assert.Null(r.Content);Assert.Contains("blocks=2",r.RequestUri.Query);return "{\"c\":0,\"d\":{\"taskId\":1,\"deduplicated\":true}}";}
            if(r.RequestUri.AbsolutePath.EndsWith("/upload"))return "{\"c\":0,\"d\":{\"taskId\":\"1\"}}";
            return "{\"c\":0,\"d\":{\"status\":5,\"url\":\"https://example.com/input.bin\"}}";
        }));
        Assert.Equal("https://example.com/input.bin",await new AholoApiClient(gateway,upload).UploadAsync(new("fixture",region),path));Assert.Equal(2,count);
    }

    [Theory][InlineData("international","ous-cos.kujiale.com")][InlineData("china","ous-sg.kujiale.com")]
    public async Task OppositeUploadHostIsRejectedBeforeSendingFile(string region,string host)
    {
        Directory.CreateDirectory(root);string path=Path.Combine(root,"input.bin");File.WriteAllBytes(path,new byte[]{1});
        using var gateway=new HttpClient(new Stub(_=>JsonSerializer.Serialize(new{ousToken="fixture-upload",globalDomain="https://"+host,blockSize=4})));
        using var upload=new HttpClient(new Stub(_=>throw new Exception("No file may leave")));
        var e=await Assert.ThrowsAsync<ProviderException>(()=>new AholoApiClient(gateway,upload).UploadAsync(new("fixture",region),path));Assert.Equal("invalid_upload_origin",e.Code);
    }

    [Fact]public async Task CollidingWorldIdsKeepOriginalTaskRegionAndRejectMismatchedRecords()
    {
        int count=0;using var http=new HttpClient(new Stub(_=>{count++;return "{\"worldId\":\"same\",\"status\":\"RUNNING\"}";}));
        var store=new TaskStore(Path.Combine(root,"tasks"));var flow=new Workflows(new(http,http),store,new(http,store));
        var intl=new ApiKeyCredential("same-key");var cn=new ApiKeyCredential("same-key","china");
        var a=await flow.GenerateAsync(intl,new("A","room"),default);var b=await flow.GenerateAsync(cn,new("B","room"),default);
        Assert.NotEqual(a.Id,b.Id);Assert.Equal("china",b.Region);
        Assert.Equal(a.Id,(await flow.ImportWorldAsync(intl,"same",default)).Id);Assert.Equal(b.Id,(await flow.RestoreRemoteAsync(cn,new(TaskService.World,"same"),default)).Id);
        int sent=count;await Assert.ThrowsAsync<InvalidOperationException>(()=>flow.RefreshAsync(intl,b.Id,default));
        store.Save(b with{Region="international"});await Assert.ThrowsAsync<InvalidOperationException>(()=>flow.RefreshAsync(cn,b.Id,default));Assert.Equal(sent,count);
    }

    [Theory][InlineData("international","https://api.aholo.ai","https://www.aholo.ai")][InlineData("china","https://api.aholo3d.cn","https://www.aholo3d.cn")]
    public async Task OAuthDiscoveryAndRefreshStayWithTheirIssuer(string region,string resource,string issuer)
    {
        int count=0;using var http=new HttpClient(new Stub(r=>
        {
            count++;Assert.False(r.Headers.Contains("Authorization"));
            if(r.RequestUri!.AbsolutePath.EndsWith("oauth-protected-resource")){Assert.Equal(resource+"/.well-known/oauth-protected-resource",r.RequestUri.AbsoluteUri);return JsonSerializer.Serialize(new{resource,authorization_servers=new[]{issuer}});}
            if(r.RequestUri.AbsolutePath.EndsWith("oauth-authorization-server")){Assert.Equal(issuer+"/.well-known/oauth-authorization-server",r.RequestUri.AbsoluteUri);return JsonSerializer.Serialize(new{issuer,authorization_endpoint=issuer+"/oauth/authorize",token_endpoint=issuer+"/oauth/token",registration_endpoint=issuer+"/oauth/register",scopes_supported=new[]{"get_user_info"},grant_types_supported=new[]{"authorization_code","refresh_token"},code_challenge_methods_supported=new[]{"S256"},token_endpoint_auth_methods_supported=new[]{"none"}});}
            Assert.Equal(issuer+"/oauth/token",r.RequestUri.AbsoluteUri);
            Assert.Contains(Uri.EscapeDataString(resource),r.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return "{\"access_token\":\"new-fixture\",\"token_type\":\"Bearer\",\"expires_in\":3600}";
        }));
        var service=new ManycoreOAuthService(http,region);var refreshed=await service.RefreshAsync(Token(region));Assert.Equal(region,refreshed.Region);Assert.Equal(3,count);
        await Assert.ThrowsAsync<ProviderException>(()=>service.RefreshAsync(Token(region=="china"?"international":"china")));Assert.Equal(3,count);
    }

    [Fact]public async Task RememberedOAuthSlotsAndLongEscapedTokensRestoreIndependently()
    {
        OAuthSessionCoordinator Account(string region)=>new(vault,Path.Combine(root,"oauth"),region);
        var intl=Account("international");var cn=Account("china");
        var a=await intl.BeginAsync(null,false,true,default);var b=await cn.BeginAsync(null,false,true,default);
        await intl.CompleteAsync(a,Token("international"),default);
        string escaped=new('&',32768);await cn.CompleteAsync(b,Token("china",escaped),default);
        Assert.Equal(escaped,(await Account("china").ReadAsync()).Token!.AccessToken);Assert.NotNull((await Account("international").ReadAsync()).Token);
        await cn.SignOutAsync();Assert.NotNull((await Account("international").ReadAsync()).Token);Assert.Null((await Account("china").ReadAsync()).Token);
        var lease=await cn.BeginAsync(null,false,true,default);await Assert.ThrowsAsync<ProviderException>(()=>cn.CompleteAsync(lease,Token("international"),default));
    }

    [Fact]public async Task RegionSwitchCancellationRejectsLateOAuthAndPreservesOtherSavedAccount()
    {
        var calls=new Dictionary<string,Auth>();var managers=new Dictionary<string,AccountSessionManager>();
        using var router=new RegionalAccounts(region=>
        {var auth=new Auth();calls[region]=auth;return managers[region]=new(auth,_=>{},persistence:new(vault,Path.Combine(root,"oauth"),region));});
        await router.RunAsync("china","begin",Guid.NewGuid(),true,default);await calls["china"].Started.Task.WaitAsync(TimeSpan.FromSeconds(3));calls["china"].Reply.SetResult(Token("china"));await managers["china"].Completion;
        await router.RunAsync("international","begin",Guid.NewGuid(),true,default);await calls["international"].Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await router.CancelPendingAsync(default);calls["international"].Reply.SetResult(Token("international"));await managers["international"].Completion;
        Assert.Equal("SignedOut",(await router.RunAsync("international","status",null,null,default)).Stage);
        var saved=await router.RunAsync("china","status",null,null,default);Assert.Equal("china",saved.Region);Assert.True(saved.Remembered);
        Assert.False(vault.Values.ContainsKey("international-oauth-session"));Assert.True(vault.Values.ContainsKey("china-oauth-session"));
    }

    [Fact]public async Task CanceledAttemptCannotStartLaterInTheSameRegion()
    {
        var auth=new Auth();using var router=new RegionalAccounts(_=>new(auth,_=>{}));var id=Guid.NewGuid();
        await router.RunAsync("china","cancel",id,null,default);
        var result=await router.RunAsync("china","begin",id,true,default);
        Assert.Equal("SignedOut",result.Stage);Assert.False(auth.Started.Task.IsCompleted);
    }
    [Fact]public async Task CancelBeforeQueuedBeginDoesNotStartAnySidecar()
    {
        await using var client=new AholoClient(root);var id=Guid.NewGuid();
        var gate=(SemaphoreSlim)typeof(AholoClient).GetField("_gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(client)!;
        await gate.WaitAsync();var pending=client.AccountAsync("begin",id,true,"china");
        try{await client.AccountAsync("cancel",id,region:"china").WaitAsync(TimeSpan.FromSeconds(2));}
        finally{gate.Release();}
        await Assert.ThrowsAsync<InvalidOperationException>(()=>pending);
        Assert.Null(typeof(AholoClient).GetField("_process",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(client));
    }
    [Fact]public async Task AccountCancellationUsesRealPrivatePipeWhileForegroundGateIsHeld()
    {
        string name="aholo-rt-"+Guid.NewGuid().ToString("N"),nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var auth=new Auth();AccountSessionManager? cn=null;
        using var router=new RegionalAccounts(region=>{var manager=new AccountSessionManager(auth,_=>{},persistence:new(vault,Path.Combine(root,"oauth"),region));if(region=="china")cn=manager;return manager;});
        var preview=new RealtimePreviewManager(Credentials(),new(root),(_,_)=>throw new Exception("No cloud calls"));
        await using var server=new RealtimeControlServer(name,nonce,preview,(region,id,ct)=>router.RunAsync(region,"cancel",id,null,ct));server.Start();
        await using var client=new AholoClient(root);
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var endpointType=typeof(AholoClient).GetNestedType("ControlEndpoint",System.Reflection.BindingFlags.NonPublic)!;
        typeof(AholoClient).GetField("realtimeEndpoint",flags)!.SetValue(client,Activator.CreateInstance(endpointType,name,nonce));
        var gate=(SemaphoreSlim)typeof(AholoClient).GetField("_gate",flags)!.GetValue(client)!;
        var id=Guid.NewGuid();await router.RunAsync("china","begin",id,true,default);await auth.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await gate.WaitAsync();
        try
        {
            var canceled=await client.AccountAsync("cancel",id,region:"china").WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal("china",canceled.Region);Assert.Equal("SignedOut",canceled.Stage);
            auth.Reply.SetResult(Token("china"));await cn!.Completion.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(vault.Values.ContainsKey("china-oauth-session"));
        }
        finally{gate.Release();await preview.ShutdownAsync(default);}
    }

    private sealed class Auth:IAccountAuthorization
    {
        public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OAuthToken> Reply=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OAuthToken> LoginAsync(Action<Uri> browser,CancellationToken ct=default){Started.TrySetResult();return Reply.Task;}
        public Task<OAuthToken> RefreshAsync(OAuthToken previous,CancellationToken ct=default)=>Reply.Task;
    }
    private sealed class Stub(Func<HttpRequestMessage,string> reply):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(reply(request))});}
    private sealed class Vault:ICredentialStore
    {
        public ConcurrentDictionary<string,string> Values=new();public bool FailRead;
        public Task<string?> GetAsync(string account,CancellationToken ct=default)=>FailRead?Task.FromException<string?>(new IOException("fixture")):Task.FromResult(Values.GetValueOrDefault(account));
        public Task SetAsync(string account,string secret,CancellationToken ct=default){Values[account]=secret;return Task.CompletedTask;}
        public Task DeleteAsync(string account,CancellationToken ct=default){Values.TryRemove(account,out _);return Task.CompletedTask;}
    }
}
