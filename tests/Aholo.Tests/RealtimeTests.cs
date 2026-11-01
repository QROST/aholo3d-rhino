using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Aholo.Provider;

namespace Aholo.Tests;

public sealed class RealtimeTests
{
    private static readonly ApiKeyCredential Key = new("gateway-private-key");
    private const string Pending = """{"operationId":"session-1","metaData":null,"done":false,"result":null,"error":null}""";
    private const string Active = """{"operationId":"session-1","metaData":null,"done":true,"result":{"sessionId":"session-1","status":"ACTIVE","currentQueuePosition":0,"host":"stream.example.com","nodeId":"node-1","streamId":"stream-1","token":"secret-stream-token"},"error":null}""";
    private static RealtimeConnection Connection() => new("stream.example.com", "node/with?symbols", "stream-1", "secret+token/&?#=value");

    [Fact]
    public async Task RestLifecycleUsesPublishedEnvelopesAndKeepsCredentialsOnGateway()
    {
        var handler = new FakeHandler(Pending, Active, """{"sessionId":"session-1","status":"ACTIVE"}""", """{"sessionId":"session-1","closed":true}""");
        var client = Client(handler);
        const string usda = "#usda 1.0\n# 中文";
        Assert.False((await client.CreateRealtimeAsync(Key, "create-once", usda)).Done);
        var active = await client.GetRealtimeAsync(Key, "session-1");
        Assert.Equal(RealtimeSessionStatus.Active, active.Status);
        Assert.NotNull(active.Connection);
        Assert.Equal(RealtimeSessionStatus.Active, (await client.PushRealtimeAsync(Key, "session-1", "patch-once", usda)).Status);
        Assert.True((await client.CloseRealtimeAsync(Key, "session-1")).Closed);
        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("gateway-private-key", r.Auth));
        Assert.All(handler.Requests, r => Assert.StartsWith("https://api.aholo.ai/global/rendercloud/v1/streams", r.Uri));
        using var create = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("create-once", create.RootElement.GetProperty("requestId").GetString());
        Assert.Equal(usda, Encoding.UTF8.GetString(Convert.FromBase64String(create.RootElement.GetProperty("usdContent").GetString()!)));
        Assert.False(create.RootElement.GetProperty("settings").GetProperty("autoEnhanceAI").GetBoolean());
        Assert.False(create.RootElement.TryGetProperty("operationId", out _));
        using var poll = JsonDocument.Parse(handler.Requests[1].Body!);
        Assert.Single(poll.RootElement.EnumerateObject());
        Assert.Equal("session-1", poll.RootElement.GetProperty("operationId").GetString());
        Assert.EndsWith("/session-1:push", handler.Requests[2].Uri);
        Assert.Equal(HttpMethod.Delete, handler.Requests[3].Method);
        Assert.Null(handler.Requests[3].Body);
    }

    [Fact]
    public async Task AlreadyClosedResponseIsSuccessfulCleanup()
    {
        var handler = new FakeHandler("""{"sessionId":"session-1","closed":false}""");
        Assert.False((await Client(handler).CloseRealtimeAsync(Key, "session-1")).Closed);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"operationId\":\"session-1\",\"done\":true,\"error\":null,\"result\":null}")]
    [InlineData("{\"operationId\":\"session-1\",\"done\":false,\"error\":{},\"result\":null}")]
    public async Task MalformedCreateIsUnknownAndNeverRetried(string json)
    {
        var handler = new FakeHandler(json);
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(handler).CreateRealtimeAsync(Key, "idempotency-1", "#usda 1.0"));
        Assert.True(error.SubmissionMayHaveSucceeded);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.TooManyRequests, false)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    public async Task CreateFailuresAreClassifiedAndRedacted(HttpStatusCode status, bool unknown)
    {
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("gateway-private-key secret-stream-token") }));
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(handler).CreateRealtimeAsync(Key, "idempotency-1", "#usda 1.0"));
        Assert.Equal(unknown, error.SubmissionMayHaveSucceeded);
        Assert.DoesNotContain("private", error.ToString());
        Assert.DoesNotContain("secret-stream-token", error.ToString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ExplicitFailedOperationIsKnownAndDoesNotExposeProviderText()
    {
        var handler = new FakeHandler("""{"operationId":"session-1","done":true,"result":null,"error":{"message":"secret-stream-token"}}""");
        var op = await Client(handler).CreateRealtimeAsync(Key, "idempotency-1", "#usda 1.0");
        Assert.Equal(RealtimeSessionStatus.Failed, op.Status);
        Assert.True(op.Done);
        Assert.Null(op.Connection);
        Assert.DoesNotContain("secret-stream-token", op.ToString());
    }

    [Fact]
    public async Task PrecancelledCreateDoesNotSend()
    {
        var handler = new FakeHandler(Pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).CreateRealtimeAsync(Key, "id", "#usda 1.0", ct: new CancellationToken(true)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PollAndPushRejectDifferentSessionIdentity()
    {
        var poll = new FakeHandler(Active.Replace("session-1", "different"));
        await Assert.ThrowsAsync<ProviderException>(() => Client(poll).GetRealtimeAsync(Key, "session-1"));
        var push = new FakeHandler("""{"sessionId":"different","status":"ACTIVE"}""");
        var error = await Assert.ThrowsAsync<ProviderException>(() => Client(push).PushRealtimeAsync(Key, "session-1", "push-1", "#usda 1.0"));
        Assert.True(error.SubmissionMayHaveSucceeded);
    }

    [Fact]
    public async Task ActiveConnectionCannotCrossSessionAndRejectsUnsafeHost()
    {
        var mismatched = new FakeHandler(Active.Replace("\"sessionId\":\"session-1\"", "\"sessionId\":\"other\""));
        await Assert.ThrowsAsync<ProviderException>(() => Client(mismatched).GetRealtimeAsync(Key, "session-1"));
        var unsafeHost = new FakeHandler(Active.Replace("stream.example.com", "127.0.0.1"));
        await Assert.ThrowsAsync<ProviderException>(() => Client(unsafeHost).GetRealtimeAsync(Key, "session-1"));
    }

    [Fact]
    public async Task TokensNeverAppearInConnectionOrOperationSerializationAndDiagnostics()
    {
        var op = await Client(new FakeHandler(Active)).GetRealtimeAsync(Key, "session-1");
        Assert.DoesNotContain("secret-stream-token", JsonSerializer.Serialize(op));
        Assert.DoesNotContain("secret-stream-token", op.ToString());
        Assert.DoesNotContain("secret-stream-token", JsonSerializer.Serialize(op.Connection));
        Assert.DoesNotContain("secret-stream-token", op.Connection!.ToString());
    }

    [Fact]
    public void WebSocketUrlUsesOnlyEncodedStreamCredentialsAndFixedRoute()
    {
        var uri = Connection().CreateWebSocketUri();
        Assert.Equal("wss", uri.Scheme);
        Assert.Equal("/streaming/ws/session", uri.AbsolutePath);
        Assert.Equal("?nid=node%2Fwith%3Fsymbols&token=secret%2Btoken%2F%26%3F%23%3Dvalue", uri.Query);
        Assert.Empty(uri.UserInfo);
        Assert.Empty(uri.Fragment);
        Assert.DoesNotContain("gateway-private-key", uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://stream.example.com")]
    [InlineData("stream.example.com:443")]
    [InlineData("stream.example.com/path")]
    [InlineData("user@stream.example.com")]
    [InlineData("localhost")]
    [InlineData("x.local")]
    [InlineData("x.internal")]
    [InlineData("127.0.0.1")]
    [InlineData("2130706433")]
    [InlineData("[::1]")]
    [InlineData("stream.example.com.")]
    [InlineData("invalid..example.com")]
    public void UntrustedStreamHostsFailClosed(string host) => Assert.Throws<ArgumentException>(() => new RealtimeConnection(host, "node", "stream", "token"));

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("::ffff:192.168.1.1", false)]
    [InlineData("::1", false)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("2002:a00:1::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void ConnectionTimeAddressesMustBePublic(string address, bool expected) => Assert.Equal(expected, RealtimeNetwork.IsPublicAddress(IPAddress.Parse(address)));

    [Fact]
    public async Task FragmentedStatusAndJpegReassembleWithNoFabricatedCameraAssociation()
    {
        var transport = new FakeTransport();
        transport.Add("{\"code\":", false); transport.Add("\"0\"}", true);
        transport.Add([0xff, 0xd8, 0x01], false); transport.Add([0x02, 0xff, 0xd9], true);
        await using var stream = new RealtimeStream(transport);
        await stream.ConnectAsync(Connection());
        using var handshake = JsonDocument.Parse(Assert.Single(transport.Sent));
        Assert.Equal(3, handshake.RootElement.GetProperty("type").GetInt32());
        Assert.Equal("stream-1", handshake.RootElement.GetProperty("slaveInit").GetProperty("masterStreamingId").GetString());
        Assert.True(handshake.RootElement.GetProperty("slaveInit").GetProperty("receiveImage").GetBoolean());
        Assert.DoesNotContain("secret", transport.Sent[0]);
        Assert.Equal(RealtimeStatusCode.Initialized, Assert.IsType<RealtimeStatusMessage>(await stream.ReadNextAsync()).Code);
        var image = Assert.IsType<RealtimeJpegFrame>(await stream.ReadNextAsync());
        Assert.Equal(new byte[] { 0xff, 0xd8, 0x01, 0x02, 0xff, 0xd9 }, image.Bytes);
        Assert.False(image.HasCameraAssociation);
        Assert.False(RealtimeCapabilities.HasDepth);
        Assert.False(RealtimeCapabilities.HasAlpha);
    }

    [Theory]
    [InlineData("1000", RealtimeStatusCode.NodeOverloaded)]
    [InlineData("1001", RealtimeStatusCode.InitializationFailed)]
    [InlineData("1003", RealtimeStatusCode.SlaveLimitExceeded)]
    [InlineData("2001", RealtimeStatusCode.RenderError)]
    [InlineData("2002", RealtimeStatusCode.EngineError)]
    [InlineData("private-server-value", RealtimeStatusCode.Unknown)]
    public async Task StatusesDoNotReflectArbitraryServerText(string code, RealtimeStatusCode expected)
    {
        var transport = new FakeTransport();
        transport.Add(JsonSerializer.Serialize(new { code, message = "secret-provider-text" }), true);
        await using var stream = new RealtimeStream(transport);
        await stream.ConnectAsync(Connection());
        var result = Assert.IsType<RealtimeStatusMessage>(await stream.ReadNextAsync());
        Assert.Equal(expected, result.Code);
        Assert.DoesNotContain("secret", result.ToString());
        Assert.Equal(0, transport.CloseCount); // Render failure is not automatically a connection close.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MalformedOrOversizeWireFramesCloseTransport(bool oversize)
    {
        var transport = new FakeTransport();
        transport.Add(oversize ? [0xff, 0xd8, 0x00, 0xff, 0xd9] : [0x00, 0x00], true);
        await using var stream = new RealtimeStream(transport, 4);
        await stream.ConnectAsync(Connection());
        var error = await Assert.ThrowsAsync<ProviderException>(() => stream.ReadNextAsync());
        Assert.Equal(oversize ? "stream_message_too_large" : "invalid_stream_message", error.Code);
        Assert.Equal(1, transport.CloseCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task ReadCancellationClosesAndDisposesSocketExactlyOnce()
    {
        var transport = new FakeTransport();
        var stream = new RealtimeStream(transport);
        await stream.ConnectAsync(Connection());
        using var cancellation = new CancellationTokenSource();
        var read = stream.ReadNextAsync(cancellation.Token);
        await transport.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await stream.CloseAsync();
        await stream.DisposeAsync();
        Assert.Equal(1, transport.CloseCount);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task PeerCloseEndsStreamAndDisposesTransport()
    {
        var transport = new FakeTransport(); transport.AddClose();
        await using var stream = new RealtimeStream(transport);
        await stream.ConnectAsync(Connection());
        Assert.IsType<RealtimeStreamClosed>(await stream.ReadNextAsync());
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task PatchQueueSerializesAndCoalescesOnlyPendingCamerasAcrossNoSceneBarrier()
    {
        var firstStarted = Signal(); var release = Signal();
        var sent = new List<string>(); var ids = new List<string>();
        int active = 0, maximum = 0;
        await using var queue = new RealtimePatchQueue("session-1", async (id, content, ct) =>
        {
            int count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
            sent.Add(content); ids.Add(id);
            if (content == "camera-1") { firstStarted.SetResult(); await release.Task.WaitAsync(ct); }
            Interlocked.Decrement(ref active);
            return new("session-1", RealtimeSessionStatus.Active);
        });
        var first = queue.EnqueueCameraAsync("camera-1");
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = queue.EnqueueCameraAsync("camera-2");
        var third = queue.EnqueueCameraAsync("camera-3");
        var scene = queue.EnqueueSceneAsync("model-4");
        var fifth = queue.EnqueueCameraAsync("camera-5");
        var sixth = queue.EnqueueCameraAsync("camera-6");
        var flush = queue.FlushAsync();
        Assert.False(flush.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second, third, scene, fifth, sixth, flush).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "camera-1", "camera-3", "model-4", "camera-6" }, sent);
        Assert.Equal(1, maximum);
        Assert.Equal(4, ids.Distinct().Count());
    }

    [Fact]
    public async Task UncertainPatchStopsQueueWithoutReplay()
    {
        var started = Signal(); var release = Signal(); int sends = 0;
        await using var queue = new RealtimePatchQueue("session-1", async (_, _, ct) =>
        {
            sends++; started.SetResult(); await release.Task.WaitAsync(ct);
            throw new ProviderException("submission_unknown", "Unknown patch result", true);
        });
        var first = queue.EnqueueCameraAsync("first");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = queue.EnqueueSceneAsync("second");
        release.SetResult();
        await Assert.ThrowsAsync<ProviderException>(() => first);
        await Assert.ThrowsAsync<ProviderException>(() => pending);
        await Assert.ThrowsAsync<ProviderException>(() => queue.FlushAsync());
        await Assert.ThrowsAsync<ProviderException>(() => queue.EnqueueCameraAsync("third"));
        Assert.Equal(1, sends);
    }

    [Fact]
    public async Task DisposingPatchQueueCancelsInFlightAndPendingWork()
    {
        var started = Signal();
        var queue = new RealtimePatchQueue("session-1", async (_, _, ct) =>
        {
            started.SetResult(); await Task.Delay(Timeout.Infinite, ct);
            return new("session-1", RealtimeSessionStatus.Active);
        });
        var first = queue.EnqueueSceneAsync("first");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = queue.EnqueueSceneAsync("second");
        await queue.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.FlushAsync());
        Assert.Throws<ObjectDisposedException>(() => { _ = queue.EnqueueSceneAsync("third"); });
    }

    [Fact]
    public async Task PatchAcknowledgementMustRemainActiveAndMatchSession()
    {
        await using var queue = new RealtimePatchQueue("session-1", (_, _, _) => Task.FromResult(new RealtimeUpdateResult("other", RealtimeSessionStatus.Active)));
        var error = await Assert.ThrowsAsync<ProviderException>(() => queue.EnqueueSceneAsync("scene"));
        Assert.Equal("inactive_stream", error.Code);
        await Assert.ThrowsAsync<ProviderException>(() => queue.FlushAsync());
    }

    [Fact]
    public async Task SessionCloseClosesSocketBeforeDeletingKnownServerSession()
    {
        var events = new List<string>();
        var handler = new FakeHandler(_ =>
        {
            events.Add("delete");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"sessionId\":\"session-1\",\"closed\":true}") });
        });
        var transport = new FakeTransport { Closing = () => events.Add("socket") };
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        await session.ConnectAsync();
        Assert.True(session.NeedsServerCleanup);
        await session.CloseAsync();
        await session.CloseAsync();
        Assert.Equal(new[] { "socket", "delete" }, events);
        Assert.False(session.NeedsServerCleanup);
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
    }

    [Fact]
    public async Task CancelledConnectStillDeletesSessionWithoutCreatingAnother()
    {
        var handler = new FakeHandler("{\"sessionId\":\"session-1\",\"closed\":true}");
        var transport = new FakeTransport();
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.ConnectAsync(new CancellationToken(true)));
        Assert.False(session.NeedsServerCleanup);
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
        Assert.Equal(1, transport.DisposeCount);
    }

    [Fact]
    public async Task FailedDeleteRetainsCleanupObligationAndAllowsExplicitRetryOnly()
    {
        int requests = 0;
        var handler = new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(++requests == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            { Content = new StringContent("{\"sessionId\":\"session-1\",\"closed\":false}") }));
        var transport = new FakeTransport();
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        await session.ConnectAsync();
        var error = await Assert.ThrowsAsync<ProviderException>(() => session.CloseAsync());
        Assert.Equal("stream_cleanup_required", error.Code);
        Assert.True(session.NeedsServerCleanup);
        Assert.Equal(1, requests);
        await session.CloseAsync();
        Assert.False(session.NeedsServerCleanup);
        Assert.Equal(2, requests);
        Assert.All(handler.Requests, request => Assert.Equal(HttpMethod.Delete, request.Method));
        Assert.Equal(1, transport.CloseCount);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("session/other")]
    [InlineData("session?query")]
    public async Task SessionIdsCannotAlterRestRoutes(string id)
    {
        var handler = new FakeHandler("{}");
        await Assert.ThrowsAsync<ProviderException>(() => Client(handler).CloseRealtimeAsync(Key, id));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SessionWaitsForStreamingInitializationAndCleansUpFatalStatus()
    {
        var handler = new FakeHandler("{\"sessionId\":\"session-1\",\"status\":\"ACTIVE\"}", "{\"sessionId\":\"session-1\",\"closed\":true}");
        var transport = new FakeTransport(); transport.Add("{\"code\":\"0\"}", true); transport.Add("{\"code\":\"1003\"}", true);
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        Assert.Throws<InvalidOperationException>(() => { _ = session.EnqueueCameraAsync("#usda 1.0"); });
        await session.ConnectAsync();
        Assert.Throws<InvalidOperationException>(() => { _ = session.EnqueueCameraAsync("#usda 1.0"); });
        await session.ReadNextAsync();
        await session.EnqueueCameraAsync("#usda 1.0");
        await session.FlushAsync();
        Assert.Equal(RealtimeStatusCode.SlaveLimitExceeded, Assert.IsType<RealtimeStatusMessage>(await session.ReadNextAsync()).Code);
        Assert.False(session.NeedsServerCleanup);
        Assert.Throws<InvalidOperationException>(() => { _ = session.EnqueueSceneAsync("#usda 1.0"); });
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
    }

    [Fact]
    public async Task SessionReadCancellationClosesSocketAndDeletesServerSession()
    {
        var handler = new FakeHandler("{\"sessionId\":\"session-1\",\"closed\":true}");
        var transport = new FakeTransport();
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        await session.ConnectAsync();
        using var cancellation = new CancellationTokenSource();
        var read = session.ReadNextAsync(cancellation.Token);
        await transport.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        Assert.False(session.NeedsServerCleanup);
        Assert.Equal(1, transport.DisposeCount);
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedSessionPatchClosesCloudWithoutWaitingForAnotherRead(bool camera)
    {
        var handler = new FakeHandler(request => Task.FromResult(new HttpResponseMessage(request.Method == HttpMethod.Delete ? HttpStatusCode.OK : HttpStatusCode.InternalServerError)
            { Content = new StringContent("{\"sessionId\":\"session-1\",\"closed\":true}") }));
        var transport = new FakeTransport(); transport.Add("{\"code\":\"0\"}", true);
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        await session.ConnectAsync(); await session.ReadNextAsync();
        var error = await Assert.ThrowsAsync<ProviderException>(() => camera ? session.EnqueueCameraAsync("#usda 1.0") : session.EnqueueSceneAsync("#usda 1.0"));
        Assert.True(error.SubmissionMayHaveSucceeded);
        Assert.False(session.NeedsServerCleanup);
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Delete }, handler.Requests.Select(r => r.Method));
        Assert.Equal(1, transport.DisposeCount);
        await Assert.ThrowsAsync<ProviderException>(() => session.FlushAsync());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CancellingSessionFlushStopsInflightPatchAndCleansUp()
    {
        var started = Signal();
        var handler = new FakeHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Post) { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"sessionId\":\"session-1\",\"closed\":true}") };
        });
        var transport = new FakeTransport(); transport.Add("{\"code\":\"0\"}", true);
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        await session.ConnectAsync(); await session.ReadNextAsync();
        var patch = session.EnqueueSceneAsync("#usda 1.0");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var flush = session.FlushAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<ProviderException>(() => patch);
        Assert.False(session.NeedsServerCleanup);
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Delete }, handler.Requests.Select(r => r.Method));
    }

    [Fact]
    public async Task ConcurrentPatchFaultsShareFailedCleanupWithoutHiddenDeleteRetry()
    {
        var started = Signal(); var release = Signal(); int deletes = 0;
        var handler = new FakeHandler(async request =>
        {
            if (request.Method == HttpMethod.Post) { started.SetResult(); await release.Task; }
            else deletes++;
            return new HttpResponseMessage(deletes > 1 ? HttpStatusCode.OK : HttpStatusCode.InternalServerError)
                { Content = new StringContent("{\"sessionId\":\"session-1\",\"closed\":false}") };
        });
        var transport = new FakeTransport(); transport.Add("{\"code\":\"0\"}", true);
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        await session.ConnectAsync(); await session.ReadNextAsync();
        var first = session.EnqueueSceneAsync("first");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.EnqueueSceneAsync("second");
        var flush = session.FlushAsync();
        release.SetResult();
        foreach (var failed in new[] { first, second, flush })
        {
            var error = await Assert.ThrowsAsync<ProviderException>(() => failed.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal("stream_cleanup_required", error.Code);
        }
        Assert.Equal(1, deletes);
        Assert.True(session.NeedsServerCleanup);
        await session.CloseAsync();
        Assert.Equal(2, deletes);
        Assert.False(session.NeedsServerCleanup);
    }

    [Fact]
    public async Task InitializationAlreadyInFlightCannotReopenClosedSession()
    {
        var receiveStarted = Signal(); var release = Signal();
        var handler = new FakeHandler("{\"sessionId\":\"session-1\",\"closed\":true}");
        var transport = new FakeTransport { BeforeReceiveReturn = async () => { receiveStarted.SetResult(); await release.Task; } };
        transport.Add("{\"code\":\"0\"}", true);
        await using var session = new RealtimeSession(Client(handler), Key,
            new("session-1", true, RealtimeSessionStatus.Active, 0, Connection(), null), transport);
        await session.ConnectAsync();
        var lateInitialization = session.ReadNextAsync();
        await receiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.CloseAsync();
        release.SetResult();
        Assert.Equal(RealtimeStatusCode.Initialized, Assert.IsType<RealtimeStatusMessage>(await lateInitialization).Code);
        Assert.Throws<InvalidOperationException>(() => { _ = session.EnqueueCameraAsync("#usda 1.0"); });
        Assert.False(session.NeedsServerCleanup);
        Assert.Equal(HttpMethod.Delete, Assert.Single(handler.Requests).Method);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static AholoApiClient Client(FakeHandler handler) => new(new HttpClient(handler), new HttpClient(new FakeHandler("{}")));
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        public List<(string Uri, HttpMethod Method, string? Auth, string? Body)> Requests { get; } = [];
        public FakeHandler(params string[] json)
        {
            var responses = new Queue<string>(json);
            _respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json") });
        }
        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => _respond = (request, _) => respond(request);
        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Method, request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null, body));
            return await _respond(request, ct);
        }
    }
    private sealed class FakeTransport : IRealtimeWebSocketTransport
    {
        private readonly Queue<(byte[] Data, WebSocketMessageType Type, bool End)> _chunks = new();
        public List<string> Sent { get; } = [];
        public Action? Closing { get; init; }
        public Func<Task>? BeforeReceiveReturn { get; init; }
        public int CloseCount { get; private set; }
        public int DisposeCount { get; private set; }
        public TaskCompletionSource Waiting { get; } = Signal();
        public void Add(string text, bool end) => _chunks.Enqueue((Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, end));
        public void Add(byte[] bytes, bool end) => _chunks.Enqueue((bytes, WebSocketMessageType.Binary, end));
        public void AddClose() => _chunks.Enqueue(([], WebSocketMessageType.Close, true));
        public Task ConnectAsync(RealtimeConnection connection, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task SendTextAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Sent.Add(Encoding.UTF8.GetString(bytes.Span)); return Task.CompletedTask; }
        public async ValueTask<RealtimeSocketChunk> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
        {
            if (_chunks.Count == 0) { Waiting.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
            var chunk = _chunks.Dequeue(); chunk.Data.CopyTo(buffer);
            if (BeforeReceiveReturn is not null) await BeforeReceiveReturn();
            return new(chunk.Data.Length, chunk.Type, chunk.End);
        }
        public Task CloseAsync(CancellationToken ct) { CloseCount++; Closing?.Invoke(); return Task.CompletedTask; }
        public void Abort() { }
        public ValueTask DisposeAsync() { DisposeCount++; return ValueTask.CompletedTask; }
    }
}
