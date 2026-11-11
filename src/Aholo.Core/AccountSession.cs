namespace Aholo.Core;

/// <summary>Optional account identity is independent of API Key permissions and task ownership.</summary>
public sealed record AccountSessionState(string Stage = "SignedOut", DateTimeOffset? ExpiresAt = null,
    Guid? AttemptId = null, string? Error = null, bool Remembered = false, bool CanRefresh = false);
public interface IAccountSessionClient
{
    Task<AccountSessionState> AccountAsync(string action, Guid? attemptId = null, bool? remember = null);
}
public sealed partial class AholoClient : IAccountSessionClient
{
    // Short RPCs only: the sidecar owns the browser wait. Closing a dialog never kills service work.
    public Task<AccountSessionState> AccountAsync(string action, Guid? attemptId = null, bool? remember = null) =>
        Call<AccountSessionState>("account", new { action, attemptId, remember }, CancellationToken.None);
}
