using Aholo.Core;
namespace Aholo.Provider;

public interface IAccountAuthorization
{
    Task<OAuthToken> LoginAsync(Action<Uri> openBrowser, CancellationToken cancellationToken = default);
    Task<OAuthToken> RefreshAsync(OAuthToken previous, CancellationToken cancellationToken = default);
}

/// <summary>Memory-only account session. It never reads or changes API Key credentials or journals.</summary>
public sealed class AccountSessionManager : IDisposable
{
    private readonly object gate = new();
    private readonly IAccountAuthorization authorization;
    private readonly Action<Uri> openBrowser;
    private readonly CancellationToken parentLifetime;
    private AccountSessionState state = new();
    private OAuthToken? token;
    private CancellationTokenSource? active;
    private long generation;
    private bool disposed;
    public Task Completion { get; private set; } = Task.CompletedTask;

    public AccountSessionManager(IAccountAuthorization authorization, Action<Uri> openBrowser, CancellationToken parentLifetime = default)
    { this.authorization = authorization; this.openBrowser = openBrowser; this.parentLifetime = parentLifetime; }

    public AccountSessionState Status()
    {
        lock (gate)
        {
            if (state.Stage == "SignedIn" && state.ExpiresAt <= DateTimeOffset.UtcNow) state = state with { Stage = "Expired" };
            return state;
        }
    }
    public AccountSessionState Begin(Guid attemptId, bool refresh = false)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            parentLifetime.ThrowIfCancellationRequested();
            if (attemptId == Guid.Empty) throw new ArgumentException("Account sign-in requires an attempt identifier.");
            if (active is not null) return Status();
            OAuthToken? previous = refresh ? token : null;
            if (refresh && previous is null) throw new InvalidOperationException("Sign in before refreshing the account session.");
            token = null;
            var source = CancellationTokenSource.CreateLinkedTokenSource(parentLifetime);
            source.CancelAfter(TimeSpan.FromMinutes(5)); active = source;
            long revision = ++generation;
            state = new(refresh ? "Refreshing" : "SigningIn", previous?.ExpiresAt, attemptId);
            Completion = Task.Run(async () =>
            {
                try
                {
                    OAuthToken result = previous is null
                        ? await authorization.LoginAsync(uri =>
                        {
                            lock (gate)
                            {
                                if (!Current(revision, source)) throw new OperationCanceledException();
                                openBrowser(uri); // Quick OS handoff, never waits for the browser.
                            }
                        }, source.Token).ConfigureAwait(false)
                        : await authorization.RefreshAsync(previous, source.Token).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (Current(revision, source))
                        { token = result; state = new(result.ExpiresAt > DateTimeOffset.UtcNow ? "SignedIn" : "Expired", result.ExpiresAt, attemptId); }
                    }
                }
                catch (Exception)
                {
                    lock (gate)
                    {
                        if (generation == revision && ReferenceEquals(active, source) && !disposed)
                            state = new("Failed", AttemptId: attemptId, Error: "Account sign-in or refresh did not complete. Sign in again; API Key access is unchanged.");
                    }
                }
                finally
                {
                    lock (gate)
                    {
                        if (ReferenceEquals(active, source))
                        {
                            if (state.Stage is "SigningIn" or "Refreshing")
                                state = new("Failed", AttemptId: attemptId, Error: "Account sign-in or refresh did not complete. Sign in again; API Key access is unchanged.");
                            active = null;
                        }
                    }
                    source.Dispose();
                }
            });
            return state;
        }
    }
    public AccountSessionState SignOut(Guid? onlyPendingAttempt = null)
    {
        CancellationTokenSource? cancel;
        AccountSessionState result;
        lock (gate)
        {
            if (onlyPendingAttempt.HasValue && (active is null || state.AttemptId != onlyPendingAttempt)) return Status();
            ++generation; token = null; cancel = active; active = null;
            result = state = new();
        }
        // Completion alone owns CTS disposal. Cancellation must not run callbacks under the state lock.
        try { cancel?.Cancel(); } catch (ObjectDisposedException) { }
        return result;
    }
    private bool Current(long revision, CancellationTokenSource source) =>
        !disposed && generation == revision && ReferenceEquals(active, source) && !source.IsCancellationRequested;
    public void Dispose()
    {
        lock (gate) { disposed = true; }
        SignOut();
    }
}
