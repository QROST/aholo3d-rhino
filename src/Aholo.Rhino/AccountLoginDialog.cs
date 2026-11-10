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
    private readonly Button signOut = new() { Text = T("Sign out or cancel account login") };
    private readonly UITimer timer = new() { Interval = 1 };
    private AccountSessionState state = new();
    private bool closed, busy;
    private Task lastWork = Task.CompletedTask;
    private Guid? ownedAttempt;
    private static string T(string s) => UiText.Get(s);
    public AccountLoginDialog(IAccountSessionClient client)
    {
        this.client = client; Title = T("Account sign-in (optional)"); ClientSize = new Size(480,370); Resizable = true;
        var close = new Button { Text = T("Close") }; close.Click += (_,_) => Close();
        Content = new StackLayout { Padding = 14, Spacing = 10, HorizontalContentAlignment = HorizontalAlignment.Stretch, Items = {
            new Label { Wrap = WrapMode.Word, Text = T("International account login is optional. Generation, history and rendering still require an API Key. Account login does not connect, replace or validate that Key.") },
            status, signIn, refresh, signOut,
            new Label { Wrap = WrapMode.Word, Text = T("Account tokens stay in this background process only. Closing Rhino or resetting the process signs out. Signing out here clears this local account session; it does not revoke server tokens or disconnect your API Key.") }, close } };
        signIn.Click += (_,_) => Start("begin"); refresh.Click += (_,_) => Start("refresh");
        signOut.Click += (_,_) => { if(!busy) lastWork = RunAsync("signOut"); };
        timer.Elapsed += (_,_) => { if(!busy&&!closed) lastWork = RunAsync("status"); };
        Shown += (_,_) => { timer.Start(); lastWork = RunAsync("status"); };
        Closed += (_,_) => { closed = true; timer.Stop(); timer.Dispose(); _ = CancelOwnedAfterWorkAsync(); };
        Update();
    }
    private void Start(string action)
    { if(busy)return; ownedAttempt = Guid.NewGuid(); lastWork = RunAsync(action, ownedAttempt); }
    private async Task RunAsync(string action, Guid? id = null)
    {
        busy = true; Update();
        try { var next = await client.AccountAsync(action,id); if(!closed) { state = next; if(state.Stage is "SignedIn" or "Expired" && state.AttemptId == ownedAttempt) ownedAttempt = null; Update(); } }
        catch(Exception error) { if(!closed) status.Text = AholoHost.FriendlyError(error); }
        finally { busy = false; if(!closed) UpdateButtons(); }
    }
    private void Update()
    {
        if(closed)return;
        status.Text = T(state.Stage switch { "SigningIn" => "Waiting for browser sign-in", "SignedIn" => "Account session established", "Expired" => "Account session expired", "Refreshing" => "Refreshing account session", "Failed" => "Account sign-in failed", _ => "Account signed out" })
            + (state.ExpiresAt is null ? "" : "\n" + UiText.Format("Account session expiry: {0}",state.ExpiresAt.Value.ToLocalTime().ToString("g")))
            + (state.Error is null ? "" : "\n" + UiText.TranslateKnownMessage(state.Error));
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        if(closed)return; bool pending = state.Stage is "SigningIn" or "Refreshing";
        signIn.Enabled = !busy&&!pending; refresh.Enabled = !busy&&!pending&&state.Stage is "SignedIn" or "Expired";
        signOut.Enabled = !busy&&state.Stage != "SignedOut";
    }
    private async Task CancelOwnedAfterWorkAsync()
    {
        try { await lastWork; if(ownedAttempt is{} id) await client.AccountAsync("cancel",id); }
        catch { /* No token or raw OAuth failure enters host diagnostics; the attempt has its own deadline. */ }
    }
}
