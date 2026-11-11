using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aholo.Core;
using Aholo.Provider;
namespace Aholo.Tests;

public sealed class OAuthPersistenceTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"aholo-oauth-tests-"+Guid.NewGuid().ToString("N"));
    private readonly Vault store=new();
    private OAuthSessionCoordinator Coordinator()=>new(store,root);
    private AccountSessionManager Manager(Auth? auth=null)=>new(auth??new(),_=>throw new Exception("No browser expected"),persistence:Coordinator());
    private static OAuthToken Token(string value="fixture-access",bool expired=false,string? refresh="fixture-refresh")=>new(){AccessToken=value,RefreshToken=refresh,ClientId="fixture-public-client",Scope="get_user_info",ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(expired?-1:30)};
    private static async Task Login(AccountSessionManager manager,bool remember=true)
    {manager.Begin(Guid.NewGuid(),remember:remember);await manager.Completion.WaitAsync(TimeSpan.FromSeconds(5));Assert.Equal("SignedIn",manager.Status().Stage);}
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
    [Theory][InlineData(false)][InlineData(true)]
    public async Task RememberIsExplicitAndShutdownOnlyClearsProcessMemory(bool remember)
    {
        var manager=Manager();await Login(manager,remember);
        Assert.Equal(remember,manager.Status().Remembered);Assert.Equal(remember,store.Values.ContainsKey(OAuthSessionCoordinator.Account));
        manager.Dispose();var auth=new Auth{Forbid=true};using var restarted=Manager(auth);
        var restored=await restarted.StatusAsync();Assert.Equal(remember?"SignedIn":"SignedOut",restored.Stage);Assert.Equal(remember,restored.Remembered);Assert.Equal(0,auth.Logins+auth.Refreshes);
        AssertNoPlainSecrets();
    }
    [Fact]
    public async Task DefaultLoginDoesNotRememberAndPreservesApiKeyScope()
    {
        var keys=new CredentialCoordinator(store,root);var before=await keys.ConnectAsync(new("fixture-api-key"),true);
        using var manager=Manager();manager.Begin(Guid.NewGuid());await manager.Completion;
        Assert.False(manager.Status().Remembered);Assert.False(store.Values.ContainsKey(OAuthSessionCoordinator.Account));
        await manager.SignOutAsync();Assert.Equal(before.Epoch,(await keys.ReadAsync()).Epoch);Assert.Same(before.Key,await keys.RequireAsync(before.Epoch));
    }
    [Fact]
    public async Task ExpiredSavedSessionRestoresWithoutAutomaticRefresh()
    {
        var c=Coordinator();var lease=await c.BeginAsync(null,false,true,default);await c.CompleteAsync(lease,Token(expired:true),default);
        var auth=new Auth{Forbid=true};using var manager=Manager(auth);var state=await manager.StatusAsync();
        Assert.Equal("Expired",state.Stage);Assert.True(state.CanRefresh);Assert.True(state.Remembered);Assert.Equal(0,auth.Logins+auth.Refreshes);
        Assert.DoesNotContain("fixture",JsonSerializer.Serialize(state));
    }
    [Fact]
    public async Task RefreshRotatesTheWholeSavedSessionAndRetainsRememberChoice()
    {
        var auth=new Auth();using var manager=Manager(auth);await Login(manager);
        auth.Refresh=previous=>{Assert.Equal("fixture-refresh",previous.RefreshToken);return Task.FromResult(Token("fixture-new-access",refresh:"fixture-new-refresh"));};
        manager.Begin(Guid.NewGuid(),refresh:true);await manager.Completion;
        using var restarted=Manager(new(){Forbid=true});Assert.True((await restarted.StatusAsync()).Remembered);
        var saved=(await Coordinator().ReadAsync()).Token!;Assert.Equal("fixture-new-access",saved.AccessToken);Assert.Equal("fixture-new-refresh",saved.RefreshToken);Assert.Equal(1,auth.Refreshes);AssertNoPlainSecrets();
    }
    [Fact]
    public async Task ExplicitSessionOnlyLoginReplacesPreviouslyRememberedAccount()
    {
        using var first=Manager();await Login(first);using var second=Manager();await Login(second,false);
        Assert.Equal("SignedOut",(await first.StatusAsync()).Stage);Assert.Null((await Coordinator().ReadAsync()).Token);Assert.False(store.Values.ContainsKey(OAuthSessionCoordinator.Account));
    }
    [Fact]
    public async Task SignOutInvalidatesOtherInstancesWithoutChangingApiKey()
    {
        var keys=new CredentialCoordinator(store,root);var key=await keys.ConnectAsync(new("fixture-key"),true);
        using var first=Manager();await Login(first);using var second=Manager();Assert.Equal("SignedIn",(await second.StatusAsync()).Stage);
        await first.SignOutAsync();Assert.Equal("SignedOut",(await second.StatusAsync()).Stage);Assert.Null((await Coordinator().ReadAsync()).Token);
        Assert.Equal(key.Epoch,(await keys.ReadAsync()).Epoch);Assert.Equal("fixture-key",store.Values[CredentialCoordinator.Account]);
    }
    [Fact]
    public async Task SignOutCannotBeUndoneByLateRefreshFromAnotherInstance()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var reply=new TaskCompletionSource<OAuthToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var auth=new Auth{Refresh=_=>{entered.TrySetResult();return reply.Task;}};using var first=Manager(auth);await Login(first);
        first.Begin(Guid.NewGuid(),true);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));using var second=Manager();await second.SignOutAsync();
        reply.SetResult(Token("fixture-late-rotated"));await first.Completion;
        Assert.Equal("Failed",first.Status().Stage);Assert.Null((await Coordinator().ReadAsync()).Token);Assert.False(store.Values.ContainsKey(OAuthSessionCoordinator.Account));
    }
    [Fact]
    public async Task OwnedCancellationRejectsIgnoredLateLoginButNotCompletedLogin()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var reply=new TaskCompletionSource<OAuthToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager=Manager(new(){Login=()=>{entered.TrySetResult();return reply.Task;}});var id=Guid.NewGuid();manager.Begin(id,remember:true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));await manager.CancelPendingAsync(id);reply.SetResult(Token());await manager.Completion;
        Assert.Null((await Coordinator().ReadAsync()).Token);Assert.False(store.Values.ContainsKey(OAuthSessionCoordinator.Account));
        using var complete=Manager();var completedId=Guid.NewGuid();complete.Begin(completedId,remember:true);await complete.Completion;
        Assert.Equal("SignedIn",(await complete.CancelPendingAsync(completedId)).Stage);Assert.NotNull((await Coordinator().ReadAsync()).Token);
    }
    [Fact]
    public async Task ClosingAttemptBeforeReservationKeepsEarlierSavedSessionButExplicitSignOutClearsIt()
    {
        using var manager=Manager();await Login(manager);string lockPath=Directory.GetFiles(root,"*.lock").Single();
        var attempt=Guid.NewGuid();
        using(var held=new FileStream(lockPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            manager.Begin(attempt,remember:true);await manager.CancelPendingAsync(attempt);
            await manager.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.NotNull((await Coordinator().ReadAsync()).Token);
        Assert.Equal("SignedIn",(await manager.StatusAsync()).Stage);
        Task<AccountSessionState> logout;
        using(var held=new FileStream(lockPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            manager.Begin(Guid.NewGuid(),remember:true);logout=manager.SignOutAsync();Assert.False(logout.IsCompleted);
        }
        await logout.WaitAsync(TimeSpan.FromSeconds(5));await manager.Completion;
        Assert.Null((await Coordinator().ReadAsync()).Token);
    }
    [Fact]
    public async Task SaveInFlightCannotSurviveCompletedSignOut()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.BeforeSet=async()=>{entered.TrySetResult();await release.Task;};using var manager=Manager();manager.Begin(Guid.NewGuid(),remember:true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));Task<AccountSessionState> logout=manager.SignOutAsync();Assert.False(logout.IsCompleted);
        release.SetResult();await logout.WaitAsync(TimeSpan.FromSeconds(5));await manager.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("SignedOut",manager.Status().Stage);Assert.Null((await Coordinator().ReadAsync()).Token);Assert.False(store.Values.ContainsKey(OAuthSessionCoordinator.Account));
    }
    [Fact]
    public async Task FailedOrAmbiguousRefreshNeverRestoresEarlierRotatingToken()
    {
        using var manager=Manager(new(){Refresh=_=>Task.FromException<OAuthToken>(new IOException("fixture-private-response"))});await Login(manager);
        manager.Begin(Guid.NewGuid(),true);await manager.Completion;
        Assert.Equal("Failed",manager.Status().Stage);Assert.Null((await Coordinator().ReadAsync()).Token);Assert.DoesNotContain("fixture-private",JsonSerializer.Serialize(manager.Status()));
    }
    [Fact]
    public async Task PartialSecureSaveFailureFallsBackOnlyToMemory()
    {
        store.FailAfterSet=true;using var manager=Manager();await Login(manager);
        Assert.False(manager.Status().Remembered);Assert.NotNull(manager.Status().Error);Assert.Equal("SignedIn",(await manager.StatusAsync()).Stage);
        Assert.Null((await Coordinator().ReadAsync()).Token);Assert.DoesNotContain("fixture-private",JsonSerializer.Serialize(manager.Status()));AssertNoPlainSecrets();
    }
    [Fact]
    public async Task FailedDeleteDisablesRestoreAndExplicitSignOutCanRecover()
    {
        using var manager=Manager();await Login(manager);store.FailDelete=true;
        Assert.Equal("Failed",(await manager.SignOutAsync()).Stage);await Assert.ThrowsAsync<ProviderException>(()=>Coordinator().ReadAsync());
        store.FailDelete=false;Assert.Equal("SignedOut",(await manager.SignOutAsync()).Stage);Assert.Null((await Coordinator().ReadAsync()).Token);
    }
    [Theory][InlineData("json")][InlineData("issuer")][InlineData("scope")][InlineData("token")][InlineData("epoch")][InlineData("schema")]
    public async Task CorruptSecureEntryNeverLeaksAndCanBeReplacedByExplicitLogin(string change)
    {
        using(var manager=Manager())await Login(manager);
        var saved=JsonNode.Parse(store.Values[OAuthSessionCoordinator.Account])!;
        if(change=="issuer")saved["Issuer"]="https://wrong.invalid";
        if(change=="scope")saved["Token"]!["Scope"]="wrong_scope";
        if(change=="token")saved["Token"]!["AccessToken"]="fixture-private invalid";
        if(change=="epoch")saved["Epoch"]=Guid.NewGuid().ToString("N");
        if(change=="schema")saved["SchemaVersion"]=999;
        store.Values[OAuthSessionCoordinator.Account]=change=="json"?"fixture-private corrupt json":saved.ToJsonString();
        using var restarted=Manager();var state=await restarted.StatusAsync();Assert.Equal("Failed",state.Stage);Assert.DoesNotContain("fixture-private",JsonSerializer.Serialize(state));
        await Login(restarted);Assert.True(restarted.Status().Remembered);
    }
    [Fact]
    public async Task MissingOrCorruptCoordinationStateNeverInheritsOrphanedCredential()
    {
        using(var manager=Manager())await Login(manager);
        string state=Directory.GetFiles(root,"*.json").Single();File.Delete(state);Assert.Null((await Coordinator().ReadAsync()).Token);
        File.WriteAllText(state,"broken state");using var restarted=Manager();Assert.Equal("Failed",(await restarted.StatusAsync()).Stage);
        await Login(restarted);Assert.Equal("SignedIn",(await restarted.StatusAsync()).Stage);
    }
    [Fact]
    public async Task CancellationAfterSecureWriteLeavesNonReadyStateUntilExplicitRecovery()
    {
        var c=Coordinator();var lease=await c.BeginAsync(null,false,true,default);using var cancel=new CancellationTokenSource();store.BeforeSet=()=>{cancel.Cancel();return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>c.CompleteAsync(lease,Token(),cancel.Token));
        await Assert.ThrowsAsync<ProviderException>(()=>Coordinator().ReadAsync());await c.SignOutAsync();Assert.Null((await Coordinator().ReadAsync()).Token);
    }
    [Fact]
    public async Task OnlyOneInstanceCanReserveRefreshForAGivenEpoch()
    {
        var first=Coordinator();var initial=await first.BeginAsync(null,false,true,default);var saved=await first.CompleteAsync(initial,Token(),default);var second=Coordinator();await second.ReadAsync();
        var lease=await first.BeginAsync(saved.Epoch,true,true,default);
        await Assert.ThrowsAsync<ProviderException>(()=>second.BeginAsync(saved.Epoch,true,true,default));
        await first.CompleteAsync(lease,Token("fixture-rotated"),default);Assert.Equal("fixture-rotated",(await second.ReadAsync()).Token!.AccessToken);
    }
    [Fact]
    public async Task DisposeDuringLoginDoesNotPersistIgnoredLateCompletion()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var reply=new TaskCompletionSource<OAuthToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager=Manager(new(){Login=()=>{entered.TrySetResult();return reply.Task;}});manager.Begin(Guid.NewGuid(),remember:true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));manager.Dispose();reply.SetResult(Token());await manager.Completion;
        Assert.Null((await Coordinator().ReadAsync()).Token);
    }
    [Fact]
    public async Task SessionWithoutRefreshTokenDoesNotOfferRefresh()
    {
        using var manager=Manager(new(){Login=()=>Task.FromResult(Token(refresh:null))});await Login(manager);Assert.False(manager.Status().CanRefresh);
        Assert.Throws<InvalidOperationException>(()=>manager.Begin(Guid.NewGuid(),true));
    }
    private void AssertNoPlainSecrets()
    {
        foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))
        {string text=File.ReadAllText(file);Assert.DoesNotContain("fixture-access",text);Assert.DoesNotContain("fixture-refresh",text);Assert.DoesNotContain("fixture-new",text);Assert.DoesNotContain("fixture-public-client",text);}
    }
    private sealed class Vault:ICredentialStore
    {
        public readonly ConcurrentDictionary<string,string> Values=new();public Func<Task>? BeforeSet;public bool FailAfterSet,FailDelete;
        public Task<string?> GetAsync(string account,CancellationToken ct=default){ct.ThrowIfCancellationRequested();return Task.FromResult(Values.TryGetValue(account,out var value)?value:null);}
        public async Task SetAsync(string account,string secret,CancellationToken ct=default){if(BeforeSet is not null)await BeforeSet();Values[account]=secret;if(FailAfterSet)throw new IOException("fixture-private storage error");}
        public Task DeleteAsync(string account,CancellationToken ct=default){ct.ThrowIfCancellationRequested();if(FailDelete)throw new IOException("fixture-private deletion error");Values.TryRemove(account,out _);return Task.CompletedTask;}
    }
    private sealed class Auth:IAccountAuthorization
    {
        public bool Forbid;public int Logins,Refreshes;public Func<Task<OAuthToken>> Login=()=>Task.FromResult(Token());public Func<OAuthToken,Task<OAuthToken>> Refresh=_=>Task.FromResult(Token("fixture-rotated"));
        public Task<OAuthToken> LoginAsync(Action<Uri> open,CancellationToken ct=default){Logins++;if(Forbid)throw new Exception("no authorization expected");return Login();}
        public Task<OAuthToken> RefreshAsync(OAuthToken previous,CancellationToken ct=default){Refreshes++;if(Forbid)throw new Exception("no refresh expected");return Refresh(previous);}
    }
}
