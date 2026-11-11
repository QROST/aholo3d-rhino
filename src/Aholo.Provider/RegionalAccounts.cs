using Aholo.Core;
namespace Aholo.Provider;

/// <summary>Each dialog routes every action to its captured issuer, including close/cancel.</summary>
public sealed class RegionalAccounts : IDisposable
{
    private readonly Dictionary<string,AccountSessionManager> accounts;
    private readonly SemaphoreSlim actions=new(1);
    private readonly HashSet<(string Region,Guid Id)> canceled=new();
    public RegionalAccounts(Func<string,AccountSessionManager> create)
    {accounts=new(){[AholoRegions.International]=create(AholoRegions.International),[AholoRegions.China]=create(AholoRegions.China)};}
    public async Task<AccountSessionState> RunAsync(string region,string action,Guid? attemptId,bool? remember,CancellationToken ct)
    {
        region=AholoRegions.Get(region).Id;
        await actions.WaitAsync(ct).ConfigureAwait(false);
        try
        {
        var account=accounts[region];
        if(action=="cancel"&&attemptId is{} canceledId)canceled.Add((region,canceledId));
        if(action is "begin" or "refresh"&&attemptId is{} id&&canceled.Contains((region,id)))return new(AttemptId:id,Region:region);
        var result=action switch
        {
            "status"=>await account.StatusAsync(ct).ConfigureAwait(false),
            "begin"=>account.Begin(attemptId??throw new ArgumentException("Account attempt identifier is missing."),remember:remember),
            "refresh"=>account.Begin(attemptId??throw new ArgumentException("Account attempt identifier is missing."),true,remember),
            "signOut"=>await account.SignOutAsync(ct).ConfigureAwait(false),
            "cancel"=>await account.CancelPendingAsync(attemptId??throw new ArgumentException("Account attempt identifier is missing."),ct).ConfigureAwait(false),
            _=>throw new ArgumentException("Unknown account operation.")
        };
        return result with{Region=region};
        }finally{actions.Release();}
    }
    public async Task CancelPendingAsync(CancellationToken ct)
    {
        await actions.WaitAsync(ct).ConfigureAwait(false);
        try{foreach(var account in accounts.Values)
        {
            var state=account.Status();
            if(state.AttemptId is{} id&&state.Stage is "SigningIn" or "Refreshing")
                await account.CancelPendingAsync(id,ct).ConfigureAwait(false);
        }}finally{actions.Release();}
    }
    public void Dispose(){foreach(var account in accounts.Values)account.Dispose();}
}
