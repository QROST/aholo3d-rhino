using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Aholo3D.Provider;
namespace Aholo3D.Tests;

public sealed class OAuthTests
{
    [Fact]
    public void PkceUsesS256AndRejectsStateAndReplay()
    {
        var attempt = OAuthAttempt.Create(new Uri("http://127.0.0.1:34567/callback"));
        string expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(attempt.Verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(expected, attempt.Challenge); Assert.InRange(attempt.Verifier.Length, 43, 128);
        Assert.Throws<ProviderException>(() => attempt.ValidateCallback(new Uri("http://127.0.0.1:34567/callback?state=wrong&code=fake")));
        var callback = new Uri("http://127.0.0.1:34567/callback?state=" + attempt.State + "&code=fake");
        Assert.Equal("fake", attempt.ValidateCallback(callback));
        Assert.Throws<ProviderException>(() => attempt.ValidateCallback(callback));
    }
    [Fact]
    public void CallbackRejectsWrongOriginDuplicateParametersAndDenial()
    {
        var a = OAuthAttempt.Create(new Uri("http://127.0.0.1:34567/callback"));
        Assert.Throws<ProviderException>(() => a.ValidateCallback(new Uri("https://evil.example/callback?state=" + a.State + "&code=fake")));
        Assert.Throws<ProviderException>(() => a.ValidateCallback(new Uri("http://127.0.0.1:34567/callback?state=" + a.State + "&state=" + a.State + "&code=fake")));
        Assert.Throws<ProviderException>(() => a.ValidateCallback(new Uri("http://127.0.0.1:34567/callback?state=" + a.State + "&error=access_denied")));
    }
    [Fact]
    public async Task DiscoveryRejectsEndpointOutsideVerifiedIssuer()
    {
        var handler = new OAuthHttp.Stub(r => Task.FromResult(OAuthHttp.Json(r.RequestUri!.Host == "api.aholo.ai"
            ? "{\"resource\":\"https://api.aholo.ai\",\"authorization_servers\":[\"https://www.aholo.ai\"]}"
            : "{\"issuer\":\"https://www.aholo.ai\",\"authorization_endpoint\":\"https://evil.example/authorize\",\"token_endpoint\":\"https://www.aholo.ai/oauth/token\",\"registration_endpoint\":\"https://www.aholo.ai/oauth/register\",\"scopes_supported\":[\"get_user_info\"],\"grant_types_supported\":[\"authorization_code\"],\"code_challenge_methods_supported\":[\"S256\"],\"token_endpoint_auth_methods_supported\":[\"none\"]}")));
        await Assert.ThrowsAsync<ProviderException>(() => new ManycoreOAuthService(new HttpClient(handler)).DiscoverAsync());
    }
    [Theory][InlineData("international")][InlineData("china")]
    public async Task FullLoginRegistersPkceAndCompletesLoopbackWithoutLeakingApiKey(string region)
    {
        HttpResponseMessage Json(string text)=>OAuthHttp.Json(region=="china"?text.Replace("aholo.ai","aholo3d.cn"):text);
        Uri? registeredRedirect = null;
        string? challenge = null;
        Task? callback = null;
        var handler = new OAuthHttp.Stub(async request =>
        {
            Assert.False(request.Headers.Contains("Authorization"));
            Assert.Contains(request.RequestUri!.Host,region=="china"?new[]{"api.aholo3d.cn","www.aholo3d.cn"}:new[]{"api.aholo.ai","www.aholo.ai"});
            if (request.RequestUri!.AbsolutePath.EndsWith("oauth-protected-resource"))
                return Json("{\"resource\":\"https://api.aholo.ai\",\"authorization_servers\":[\"https://www.aholo.ai\"]}");
            if (request.RequestUri.AbsolutePath.EndsWith("oauth-authorization-server"))
                return Json("{\"issuer\":\"https://www.aholo.ai\",\"authorization_endpoint\":\"https://www.aholo.ai/oauth/authorize\",\"token_endpoint\":\"https://www.aholo.ai/oauth/token\",\"registration_endpoint\":\"https://www.aholo.ai/oauth/register\",\"scopes_supported\":[\"get_user_info\"],\"grant_types_supported\":[\"authorization_code\",\"refresh_token\"],\"code_challenge_methods_supported\":[\"S256\"],\"token_endpoint_auth_methods_supported\":[\"none\"]}");
            if (request.RequestUri.AbsolutePath.EndsWith("/register"))
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement;
                registeredRedirect = new Uri(body.GetProperty("redirect_uris")[0].GetString()!);
                Assert.Equal("127.0.0.1", registeredRedirect.Host);
                Assert.Equal("none", body.GetProperty("token_endpoint_auth_method").GetString());
                return Json("{\"client_id\":\"public-test-client\",\"token_endpoint_auth_method\":\"none\"}");
            }
            var fields = Query(await request.Content!.ReadAsStringAsync());
            Assert.Equal("test-code", fields["code"]);
            Assert.Equal(registeredRedirect!.AbsoluteUri, fields["redirect_uri"]);
            Assert.Equal(challenge, Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(fields["code_verifier"]))).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
            return Json("{\"access_token\":\"fake-access\",\"refresh_token\":\"fake-refresh\",\"token_type\":\"Bearer\",\"expires_in\":3600,\"scope\":\"get_user_info\"}");
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = await new ManycoreOAuthService(new HttpClient(handler),region).LoginAsync( url =>
        {
            var fields = Query(url.Query.TrimStart('?')); challenge = fields["code_challenge"];
            Assert.Equal("S256", fields["code_challenge_method"]);
            callback = Task.Run(async () =>
            {
                using var local = new HttpClient();
                using var unrelated = await local.GetAsync(registeredRedirect + "?state=unrelated&code=wrong", timeout.Token);
                Assert.Equal(HttpStatusCode.BadRequest, unrelated.StatusCode);
                using var response = await local.GetAsync(registeredRedirect + "?state=" + fields["state"] + "&code=test-code", timeout.Token);
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }, timeout.Token);
        }, timeout.Token);
        await callback!;
        Assert.Equal(region,token.Region);Assert.Equal("fake-access", token.AccessToken); Assert.Equal(4, handler.Count);
    }
    private static Dictionary<string, string> Query(string text) => text.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    [Fact]
    public void TokenRoundTripKeepsStableIdentityAcrossRefreshAndRedactsToString()
    {
        var one = new OAuthToken { AccessToken = "fake-access-one", ClientId = "fake-client", RefreshToken = "fake-refresh", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
        var two = JsonSerializer.Deserialize<OAuthToken>(JsonSerializer.Serialize(one))!;
        Assert.Equal(one.AccessToken, two.AccessToken); Assert.DoesNotContain(one.AccessToken, one.ToString());
        Assert.Equal(one.ClientId, new OAuthToken { AccessToken = "fake-access-two", ClientId = one.ClientId }.ClientId);
    }
}
