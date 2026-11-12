using System.Net;
using System.Text;
using System.Text.Json;
using Aholo3D.Provider;

namespace Aholo3D.Tests;

public sealed class ProviderTests
{
    private static readonly ApiKeyCredential Key = new("private-api-key");

    [Fact]
    public void CredentialIdentityIsStableAndSecretIsNotPrinted()
    {
        Assert.Equal(Key.ConnectionId, new ApiKeyCredential("private-api-key").ConnectionId);
        Assert.NotEqual(Key.ConnectionId, new ApiKeyCredential("another-key").ConnectionId);
        Assert.DoesNotContain("private-api-key", Key.ToString());
        Assert.DoesNotContain("private-api-key", Key.ConnectionId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bearer secret")]
    [InlineData("key\nvalue")]
    [InlineData(" key ")]
    public void InvalidCredentialsAreRejected(string value) => Assert.Throws<ArgumentException>(() => new ApiKeyCredential(value));

    [Fact]
    public async Task ValidationUsesWorldListAndRawAuthorization()
    {
        var api = new FakeHandler("{\"result\":[]}");
        await Client(api).ValidateKeyAsync(Key);
        var request = Assert.Single(api.Requests);
        Assert.Equal("https://api.aholo.ai/global/world/v1/list", request.Url);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("private-api-key", request.Authorization);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(0, body.RootElement.GetProperty("pageNum").GetInt32());
        Assert.Equal(20, body.RootElement.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task WorldGenerationUsesImageResourceAndReturnsStringId()
    {
        var api = new FakeHandler("{\"worldId\":\"A1b2C3\"}");
        Assert.Equal("A1b2C3", await Client(api).GenerateWorldAsync(Key, "Room", "Sunny room", "https://assets.example.com/room.png"));
        var request = Assert.Single(api.Requests);
        Assert.EndsWith("/world/v1/generations", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("Sunny room", body.RootElement.GetProperty("prompt").GetString());
        Assert.Equal("image", body.RootElement.GetProperty("resources")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task WorldDetailsParseSplatUrlsAndAxis()
    {
        var api = new FakeHandler("""{"worldId":"World123","name":"Room","status":"SUCCEEDED","progress":1,"assets":{"splats":{"urls":{"plyPath":"https://assets.example.com/world.ply","spzPath":"https://assets.example.com/world.spz"}},"imagery":{"panoUrl":"https://assets.example.com/pano.jpg"},"semanticsMetadata":{"upAxis":"Z"}}} """);
        var world = await Client(api).GetWorldAsync(Key, "World123");
        Assert.Equal("Z", world.UpAxis);
        Assert.Equal("SUCCEEDED", world.Status);
        Assert.Equal(1, world.Progress);
        Assert.EndsWith("world.ply", world.PlyUrl);
        Assert.EndsWith("world.spz", world.SpzUrl);
        Assert.EndsWith("pano.jpg", world.ImageUrl);
    }

    [Fact]
    public async Task WorldIdentityMismatchIsRejected()
    {
        var api = new FakeHandler("{\"worldId\":\"other-world\",\"status\":\"RUNNING\"}");
        Assert.Equal("invalid_response", (await Assert.ThrowsAsync<ProviderException>(() => Client(api).GetWorldAsync(Key, "expected-world"))).Code);
    }

    [Fact]
    public async Task WorldListParsesNullableAssets()
    {
        var api = new FakeHandler("{\"result\":[{\"worldId\":\"world\",\"status\":\"PENDING\",\"progress\":null,\"assets\":null}]}");
        var result = Assert.Single(await Client(api).ListWorldsAsync(Key, 2));
        Assert.Null(result.PlyUrl);
        Assert.Null(result.Progress);
        Assert.Contains("\"pageNum\":2", Assert.Single(api.Requests).Body);
    }

    [Fact]
    public async Task MeshTaskIdsNeverGoThroughDouble()
    {
        const string id = "9223372036854775806";
        var api = new FakeHandler("{\"success\":true,\"data\":" + id + "}", "{\"success\":true,\"data\":{\"taskId\":" + id + ",\"status\":3,\"outputs\":[{\"content\":\"MESH123\"}]}}");
        var client = Client(api);
        Assert.Equal(id, await client.CreateMeshAsync(Key, "https://assets.example.com/scene.glb"));
        var mesh = await client.GetMeshAsync(Key, id);
        Assert.Equal(id, mesh.TaskId);
        Assert.Equal("MESH123", mesh.MeshId);
        Assert.EndsWith("?taskid=" + id, api.Requests[1].Url);
        Assert.Contains("\"fastUpload\":true", api.Requests[0].Body);
    }

    [Fact]
    public async Task MalformedMeshDetailsAreRejectedWithoutExposingData()
    {
        var api = new FakeHandler("{\"success\":true,\"data\":123}");
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(api).GetMeshAsync(Key, "123"));
        Assert.Equal("invalid_response", error.Code);
    }

    [Fact]
    public async Task RenderSubmitBase64EncodesUsdaAndPollOnlyContainsOperationId()
    {
        var api = new FakeHandler("{\"operationId\":\"op/one\",\"done\":false,\"result\":null,\"error\":null}", "{\"operationId\":\"op/one\",\"done\":true,\"result\":{\"resultUrl\":\"https://assets.example.com/render.png\"},\"error\":null}");
        var client = Client(api);
        const string usda = "#usda 1.0\n# 场景";
        Assert.False((await client.SubmitRenderAsync(Key, "trace-123", usda)).Done);
        var completed = await client.GetRenderAsync(Key, "op/one");
        Assert.True(completed.Done);
        Assert.EndsWith("render.png", completed.ResultUrl);
        using var submit = JsonDocument.Parse(api.Requests[0].Body!);
        Assert.Equal(usda, Encoding.UTF8.GetString(Convert.FromBase64String(submit.RootElement.GetProperty("usdContent").GetString()!)));
        Assert.False(submit.RootElement.TryGetProperty("operationId", out _));
        using var poll = JsonDocument.Parse(api.Requests[1].Body!);
        Assert.Single(poll.RootElement.EnumerateObject());
        Assert.Equal("op/one", poll.RootElement.GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task RenderFailureDoesNotReflectServerErrorText()
    {
        var api = new FakeHandler("{\"operationId\":\"operation\",\"done\":true,\"error\":{\"message\":\"private-api-key signed-secret-url\"},\"result\":null}");
        var result = await Client(api).GetRenderAsync(Key, "operation");
        Assert.NotNull(result.Error);
        Assert.DoesNotContain("private-api-key", result.Error);
        Assert.DoesNotContain("signed-secret-url", result.Error);
    }

    [Fact]
    public async Task RenderPollRejectsIdentityMismatch()
    {
        var api = new FakeHandler("{\"operationId\":\"different\",\"done\":false}");
        await Assert.ThrowsAsync<ProviderException>(() => Client(api).GetRenderAsync(Key, "expected"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"worldId\":123}")]
    public async Task MalformedCreateResponsesAreUncertainAndNotRetried(string response)
    {
        var api = new FakeHandler(response);
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(api).GenerateWorldAsync(Key, "Room", "A room", null));
        Assert.True(error.SubmissionMayHaveSucceeded);
        Assert.Equal("submission_unknown", error.Code);
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task MissingRenderOperationIsUncertain()
    {
        var api = new FakeHandler("{\"done\":true,\"result\":null}");
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(api).SubmitRenderAsync(Key, "trace", "#usda 1.0"));
        Assert.True(error.SubmissionMayHaveSucceeded);
        Assert.Single(api.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    public async Task HttpErrorsAreRedactedAndClassified(HttpStatusCode status, bool uncertain)
    {
        var api = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("private-api-key signed-secret-url") }));
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(api).GenerateWorldAsync(Key, "Room", "A room", null));
        Assert.Equal(uncertain, error.SubmissionMayHaveSucceeded);
        Assert.DoesNotContain("private-api-key", error.ToString());
        Assert.DoesNotContain("signed-secret-url", error.ToString());
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task TransportFailureAfterCreateIsUncertain()
    {
        var api = new FakeHandler(_ => throw new HttpRequestException("private-api-key"));
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(api).GenerateWorldAsync(Key, "Room", "A room", null));
        Assert.True(error.SubmissionMayHaveSucceeded);
        Assert.DoesNotContain("private-api-key", error.ToString());
        Assert.Single(api.Requests);
    }

    [Fact]
    public async Task PreCancelledCreateIsNeverSent()
    {
        var api = new FakeHandler("{}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(api).GenerateWorldAsync(Key, "Room", "A room", null, cancellation.Token));
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task ExplicitMeshRejectionIsNotUncertain()
    {
        var api = new FakeHandler("{\"success\":false,\"errorMsg\":\"private-api-key\",\"data\":null}");
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(api).CreateMeshAsync(Key, "https://assets.example.com/model.glb"));
        Assert.False(error.SubmissionMayHaveSucceeded);
        Assert.Equal("business_rejected", error.Code);
        Assert.DoesNotContain("private-api-key", error.ToString());
    }

    [Fact]
    public async Task UploadSingleUsesDedicatedTokenWithoutAuthorization()
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "sample image");
            var api = new FakeHandler("{\"ousToken\":\"private-upload-token\",\"globalDomain\":\"https://ous-sg.kujiale.com\",\"blockSize\":1048576}");
            var upload = new FakeHandler("{\"c\":\"0\",\"d\":{\"taskId\":\"upload\"}}", "{\"c\":\"0\",\"d\":{\"status\":5,\"url\":\"https://assets.example.com/image.png\"}}");
            var url = await Client(api, upload).UploadAsync(Key, file);
            Assert.Equal("https://assets.example.com/image.png", url);
            Assert.EndsWith("/asset/v1/token", Assert.Single(api.Requests).Url);
            Assert.All(upload.Requests, r => { Assert.Null(r.Authorization); Assert.Equal("private-upload-token", r.UploadToken); Assert.DoesNotContain("/global/", r.Url); });
            Assert.Contains("sample image", upload.Requests[0].Body);
            Assert.Contains("name=md5", upload.Requests[0].Body);
            Assert.Equal(HttpMethod.Get, upload.Requests[1].Method);
            Assert.Null(upload.Requests[1].Body);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task MultipartInitUsesQueryAndPartsAreOneBased()
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "abcdef");
            var api = new FakeHandler("{\"ousToken\":\"upload-token\",\"globalDomain\":\"https://ous-sg.kujiale.com\",\"blockSize\":4}");
            var upload = new FakeHandler("{\"c\":\"0\",\"d\":{\"taskId\":12,\"deduplicated\":false}}", "{\"c\":\"0\",\"d\":null}", "{\"c\":\"0\",\"d\":null}", "{\"c\":\"0\",\"d\":{\"status\":5,\"url\":\"https://assets.example.com/mesh.glb\"}}");
            await Client(api, upload).UploadAsync(Key, file);
            var init = upload.Requests[0];
            Assert.Equal(HttpMethod.Post, init.Method);
            Assert.Null(init.Body);
            Assert.Contains("/block/upload/init?md5=", init.Url);
            Assert.Contains("&blocks=2&size=6&name=", init.Url);
            Assert.Contains("\r\n\r\n1\r\n", upload.Requests[1].Body);
            Assert.Contains("abcd", upload.Requests[1].Body);
            Assert.Contains("\r\n\r\n2\r\n", upload.Requests[2].Body);
            Assert.Contains("ef", upload.Requests[2].Body);
            Assert.All(upload.Requests, r => Assert.Null(r.Authorization));
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("https://ous-sg.kujiale.com.evil.example.com")]
    [InlineData("http://ous-sg.kujiale.com")]
    [InlineData("https://ous-sg.kujiale.com/path")]
    public async Task UntrustedUploadOriginsNeverReceiveCredentials(string origin)
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "data");
            var api = new FakeHandler(JsonSerializer.Serialize(new { ousToken = "upload-secret", globalDomain = origin, blockSize = 100 }));
            var upload = new FakeHandler();
            await Assert.ThrowsAsync<ProviderException>(() => Client(api, upload).UploadAsync(Key, file));
            Assert.Empty(upload.Requests);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void UploadClientWithDefaultApiAuthorizationIsRejected()
    {
        using var upload = new HttpClient(new FakeHandler());
        upload.DefaultRequestHeaders.Add("Authorization", "must-not-leak");
        Assert.Throws<ArgumentException>(() => new Aholo3DApiClient(new HttpClient(new FakeHandler()), upload));
    }

    private static Aholo3DApiClient Client(FakeHandler api, FakeHandler? upload = null) => new(new HttpClient(api), new HttpClient(upload ?? new FakeHandler()));
    private sealed record Request(HttpMethod Method, string Url, string? Body, string? Authorization, string? UploadToken);
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public List<Request> Requests { get; } = [];
        public FakeHandler(params string[] bodies)
        {
            var queue = new Queue<string>(bodies);
            _send = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(queue.Dequeue(), Encoding.UTF8, "application/json") });
        }
        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new(request.Method, request.RequestUri!.AbsoluteUri, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken), Header(request, "Authorization"), Header(request, "ous-token-v2")));
            return await _send(request);
        }
        private static string? Header(HttpRequestMessage request, string name) => request.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
    }
}
