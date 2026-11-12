using Aholo3D.Core;
namespace Aholo3D.Provider;

public interface IAccountAuthorization
{
    Task<OAuthToken> LoginAsync(Action<Uri> openBrowser,CancellationToken cancellationToken=default);
    Task<OAuthToken> RefreshAsync(OAuthToken previous,CancellationToken cancellationToken=default);
}

/// <summary>Optional account identity, independent of API Key credentials and task ownership.</summary>
public sealed class AccountSessionManager : IDisposable
{
    private readonly object gate=new();
    private readonly IAccountAuthorization authorization;
    private readonly Action<Uri> openBrowser;
    private readonly CancellationToken parentLifetime;
    private readonly OAuthSessionCoordinator? persistence;
    private AccountSessionState state=new();
    private OAuthToken? token;
    private string? epoch,activeEpoch;
    private CancellationTokenSource? active;
    private long generation;
    private bool disposed;
    public Task Completion{get;private set;}=Task.CompletedTask;
    public AccountSessionManager(IAccountAuthorization authorization,Action<Uri> openBrowser,CancellationToken parentLifetime=default,OAuthSessionCoordinator? persistence=null)
    {this.authorization=authorization;this.openBrowser=openBrowser;this.parentLifetime=parentLifetime;this.persistence=persistence;}
    public AccountSessionState Status()
    {
        lock(gate)
        {
            if(state.Stage=="SignedIn"&&state.ExpiresAt<=DateTimeOffset.UtcNow)state=state with{Stage="Expired"};
            return state;
        }
    }
    public async Task<AccountSessionState> StatusAsync(CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();long revision;
        lock(gate){ObjectDisposedException.ThrowIf(disposed,this);if(active is not null||persistence is null)return Status();revision=generation;}
        try
        {
            var saved=await persistence.ReadAsync(ct).ConfigureAwait(false);
            lock(gate)
            {
                if(disposed||generation!=revision||active is not null)return Status();
                Guid? attempt=epoch==saved.Epoch?state.AttemptId:null;
                epoch=saved.Epoch;token=saved.Token;
                if(token is not null)state=Describe(token,saved.Remembered,attempt,saved.Warning);
                else if(state.Stage!="Failed")state=new();
                return Status();
            }
        }
        catch(OperationCanceledException){throw;}
        catch(Exception)
        {
            lock(gate)
            {
                if(!disposed&&generation==revision&&active is null)
                {token=null;epoch=null;state=new("Failed",Error:"The saved account session could not be restored. Sign in again or sign out to clear it; API Key access is unchanged.");}
                return Status();
            }
        }
    }
    public AccountSessionState Begin(Guid attemptId,bool refresh=false,bool? remember=null)
    {
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);parentLifetime.ThrowIfCancellationRequested();
            if(attemptId==Guid.Empty)throw new ArgumentException("Account sign-in requires an attempt identifier.");
            if(active is not null)return Status();
            OAuthToken? previous=refresh?token:null;
            if(refresh&&(previous is null||string.IsNullOrWhiteSpace(previous.RefreshToken)))throw new InvalidOperationException("Sign in before refreshing the account session.");
            bool save=remember??(refresh&&state.Remembered);string? expectedEpoch=epoch;
            token=null;activeEpoch=null;
            var source=CancellationTokenSource.CreateLinkedTokenSource(parentLifetime);source.CancelAfter(TimeSpan.FromMinutes(5));active=source;
            long revision=++generation;state=new(refresh?"Refreshing":"SigningIn",previous?.ExpiresAt,attemptId);
            Completion=Task.Run(()=>RunAsync(attemptId,revision,source,previous,expectedEpoch,refresh,save));
            return state;
        }
    }
    private async Task RunAsync(Guid attemptId,long revision,CancellationTokenSource source,OAuthToken? previous,string? expectedEpoch,bool refresh,bool remember)
    {
        OAuthSessionLease? lease=null;bool committed=false;
        try
        {
            if(persistence is not null)
            {
                lease=await persistence.BeginAsync(expectedEpoch,refresh,remember,source.Token).ConfigureAwait(false);
                lock(gate){if(!Current(revision,source))throw new OperationCanceledException(source.Token);activeEpoch=lease.Epoch;}
                previous=lease.Previous;
            }
            source.Token.ThrowIfCancellationRequested();
            OAuthToken result=previous is null
                ?await authorization.LoginAsync(uri=>
                {
                    lock(gate){if(!Current(revision,source))throw new OperationCanceledException();openBrowser(uri);}
                },source.Token).ConfigureAwait(false)
                :await authorization.RefreshAsync(previous,source.Token).ConfigureAwait(false);
            lock(gate){if(!Current(revision,source))throw new OperationCanceledException(source.Token);}
            OAuthSessionCoordinator.Validate(result);
            OAuthSessionSnapshot? saved=persistence is not null
                ?await persistence.CompleteAsync(lease!,result,source.Token).ConfigureAwait(false):null;
            lock(gate)
            {
                if(!Current(revision,source))throw new OperationCanceledException(source.Token);
                token=result;epoch=saved?.Epoch;state=Describe(result,saved?.Remembered==true,attemptId,saved?.Warning);committed=true;
            }
        }
        catch(Exception)
        {
            lock(gate)
                if(generation==revision&&ReferenceEquals(active,source)&&!disposed)
                    state=new("Failed",AttemptId:attemptId,Error:"Account sign-in or refresh did not complete. Sign in again; API Key access is unchanged.");
        }
        finally
        {
            if(!committed&&lease is not null&&persistence is not null)
            {
                using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try{await persistence.SignOutAsync(lease.Epoch,cleanup.Token).ConfigureAwait(false);}
                catch{/* The reserved non-ready epoch already prevents restoring an incomplete write. */}
            }
            lock(gate)
            {
                if(ReferenceEquals(active,source))
                {
                    if(state.Stage is "SigningIn" or "Refreshing")state=new("Failed",AttemptId:attemptId,Error:"Account sign-in or refresh did not complete. Sign in again; API Key access is unchanged.");
                    active=null;activeEpoch=null;
                }
            }
            source.Dispose();
        }
    }
    public Task<AccountSessionState> SignOutAsync(CancellationToken ct=default)=>ClearAsync(null,ct);
    // Closing a dialog cancels only its unfinished attempt. It does not sign out an earlier
    // remembered session when the replacement attempt has not reserved an epoch yet.
    public Task<AccountSessionState> CancelPendingAsync(Guid attemptId,CancellationToken ct=default)=>ClearAsync(attemptId,ct);
    private async Task<AccountSessionState> ClearAsync(Guid? onlyPendingAttempt,CancellationToken ct)
    {
        CancellationTokenSource? cancel;string? ownedEpoch;long revision;
        lock(gate)
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            if(onlyPendingAttempt.HasValue&&(active is null||state.AttemptId!=onlyPendingAttempt||state.Stage is not ("SigningIn" or "Refreshing")))return Status();
            revision=++generation;token=null;epoch=null;ownedEpoch=activeEpoch;cancel=active;active=null;activeEpoch=null;state=new();
        }
        try{cancel?.Cancel();}catch(ObjectDisposedException){}
        try
        {
            // A cancellation arriving before lease acquisition is handled by that worker's cleanup.
            if(persistence is not null&&(!onlyPendingAttempt.HasValue||ownedEpoch is not null))
                await persistence.SignOutAsync(onlyPendingAttempt.HasValue?ownedEpoch:null,ct).ConfigureAwait(false);
        }
        catch(Exception)
        {
            lock(gate)if(generation==revision&&!disposed)
                state=new("Failed",Error:"The remembered account could not be fully removed. Retry sign out; API Key access is unchanged.");
        }
        return Status();
    }
    private static AccountSessionState Describe(OAuthToken token,bool remembered,Guid? attempt,string? warning)=>
        new(token.ExpiresAt>DateTimeOffset.UtcNow?"SignedIn":"Expired",token.ExpiresAt,attempt,warning,remembered,!string.IsNullOrWhiteSpace(token.RefreshToken));
    private bool Current(long revision,CancellationTokenSource source)=>!disposed&&generation==revision&&ReferenceEquals(active,source)&&!source.IsCancellationRequested;
    public void Dispose()
    {
        CancellationTokenSource? cancel;
        lock(gate){if(disposed)return;disposed=true;++generation;token=null;epoch=null;cancel=active;active=null;activeEpoch=null;state=new();}
        try{cancel?.Cancel();}catch(ObjectDisposedException){}
        // Completed remembered sessions survive normal process shutdown. Pending work owns cleanup.
    }
}
