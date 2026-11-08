using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Aholo.Core;
using Eto.Drawing;
using Eto.Forms;
using Rhino.UI;
using RhinoDoc = global::Rhino.RhinoDoc;
using OpenFileDialog = Eto.Forms.OpenFileDialog;
namespace Aholo.Rhino;

[Guid("676AA7F9-6F1F-4F15-9CE1-DB3CB70CF397")]
public sealed class AholoPanel : Panel, IPanel
{
    private readonly uint _serial;
    private readonly AholoClient _client = AholoPlugin.CreateClient();
    private CancellationTokenSource _lifetime = new();
    private readonly List<Button> _actions = new();
    private readonly List<LocalTask> _records = new();
    private readonly UITimer _poll = new() { Interval = 8 };
    private readonly UITimer _elapsed = new() { Interval = 1 };
    private readonly Stopwatch _clock = new();
    private readonly TabControl _tabs = new();
    private readonly PasswordBox _key = new();
    private readonly CheckBox _remember = new() { Text = global::Aholo.Core.UiText.Get("Save in secure local storage"), Checked = false };
    private readonly Label _connection = Info(global::Aholo.Core.UiText.Get("Not connected · International API Key"));
    private readonly TextArea _prompt = new() { Height = 100, Wrap = true };
    private readonly TextBox _worldName = new() { PlaceholderText = global::Aholo.Core.UiText.Get("Task name (optional)") };
    private readonly TextBox _image = new() { ReadOnly = true, PlaceholderText = global::Aholo.Core.UiText.Get("Optional: PNG / JPEG / WebP image") };
    private readonly TextBox _worldId = new() { PlaceholderText = global::Aholo.Core.UiText.Get("Existing Aholo World ID") };
    private readonly TextBox _scale = new() { Text = "1" };
    private readonly DropDown _axis = new() { DataStore = new[] { global::Aholo.Core.UiText.Get("Use world metadata (Z-up if unavailable)"), global::Aholo.Core.UiText.Get("Z-up"), global::Aholo.Core.UiText.Get("Y-up") }, SelectedIndex = 0 };
    private readonly DropDown _gsExportScope = new() { DataStore = new[] { global::Aholo.Core.UiText.Get("Selected top-level GS"), global::Aholo.Core.UiText.Get("All top-level GS (including hidden and locked)") }, SelectedIndex = 0 };
    private CancellationTokenSource? _localGsOperation;
    private readonly Button _cancelGs = new() { Text = global::Aholo.Core.UiText.Get("Cancel local GS operation"), Enabled = false };
    private readonly ListBox _worldTasks = new() { Height = 125 };
    private readonly ListBox _renderTasks = new() { Height = 125 };
    private readonly TextArea _worldDetail = new() { ReadOnly = true, Wrap = true, Height = 105 };
    private readonly TextArea _renderDetail = new() { ReadOnly = true, Wrap = true, Height = 105 };
    private readonly DropDown _resolution = new() { DataStore = new[] { "1280 × 720", "1920 × 1080" }, SelectedIndex = 0 };
    private readonly CheckBox _autoRefresh = new() { Text = global::Aholo.Core.UiText.Get("Auto-refresh active tasks (every 8 seconds)"), Checked = true };
    private readonly Label _operation = Info(global::Aholo.Core.UiText.Get("Ready"));
    private readonly TextArea _feedback = new() { ReadOnly = true, Height = 80, Wrap = true, Text = global::Aholo.Core.UiText.Get("World generates worlds; RenderCloud renders the Rhino selection. Results and tasks are stored locally.") };
    private readonly Button _generate;
    private readonly Button _render;
    private bool _busy, _disposed, _shown, _connected, _feedbackError;
    private int _pollCursor, _feedbackRevision;
    private string _operationName = "";
    private string? _connectionId;

    public AholoPanel(uint documentSerialNumber)
    {
        _serial = documentSerialNumber;
        var doc = RhinoDoc.FromRuntimeSerialNumber(_serial);
        _cancelGs.Click += (_, _) => _localGsOperation?.Cancel();
        _generate = ActionButton(global::Aholo.Core.UiText.Get("Generate world · Uses service credits"), GenerateAsync);
        _render = ActionButton(global::Aholo.Core.UiText.Get("Upload selection and render · Uses service credits"), RenderAsync);
        _generate.Enabled = _render.Enabled = false;
        _worldTasks.SelectedIndexChanged += (_, _) => DescribeTask("world");
        _renderTasks.SelectedIndexChanged += (_, _) => DescribeTask("render");
        _poll.Elapsed += (_, _) => Poll();
        _elapsed.Elapsed += (_, _) => { if (!_disposed && _busy) _operation.Text = _operationName + " · " + (int)_clock.Elapsed.TotalSeconds + global::Aholo.Core.UiText.Get(" s"); };
        _tabs.Pages.Add(new TabPage { Text = "World", Content = Column(
            Info(global::Aholo.Core.UiText.Get("Generate a 3D Gaussian Splatting world from text or one image. The current API works best for indoor scenes.")),
            _worldName, _prompt, _image,
            Row(LocalButton(global::Aholo.Core.UiText.Get("Choose image"), ChooseImage), LocalButton(global::Aholo.Core.UiText.Get("Clear"), () => _image.Text = "")),
            _generate,
            Info(global::Aholo.Core.UiText.Get("World history (World entries among the latest 100 local tasks)")), _worldTasks,
            Row(ActionButton(global::Aholo.Core.UiText.Get("Load history"), () => ReloadAsync()), ActionButton(global::Aholo.Core.UiText.Get("Refresh selected task"), () => RefreshSelectedAsync("world"))),
            _worldDetail,
            Row(ActionButton(global::Aholo.Core.UiText.Get("Download PLY"), () => DownloadSelectedAsync("world", "ply")), ActionButton(global::Aholo.Core.UiText.Get("Download SPZ"), () => DownloadSelectedAsync("world", "spz"))),
            Row(ActionButton(global::Aholo.Core.UiText.Get("Download panorama"), () => DownloadSelectedAsync("world", "image")), LocalButton(global::Aholo.Core.UiText.Get("Open panorama"), () => OpenSelectedImage("world"))),
            Info(global::Aholo.Core.UiText.Get("GS import scale: 1 world unit equals these document units (") + (doc?.ModelUnitSystem.ToString() ?? global::Aholo.Core.UiText.Get("Current document")) + ")"),
            _scale, _axis, ActionButton(global::Aholo.Core.UiText.Get("Place in current document · Experimental GS viewport"), PlaceSelectedAsync),
            Info(global::Aholo.Core.UiText.Get("Complete Gaussians and original PLY / SPZ are retained. Bounds appear during background preparation; GS returns after view motion stops. Experimental color, occlusion and navigation performance require native acceptance.")),
            Row(ActionButton(global::Aholo.Core.UiText.Get("Import local GS · PLY / USD / USDZ"), ImportLocalGsAsync), LocalButton(global::Aholo.Core.UiText.Get("Switch to Aholo GS"), () => GaussianViewport.Activate(Document()))),
            Info(global::Aholo.Core.UiText.Get("Local USD uses file units multiplied by the scale above. The up-axis selection applies; Auto reads the file. Ordinary mesh USD is not GS.")),
            _gsExportScope, ActionButton(global::Aholo.Core.UiText.Get("Export GS · USD / USDZ"), ExportLocalGsAsync), _cancelGs,
            Info(global::Aholo.Core.UiText.Get("Export uses GS, placements and document units at the time of the click. Only top-level GS is supported; ordinary geometry, nested blocks and hidden state are not exported. Complete SH is retained; official USD Gaussian fields use float32.")),
            Info(global::Aholo.Core.UiText.Get("Offline acceptance scene: AholoGsProbe. Diagnostics: AholoGsDiagnostics. Transparent Rhino material interleaving and nested blocks are unsupported; full clipping-plane behavior still needs acceptance.")),
            ActionButton(global::Aholo.Core.UiText.Get("Diagnostic: import PLY centers"), ImportLocalPlyAsync),
            _worldId, ActionButton(global::Aholo.Core.UiText.Get("Restore from an existing World ID"), ImportExistingWorldAsync)
        ) });
        _tabs.Pages.Add(new TabPage { Text = "RenderCloud", Content = Column(
            Info(global::Aholo.Core.UiText.Get("Select Rhino meshes, surfaces/polysurfaces or extrusions and activate a standard perspective view before rendering.")),
            Info(global::Aholo.Core.UiText.Get("Selected geometry and base colors will be uploaded with the current camera. Textures, lights, procedural materials and block instances are not converted in this version.")),
            _resolution,
            Info(global::Aholo.Core.UiText.Get("Output preserves the camera's horizontal field of view; vertical framing follows the output aspect ratio.")), _render,
            Info(global::Aholo.Core.UiText.Get("Render history (RenderCloud entries among the latest 100 local tasks)")), _renderTasks,
            Row(ActionButton(global::Aholo.Core.UiText.Get("Load history"), () => ReloadAsync()), ActionButton(global::Aholo.Core.UiText.Get("Refresh selected task"), () => RefreshSelectedAsync("render"))),
            _renderDetail, ActionButton(UiText.Get("Continue original render · Uses service credits"), ContinueRenderAsync),
            Row(ActionButton(global::Aholo.Core.UiText.Get("Download render"), () => DownloadSelectedAsync("render", "image")), LocalButton(global::Aholo.Core.UiText.Get("Open local render"), () => OpenSelectedImage("render"))),
            Info(global::Aholo.Core.UiText.Get("This page uses offline rendering. Realtime protocol groundwork is experimental; cloud viewports and World GS ingestion are not exposed yet. Failed or unconfirmed submissions never automatically create another paid task."))
        ) });
        _tabs.Pages.Add(new TabPage { Text = global::Aholo.Core.UiText.Get("Connection"), Content = Column(
            _connection, Info(global::Aholo.Core.UiText.Get("This version connects to Aholo international. Generation and rendering require a regional API Key; OAuth is not integrated.")),
            new Label { Text = global::Aholo.Core.UiText.Get("International API Key") }, _key, _remember,
            ActionButton(global::Aholo.Core.UiText.Get("Validate and connect"), ConnectAsync),
            Row(ActionButton(global::Aholo.Core.UiText.Get("Read connection status"), ReadConnectionAsync), ActionButton(global::Aholo.Core.UiText.Get("Disconnect and clear credentials"), DisconnectAsync)),
            Info(global::Aholo.Core.UiText.Get("Validation only queries existing tasks; it does not generate, render or guarantee available credits. The key is never stored in Rhino documents or task history.")),
            _autoRefresh, LocalButton(global::Aholo.Core.UiText.Get("Open local asset folder"), OpenAssetFolder),
            Info(global::Aholo.Core.UiText.Get("Closing the panel stops local waiting, not remote tasks. Reopen it to query history; verify server records before retrying an unconfirmed submission.")),
            Info("Aholo for Rhino " + AholoPlugin.DisplayVersion + global::Aholo.Core.UiText.Get("\nCuriousArc · qrost\nIndependent Aholo integration plugin."))
        ) });
        Content = new StackLayout
        {
            Padding = 10, Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { new Label { Text = "Aholo for Rhino", Font = new Font(SystemFont.Bold, 15) },
                Info(global::Aholo.Core.UiText.Get("Worlds and rendering · ") + (doc?.Name ?? global::Aholo.Core.UiText.Get("Current document"))), new StackLayoutItem(_tabs, true), _operation, _feedback }
        };
    }
    private static Label Info(string text) => new() { Text = text, Wrap = WrapMode.Word };
    private static Control Column(params Control[] controls)
    {
        var layout = new StackLayout { Padding = 8, Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        foreach (Control control in controls) layout.Items.Add(control);
        return new Scrollable { Content = layout };
    }
    private static Control Row(params Control[] controls)
    {
        var layout = new StackLayout { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (Control control in controls) layout.Items.Add(new StackLayoutItem(control, true));
        return layout;
    }
    private Button ActionButton(string title, Func<Task> action)
    {
        var button = new Button { Text = title };
        button.Click += (_, _) => _ = RunAsync(title, action);
        _actions.Add(button); return button;
    }
    private Button LocalButton(string title, Action action)
    {
        var button = new Button { Text = title };
        button.Click += (_, _) => { if (_busy || _disposed) return; try { action(); } catch (Exception ex) { Feedback(AholoHost.FriendlyError(ex), true); } };
        _actions.Add(button); return button;
    }
    private CancellationToken Token => _lifetime.Token;
    private RhinoDoc Document() => RhinoDoc.FromRuntimeSerialNumber(_serial) ?? throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The target document has closed. Reopen the panel in the target document."));
    private async Task RunAsync(string name, Func<Task> action, bool background = false)
    {
        if (_busy || _disposed || _lifetime.IsCancellationRequested) return;
        _busy = true; int revision = _feedbackRevision;
        _operationName = name; _operation.Text = name; _clock.Restart(); _elapsed.Start();
        foreach (Button button in _actions) button.Enabled = false;
        bool failed = false;
        try
        {
            await action();
            AholoHost.Ui(() => { if (!background && !_disposed && revision == _feedbackRevision) Feedback(global::Aholo.Core.UiText.Get("Operation completed.")); });
        }
        catch (Exception ex)
        {
            failed = true;
            AholoHost.Ui(() =>
            {
                if (_disposed) return;
                if (!background || !_feedbackError) Feedback(AholoHost.FriendlyError(ex), true);
            });
        }
        finally
        {
            AholoHost.Ui(() =>
            {
                _busy = false; _clock.Stop();
                if (_disposed) return;
                _elapsed.Stop(); _operation.Text = (failed ? global::Aholo.Core.UiText.Get("Operation did not finish") : global::Aholo.Core.UiText.Get("Ready")) + " · " + (int)_clock.Elapsed.TotalSeconds + global::Aholo.Core.UiText.Get(" s");
                foreach (Button button in _actions) button.Enabled = true;
                _generate.Enabled = _render.Enabled = _connected;
                if (_shown && _lifetime.IsCancellationRequested) StartPanelSession();
            });
        }
    }
    private void Feedback(string text, bool error = false)
    {
        if (_disposed) return;
        _feedbackRevision++; _feedbackError = error; _feedback.Text = (error ? global::Aholo.Core.UiText.Get("Action required: ") : "") + text;
    }
    private void SetConnection(ConnectionState state)
    {
        if (_disposed) return;
        _connected = state.Connected; _connectionId = state.ConnectionId;
        _connection.Text = state.Connected ? global::Aholo.Core.UiText.Get("International · Connected") + (state.Remembered ? global::Aholo.Core.UiText.Get(" · Credentials saved securely") : global::Aholo.Core.UiText.Get(" · This session only")) : global::Aholo.Core.UiText.Get("International · Not connected");
        _generate.Enabled = _render.Enabled = _connected && !_busy;
    }
    private async Task ConnectAsync()
    {
        string key = _key.Text; bool remember = _remember.Checked == true; _key.Text = "";
        var connection = await _client.ConnectAsync(new ConnectRequest(key, remember), Token);
        AholoHost.Ui(() => { SetConnection(connection); Feedback(global::Aholo.Core.UiText.Get("Connected. Submit tasks from World or RenderCloud. Service credits are determined by your Aholo account.")); });
        await ReloadAsync();
    }
    private async Task ReadConnectionAsync()
    {
        var connection = await _client.GetConnectionAsync(Token);
        AholoHost.Ui(() => SetConnection(connection));
    }
    private async Task DisconnectAsync()
    {
        var connection = await _client.DisconnectAsync(Token);
        AholoHost.Ui(() => { SetConnection(connection); _key.Text = ""; Feedback(global::Aholo.Core.UiText.Get("Disconnected and cleared locally saved credentials. Remote tasks are not cancelled.")); });
    }
    private void ChooseImage()
    {
        using var dialog = new OpenFileDialog { Title = global::Aholo.Core.UiText.Get("Choose a world-generation reference image"), MultiSelect = false };
        dialog.Filters.Add(new FileFilter(global::Aholo.Core.UiText.Get("Images"), ".png", ".jpg", ".jpeg", ".webp"));
        if (dialog.ShowDialog(this) == DialogResult.Ok) _image.Text = dialog.FileName;
    }
    private async Task GenerateAsync()
    {
        string prompt = _prompt.Text.Trim(), image = _image.Text.Trim(), name = _worldName.Text.Trim();
        if (prompt.Length == 0 && image.Length == 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Enter a scene description or choose a reference image."));
        if (name.Length == 0) name = prompt.Length == 0 ? global::Aholo.Core.UiText.Get("World from image") : prompt[..Math.Min(prompt.Length, 45)];
        var task = await _client.GenerateWorldAsync(new GenerateWorldRequest(name, prompt, image.Length == 0 ? null : image), Token);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback((task.Error is null ? null : UiText.TranslateKnownMessage(task.Error)) ?? global::Aholo.Core.UiText.Get("World task recorded. When generation finishes, download PLY and click Place in current document."), task.Error is not null));
    }
    private async Task ImportExistingWorldAsync()
    {
        string id = _worldId.Text.Trim();
        if (id.Length == 0) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Enter an existing World ID."));
        var task = await _client.ImportWorldAsync(id, Token);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback(global::Aholo.Core.UiText.Get("Loaded the original world task; no new generation was submitted.")));
    }
    private async Task ContinueRenderAsync()
    {
        var task = Selected("render");
        var updated = await _client.ContinueRenderAsync(task.Id, Token);
        AholoHost.Ui(() => ObserveTask(updated));
    }
    private async Task RenderAsync()
    {
        int width = _resolution.SelectedIndex == 1 ? 1920 : 1280, height = _resolution.SelectedIndex == 1 ? 1080 : 720;
        RenderSnapshot snapshot = AholoHost.CaptureRender(Document(), width, height);
        CancellationToken ct = Token;
        string path = Path.Combine(TaskStore.DefaultRoot, "exports", Guid.NewGuid().ToString("N") + ".glb");
        AholoHost.Ui(() => _operationName = global::Aholo.Core.UiText.Get("Preparing selection geometry"));
        await Task.Run(() => { Directory.CreateDirectory(Path.GetDirectoryName(path)!); GlbWriter.Write(path, snapshot.Meshes, ct); }, ct);
        // Capture already reflects the user's selected document and camera; no document mutations occur during submission.
        AholoHost.Ui(() => _operationName = global::Aholo.Core.UiText.Get("Uploading selection and submitting render"));
        var task = await _client.RenderAsync(new RenderRequest(snapshot.Name, path, snapshot.Camera, width, height), ct);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback((task.Error is null ? null : UiText.TranslateKnownMessage(task.Error)) ?? global::Aholo.Core.UiText.Get("Selection render task recorded. Query the original task for mesh processing and rendering; download the render when finished."), task.Error is not null));
    }
    private LocalTask Selected(string kind)
    {
        var list = kind == "world" ? _worldTasks : _renderTasks;
        return list.SelectedValue as LocalTask ?? throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Select a task first."));
    }
    private async Task ReloadAsync(Guid? selected = null)
    {
        var tasks = await _client.GetTasksAsync(Token);
        AholoHost.Ui(() =>
        {
            if (_disposed) return;
            Guid? world = selected ?? (_worldTasks.SelectedValue as LocalTask)?.Id, render = selected ?? (_renderTasks.SelectedValue as LocalTask)?.Id;
            _records.Clear(); _records.AddRange(tasks);
            SetList(_worldTasks, _records.Where(t => t.Kind == "world").ToArray(), world);
            SetList(_renderTasks, _records.Where(t => t.Kind == "render").ToArray(), render);
            DescribeTask("world"); DescribeTask("render");
        });
    }
    private void ObserveTask(LocalTask task, bool select = true)
    {
        if (_disposed) return;
        Guid? world = select && task.Kind == "world" ? task.Id : (_worldTasks.SelectedValue as LocalTask)?.Id;
        Guid? render = select && task.Kind == "render" ? task.Id : (_renderTasks.SelectedValue as LocalTask)?.Id;
        _records.RemoveAll(t => t.Id == task.Id); _records.Add(task);
        var latest = _records.OrderByDescending(t => t.CreatedAt).Take(100).ToArray();
        _records.Clear(); _records.AddRange(latest);
        SetList(_worldTasks, latest.Where(t => t.Kind == "world").ToArray(), world);
        SetList(_renderTasks, latest.Where(t => t.Kind == "render").ToArray(), render);
        DescribeTask("world"); DescribeTask("render");
    }
    private static void SetList(ListBox list, LocalTask[] tasks, Guid? selected)
    {
        list.DataStore = tasks;
        int index = Array.FindIndex(tasks, t => t.Id == selected);
        list.SelectedIndex = index >= 0 ? index : tasks.Length > 0 ? 0 : -1;
    }
    private void DescribeTask(string kind)
    {
        var list = kind == "world" ? _worldTasks : _renderTasks;
        var detail = kind == "world" ? _worldDetail : _renderDetail;
        if (list.SelectedValue is not LocalTask task) { detail.Text = global::Aholo.Core.UiText.Get("No task selected."); return; }
        string progress = task.Progress.HasValue ? global::Aholo.Core.UiText.Get(" · Progress ") + task.Progress.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";
        string id = task.WorldId is not null ? "\nWorld ID: " + task.WorldId : task.MeshTaskId is not null ? global::Aholo.Core.UiText.Get("\nMesh processing ID: ") + task.MeshTaskId : "";
        if (task.OperationId is not null) id += global::Aholo.Core.UiText.Get("\nRender operation ID: ") + task.OperationId;
        string cache = task.Kind == "world" ? global::Aholo.Core.UiText.Get("\nLocal assets: ") + (task.PlyPath is null ? global::Aholo.Core.UiText.Get("PLY not downloaded") : global::Aholo.Core.UiText.Get("PLY downloaded")) + " / " + (task.SpzPath is null ? global::Aholo.Core.UiText.Get("SPZ not downloaded") : global::Aholo.Core.UiText.Get("SPZ downloaded")) : global::Aholo.Core.UiText.Get("\nRender image: ") + (task.ImagePath is null ? global::Aholo.Core.UiText.Get("Not downloaded") : global::Aholo.Core.UiText.Get("Downloaded"));
        detail.Text = task.Name + "\n" + UiText.Status(task.Status) + progress + id + cache + (string.IsNullOrWhiteSpace(task.Error) ? "" : "\n" + UiText.TranslateKnownMessage(task.Error));
    }
    private async Task RefreshSelectedAsync(string kind)
    {
        var selected = Selected(kind);
        var task = await _client.RefreshAsync(selected.Id, Token);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback(global::Aholo.Core.UiText.Get("Original task status: ") + UiText.Status(task.Status) + (task.Error is null ? "" : ". " + UiText.TranslateKnownMessage(task.Error)), task.Error is not null));
    }
    private async Task DownloadSelectedAsync(string kind, string asset)
    {
        LocalTask selected = Selected(kind);
        var task = await _client.DownloadAsync(new DownloadRequest(selected.Id, asset), Token);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback(asset.ToUpperInvariant() + global::Aholo.Core.UiText.Get(" saved to the local task asset folder.")));
    }
    private double ImportScale()
    {
        if ((!double.TryParse(_scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) && !double.TryParse(_scale.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out scale)) || !double.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Enter an import scale greater than zero, such as 1 or 1000."));
        return scale;
    }
    private string ImportAxis(string? metadata) => _axis.SelectedIndex == 2 ? "Y" : _axis.SelectedIndex == 1 ? "Z" : string.Equals(metadata, "Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "Z";
    private async Task PlaceSelectedAsync()
    {
        LocalTask task = Selected("world");
        var target = AholoHost.CaptureTarget(Document()); double scale = ImportScale(); int axisChoice = _axis.SelectedIndex;
        CancellationToken ct = Token;
        if (task.PlyPath is null || !File.Exists(task.PlyPath)) task = await _client.DownloadAsync(new DownloadRequest(task.Id, "ply"), ct);
        if (task.PlyPath is null) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The original task has no PLY yet. Refresh generation status first."));
        AholoHost.Ui(() => _operationName = global::Aholo.Core.UiText.Get("Reading complete GS and preparing native viewport"));
        string axis = axisChoice == 2 ? "Y" : axisChoice == 1 ? "Z" : string.Equals(task.UpAxis, "Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "Z";
        string message = await GaussianDocument.ImportAsync(task.PlyPath, target, scale, axis, task.Id, ct);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback(message));
    }
    private async Task ImportLocalGsAsync()
    {
        using var dialog = new OpenFileDialog { Title = global::Aholo.Core.UiText.Get("Choose complete 3DGS PLY / USD / USDZ"), MultiSelect = false };
        dialog.Filters.Add(new FileFilter(global::Aholo.Core.UiText.Get("Gaussian Splat scene"), ".ply", ".usd", ".usda", ".usdc", ".usdz"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        var target = AholoHost.CaptureTarget(Document());
        double scale = ImportScale();
        string axis = _axis.SelectedIndex == 2 ? "Y" : _axis.SelectedIndex == 1 ? "Z" : "Auto";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        _localGsOperation = cancellation; _cancelGs.Enabled = true;
        try
        {
            var progress = new Progress<string>(message => AholoHost.Ui(() => { if (!_disposed && ReferenceEquals(_localGsOperation, cancellation)) _operationName = message; }));
            string message = await GaussianDocument.ImportSceneAsync(dialog.FileName, target, scale, axis, true, cancellation.Token, progress).ConfigureAwait(false);
            AholoHost.Ui(() => Feedback(message));
        }
        catch (OperationCanceledException) { AholoHost.Ui(() => Feedback(global::Aholo.Core.UiText.Get("Local GS import cancelled; the source file is retained."))); }
        finally { await AholoHost.OnUi(() => { _localGsOperation = null; if (!_disposed) _cancelGs.Enabled = false; return true; }).ConfigureAwait(false); }
    }
    private async Task ExportLocalGsAsync()
    {
        var snapshot = GaussianDocument.CaptureExport(Document(), selectedOnly: _gsExportScope.SelectedIndex == 0);
        using var dialog = GaussianExportDialogs.CreateSaveDialog();
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        string path = GaussianExportDialogs.NormalizePath(dialog);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        _localGsOperation = cancellation; _cancelGs.Enabled = true;
        Feedback(global::Aholo.Core.UiText.Format("Preparing to export {0:N0} GS instances using captured placements and units. The existing destination is retained until completion.", snapshot.Parts.Length));
        try
        {
            string message = await GaussianDocument.ExportAsync(path, snapshot, cancellation.Token).ConfigureAwait(false);
            AholoHost.Ui(() => Feedback(message));
        }
        catch (OperationCanceledException) { AholoHost.Ui(() => Feedback(global::Aholo.Core.UiText.Get("Local GS export cancelled; the existing destination is retained."))); }
        finally { await AholoHost.OnUi(() => { _localGsOperation = null; if (!_disposed) _cancelGs.Enabled = false; return true; }).ConfigureAwait(false); }
    }
    private async Task ImportLocalPlyAsync()
    {
        using var dialog = new OpenFileDialog { Title = global::Aholo.Core.UiText.Get("Diagnostic: import only PLY centers and base colors"), MultiSelect = false };
        dialog.Filters.Add(new FileFilter(global::Aholo.Core.UiText.Get("PLY point cloud"), ".ply"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        var target = AholoHost.CaptureTarget(Document());
        string message = await AholoHost.ImportAsync(dialog.FileName, target, ImportScale(), ImportAxis(null), null, Token);
        AholoHost.Ui(() => Feedback(message));
    }
    private void OpenSelectedImage(string kind)
    {
        string? path = Selected(kind).ImagePath;
        if (path is null || !File.Exists(path)) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("Download this task's result image first."));
        string full = Path.GetFullPath(path), root = Path.GetFullPath(Path.Combine(TaskStore.DefaultRoot, "assets")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || new FileInfo(full).LinkTarget is not null) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The render image is outside the plugin asset directory."));
        Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
    }
    private static void OpenAssetFolder()
    {
        string directory = Path.Combine(TaskStore.DefaultRoot, "assets");
        if (!Directory.Exists(directory)) throw new InvalidOperationException(global::Aholo.Core.UiText.Get("The asset directory does not exist yet. Load history or download a result first."));
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
    private static bool InProgress(LocalTask task) => task.Status is "MeshProcessing" or "Running" or "PENDING" or "PREPROCESSING" or "RUNNING";
    private void Poll()
    {
        if (_disposed || !_shown || _busy || !_connected || _autoRefresh.Checked != true) return;
        var pending = _records.Where(t => InProgress(t) && t.ConnectionId == _connectionId).ToArray();
        if (pending.Length == 0) return;
        LocalTask task = pending[_pollCursor++ % pending.Length];
        _ = RunAsync(global::Aholo.Core.UiText.Get("Querying original task"), async () => { var updated = await _client.RefreshAsync(task.Id, Token); AholoHost.Ui(() => ObserveTask(updated, false)); }, true);
    }
    public void PanelShown(uint documentSerialNumber, ShowPanelReason reason)
    {
        if (_disposed) return;
        _shown = true;
        if (!_busy) StartPanelSession();
    }
    private void StartPanelSession()
    {
        if (_disposed || !_shown) return;
        if (_lifetime.IsCancellationRequested) { _lifetime.Dispose(); _lifetime = new CancellationTokenSource(); }
        _poll.Start();
        _ = RunAsync(global::Aholo.Core.UiText.Get("Reading connection and history"), async () => { await ReadConnectionAsync(); await ReloadAsync(); }, true);
    }
    public void PanelHidden(uint documentSerialNumber, ShowPanelReason reason) { _shown = false; _poll.Stop(); _elapsed.Stop(); }
    public void PanelClosing(uint documentSerialNumber, bool onCloseDocument)
    {
        _shown = false; _poll.Stop(); _elapsed.Stop(); _lifetime.Cancel();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true; _shown = false; _lifetime.Cancel(); _poll.Stop(); _elapsed.Stop(); _poll.Dispose(); _elapsed.Dispose();
            _ = _client.DisposeAsync().AsTask();
        }
        base.Dispose(disposing);
    }
}
