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
    private readonly CheckBox _remember = new() { Text = "保存到本机安全存储", Checked = false };
    private readonly Label _connection = Info("尚未连接 · 国际区 API Key");
    private readonly TextArea _prompt = new() { Height = 100, Wrap = true };
    private readonly TextBox _worldName = new() { PlaceholderText = "任务名称（可选）" };
    private readonly TextBox _image = new() { ReadOnly = true, PlaceholderText = "可选：PNG / JPEG / WebP 图片" };
    private readonly TextBox _worldId = new() { PlaceholderText = "已有的 Aholo World ID" };
    private readonly TextBox _scale = new() { Text = "1" };
    private readonly DropDown _axis = new() { DataStore = new[] { "使用世界元数据（缺失时 Z 向上）", "Z 向上", "Y 向上" }, SelectedIndex = 0 };
    private readonly ListBox _worldTasks = new() { Height = 125 };
    private readonly ListBox _renderTasks = new() { Height = 125 };
    private readonly TextArea _worldDetail = new() { ReadOnly = true, Wrap = true, Height = 105 };
    private readonly TextArea _renderDetail = new() { ReadOnly = true, Wrap = true, Height = 105 };
    private readonly DropDown _resolution = new() { DataStore = new[] { "1280 × 720", "1920 × 1080" }, SelectedIndex = 0 };
    private readonly CheckBox _autoRefresh = new() { Text = "自动查询进行中的任务（每 8 秒）", Checked = true };
    private readonly Label _operation = Info("就绪");
    private readonly TextArea _feedback = new() { ReadOnly = true, Height = 80, Wrap = true, Text = "World 生成世界，RenderCloud 渲染 Rhino 选区。结果和任务保存在本机。" };
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
        _generate = ActionButton("生成世界 · 消耗服务额度", GenerateAsync);
        _render = ActionButton("上传选区并渲染 · 消耗服务额度", RenderAsync);
        _generate.Enabled = _render.Enabled = false;
        _worldTasks.SelectedIndexChanged += (_, _) => DescribeTask("world");
        _renderTasks.SelectedIndexChanged += (_, _) => DescribeTask("render");
        _poll.Elapsed += (_, _) => Poll();
        _elapsed.Elapsed += (_, _) => { if (!_disposed && _busy) _operation.Text = _operationName + " · " + (int)_clock.Elapsed.TotalSeconds + " 秒"; };
        _tabs.Pages.Add(new TabPage { Text = "World", Content = Column(
            Info("从文字或一张图片生成 3D Gaussian Splatting 世界。当前接口更适合室内场景。"),
            _worldName, _prompt, _image,
            Row(LocalButton("选择图片", ChooseImage), LocalButton("清除", () => _image.Text = "")),
            _generate,
            Info("世界历史（本地最近 100 条任务中的 World 项）"), _worldTasks,
            Row(ActionButton("读取历史", () => ReloadAsync()), ActionButton("查询选中任务", () => RefreshSelectedAsync("world"))),
            _worldDetail,
            Row(ActionButton("下载 PLY", () => DownloadSelectedAsync("world", "ply")), ActionButton("下载 SPZ", () => DownloadSelectedAsync("world", "spz"))),
            Row(ActionButton("下载全景图", () => DownloadSelectedAsync("world", "image")), LocalButton("打开全景图", () => OpenSelectedImage("world"))),
            Info("GS 导入比例：1 个世界单位对应以下文档单位（" + (doc?.ModelUnitSystem.ToString() ?? "当前文档") + "）"),
            _scale, _axis, ActionButton("放入当前文档 · GS 实验视口", PlaceSelectedAsync),
            Info("保留高斯尺度、旋转、透明度和视角颜色。当前为小场景实验原型，实际宿主遮挡与性能仍待验收；原始 PLY / SPZ 保留。"),
            Row(ActionButton("导入本地 GS PLY", ImportLocalGsAsync), LocalButton("切换到 Aholo GS", () => GaussianViewport.Activate(Document()))),
            Info("离线验收场景：运行 AholoGsProbe；诊断信息：AholoGsDiagnostics。透明 Rhino 材质的交错合成、裁切平面和嵌套块尚不支持。"),
            ActionButton("诊断：导入 PLY 中心点", ImportLocalPlyAsync),
            _worldId, ActionButton("从已有 World ID 恢复", ImportExistingWorldAsync)
        ) });
        _tabs.Pages.Add(new TabPage { Text = "RenderCloud", Content = Column(
            Info("选中 Rhino 网格、曲面/多重曲面或挤出，激活标准透视视图，再提交渲染。"),
            Info("将上传所选几何和基础颜色，并提交当前相机。当前版不转换纹理、灯光、程序材质或块实例。"),
            _resolution,
            Info("输出保留当前相机的水平视角；上下范围按输出画幅调整。"), _render,
            Info("渲染历史（本地最近 100 条任务中的 RenderCloud 项）"), _renderTasks,
            Row(ActionButton("读取历史", () => ReloadAsync()), ActionButton("查询选中任务", () => RefreshSelectedAsync("render"))),
            _renderDetail,
            Row(ActionButton("下载渲染图", () => DownloadSelectedAsync("render", "image")), LocalButton("打开本地渲染图", () => OpenSelectedImage("render"))),
            Info("此页使用离线渲染接口。实时协议已有实验基础，但云视口与 World GS 入库桥接尚未开放。失败或结果未知时，不会自动重复创建付费任务。")
        ) });
        _tabs.Pages.Add(new TabPage { Text = "连接", Content = Column(
            _connection, Info("当前版本连接 Aholo 国际区。生成与渲染使用区域 API Key；此插件未接入 OAuth。"),
            new Label { Text = "国际区 API Key" }, _key, _remember,
            ActionButton("验证并连接", ConnectAsync),
            Row(ActionButton("读取连接状态", ReadConnectionAsync), ActionButton("断开并清除凭证", DisconnectAsync)),
            Info("验证只查询已有任务，不生成世界、不渲染，也不保证剩余额度。Key 不会写入 Rhino 文档或任务历史。"),
            _autoRefresh, LocalButton("打开本地资产目录", OpenAssetFolder),
            Info("关闭面板会停止本地等待，不代表远程任务取消。重新打开后可查询历史；创建结果未知时先核对服务端记录。"),
            Info("Aholo for Rhino " + AholoPlugin.DisplayVersion + "\nCuriousArc · qrost\n独立开发的 Aholo 集成插件。")
        ) });
        Content = new StackLayout
        {
            Padding = 10, Spacing = 8, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Items = { new Label { Text = "Aholo for Rhino", Font = new Font(SystemFont.Bold, 15) },
                Info("世界与渲染 · " + (doc?.Name ?? "当前文档")), new StackLayoutItem(_tabs, true), _operation, _feedback }
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
    private RhinoDoc Document() => RhinoDoc.FromRuntimeSerialNumber(_serial) ?? throw new InvalidOperationException("目标文档已关闭，请在目标文档重新打开面板。");
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
            AholoHost.Ui(() => { if (!background && !_disposed && revision == _feedbackRevision) Feedback("操作已完成。"); });
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
                _elapsed.Stop(); _operation.Text = (failed ? "操作未完成" : "就绪") + " · " + (int)_clock.Elapsed.TotalSeconds + " 秒";
                foreach (Button button in _actions) button.Enabled = true;
                _generate.Enabled = _render.Enabled = _connected;
                if (_shown && _lifetime.IsCancellationRequested) StartPanelSession();
            });
        }
    }
    private void Feedback(string text, bool error = false)
    {
        if (_disposed) return;
        _feedbackRevision++; _feedbackError = error; _feedback.Text = (error ? "需要处理：" : "") + text;
    }
    private void SetConnection(ConnectionState state)
    {
        if (_disposed) return;
        _connected = state.Connected; _connectionId = state.ConnectionId;
        _connection.Text = state.Connected ? "国际区 · 已连接" + (state.Remembered ? " · 凭证已安全保存" : " · 仅本次会话") : "国际区 · 尚未连接";
        _generate.Enabled = _render.Enabled = _connected && !_busy;
    }
    private async Task ConnectAsync()
    {
        string key = _key.Text; bool remember = _remember.Checked == true; _key.Text = "";
        var connection = await _client.ConnectAsync(new ConnectRequest(key, remember), Token);
        AholoHost.Ui(() => { SetConnection(connection); Feedback("已连接。可在 World 或 RenderCloud 页提交任务。服务额度仍由 Aholo 账户决定。"); });
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
        AholoHost.Ui(() => { SetConnection(connection); _key.Text = ""; Feedback("已断开并清除本机保存的凭证。远程任务不会因此取消。"); });
    }
    private void ChooseImage()
    {
        using var dialog = new OpenFileDialog { Title = "选择世界生成参考图", MultiSelect = false };
        dialog.Filters.Add(new FileFilter("图片", ".png", ".jpg", ".jpeg", ".webp"));
        if (dialog.ShowDialog(this) == DialogResult.Ok) _image.Text = dialog.FileName;
    }
    private async Task GenerateAsync()
    {
        string prompt = _prompt.Text.Trim(), image = _image.Text.Trim(), name = _worldName.Text.Trim();
        if (prompt.Length == 0 && image.Length == 0) throw new InvalidOperationException("请填写场景描述或选择一张参考图。");
        if (name.Length == 0) name = prompt.Length == 0 ? "图片生成世界" : prompt[..Math.Min(prompt.Length, 45)];
        var task = await _client.GenerateWorldAsync(new GenerateWorldRequest(name, prompt, image.Length == 0 ? null : image), Token);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback(task.Error ?? "世界任务已记录。生成完成后下载 PLY，再点击“放入当前文档”。", task.Error is not null));
    }
    private async Task ImportExistingWorldAsync()
    {
        string id = _worldId.Text.Trim();
        if (id.Length == 0) throw new InvalidOperationException("请输入已有的 World ID。");
        var task = await _client.ImportWorldAsync(id, Token);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback("已读取原世界任务；没有重新生成。"));
    }
    private async Task RenderAsync()
    {
        int width = _resolution.SelectedIndex == 1 ? 1920 : 1280, height = _resolution.SelectedIndex == 1 ? 1080 : 720;
        RenderSnapshot snapshot = AholoHost.CaptureRender(Document(), width, height);
        CancellationToken ct = Token;
        string path = Path.Combine(TaskStore.DefaultRoot, "exports", Guid.NewGuid().ToString("N") + ".glb");
        AholoHost.Ui(() => _operationName = "准备选区几何");
        await Task.Run(() => { Directory.CreateDirectory(Path.GetDirectoryName(path)!); GlbWriter.Write(path, snapshot.Meshes, ct); }, ct);
        // Capture already reflects the user's selected document and camera; no document mutations occur during submission.
        AholoHost.Ui(() => _operationName = "上传选区并提交渲染");
        var task = await _client.RenderAsync(new RenderRequest(snapshot.Name, path, snapshot.Camera, width, height), ct);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback(task.Error ?? "选区渲染任务已记录。网格处理与渲染可继续从原任务查询；完成后下载渲染图。", task.Error is not null));
    }
    private LocalTask Selected(string kind)
    {
        var list = kind == "world" ? _worldTasks : _renderTasks;
        return list.SelectedValue as LocalTask ?? throw new InvalidOperationException("请先选择一条任务。");
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
        if (list.SelectedValue is not LocalTask task) { detail.Text = "尚无选中任务。"; return; }
        string progress = task.Progress.HasValue ? " · 进度 " + task.Progress.Value.ToString("0.##", CultureInfo.InvariantCulture) : "";
        string id = task.WorldId is not null ? "\nWorld ID：" + task.WorldId : task.MeshTaskId is not null ? "\n网格处理 ID：" + task.MeshTaskId : "";
        if (task.OperationId is not null) id += "\n渲染操作 ID：" + task.OperationId;
        string cache = task.Kind == "world" ? "\n本地资产：" + (task.PlyPath is null ? "PLY 未下载" : "PLY 已下载") + " / " + (task.SpzPath is null ? "SPZ 未下载" : "SPZ 已下载") : "\n渲染图：" + (task.ImagePath is null ? "未下载" : "已下载");
        detail.Text = task.Name + "\n" + task.Status + progress + id + cache + (string.IsNullOrWhiteSpace(task.Error) ? "" : "\n" + task.Error);
    }
    private async Task RefreshSelectedAsync(string kind)
    {
        var selected = Selected(kind);
        var task = await _client.RefreshAsync(selected.Id, Token);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback("原任务当前状态：" + task.Status + (task.Error is null ? "" : "。" + task.Error), task.Error is not null));
    }
    private async Task DownloadSelectedAsync(string kind, string asset)
    {
        LocalTask selected = Selected(kind);
        var task = await _client.DownloadAsync(new DownloadRequest(selected.Id, asset), Token);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback(asset.ToUpperInvariant() + " 已保存到本机任务资产目录。"));
    }
    private double ImportScale()
    {
        if ((!double.TryParse(_scale.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) && !double.TryParse(_scale.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out scale)) || !double.IsFinite(scale) || scale <= 0)
            throw new InvalidOperationException("请填写大于零的导入比例，例如 1 或 1000。");
        return scale;
    }
    private string ImportAxis(string? metadata) => _axis.SelectedIndex == 2 ? "Y" : _axis.SelectedIndex == 1 ? "Z" : string.Equals(metadata, "Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "Z";
    private async Task PlaceSelectedAsync()
    {
        LocalTask task = Selected("world");
        var target = AholoHost.CaptureTarget(Document()); double scale = ImportScale(); int axisChoice = _axis.SelectedIndex;
        CancellationToken ct = Token;
        if (task.PlyPath is null || !File.Exists(task.PlyPath)) task = await _client.DownloadAsync(new DownloadRequest(task.Id, "ply"), ct);
        if (task.PlyPath is null) throw new InvalidOperationException("原任务尚未提供 PLY，请先查询生成状态。");
        AholoHost.Ui(() => _operationName = "读取完整 GS 并准备原生视口");
        string axis = axisChoice == 2 ? "Y" : axisChoice == 1 ? "Z" : string.Equals(task.UpAxis, "Y", StringComparison.OrdinalIgnoreCase) ? "Y" : "Z";
        string message = await GaussianDocument.ImportAsync(task.PlyPath, target, scale, axis, task.Id, ct);
        AholoHost.Ui(() => ObserveTask(task));
        AholoHost.Ui(() => Feedback(message));
    }
    private async Task ImportLocalGsAsync()
    {
        using var dialog = new OpenFileDialog { Title = "选择完整 3DGS PLY", MultiSelect = false };
        dialog.Filters.Add(new FileFilter("Gaussian PLY", ".ply"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        var target = AholoHost.CaptureTarget(Document());
        string message = await GaussianDocument.ImportAsync(dialog.FileName, target, ImportScale(), ImportAxis(null), null, Token);
        AholoHost.Ui(() => Feedback(message));
    }
    private async Task ImportLocalPlyAsync()
    {
        using var dialog = new OpenFileDialog { Title = "诊断：仅导入 PLY 中心点和基础颜色", MultiSelect = false };
        dialog.Filters.Add(new FileFilter("PLY 点云", ".ply"));
        if (dialog.ShowDialog(this) != DialogResult.Ok) return;
        var target = AholoHost.CaptureTarget(Document());
        string message = await AholoHost.ImportAsync(dialog.FileName, target, ImportScale(), ImportAxis(null), null, Token);
        AholoHost.Ui(() => Feedback(message));
    }
    private void OpenSelectedImage(string kind)
    {
        string? path = Selected(kind).ImagePath;
        if (path is null || !File.Exists(path)) throw new InvalidOperationException("请先下载该任务的结果图片。");
        string full = Path.GetFullPath(path), root = Path.GetFullPath(Path.Combine(TaskStore.DefaultRoot, "assets")) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || new FileInfo(full).LinkTarget is not null) throw new InvalidOperationException("渲染图不在插件资产目录中。");
        Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
    }
    private static void OpenAssetFolder()
    {
        string directory = Path.Combine(TaskStore.DefaultRoot, "assets");
        if (!Directory.Exists(directory)) throw new InvalidOperationException("资产目录尚未创建，请先读取历史或下载结果。");
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    }
    private static bool InProgress(LocalTask task) => task.Status is "MeshProcessing" or "Running" or "PENDING" or "PREPROCESSING" or "RUNNING";
    private void Poll()
    {
        if (_disposed || !_shown || _busy || !_connected || _autoRefresh.Checked != true) return;
        var pending = _records.Where(t => InProgress(t) && t.ConnectionId == _connectionId).ToArray();
        if (pending.Length == 0) return;
        LocalTask task = pending[_pollCursor++ % pending.Length];
        _ = RunAsync("查询原任务", async () => { var updated = await _client.RefreshAsync(task.Id, Token); AholoHost.Ui(() => ObserveTask(updated, false)); }, true);
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
        _ = RunAsync("读取连接与历史", async () => { await ReadConnectionAsync(); await ReloadAsync(); }, true);
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
