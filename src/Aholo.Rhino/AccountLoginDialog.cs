using Aholo.Core;
using Eto.Forms;
using Eto.Drawing;
namespace Aholo.Rhino;

internal sealed class AccountLoginDialog : Dialog
{
    private readonly IAccountSessionClient client;
    private readonly Label status = new() { Wrap = WrapMode.Word };
    private readonly Button signIn = new() { Text = T("Sign in with Manycore") };
    private readonly Button refresh = new() { Text = T("Refresh account session") };
    private readonly Button signOut = new() { Text = T("Sign out of account") };
    private readonly CheckBox remember=new(){Text=T("Remember this account on this device"),Checked=false};
    private bool rememberEdited,syncingRemember;
    private readonly UITimer timer = new() { Interval = 1 };
    private AccountSessionState state = new();
    private bool closed, busy;
    private Task lastWork = Task.CompletedTask;
    private Guid? ownedAttempt;
    private static string T(string s) => UiText.Get(s);
    public AccountLoginDialog(IAccountSessionClient client)
    {
        this.client = client; Title = T("Account sign-in (optional)"); ClientSize = new Size(480,430); Resizable = true;
        var close = new Button { Text = T("Close") }; close.Click += (_,_) => Close();
        Content = new StackLayout { Padding = 14, Spacing = 10, HorizontalContentAlignment = HorizontalAlignment.Stretch, Items = {
            new Label { Wrap = WrapMode.Word, Text = T("International account login is optional. Generation, history and rendering still require an API Key. Account login does not connect, replace or validate that Key.") },
            status, remember, signIn, refresh, signOut,
            new Label { Wrap = WrapMode.Word, Text = T("Remembered account tokens use macOS Keychain or Windows user encryption. Otherwise they last only for this process. Sign-in replaces the shared account on this device; sign-out removes its saved session across Rhino instances. Neither action revokes server tokens or changes your API Key.") }, close } };
        remember.CheckedChanged+=(_,_)=>{if(!syncingRemember)rememberEdited=true;};
        signIn.Click += (_,_) => Start("begin"); refresh.Click += (_,_) => Start("refresh");
        signOut.Click += (_,_) => { if(!busy){rememberEdited=false;lastWork = RunAsync("signOut");} };
        timer.Elapsed += (_,_) => { if(!busy&&!closed) lastWork = RunAsync("status"); };
        Shown += (_,_) => { timer.Start(); lastWork = RunAsync("status"); };
        Closed += (_,_) => { closed = true; timer.Stop(); timer.Dispose(); _ = CancelOwnedAfterWorkAsync(); };
        Update();
    }
    private void Start(string action)
    { if(busy)return; ownedAttempt = Guid.NewGuid();bool save=remember.Checked==true;rememberEdited=true;lastWork = RunAsync(action, ownedAttempt,save); }
    private async Task RunAsync(string action, Guid? id = null,bool? save=null)
    {
        busy = true; Update();
        try
        {
            var next=await client.AccountAsync(action,id,save);
            if(!closed)
            {
                state=next;
                // A duplicate begin may return another window's in-flight attempt. Own only
                // our accepted attempt, and release ownership whenever it is no longer pending.
                if(ownedAttempt.HasValue&&(id.HasValue&&state.AttemptId!=id||state.Stage is not ("SigningIn" or "Refreshing")))
                {ownedAttempt=null;rememberEdited=false;}
                Update();
            }
        }
        catch(Exception error) { if(!closed) status.Text = AholoHost.FriendlyError(error); }
        finally { busy = false; if(!closed) UpdateButtons(); }
    }
    private void Update()
    {
        if(closed)return;
        status.Text = T(state.Stage switch { "SigningIn" => "Waiting for browser sign-in", "SignedIn" => "Account session established", "Expired" => "Account session expired", "Refreshing" => "Refreshing account session", "Failed" => "Account sign-in failed", _ => "Account signed out" })
            + (state.ExpiresAt is null ? "" : "\n" + UiText.Format("Account session expiry: {0}",state.ExpiresAt.Value.ToLocalTime().ToString("g")))
            + (state.Error is null ? "" : "\n" + UiText.TranslateKnownMessage(state.Error));
        if(!rememberEdited&&state.Stage is not ("SigningIn" or "Refreshing"))
        {syncingRemember=true;try{remember.Checked=state.Remembered;}finally{syncingRemember=false;}}
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        if(closed)return; bool pending = state.Stage is "SigningIn" or "Refreshing";
        signIn.Enabled = !busy&&!pending; refresh.Enabled = !busy&&!pending&&state.CanRefresh&&state.Stage is "SignedIn" or "Expired";
        remember.Enabled=!busy&&!pending;
        signOut.Enabled = !busy&&state.Stage != "SignedOut";
    }
    private async Task CancelOwnedAfterWorkAsync()
    {
        try { await lastWork; if(ownedAttempt is{} id) await client.AccountAsync("cancel",id); }
        catch { /* No token or raw OAuth failure enters host diagnostics; the attempt has its own deadline. */ }
    }
}
