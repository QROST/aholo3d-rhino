using System.Net;
using System.Text;
using System.Text.Json;
using Aholo.Provider;
using Xunit;

namespace Aholo.Tests;

public sealed class OAuthSafetyTests
{
    private const string Metadata = "{\"issuer\":\"https://www.aholo.ai\",\"authorization_endpoint\":\"https://www.aholo.ai/oauth/authorize\",\"token_endpoint\":\"https://www.aholo.ai/oauth/token\",\"registration_endpoint\":\"https://www.aholo.ai/oauth/register\",\"scopes_supported\":[\"get_user_info\"],\"grant_types_supported\":[\"authorization_code\",\"refresh_token\"],\"code_challenge_methods_supported\":[\"S256\"],\"token_endpoint_auth_methods_supported\":[\"none\"]}";
    [Theory]
    [InlineData("http://127.0.0.1:34567/callback?state={state}&code=%GG")]
    [InlineData("http://127.0.0.1:34567/callback?state={state}&code=%")]
    [InlineData("http://127.0.0.1:34567/callback?state={state}&code=%0d%0a")]
    [InlineData("http://127.0.0.1:34567/path/../callback?state={state}&code=valid")]
    [InlineData("http://127.0.0.1:34567/callback/extra?state={state}&code=valid")]
    [InlineData("http://127.0.0.1:34567/%63allback?state={state}&code=valid")]
    public void CallbackRejectsMalformedEncodingAndPath(string target)
    {
        var attempt = OAuthAttempt.Create(new Uri("http://127.0.0.1:34567/callback"));
        Assert.Throws<ProviderException>(() => attempt.ValidateCallback(new Uri(target.Replace("{state}", attempt.State))));
    }
    [Theory]
    [InlineData("token_type", "Basic")]
    [InlineData("scope", "get_user_info other_scope")]
    [InlineData("scope", 42)]
    [InlineData("expires_in", "not-number")]
    [InlineData("refresh_token", "")]
    [InlineData("refresh_token", "contains\ncontrol")]
    public async Task RefreshRejectsMalformedTokenFields(string field, object replacement)
    {
        var response = new Dictionary<string, object> { ["access_token"] = "new-access", ["token_type"] = "Bearer", ["expires_in"] = 3600, ["scope"] = "get_user_info", ["refresh_token"] = "new-refresh" };
        response[field] = replacement;
        var service = new ManycoreOAuthService(new HttpClient(Handler(JsonSerializer.Serialize(response))));
        await Assert.ThrowsAsync<ProviderException>(() => service.RefreshAsync(Token()));
    }
    [Fact]
    public async Task RefreshRejectsOversizedRefreshAndKeepsOldTokenWhenRotationOmitted()
    {
        var invalid = new ManycoreOAuthService(new HttpClient(Handler(JsonSerializer.Serialize(new { access_token = "new-access", token_type = "Bearer", refresh_token = new string('x', 32769) }))));
        await Assert.ThrowsAsync<ProviderException>(() => invalid.RefreshAsync(Token()));
        var valid = new ManycoreOAuthService(new HttpClient(Handler("{\"access_token\":\"new-access\",\"token_type\":\"Bearer\",\"expires_in\":3600}")));
        var previous = Token(); var next = await valid.RefreshAsync(previous);
        Assert.Equal(previous.RefreshToken, next.RefreshToken); Assert.Equal(previous.ClientId, next.ClientId); Assert.Equal("get_user_info", next.Scope);
    }
    private static OAuthHttp.Stub Handler(string tokenResponse) => new(request => Task.FromResult(OAuthHttp.Json(request.RequestUri!.Host == "api.aholo.ai"
        ? "{\"resource\":\"https://api.aholo.ai\",\"authorization_servers\":[\"https://www.aholo.ai\"]}"
        : request.RequestUri.AbsolutePath.EndsWith("oauth-authorization-server") ? Metadata : tokenResponse)));
    internal static OAuthToken Token() => new() { AccessToken = "fake-access", RefreshToken = "fake-refresh", ClientId = "fake-public-client", Scope = "get_user_info", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
}

internal static class OAuthHttp
{
    public static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json,Encoding.UTF8,"application/json") };
    public sealed class Stub(Func<HttpRequestMessage,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Count { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) { Count++; return send(request); }
    }
}
