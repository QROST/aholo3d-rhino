namespace Aholo.Core;

/// <summary>Optional account identity is independent of API Key permissions and task ownership.</summary>
public sealed record AccountSessionState(string Stage = "SignedOut", DateTimeOffset? ExpiresAt = null,
    Guid? AttemptId = null, string? Error = null, bool Remembered = false, bool CanRefresh = false, string Region = AholoRegions.International);
public interface IAccountSessionClient
{
    Task<AccountSessionState> AccountAsync(string action, Guid? attemptId = null, bool? remember = null, string region = AholoRegions.International);
}
public sealed partial class AholoClient : IAccountSessionClient
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(string Region,Guid Id),byte> canceledAccounts=new();
    // Only cancellation uses the independent private control pipe. It never kills an upload.
    public async Task<AccountSessionState> AccountAsync(string action,Guid? attemptId=null,bool? remember=null,string region=AholoRegions.International)
    {
        region=AholoRegions.Get(region).Id;
        if(action=="cancel"&&attemptId is{} canceled)
        {
            canceledAccounts.TryAdd((region,canceled),0);
            if(Volatile.Read(ref realtimeEndpoint) is null)return new(Region:region);
            return (await RealtimeAsync("cancelAccount",new{region,attemptId=canceled}).ConfigureAwait(false)).Reply.Account
                ??throw new IOException("The account cancellation response is missing.");
        }
        return await Call<AccountSessionState>("account",new{action,attemptId,remember,region},CancellationToken.None,()=>
        {
            if(action is "begin" or "refresh"&&attemptId is{} id&&canceledAccounts.ContainsKey((region,id)))
                throw new InvalidOperationException("This account sign-in attempt was canceled before it started.");
        }).ConfigureAwait(false);
    }
}
