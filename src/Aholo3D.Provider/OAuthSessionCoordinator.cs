using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aholo3D.Core;
namespace Aholo3D.Provider;

public sealed record OAuthSessionSnapshot(OAuthToken? Token,bool Remembered,string Epoch,string? Warning=null);
public sealed record OAuthSessionLease(string Epoch,OAuthToken? Previous,bool Remember);

/// <summary>Independent OAuth scope; only opaque epochs and flags enter coordination files.</summary>
public sealed class OAuthSessionCoordinator
{
    public const string Account="international-oauth-session";
    private readonly string account,issuer,resource,region;
    private sealed record State(int SchemaVersion,string Epoch,bool Ready,bool Remembered);
    private sealed record Envelope(int SchemaVersion,string Epoch,string Issuer,string Resource,OAuthToken Token);
    private readonly ICredentialStore store;
    private readonly string statePath;
    private OAuthSessionSnapshot? cached;
    public OAuthSessionCoordinator(ICredentialStore store,string? root=null,string region=Aholo3DRegions.International)
    {
        this.store=store;
        var config=Aholo3DRegions.Get(region);this.region=config.Id;
        account=region+"-oauth-session";issuer=config.OAuthIssuer;resource=config.OAuthResource;
        string scope=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(CredentialStoreFactory.DefaultService+"\0"+account))).ToLowerInvariant();
        statePath=Path.Combine(root??CredentialCoordinator.DefaultRoot,scope+".json");
    }
    public async Task<OAuthSessionSnapshot> ReadAsync(CancellationToken ct=default)
    {
        using var gate=await LockAsync(ct).ConfigureAwait(false);
        return await ReadLockedAsync(ReadState(),ct).ConfigureAwait(false);
    }
    private async Task<OAuthSessionSnapshot> ReadLockedAsync(State state,CancellationToken ct)
    {
        if(!state.Ready){cached=null;throw Changed();}
        if(cached?.Epoch==state.Epoch)return cached;
        cached=null;
        if(!state.Remembered)return new(null,false,state.Epoch);
        string? json=await store.GetAsync(account,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if(json is null||json.Length>512*1024)throw Changed();
        Envelope? saved;
        try{saved=JsonSerializer.Deserialize<Envelope>(json);}catch(JsonException){throw Changed();}
        if(saved is null||saved.SchemaVersion!=1||saved.Epoch!=state.Epoch||saved.Issuer!=issuer||saved.Resource!=resource||saved.Token?.Region!=region)throw Changed();
        Validate(saved.Token);
        return cached=new(saved.Token,true,state.Epoch);
    }
    public async Task<OAuthSessionLease> BeginAsync(string? expectedEpoch,bool refresh,bool remember,CancellationToken ct)
    {
        using var gate=await LockAsync(ct).ConfigureAwait(false);
        State state=refresh?ReadState():new(1,"",false,false);OAuthToken? previous=null;
        if(refresh)
        {
            if(expectedEpoch is null||expectedEpoch!=state.Epoch)throw Changed();
            previous=(await ReadLockedAsync(state,ct).ConfigureAwait(false)).Token;
            if(previous is null||string.IsNullOrWhiteSpace(previous.RefreshToken))throw Changed();
        }
        ct.ThrowIfCancellationRequested();cached=null;
        state=new(1,Guid.NewGuid().ToString("N"),false,false);WriteState(state);
        // Reserve one refresh/login before network work. A crash or ambiguous rotated refresh
        // must not silently restore the earlier token; explicit sign-in remains available.
        await store.DeleteAsync(account,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return new(state.Epoch,previous,remember);
    }
    public async Task<OAuthSessionSnapshot> CompleteAsync(OAuthSessionLease lease,OAuthToken token,CancellationToken ct)
    {
        Validate(token);if(token.Region!=region)throw Changed();
        using var gate=await LockAsync(ct).ConfigureAwait(false);
        State state=ReadState();
        if(state.Epoch!=lease.Epoch||state.Ready)throw Changed();
        ct.ThrowIfCancellationRequested();bool remembered=false;string? warning=null;
        if(lease.Remember)
        {
            try
            {
                string json=JsonSerializer.Serialize(new Envelope(1,lease.Epoch,issuer,resource,token));
                if(json.Length>512*1024)throw Changed();
                await store.SetAsync(account,json,ct).ConfigureAwait(false);remembered=true;
            }
            catch(OperationCanceledException){throw;}
            catch(Exception){warning="Signed in for this process only. The account could not be saved securely.";}
        }
        ct.ThrowIfCancellationRequested();
        // A partially written secure item is unreachable when Remembered is false.
        WriteState(state with{Ready=true,Remembered=remembered});
        return cached=new(token,remembered,state.Epoch,warning);
    }
    public async Task<bool> SignOutAsync(string? onlyEpoch=null,CancellationToken ct=default)
    {
        using var gate=await LockAsync(ct).ConfigureAwait(false);
        State state=onlyEpoch is null?new(1,"",false,false):ReadState();
        if(onlyEpoch is not null&&onlyEpoch!=state.Epoch)return false;
        cached=null;state=new(1,Guid.NewGuid().ToString("N"),false,false);WriteState(state);
        await store.DeleteAsync(account,ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();WriteState(state with{Ready=true});return true;
    }
    internal static void Validate(OAuthToken? token)
    {
        static bool Opaque(string? value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=32768&&value.All(c=>c>=0x21&&c<=0x7e);
        if(token is null||token.Region is not (Aholo3DRegions.International or Aholo3DRegions.China)||!Opaque(token.AccessToken)||token.RefreshToken is not null&&!Opaque(token.RefreshToken)||
            string.IsNullOrWhiteSpace(token.ClientId)||token.ClientId.Length>2048||token.ClientId.Any(char.IsControl)||
            !string.Equals(token.TokenType,"Bearer",StringComparison.OrdinalIgnoreCase)||token.Scope!="get_user_info"||
            token.ExpiresAt<=DateTimeOffset.UnixEpoch||token.ExpiresAt>DateTimeOffset.UtcNow.AddDays(366))throw Changed();
    }
    private State ReadState()
    {
        if(!File.Exists(statePath)){var fresh=new State(1,Guid.NewGuid().ToString("N"),true,false);WriteState(fresh);return fresh;}
        try
        {
            using var input=File.OpenRead(statePath);if(input.Length>8192)throw Changed();
            var state=JsonSerializer.Deserialize<State>(input);
            if(state?.SchemaVersion!=1||!Guid.TryParseExact(state.Epoch,"N",out _))throw Changed();
            return state;
        }
        catch(JsonException){throw Changed();}
    }
    private void WriteState(State state)
    {
        string temporary=statePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            var options=new FileStreamOptions{Mode=FileMode.CreateNew,Access=FileAccess.Write,Share=FileShare.None};
            if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
            using(var output=new FileStream(temporary,options)){JsonSerializer.Serialize(output,state);output.Flush(true);}
            File.Move(temporary,statePath,true);
        }
        finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private async Task<FileStream> LockAsync(CancellationToken ct)
    {
        string directory=Path.GetDirectoryName(statePath)!;
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var options=new FileStreamOptions{Mode=FileMode.OpenOrCreate,Access=FileAccess.ReadWrite,Share=FileShare.None};
        if(!OperatingSystem.IsWindows())options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
        var clock=Stopwatch.StartNew();
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            try{return new FileStream(statePath+".lock",options);}
            catch(IOException)when(File.Exists(statePath+".lock"))
            {
                if(clock.Elapsed>=TimeSpan.FromSeconds(10))throw new IOException("Another account storage operation is active. Try again after it finishes.");
                await Task.Delay(50,ct).ConfigureAwait(false);
            }
        }
        // Keep the lock pathname: replacing/deleting it permits different inode owners.
    }
    private static ProviderException Changed()=>new("oauth_session_changed","The saved account session changed or is incomplete. Sign in again; API Key access is unchanged.");
}
