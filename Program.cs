using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace CodexLimitWidget;

internal static class Program
{
    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "widget.log");

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) => LogException("UI", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception ex)
            {
                LogException("AppDomain", ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            LogException("Task", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        ApplicationConfiguration.Initialize();

        var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(command) && OperatingSystem.IsWindows())
        {
            command = "gui";
        }

        return command switch
        {
            "status" => RunStatus(),
            "watch" => RunWatch(args),
            "gui" => RunGui(args),
            null => RunStatus(),
            _ => ShowHelp()
        };
    }

    private static int RunStatus()
    {
        try
        {
            using var client = new CodexAppServerClient();
            var snapshot = client.ReadRateLimits();
            Console.WriteLine(snapshot.FormatMultiline());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"读取失败: {ex.Message}");
            return 1;
        }
    }

    private static int RunWatch(string[] args)
    {
        var intervalSeconds = ParseInterval(args, 60);
        while (true)
        {
            try
            {
                using var client = new CodexAppServerClient();
                var snapshot = client.ReadRateLimits();
                Console.WriteLine(snapshot.FormatMultiline());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"读取失败: {ex.Message}");
            }

            Console.WriteLine(new string('-', 40));
            Thread.Sleep(TimeSpan.FromSeconds(intervalSeconds));
        }
    }

    private static int RunGui(string[] args)
    {
        var intervalSeconds = ParseInterval(args, 60);
        NativeMethods.FreeConsole();
        Application.Run(new WidgetForm(intervalSeconds));
        return 0;
    }

    private static int ShowHelp()
    {
        Console.WriteLine("""
        CodexLimitWidget

        用法:
          CodexLimitWidget status
          CodexLimitWidget watch [--interval 60]
          CodexLimitWidget gui [--interval 60]
        """);
        return 0;
    }

    internal static void LogInfo(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] INFO {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
        }
    }

    internal static void LogException(string source, Exception exception)
    {
        try
        {
            var content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ERROR {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(LogPath, content, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static int ParseInterval(string[] args, int defaultValue)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--interval", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out var value) &&
                value > 0)
            {
                return value;
            }
        }

        return defaultValue;
    }
}

internal sealed class CodexAppServerClient : IDisposable
{
    private readonly Process _process;
    private readonly ConcurrentQueue<JsonElement> _messages = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly CancellationTokenSource _cts = new();
    private int _nextId;

    public CodexAppServerClient()
    {
        _process = StartProcess();
        _ = Task.Run(() => DrainStderrAsync(_process.StandardError, _cts.Token));
        _ = Task.Run(() => ReadStdoutLoopAsync(_process.StandardOutput, _cts.Token));

        var initId = NextId();
        Send(new
        {
            id = initId,
            method = "initialize",
            @params = new
            {
                clientInfo = new
                {
                    name = "codex-limit-widget",
                    version = "0.3.0",
                },
                capabilities = new
                {
                    experimentalApi = true,
                },
            },
        });

        var initResponse = WaitForResponse(initId, TimeSpan.FromSeconds(15));
        if (!initResponse.TryGetProperty("result", out _))
        {
            throw new InvalidOperationException("Codex app-server 初始化失败。");
        }
    }

    public RateLimitSnapshot ReadRateLimits()
    {
        var requestId = NextId();
        Send(new
        {
            id = requestId,
            method = "account/rateLimits/read",
            @params = (object?)null,
        });

        var response = WaitForResponse(requestId, TimeSpan.FromSeconds(15));
        if (response.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"读取限额失败: {error}");
        }

        return RateLimitSnapshot.FromJson(response.GetProperty("result"));
    }

    private Process StartProcess()
    {
        var psi = new ProcessStartInfo
        {
            FileName = ResolvePwsh(),
            Arguments = "-NoLogo -NoProfile -Command \"codex app-server --listen stdio://\"",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        return Process.Start(psi) ?? throw new InvalidOperationException("无法启动 Codex app-server。");
    }

    private static string ResolvePwsh()
    {
        var candidates = new[]
        {
            "pwsh.exe",
            "powershell.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
        };

        foreach (var candidate in candidates)
        {
            try
            {
                if (Path.IsPathRooted(candidate))
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                else
                {
                    var found = Environment.GetEnvironmentVariable("PATH")
                        ?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                        .Select(path => Path.Combine(path, candidate))
                        .FirstOrDefault(File.Exists);
                    if (!string.IsNullOrWhiteSpace(found))
                    {
                        return found;
                    }
                }
            }
            catch
            {
            }
        }

        throw new InvalidOperationException("找不到 pwsh 或 powershell。");
    }

    private int NextId() => Interlocked.Increment(ref _nextId);

    private void Send(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions());
        _process.StandardInput.WriteLine(json);
        _process.StandardInput.Flush();
    }

    private JsonElement WaitForResponse(int id, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            while (_messages.TryDequeue(out var message))
            {
                if (message.TryGetProperty("id", out var responseId))
                {
                    if (responseId.ValueKind == JsonValueKind.Number && responseId.GetInt32() == id)
                    {
                        return message;
                    }

                    if (responseId.ValueKind == JsonValueKind.String && responseId.GetString() == id.ToString())
                    {
                        return message;
                    }
                }
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            _signal.WaitOne(remaining > TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : remaining);
        }

        throw new TimeoutException("等待 Codex app-server 响应超时。");
    }

    private async Task ReadStdoutLoopAsync(StreamReader reader, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                _messages.Enqueue(doc.RootElement.Clone());
                _signal.Set();
            }
            catch
            {
            }
        }
    }

    private static async Task DrainStderrAsync(StreamReader reader, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        _process.Dispose();
        _signal.Dispose();
        _cts.Dispose();
    }
}

internal sealed record RateLimitWindow(int? UsedPercent, int? WindowDurationMins, long? ResetsAt)
{
    public static RateLimitWindow Empty { get; } = new(null, null, null);

    public string FormatResetCountdown()
    {
        if (ResetsAt is null or <= 0)
        {
            return "未知";
        }

        var delta = DateTimeOffset.FromUnixTimeSeconds(ResetsAt.Value) - DateTimeOffset.Now;
        if (delta <= TimeSpan.Zero)
        {
            return "已到期";
        }

        var totalMinutes = (int)Math.Floor(delta.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0 ? $"{hours}小时{minutes}分钟" : $"{minutes}分钟";
    }

    public string FormatResetMoment(bool includeDate)
    {
        if (ResetsAt is null or <= 0)
        {
            return "未知";
        }

        var value = DateTimeOffset.FromUnixTimeSeconds(ResetsAt.Value).LocalDateTime;
        return includeDate || value.Date != DateTime.Today
            ? value.ToString("M月d日 HH:mm")
            : value.ToString("HH:mm");
    }
}

internal sealed record CreditsSnapshot(bool HasCredits, bool Unlimited, string? Balance);

internal sealed record RateLimitSnapshot(
    string LimitId,
    string? LimitName,
    string? PlanType,
    string? RateLimitReachedType,
    CreditsSnapshot? Credits,
    RateLimitWindow Primary,
    RateLimitWindow Secondary,
    int? RateLimitResetCredits)
{
    public int? RemainingPercent => Primary.UsedPercent is null ? null : Math.Max(0, 100 - Primary.UsedPercent.Value);

    public static RateLimitSnapshot FromJson(JsonElement result)
    {
        JsonElement? snapshot = null;
        if (result.TryGetProperty("rateLimitsByLimitId", out var byLimitId) &&
            byLimitId.ValueKind == JsonValueKind.Object &&
            byLimitId.TryGetProperty("codex", out var codex))
        {
            snapshot = codex;
        }
        else if (result.TryGetProperty("rateLimits", out var rateLimits))
        {
            snapshot = rateLimits;
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException("app-server 没有返回 codex 限额桶。");
        }

        var snapshotValue = snapshot.Value;
        var primary = snapshotValue.TryGetProperty("primary", out var primaryElement)
            ? ReadWindow(primaryElement)
            : RateLimitWindow.Empty;
        var secondary = snapshotValue.TryGetProperty("secondary", out var secondaryElement)
            ? ReadWindow(secondaryElement)
            : RateLimitWindow.Empty;

        CreditsSnapshot? credits = null;
        if (snapshotValue.TryGetProperty("credits", out var creditsElement) && creditsElement.ValueKind == JsonValueKind.Object)
        {
            credits = new CreditsSnapshot(
                creditsElement.TryGetProperty("hasCredits", out var hasCredits) && hasCredits.GetBoolean(),
                creditsElement.TryGetProperty("unlimited", out var unlimited) && unlimited.GetBoolean(),
                creditsElement.TryGetProperty("balance", out var balance) && balance.ValueKind != JsonValueKind.Null ? balance.GetString() : null);
        }

        int? resetCredits = null;
        if (result.TryGetProperty("rateLimitResetCredits", out var resetCreditsElement) &&
            resetCreditsElement.ValueKind == JsonValueKind.Object &&
            resetCreditsElement.TryGetProperty("availableCount", out var availableCount))
        {
            resetCredits = availableCount.GetInt32();
        }

        return new RateLimitSnapshot(
            snapshotValue.TryGetProperty("limitId", out var limitId) && limitId.ValueKind != JsonValueKind.Null ? limitId.GetString() ?? "codex" : "codex",
            snapshotValue.TryGetProperty("limitName", out var limitName) && limitName.ValueKind != JsonValueKind.Null ? limitName.GetString() : null,
            snapshotValue.TryGetProperty("planType", out var planType) && planType.ValueKind != JsonValueKind.Null ? planType.GetString() : null,
            snapshotValue.TryGetProperty("rateLimitReachedType", out var reachedType) && reachedType.ValueKind != JsonValueKind.Null ? reachedType.GetString() : null,
            credits,
            primary,
            secondary,
            resetCredits);
    }

    private static RateLimitWindow ReadWindow(JsonElement element)
    {
        int? usedPercent = element.TryGetProperty("usedPercent", out var used) ? used.GetInt32() : null;
        int? duration = element.TryGetProperty("windowDurationMins", out var mins) && mins.ValueKind != JsonValueKind.Null ? mins.GetInt32() : null;
        long? resetsAt = element.TryGetProperty("resetsAt", out var reset) && reset.ValueKind != JsonValueKind.Null ? reset.GetInt64() : null;
        return new RateLimitWindow(usedPercent, duration, resetsAt);
    }

    public string FormatMultiline()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"计划: {PlanType ?? "unknown"}");
        sb.AppendLine($"限额ID: {LimitId}");
        if (Primary.UsedPercent is not null)
        {
            sb.AppendLine($"主窗口: 已用 {Primary.UsedPercent}% / 剩余 {RemainingPercent}%");
            sb.AppendLine($"主窗口重置: {FormatResetTime(Primary.ResetsAt)} ({Primary.FormatResetCountdown()})");
        }
        if (Secondary.UsedPercent is not null)
        {
            sb.AppendLine($"次窗口: 已用 {Secondary.UsedPercent}%");
            sb.AppendLine($"次窗口重置: {FormatResetTime(Secondary.ResetsAt)} ({Secondary.FormatResetCountdown()})");
        }
        if (Credits is not null)
        {
            sb.AppendLine($"Credits: {(Credits.Unlimited ? "无限" : Credits.Balance ?? "0")}");
        }
        if (RateLimitReachedType is not null)
        {
            sb.AppendLine($"状态: {RateLimitReachedType}");
        }
        if (RateLimitResetCredits is not null)
        {
            sb.AppendLine($"可用重置额度: {RateLimitResetCredits}");
        }

        return sb.ToString().TrimEnd();
    }

    public string PrimaryBadgeText()
    {
        if (Primary.UsedPercent is null)
        {
            return "--";
        }

        return $"{Primary.UsedPercent}%";
    }

    public string HeadlineText()
    {
        return RemainingPercent is null ? "限额未知" : $"剩余 {RemainingPercent}%";
    }

    public string SummaryText()
    {
        return $"5小时窗 {Primary.UsedPercent?.ToString() ?? "未知"}% | 周窗 {Secondary.UsedPercent?.ToString() ?? "未知"}%";
    }

    private static string FormatResetTime(long? epochSeconds)
    {
        if (epochSeconds is null or <= 0)
        {
            return "未知";
        }

        return DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}

internal sealed class WidgetForm : Form, IMessageFilter
{
    private readonly int _intervalSeconds;
    private CodexAppServerClient? _client;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _showMenuItem = new("显示")
    {
        CheckOnClick = true,
        Checked = true,
    };
    private readonly ToolTip _toolTip = new();
    private Icon? _applicationIcon;

    private readonly UsageBadgeLabel _badgeLabel = new();
    private readonly Label _headlineLabel = new();
    private readonly Label _planLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _countdownLabel = new();
    private readonly Label _detailLabel = new();
    private readonly Label _updatedLabel = new();
    private readonly PinButton _topMostButton = new();
    private readonly RefreshButton _refreshButton = new();
    private readonly CloseButton _closeButton = new();
    private bool _hasSnapshot;
    private int? _lastSuccessfulUsedPercent;
    private bool _dragPending;
    private Point _dragStartScreen;
    private bool _swallowDoubleClickUp;
    private readonly UsageBar _usageBar = new();

    public WidgetForm(int intervalSeconds)
    {
        _intervalSeconds = Math.Max(10, intervalSeconds);

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(5, 10, 18);
        Padding = new Padding(1);
        Width = 300;
        Height = 98;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        DoubleBuffered = true;
        Opacity = 0.98;

        BuildUi();
        ConfigureTray();

        _timer.Interval = _intervalSeconds * 1000;
        _timer.Tick += async (_, _) => await RefreshSnapshotAsync();

        Load += async (_, _) => await RefreshSnapshotAsync();
        Shown += (_, _) =>
        {
            PositionWidget();
            ApplyRoundCorners();
        };
        Resize += (_, _) => ApplyRoundCorners();
        MouseDown += StartDrag;
        MouseMove += ContinueDrag;
        MouseUp += EndDrag;

        RegisterDragSurface(this);
        Application.AddMessageFilter(this);
        _notifyIcon.Visible = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _applicationIcon?.Dispose();
        _trayMenu.Dispose();
        _toolTip.Dispose();
        _timer.Dispose();
        _refreshGate.Dispose();
        _client?.Dispose();
        Application.RemoveMessageFilter(this);
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        Paint += PaintBackgroundCard;

        _badgeLabel.AutoSize = false;
        _badgeLabel.Location = new Point(11, 7);
        _badgeLabel.Size = new Size(60, 40);
        _badgeLabel.TextAlign = ContentAlignment.MiddleLeft;
        _badgeLabel.Font = new Font("Bahnschrift SemiCondensed", 26F, FontStyle.Bold, GraphicsUnit.Point);
        _badgeLabel.ForeColor = Color.White;
        _badgeLabel.BackColor = Color.Transparent;
        Controls.Add(_badgeLabel);

        _headlineLabel.AutoSize = false;
        _headlineLabel.Location = new Point(78, 8);
        _headlineLabel.Size = new Size(146, 25);
        _headlineLabel.Font = new Font("Bahnschrift SemiBold", 16F, FontStyle.Bold, GraphicsUnit.Point);
        _headlineLabel.ForeColor = Color.White;
        _headlineLabel.BackColor = Color.Transparent;
        Controls.Add(_headlineLabel);

        _planLabel.AutoSize = false;
        _planLabel.Location = new Point(80, 32);
        _planLabel.Size = new Size(144, 15);
        _planLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold, GraphicsUnit.Point);
        _planLabel.ForeColor = Color.FromArgb(148, 163, 184);
        _planLabel.BackColor = Color.Transparent;
        Controls.Add(_planLabel);

        _topMostButton.FlatStyle = FlatStyle.Flat;
        _topMostButton.FlatAppearance.BorderSize = 0;
        _topMostButton.BackColor = Color.FromArgb(20, 28, 44);
        _topMostButton.ForeColor = Color.White;
        _topMostButton.Location = new Point(232, 8);
        _topMostButton.Size = new Size(18, 18);
        _topMostButton.TabStop = false;
        _topMostButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 41, 59);
        _topMostButton.Click += (_, _) => ToggleTopMost();
        Controls.Add(_topMostButton);

        _refreshButton.FlatStyle = FlatStyle.Flat;
        _refreshButton.FlatAppearance.BorderSize = 0;
        _refreshButton.BackColor = Color.FromArgb(20, 28, 44);
        _refreshButton.ForeColor = Color.FromArgb(226, 232, 240);
        _refreshButton.Location = new Point(254, 8);
        _refreshButton.Size = new Size(18, 18);
        _refreshButton.TabStop = false;
        _refreshButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 41, 59);
        _refreshButton.Click += async (_, _) => await RefreshSnapshotAsync();
        Controls.Add(_refreshButton);

        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.BackColor = Color.FromArgb(20, 28, 44);
        _closeButton.ForeColor = Color.FromArgb(248, 113, 113);
        _closeButton.Location = new Point(276, 8);
        _closeButton.Size = new Size(18, 18);
        _closeButton.TabStop = false;
        _closeButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 41, 59);
        _closeButton.Click += (_, _) => Close();
        Controls.Add(_closeButton);

        _summaryLabel.AutoSize = false;
        _summaryLabel.Location = new Point(10, 66);
        _summaryLabel.Size = new Size(135, 15);
        _summaryLabel.ForeColor = Color.FromArgb(254, 247, 255);
        _summaryLabel.BackColor = Color.Transparent;
        _summaryLabel.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold, GraphicsUnit.Point);
        Controls.Add(_summaryLabel);

        _usageBar.Location = new Point(10, 50);
        _usageBar.Size = new Size(280, 10);
        Controls.Add(_usageBar);

        _countdownLabel.AutoSize = false;
        _countdownLabel.Location = new Point(145, 66);
        _countdownLabel.Size = new Size(145, 15);
        _countdownLabel.TextAlign = ContentAlignment.MiddleRight;
        _countdownLabel.ForeColor = Color.FromArgb(254, 247, 255);
        _countdownLabel.BackColor = Color.Transparent;
        _countdownLabel.Font = new Font("Segoe UI", 8.25F, FontStyle.Bold, GraphicsUnit.Point);
        Controls.Add(_countdownLabel);

        _detailLabel.AutoSize = false;
        _detailLabel.Location = new Point(10, 81);
        _detailLabel.Size = new Size(145, 13);
        _detailLabel.TextAlign = ContentAlignment.MiddleLeft;
        _detailLabel.ForeColor = Color.FromArgb(100, 116, 139);
        _detailLabel.BackColor = Color.Transparent;
        _detailLabel.Font = new Font("Segoe UI", 6.5F, FontStyle.Regular, GraphicsUnit.Point);
        Controls.Add(_detailLabel);

        _updatedLabel.AutoSize = false;
        _updatedLabel.Location = new Point(170, 81);
        _updatedLabel.Size = new Size(120, 13);
        _updatedLabel.TextAlign = ContentAlignment.MiddleRight;
        _updatedLabel.ForeColor = Color.FromArgb(100, 116, 139);
        _updatedLabel.BackColor = Color.Transparent;
        _updatedLabel.Font = new Font("Segoe UI", 6.5F);
        Controls.Add(_updatedLabel);


        _toolTip.SetToolTip(_badgeLabel, "主窗口已用比例");
        _toolTip.SetToolTip(_usageBar, "绿色安全，橙色接近上限，红色临界");
        _toolTip.SetToolTip(_topMostButton, "取消置顶");
    }

    private void RegisterDragSurface(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child == _topMostButton || child == _refreshButton || child == _closeButton)
            {
                continue;
            }

            child.MouseDown += StartDrag;
            child.MouseMove += ContinueDrag;
            child.MouseUp += EndDrag;
            RegisterDragSurface(child);
        }
    }

    private void PaintBackgroundCard(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundRect(rect, 10);
        using var brush = new LinearGradientBrush(rect, Color.FromArgb(12, 18, 31), Color.FromArgb(18, 30, 46), 35F);
        using var border = new Pen(Color.FromArgb(42, 56, 78));
        using var accent = new Pen(Color.FromArgb(28, 163, 74, 90), 2F);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(accent, path);
        e.Graphics.DrawPath(border, path);
        using var divider = new Pen(Color.FromArgb(65, 78, 96));
        e.Graphics.DrawLine(divider, 70, 10, 70, 47);
    }

    private void ToggleTopMost()
    {
        TopMost = !TopMost;
        _topMostButton.ForeColor = TopMost
            ? Color.White
            : Color.FromArgb(148, 163, 184);
        _toolTip.SetToolTip(_topMostButton, TopMost ? "取消置顶" : "启用置顶");
    }

    private void PositionWidget()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(area.Right - Width - 20, area.Top + 20);
    }

    private void ConfigureTray()
    {
        _showMenuItem.CheckedChanged += (_, _) => SetWidgetVisible(_showMenuItem.Checked);
        _trayMenu.Items.Add(_showMenuItem);
        _trayMenu.Items.Add("刷新", null, async (_, _) => await RefreshSnapshotAsync());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出", null, (_, _) => Close());

        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _notifyIcon.Icon = _applicationIcon ?? SystemIcons.Application;
        _notifyIcon.Text = "Codex Limit Widget";
        _notifyIcon.ContextMenuStrip = _trayMenu;
        _notifyIcon.DoubleClick += (_, _) =>
        {
            _showMenuItem.Checked = true;
            SetWidgetVisible(true);
        };
    }

    private void SetWidgetVisible(bool visible)
    {
        if (!visible)
        {
            Hide();
            return;
        }

        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ApplyRoundCorners()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = CreateRoundRect(new Rectangle(0, 0, Width, Height), 10);
        Region = new Region(path);
    }

    private static GraphicsPath CreateRoundRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void StartDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _dragPending = true;
        _dragStartScreen = Cursor.Position;
    }

    private void ContinueDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragPending || e.Button != MouseButtons.Left)
        {
            return;
        }

        var current = Cursor.Position;
        var dragSize = SystemInformation.DragSize;
        if (Math.Abs(current.X - _dragStartScreen.X) < dragSize.Width / 2 &&
            Math.Abs(current.Y - _dragStartScreen.Y) < dragSize.Height / 2)
        {
            return;
        }

        _dragPending = false;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
    }

    private void EndDrag(object? sender, MouseEventArgs e) => _dragPending = false;

    public bool PreFilterMessage(ref Message m)
    {
        const int WmLeftButtonDoubleClick = 0x0203;
        const int WmLeftButtonUp = 0x0202;
        const int WmNonClientLeftButtonDoubleClick = 0x00A3;
        const int WmNonClientLeftButtonUp = 0x00A2;
        const int WmCopy = 0x0301;

        var target = Control.FromHandle(m.HWnd);
        var belongsToWidget = m.HWnd == Handle ||
            (IsHandleCreated && IsChild(Handle, m.HWnd)) ||
            (target is not null && IsWidgetControl(target));

        if (!belongsToWidget)
        {
            return false;
        }

        if (m.Msg == WmCopy && target is Label)
        {
            return true;
        }

        if (_swallowDoubleClickUp && m.Msg is WmLeftButtonUp or WmNonClientLeftButtonUp)
        {
            _swallowDoubleClickUp = false;
            return true;
        }

        if (m.Msg is not WmLeftButtonDoubleClick and not WmNonClientLeftButtonDoubleClick || target is Button)
        {
            return false;
        }

        _dragPending = false;
        _swallowDoubleClickUp = true;
        ReleaseCapture();
        _ = RefreshSnapshotAsync();
        return true;
    }

    private bool IsWidgetControl(Control control)
    {
        for (Control? current = control; current is not null; current = current.Parent)
        {
            if (current == this)
            {
                return true;
            }
        }

        return false;
    }

    private async Task RefreshSnapshotAsync()
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            SetBusy();
            var previous = _lastSuccessfulUsedPercent;
            var snapshot = await Task.Run(ReadSnapshotWithRecovery);
            if (previous is int previousValue &&
                snapshot.Primary.UsedPercent is int currentValue &&
                currentValue < previousValue)
            {
                Program.LogInfo($"Primary used percent dropped from {previousValue}% to {currentValue}%; waiting one second before an unconditional confirmation read.");
                await Task.Delay(TimeSpan.FromSeconds(1));
                snapshot = await Task.Run(ReadSnapshotWithRecovery);
            }

            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            Program.LogException("RefreshSnapshotAsync", ex);
            SetError(ex.Message);
        }
        finally
        {
            _timer.Stop();
            _timer.Start();
            _refreshGate.Release();
        }
    }

    private RateLimitSnapshot ReadSnapshotWithRecovery()
    {
        try
        {
            _client ??= new CodexAppServerClient();
            return _client.ReadRateLimits();
        }
        catch (Exception firstError)
        {
            Program.LogException("ReadSnapshotFirstAttempt", firstError);
            try
            {
                _client?.Dispose();
            }
            catch (Exception disposeError)
            {
                Program.LogException("DisposeBrokenClient", disposeError);
            }

            _client = new CodexAppServerClient();
            Program.LogInfo("Codex app-server client recreated after failed read.");
            return _client.ReadRateLimits();
        }
    }

    private void ApplySnapshot(RateLimitSnapshot snapshot)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplySnapshot(snapshot)));
            return;
        }

        _badgeLabel.Text = snapshot.Primary.UsedPercent?.ToString("D3") ?? "---";
        _badgeLabel.BackColor = Color.Transparent;
        _badgeLabel.ForeColor = snapshot.Primary.UsedPercent switch
        {
            >= 85 => Color.FromArgb(248, 113, 113),
            >= 60 => Color.FromArgb(251, 191, 36),
            _ => Color.FromArgb(74, 222, 128),
        };

        _headlineLabel.Text = snapshot.HeadlineText();
        _planLabel.Text = $"PLAN {(snapshot.PlanType ?? "unknown").ToUpperInvariant()}";
        _summaryLabel.Text = $"将于 {snapshot.Primary.FormatResetMoment(false)} 重置";
        _countdownLabel.Text = $"周限额 {snapshot.Secondary.UsedPercent?.ToString() ?? "--"}% · {snapshot.Secondary.FormatResetMoment(true)}";
        _detailLabel.Text = $"Credits {(snapshot.Credits?.Unlimited == true ? "无限" : snapshot.Credits?.Balance ?? "0")} | 重置额度 {snapshot.RateLimitResetCredits?.ToString() ?? "0"}";
        _updatedLabel.Text = $"{DateTime.Now:HH:mm:ss} 更新";
        _usageBar.ValuePercent = snapshot.Primary.UsedPercent;
        _hasSnapshot = true;
        _lastSuccessfulUsedPercent = snapshot.Primary.UsedPercent;
        _refreshButton.Enabled = true;
        Invalidate();
    }

    private void SetBusy()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(SetBusy));
            return;
        }

        _refreshButton.Enabled = false;
        _updatedLabel.Text = "刷新中…";
    }

    private void SetError(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetError(text)));
            return;
        }

        _refreshButton.Enabled = true;
        _updatedLabel.Text = "刷新失败";
        _toolTip.SetToolTip(_updatedLabel, text);
        if (!_hasSnapshot)
        {
            _badgeLabel.Text = "!";
            _badgeLabel.ForeColor = Color.FromArgb(248, 113, 113);
            _headlineLabel.Text = "读取失败";
            _summaryLabel.Text = text;
            _countdownLabel.Text = "将自动重试，也可双击立即刷新";
        }
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

}

internal static class NativeMethods
{
    [DllImport("kernel32.dll")]
    internal static extern bool FreeConsole();
}

internal sealed class UsageBadgeLabel : Label
{
    protected override void OnPaint(PaintEventArgs e)
    {
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var measured = TextRenderer.MeasureText(e.Graphics, Text, Font, new Size(1000, 1000), flags);
        Font? fittedFont = null;
        var drawFont = Font;
        var availableWidth = Math.Max(1, ClientSize.Width);

        if (measured.Width > availableWidth)
        {
            var fittedSize = Math.Max(8F, Font.Size * availableWidth / measured.Width);
            fittedFont = new Font(Font.FontFamily, fittedSize, Font.Style, GraphicsUnit.Point);
            drawFont = fittedFont;
        }

        TextRenderer.DrawText(e.Graphics, Text, drawFont, ClientRectangle, ForeColor, flags);
        fittedFont?.Dispose();
    }
}

internal sealed class UsageBar : Control
{
    private int? _valuePercent;

    public UsageBar()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        Height = 16;
        BackColor = Color.Transparent;
        Font = new Font("Segoe UI", 6.5F, FontStyle.Bold, GraphicsUnit.Point);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int? ValuePercent
    {
        get => _valuePercent;
        set
        {
            _valuePercent = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = ClientRectangle;
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var bgBrush = new SolidBrush(Color.FromArgb(30, 41, 59));
        using var bgPath = CreateRoundRect(rect, Math.Max(1, rect.Height / 2));
        e.Graphics.FillPath(bgBrush, bgPath);

        if (_valuePercent is null)
        {
            e.Graphics.DrawString("等待数据", Font, Brushes.WhiteSmoke, 8, 0);
            return;
        }

        var fillWidth = (int)Math.Round(rect.Width * Math.Clamp(_valuePercent.Value, 0, 100) / 100.0);
        var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);
        var color = _valuePercent.Value switch
        {
            >= 85 => Color.FromArgb(239, 68, 68),
            >= 60 => Color.FromArgb(245, 158, 11),
            _ => Color.FromArgb(34, 197, 94),
        };
        if (fillWidth > 0)
        {
            using var fillBrush = new SolidBrush(color);
            var state = e.Graphics.Save();
            e.Graphics.SetClip(bgPath);
            e.Graphics.FillRectangle(fillBrush, fillRect);
            e.Graphics.Restore(state);
        }

        var text = $"{_valuePercent}%";
        var textRect = new Rectangle(4, -1, Math.Max(0, rect.Width - 6), rect.Height);
        TextRenderer.DrawText(e.Graphics, text, Font, textRect, Color.White,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
    }

    private static GraphicsPath CreateRoundRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(rect.Location, new Size(diameter, diameter));

        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return path;
        }

        path.AddArc(arc, 180, 90);
        arc.X = rect.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rect.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rect.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal class WidgetButton : Button
{
    public WidgetButton() => SetStyle(ControlStyles.Selectable, false);
    protected override bool ShowFocusCues => false;
}

internal sealed class PinButton : WidgetButton
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var brush = new SolidBrush(ForeColor);
        using var pin = new GraphicsPath();
        pin.StartFigure();
        pin.AddBezier(648.73F, 130.78F, 657F, 132F, 665F, 139F, 671.40F, 146.21F);
        pin.AddLine(671.40F, 146.21F, 862.96F, 337.97F);
        pin.AddBezier(862.96F, 337.97F, 895F, 370F, 882F, 438F, 840.83F, 456.53F);
        pin.AddLine(840.83F, 456.53F, 772.95F, 486.60F);
        pin.AddLine(772.95F, 486.60F, 645.61F, 614.08F);
        pin.AddLine(645.61F, 614.08F, 635.51F, 754.32F);
        pin.AddBezier(635.51F, 754.32F, 632F, 809F, 559F, 830F, 510.83F, 800.77F);
        pin.AddLine(510.83F, 800.77F, 387.17F, 676.99F);
        pin.AddLine(387.17F, 676.99F, 176.44F, 888.69F);
        pin.AddLine(176.44F, 888.69F, 124.61F, 837.07F);
        pin.AddLine(124.61F, 837.07F, 335.46F, 625.25F);
        pin.AddLine(335.46F, 625.25F, 207.53F, 497.23F);
        pin.AddBezier(207.53F, 497.23F, 174F, 464F, 199F, 386F, 253.83F, 372.59F);
        pin.AddLine(253.83F, 372.59F, 398.07F, 361.81F);
        pin.AddLine(398.07F, 361.81F, 523.14F, 236.59F);
        pin.AddLine(523.14F, 236.59F, 552.52F, 168.81F);
        pin.AddBezier(552.52F, 168.81F, 568F, 132F, 618F, 113F, 648.73F, 130.78F);
        pin.CloseFigure();

        var state = e.Graphics.Save();
        using var transform = new Matrix(14F / 1024F, 0F, 0F, 14F / 1024F, 2F, 2F);
        e.Graphics.Transform = transform;
        e.Graphics.FillPath(brush, pin);
        e.Graphics.Restore(state);
    }
}

internal sealed class RefreshButton : WidgetButton
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(ForeColor, 1.1F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        var cx = Width / 2F;
        var cy = Height / 2F;
        var arc = new RectangleF(cx - 4.5F, cy - 4.5F, 9F, 9F);
        e.Graphics.DrawArc(pen, arc, 195F, 150F);
        e.Graphics.DrawArc(pen, arc, 15F, 150F);
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(cx + 2.2F, cy - 4.2F),
            new PointF(cx + 4.5F, cy - 4.2F),
            new PointF(cx + 4.5F, cy - 1.8F),
        });
        e.Graphics.DrawLines(pen, new[]
        {
            new PointF(cx - 2.2F, cy + 4.2F),
            new PointF(cx - 4.5F, cy + 4.2F),
            new PointF(cx - 4.5F, cy + 1.8F),
        });
    }
}

internal sealed class CloseButton : WidgetButton
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(ForeColor, 1.1F)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        var cx = Width / 2F;
        var cy = Height / 2F;
        e.Graphics.DrawLine(pen, cx - 4F, cy - 4F, cx + 4F, cy + 4F);
        e.Graphics.DrawLine(pen, cx + 4F, cy - 4F, cx - 4F, cy + 4F);
    }
}
