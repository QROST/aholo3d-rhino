using System.Text.Json;
using Aholo3D.Core;
using Aholo3D.Provider;
namespace Aholo3D.Tests;

public sealed class AccountSessionTests
{
    private static OAuthToken Token(string value="sensitive-access",bool expired=false) => new()
    { AccessToken=value,RefreshToken="sensitive-refresh",ClientId="public-client",ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(expired?-1:30),Scope="get_user_info" };
    [Fact]
    public async Task BeginReturnsWithoutWaitingAndDuplicateDoesNotStartAnotherLogin()
    {
        var service=new Fake();using var manager=new AccountSessionManager(service,_=>{});
        Guid id=Guid.NewGuid();Assert.Equal("SigningIn",manager.Begin(id).Stage);
        Assert.Equal(id,manager.Begin(Guid.NewGuid()).AttemptId);
        await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal(1,service.Logins);
        service.Login.TrySetResult(Token());await manager.Completion;
        Assert.Equal("SignedIn",manager.Status().Stage);
        string visible=JsonSerializer.Serialize(manager.Status());
        Assert.DoesNotContain("sensitive",visible);Assert.DoesNotContain("public-client",visible);
    }
    [Fact]
    public async Task SignOutPreventsLateLoginAndLateBrowserHandoff()
    {
        int opened=0;var service=new Fake();using var manager=new AccountSessionManager(service,_=>opened++);
        manager.Begin(Guid.NewGuid());await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var old=manager.Completion;await manager.SignOutAsync();
        Assert.Throws<OperationCanceledException>(()=>service.Open!(new Uri("https://www.aholo.ai/oauth/authorize")));
        service.Login.TrySetResult(Token());await old;
        Assert.Equal("SignedOut",manager.Status().Stage);Assert.Equal(0,opened);
    }
    [Fact]
    public async Task RefreshIsExplicitSingleFlightAndCannotResurrectSignedOutAccount()
    {
        var service=new Fake();using var manager=new AccountSessionManager(service,_=>{});
        manager.Begin(Guid.NewGuid());service.Login.TrySetResult(Token(expired:true));await manager.Completion;
        Assert.Equal("Expired",manager.Status().Stage);Assert.Equal(0,service.Refreshes);
        Guid id=Guid.NewGuid();Assert.Equal("Refreshing",manager.Begin(id,true).Stage);
        Assert.Equal(id,manager.Begin(Guid.NewGuid(),true).AttemptId);
        await service.RefreshEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));var pending=manager.Completion;
        await manager.SignOutAsync();service.Refresh.TrySetResult(Token("rotated-secret"));await pending;
        Assert.Equal("SignedOut",manager.Status().Stage);Assert.Equal(1,service.Refreshes);
    }
    [Fact]
    public async Task ClosingDialogNeverSignsOutAnAlreadyCompletedSession()
    {
        var service=new Fake();using var manager=new AccountSessionManager(service,_=>{});
        Guid id=Guid.NewGuid();manager.Begin(id);service.Login.TrySetResult(Token());await manager.Completion;
        Assert.Equal("SignedIn",(await manager.CancelPendingAsync(Guid.NewGuid())).Stage);
        Assert.Equal("SignedIn",(await manager.CancelPendingAsync(id)).Stage);
        Assert.Equal("SignedOut",(await manager.SignOutAsync()).Stage);
    }
    [Fact]
    public async Task CancellationIgnoringSuccessCannotLeavePermanentPendingState()
    {
        using var lifetime=new CancellationTokenSource();var service=new Fake();using var manager=new AccountSessionManager(service,_=>{},lifetime.Token);
        manager.Begin(Guid.NewGuid());await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));lifetime.Cancel();
        service.Login.TrySetResult(Token());await manager.Completion;
        Assert.Equal("Failed",manager.Status().Stage);Assert.Null(manager.Status().ExpiresAt);
    }
    [Fact]
    public async Task ClosingCurrentPendingAttemptRejectsItsLateResult()
    {
        var service=new Fake();using var manager=new AccountSessionManager(service,_=>{});Guid id=Guid.NewGuid();
        manager.Begin(id);await service.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await manager.CancelPendingAsync(id);
        service.Login.TrySetResult(Token());await manager.Completion;Assert.Equal("SignedOut",manager.Status().Stage);
    }
    [Fact]
    public async Task FailuresAreSanitizedAndDisposeRejectsNewWork()
    {
        var service=new Fake();var manager=new AccountSessionManager(service,_=>{});
        manager.Begin(Guid.NewGuid());service.Login.TrySetException(new IOException("private-token response body"));await manager.Completion;
        Assert.Equal("Failed",manager.Status().Stage);Assert.DoesNotContain("private-token",JsonSerializer.Serialize(manager.Status()));
        manager.Dispose();Assert.Throws<ObjectDisposedException>(()=>manager.Begin(Guid.NewGuid()));
    }
    [Fact]
    public async Task OptionalAccountLoginNeverChangesApiKeyEpochOrGrantsApiAccess()
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var credentials=new CredentialCoordinator(new Store(),root);var before=await credentials.ReadAsync();
            var service=new Fake();using var manager=new AccountSessionManager(service,_=>{});
            manager.Begin(Guid.NewGuid());service.Login.TrySetResult(Token());await manager.Completion;
            Assert.Equal(before.Epoch,(await credentials.ReadAsync()).Epoch);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>credentials.RequireAsync(before.Epoch));
            var key=await credentials.ConnectAsync(new ApiKeyCredential("local-test-key"),false);
            await manager.SignOutAsync();Assert.Equal(key.Key,await credentials.RequireAsync(key.Epoch));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private sealed class Fake:IAccountAuthorization
    {
        public readonly TaskCompletionSource<OAuthToken> Login=new(TaskCreationOptions.RunContinuationsAsynchronously),Refresh=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),RefreshEntered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Logins,Refreshes;public Action<Uri>? Open;
        public Task<OAuthToken> LoginAsync(Action<Uri> openBrowser,CancellationToken ct=default){Logins++;Open=openBrowser;Entered.TrySetResult();return Login.Task;}
        public Task<OAuthToken> RefreshAsync(OAuthToken previous,CancellationToken ct=default){Refreshes++;RefreshEntered.TrySetResult();return Refresh.Task;}
    }
    private sealed class Store:ICredentialStore
    {
        private string? value;
        public Task<string?> GetAsync(string account,CancellationToken cancellationToken=default)=>Task.FromResult(value);
        public Task SetAsync(string account,string secret,CancellationToken cancellationToken=default){value=secret;return Task.CompletedTask;}
        public Task DeleteAsync(string account,CancellationToken cancellationToken=default){value=null;return Task.CompletedTask;}
    }
}
