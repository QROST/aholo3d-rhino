using Aholo.Core;
using Eto.Drawing;
using Eto.Forms;
namespace Aholo.Rhino;

internal sealed class CloudLibraryDialog : Dialog<LocalTask?>
{
    private readonly AholoSession session;
    private readonly string scope, connectionId;
    private readonly CancellationTokenSource lifetime;
    private readonly DropDown service = new() { DataStore = new[] { "World", "Lux3D", UiText.Get("Spatial understanding"), "RenderCloud" }, SelectedIndex = 0 };
    private readonly DropDown filter = new() { DataStore = new[] { UiText.Get("All statuses"), UiText.Status("PENDING"), UiText.Status("RUNNING"), UiText.Status("SUCCEEDED"), UiText.Status("FAILED") }, SelectedIndex = 0 };
    private readonly DropDown operation = new();
    private readonly ListBox rows = new() { Height = 240 };
    private readonly TextBox remoteId = new() { PlaceholderText = UiText.Get("Existing remote task ID") };
    private readonly Label status = new() { Wrap = WrapMode.Word };
    private readonly Button load = new() { Text = UiText.Get("Refresh cloud history") }, previous = new() { Text = UiText.Get("Previous") }, next = new() { Text = UiText.Get("Next") }, restore = new() { Text = UiText.Get("Restore to Library") };
    private readonly string[] luxOperations = ServiceOperations.All.Where(o => o.Service == TaskService.Lux3D).Select(o => o.Id).ToArray();
    private int page = 1;
    private bool hasMore, busy, closed, disposed;
    private TaskService Service => service.SelectedIndex switch { 1 => TaskService.Lux3D, 2 => TaskService.SpatialUnderstanding, 3 => TaskService.RenderCloud, _ => TaskService.World };
    internal CloudLibraryDialog(AholoSession session, string scope, CancellationToken parentToken)
    {
        this.session = session; this.scope = scope + "cloud"; lifetime=CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        connectionId = session.Connection.ConnectionId ?? "";
        Title = UiText.Get("Cloud history and recovery"); ClientSize = new Size(580,620); Resizable = true;
        operation.DataStore = luxOperations.Select(id => UiText.Get(ServiceOperations.Get(id).Title)).ToArray(); operation.SelectedIndex = -1;
        var operationBox = new StackLayout { Spacing = 6, Visible = false, Items = { new Label { Text = UiText.Get("Choose the original Lux3D tool"), Wrap = WrapMode.Word }, operation } };
        service.SelectedIndexChanged += (_,_) => { page = 1; hasMore = false; rows.DataStore = Array.Empty<CloudTaskRow>(); remoteId.Text = ""; operationBox.Visible = Service == TaskService.Lux3D; status.Text = UiText.Get("Select a service, then load history or enter an existing ID."); Update(); };
        filter.SelectedIndexChanged += (_,_) => { page = 1; hasMore = false; rows.DataStore = Array.Empty<CloudTaskRow>(); rows.SelectedIndex=-1; remoteId.Text=""; Update(); };
        rows.SelectedIndexChanged += (_,_) => { if (rows.SelectedValue is CloudTaskRow row) remoteId.Text = row.RemoteId; };
        load.Click += (_,_) => _ = RunAsync(token=>LoadAsync(page,token));
        previous.Click += (_,_) => _ = RunAsync(token=>LoadAsync(page-1,token));
        next.Click += (_,_) => _ = RunAsync(token=>LoadAsync(page+1,token));
        restore.Click += (_,_) => _ = RunAsync(async token =>
        {
            string? tool = Service == TaskService.Lux3D && operation.SelectedIndex >= 0 ? luxOperations[operation.SelectedIndex] : null;
            var task = await session.Client.RestoreRemoteAsync(new(Service, remoteId.Text.Trim(), tool, connectionId), token);
            session.Observe(task); if (!closed) Close(task);
        });
        var close = new Button { Text = UiText.Get("Close") }; close.Click += (_,_) => Close(null);
        var cancel = new Button { Text = UiText.Get("Cancel local operation") }; cancel.Click += (_,_) => session.Operations.Cancel(scope + "cloud");
        Content = new Scrollable { Content = new StackLayout { Padding = 12, Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Stretch, Items = {
            new Label { Text = UiText.Get("Browse existing tasks without creating or charging a new generation."), Wrap = WrapMode.Word }, service, filter, load, rows,
            new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items = {previous,next} },
            remoteId, operationBox, new Label { Text = UiText.Get("RenderCloud has no public history list; enter its existing operation ID. Restoring a result does not restart generation or rendering."), Wrap = WrapMode.Word },
            restore, status, new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items = {cancel,close} } } } };
        Closed += (_,_) => { if(!closed){closed = true; lifetime.Cancel();} };
        Update();
    }
    private async Task LoadAsync(int requestedPage,CancellationToken token)
    {
        remoteId.Text=""; rows.SelectedIndex=-1;
        string? value = filter.SelectedIndex switch { 1 => "PENDING", 2 => "RUNNING", 3 => "SUCCEEDED", 4 => "FAILED", _ => null };
        var result = await session.Client.CloudHistoryAsync(new(Service, requestedPage, 20, value), token);
        if (closed) return;
        if (result.ConnectionId != connectionId) throw new InvalidOperationException(UiText.Get("The connection changed. Reload cloud history before restoring this result."));
        rows.DataStore = result.Items; rows.SelectedIndex=-1; page=result.Page; hasMore = result.HasMore;
        status.Text = UiText.Format("Cloud page {0} · {1} tasks", result.Page, result.Total);
    }
    private async Task RunAsync(Func<CancellationToken,Task> work)
    {
        if (busy || closed) return; busy = true; Update();
        try
        {
            if (!session.Connection.Connected || session.Connection.ConnectionId != connectionId) throw new InvalidOperationException(UiText.Get("The connection changed. Close this window and reconnect in Settings."));
            await session.Operations.RunAsync(scope, "Cloud history", work, lifetime.Token);
        }
        catch (Exception error) { if (!closed) status.Text = AholoHost.FriendlyError(error); }
        finally { busy = false; if (!closed) Update(); }
    }
    private void Update()
    {
        rows.Enabled = service.Enabled = filter.Enabled = remoteId.Enabled = operation.Enabled = restore.Enabled = !busy;
        bool canList = Service != TaskService.RenderCloud;
        load.Enabled = !busy && canList; previous.Enabled = !busy && canList && page > 1; next.Enabled = !busy && canList && hasMore;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed) { disposed=true; closed = true; lifetime.Cancel(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
