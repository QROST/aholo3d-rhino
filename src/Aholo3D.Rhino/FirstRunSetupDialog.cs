using System.Diagnostics;
using Aholo3D.Core;
using Eto.Forms;
using Eto.Drawing;
namespace Aholo3D.Rhino;

/// <summary>One-time setup offered when no connection exists yet: region, optional account identity, and the API Key that all cloud work requires.</summary>
internal sealed class FirstRunSetupDialog : Dialog
{
    private const string ConsoleUrl = "https://labs.aholo.ai/api-keys";
    private readonly Aholo3DSession session;
    private readonly RadioButton international = new() { Text = T("International") };
    private readonly RadioButton china;
    private readonly PasswordBox key = new();
    private readonly Label status = UiLayout.Text("");
    private readonly Button paste = new() { Text = T("Paste from clipboard") };
    private readonly Button open = new() { Text = T("Open the key console in your browser") };
    private readonly Button account = new() { Text = T("Sign in with Manycore (optional)") };
    private readonly Button finish = new() { Text = T("Validate and finish") };
    private readonly Button skip = new() { Text = T("Skip for now") };
    private bool closed, busy;
    private static string T(string s) => UiText.Get(s);
    public FirstRunSetupDialog(Aholo3DSession session)
    {
        this.session = session; china = new RadioButton(international) { Text = T("China") };
        Title = T("Aholo3D first-run setup"); ClientSize = new Size(480, 440); MinimumSize = new Size(320, 260); Resizable = true;
        paste.Click += (_, _) => { if (Clipboard.Instance.Text is { } text && text.Trim().Length > 0) key.Text = text.Trim(); };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(ConsoleUrl) { UseShellExecute = true });
        account.Click += (_, _) => { if (busy) return; if (session.Client is IAccountSessionClient accountClient) { using var login = new AccountLoginDialog(accountClient, Region()); login.ShowModal(this); } };
        finish.Click += (_, _) => _ = ConnectAsync();
        skip.Click += (_, _) => { FirstRunGuard.Dismiss(session.Store.Root); Close(); };
        Closed += (_, _) => { closed = true; };
        Content = UiLayout.DialogBody(UiLayout.Scroll(
            UiLayout.Text(T("Choose your service region. Regions have separate API Keys.")), international, china,
            UiLayout.Text(T("Account sign-in is optional and never replaces the API Key. All generation and rendering uses the API Key of the selected region.")), account,
            UiLayout.Text(T("Create or copy an API Key there, then paste it below.")), open, UiLayout.Text(T("API Key")), key, paste, skip, status), finish);
        UpdateButtons();
    }
    private string Region() => china.Checked == true ? Aholo3DRegions.China : Aholo3DRegions.International;
    private async Task ConnectAsync()
    {
        if (busy) return; string value = key.Text?.Trim() ?? ""; key.Text = "";
        if (value.Length == 0) { status.Text = T("Paste an API Key first."); return; }
        busy = true; UpdateButtons(); status.Text = UiText.Status("Connecting");
        try
        {
            await session.ConnectAsync(new(value, true, "world", Region()), CancellationToken.None);
            FirstRunGuard.Dismiss(session.Store.Root);
            if (!closed) { status.Text = T("Connected. Service permissions and credits are checked per operation."); Close(); }
        }
        catch (Exception error) { if (!closed) status.Text = Aholo3DHost.FriendlyError(error); }
        finally { busy = false; if (!closed) UpdateButtons(); }
    }
    private void UpdateButtons()
    {
        paste.Enabled = !busy; open.Enabled = !busy; account.Enabled = !busy; finish.Enabled = !busy; skip.Enabled = !busy;
    }
}
