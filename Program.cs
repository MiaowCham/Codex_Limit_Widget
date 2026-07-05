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

internal sealed class WidgetForm : Form
{
    private readonly int _intervalSeconds;
    private CodexAppServerClient? _client;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly NotifyIcon _notifyIcon = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolTip _toolTip = new();

    private readonly Label _badgeLabel = new();
    private readonly Label _headlineLabel = new();
    private readonly Label _planLabel = new();
    private readonly Label _summaryLabel = new();
    private readonly Label _countdownLabel = new();
    private readonly Label _detailLabel = new();
    private readonly Label _footerLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _closeButton = new();
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
        Height = 172;
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
        DoubleClick += async (_, _) => await RefreshSnapshotAsync();

        RegisterDragSurface(this);
        _notifyIcon.Visible = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayMenu.Dispose();
        _toolTip.Dispose();
        _timer.Dispose();
        _refreshGate.Dispose();
        _client?.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        Paint += PaintBackgroundCard;

        _badgeLabel.AutoSize = false;
        _badgeLabel.Location = new Point(14, 14);
        _badgeLabel.Size = new Size(74, 56);
        _badgeLabel.TextAlign = ContentAlignment.MiddleCenter;
        _badgeLabel.Font = new Font("Bahnschrift SemiBold", 24F, FontStyle.Bold, GraphicsUnit.Point);
        _badgeLabel.ForeColor = Color.White;
        _badgeLabel.BackColor = Color.Transparent;
        Controls.Add(_badgeLabel);

        _headlineLabel.AutoSize = false;
        _headlineLabel.Location = new Point(94, 16);
        _headlineLabel.Size = new Size(128, 28);
        _headlineLabel.Font = new Font("Bahnschrift SemiBold", 14F, FontStyle.Bold, GraphicsUnit.Point);
        _headlineLabel.ForeColor = Color.White;
        _headlineLabel.BackColor = Color.Transparent;
        Controls.Add(_headlineLabel);

        _planLabel.AutoSize = false;
        _planLabel.Location = new Point(96, 42);
        _planLabel.Size = new Size(120, 18);
        _planLabel.Font = new Font("Segoe UI", 8F, FontStyle.Bold, GraphicsUnit.Point);
        _planLabel.ForeColor = Color.FromArgb(148, 163, 184);
        _planLabel.BackColor = Color.Transparent;
        Controls.Add(_planLabel);

        _refreshButton.Text = "↻";
        _refreshButton.FlatStyle = FlatStyle.Flat;
        _refreshButton.FlatAppearance.BorderSize = 0;
        _refreshButton.BackColor = Color.FromArgb(20, 28, 44);
        _refreshButton.ForeColor = Color.FromArgb(226, 232, 240);
        _refreshButton.Location = new Point(236, 14);
        _refreshButton.Size = new Size(20, 20);
        _refreshButton.Click += async (_, _) => await RefreshSnapshotAsync();
        Controls.Add(_refreshButton);

        _closeButton.Text = "×";
        _closeButton.FlatStyle = FlatStyle.Flat;
        _closeButton.FlatAppearance.BorderSize = 0;
        _closeButton.BackColor = Color.FromArgb(20, 28, 44);
        _closeButton.ForeColor = Color.FromArgb(248, 113, 113);
        _closeButton.Location = new Point(262, 14);
        _closeButton.Size = new Size(20, 20);
        _closeButton.Click += (_, _) => Close();
        Controls.Add(_closeButton);

        _summaryLabel.AutoSize = false;
        _summaryLabel.Location = new Point(14, 72);
        _summaryLabel.Size = new Size(270, 16);
        _summaryLabel.ForeColor = Color.FromArgb(203, 213, 225);
        _summaryLabel.BackColor = Color.Transparent;
        _summaryLabel.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        Controls.Add(_summaryLabel);

        _usageBar.Location = new Point(14, 92);
        _usageBar.Size = new Size(270, 14);
        Controls.Add(_usageBar);

        _countdownLabel.AutoSize = false;
        _countdownLabel.Location = new Point(14, 112);
        _countdownLabel.Size = new Size(270, 16);
        _countdownLabel.ForeColor = Color.FromArgb(148, 163, 184);
        _countdownLabel.BackColor = Color.Transparent;
        _countdownLabel.Font = new Font("Segoe UI", 8F, FontStyle.Regular, GraphicsUnit.Point);
        Controls.Add(_countdownLabel);

        _detailLabel.AutoSize = false;
        _detailLabel.Location = new Point(14, 127);
        _detailLabel.Size = new Size(270, 14);
        _detailLabel.ForeColor = Color.FromArgb(100, 116, 139);
        _detailLabel.BackColor = Color.Transparent;
        _detailLabel.Font = new Font("Segoe UI", 7.5F, FontStyle.Regular, GraphicsUnit.Point);
        Controls.Add(_detailLabel);

        _footerLabel.AutoSize = false;
        _footerLabel.Location = new Point(14, 143);
        _footerLabel.Size = new Size(270, 12);
        _footerLabel.ForeColor = Color.FromArgb(71, 85, 105);
        _footerLabel.BackColor = Color.Transparent;
        _footerLabel.Font = new Font("Segoe UI", 7F, FontStyle.Regular, GraphicsUnit.Point);
        Controls.Add(_footerLabel);

        _toolTip.SetToolTip(_badgeLabel, "主窗口已用比例");
        _toolTip.SetToolTip(_usageBar, "绿色安全，橙色接近上限，红色临界");
    }

    private void RegisterDragSurface(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child == _refreshButton || child == _closeButton)
            {
                continue;
            }

            child.MouseDown += StartDrag;
            RegisterDragSurface(child);
        }
    }

    private void PaintBackgroundCard(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundRect(rect, 18);
        using var brush = new LinearGradientBrush(rect, Color.FromArgb(12, 18, 31), Color.FromArgb(18, 30, 46), 35F);
        using var border = new Pen(Color.FromArgb(42, 56, 78));
        using var accent = new Pen(Color.FromArgb(28, 163, 74, 90), 2F);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(accent, path);
        e.Graphics.DrawPath(border, path);
    }

    private void PositionWidget()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(area.Right - Width - 20, area.Top + 20);
    }

    private void ConfigureTray()
    {
        _trayMenu.Items.Add("显示", null, (_, _) =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        });
        _trayMenu.Items.Add("刷新", null, async (_, _) => await RefreshSnapshotAsync());
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add("退出", null, (_, _) => Close());

        _notifyIcon.Icon = SystemIcons.Application;
        _notifyIcon.Text = "Codex Limit Widget";
        _notifyIcon.ContextMenuStrip = _trayMenu;
        _notifyIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        };
    }

    private void ApplyRoundCorners()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = CreateRoundRect(new Rectangle(0, 0, Width, Height), 18);
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

        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
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
            var snapshot = await Task.Run(() =>
            {
                return ReadSnapshotWithRecovery();
            });
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

        _badgeLabel.Text = snapshot.PrimaryBadgeText();
        _badgeLabel.BackColor = Color.Transparent;
        _badgeLabel.ForeColor = snapshot.Primary.UsedPercent switch
        {
            >= 85 => Color.FromArgb(248, 113, 113),
            >= 60 => Color.FromArgb(251, 191, 36),
            _ => Color.FromArgb(74, 222, 128),
        };

        _headlineLabel.Text = snapshot.HeadlineText();
        _planLabel.Text = $"PLAN {(snapshot.PlanType ?? "unknown").ToUpperInvariant()}";
        _summaryLabel.Text = snapshot.SummaryText();
        _countdownLabel.Text = $"重置: 5小时窗 {snapshot.Primary.FormatResetCountdown()} | 周窗 {snapshot.Secondary.FormatResetCountdown()}";
        _detailLabel.Text = $"Credits {(snapshot.Credits?.Unlimited == true ? "无限" : snapshot.Credits?.Balance ?? "0")} | 重置额度 {snapshot.RateLimitResetCredits?.ToString() ?? "0"}";
        _footerLabel.Text = $"{DateTime.Now:HH:mm:ss} 更新 | 双击刷新 | 日志 widget.log";
        _usageBar.ValuePercent = snapshot.Primary.UsedPercent;
        Invalidate();
    }

    private void SetBusy()
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(SetBusy));
            return;
        }

        _badgeLabel.Text = "..";
        _badgeLabel.ForeColor = Color.FromArgb(96, 165, 250);
        _headlineLabel.Text = "同步中";
        _planLabel.Text = "读取 Codex 数据";
        _summaryLabel.Text = "等待 app-server 返回限额信息";
        _countdownLabel.Text = string.Empty;
        _detailLabel.Text = "请稍候";
        _footerLabel.Text = $"刷新间隔 {_intervalSeconds} 秒";
        _usageBar.ValuePercent = null;
    }

    private void SetError(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetError(text)));
            return;
        }

        _badgeLabel.Text = "!";
        _badgeLabel.ForeColor = Color.FromArgb(248, 113, 113);
        _headlineLabel.Text = "读取失败";
        _planLabel.Text = "检查 Codex 登录状态";
        _summaryLabel.Text = text;
        _countdownLabel.Text = "仍会在下一个间隔自动重试";
        _detailLabel.Text = "也可以在终端运行 status，看同目录 widget.log";
        _footerLabel.Text = "双击立即重试";
        _usageBar.ValuePercent = null;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

}

internal static class NativeMethods
{
    [DllImport("kernel32.dll")]
    internal static extern bool FreeConsole();
}

internal sealed class UsageBar : Control
{
    private int? _valuePercent;

    public UsageBar()
    {
        DoubleBuffered = true;
        Height = 16;
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
        using var bgPath = CreateRoundRect(rect, 8);
        e.Graphics.FillPath(bgBrush, bgPath);

        if (_valuePercent is null)
        {
            e.Graphics.DrawString("等待数据", Font, Brushes.WhiteSmoke, 8, 0);
            return;
        }

        var fillWidth = Math.Max(6, (int)Math.Round(rect.Width * Math.Clamp(_valuePercent.Value, 0, 100) / 100.0));
        var fillRect = new Rectangle(rect.X, rect.Y, fillWidth, rect.Height);
        var color = _valuePercent.Value switch
        {
            >= 85 => Color.FromArgb(239, 68, 68),
            >= 60 => Color.FromArgb(245, 158, 11),
            _ => Color.FromArgb(34, 197, 94),
        };
        using var fillBrush = new SolidBrush(color);
        using var fillPath = CreateRoundRect(fillRect, 8);
        e.Graphics.FillPath(fillBrush, fillPath);

        var text = $"{_valuePercent}%";
        var size = e.Graphics.MeasureString(text, Font);
        var x = rect.Width - size.Width - 8;
        var y = (rect.Height - size.Height) / 2 - 1;
        e.Graphics.DrawString(text, Font, Brushes.White, x, y);
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
