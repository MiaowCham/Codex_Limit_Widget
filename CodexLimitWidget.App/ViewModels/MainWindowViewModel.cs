using CodexLimitWidget.Core;
using Avalonia.Media;

namespace CodexLimitWidget.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IRateLimitProvider _provider;
    private readonly IAppLogger _logger;
    private int _refreshing;
    private int? _lastSuccessfulUsedPercent;
    private string _headline = "正在读取…", _plan = "", _summary = "", _countdown = "", _details = "", _updated = "", _errorMessage = "", _badge = "---";
    private double _usage;
    private IBrush _badgeBrush = Brushes.LightGreen;
    public MainWindowViewModel(IRateLimitProvider provider, IAppLogger? logger = null) { _provider = provider; _logger = logger ?? NullAppLogger.Instance; }
    public string Headline { get => _headline; private set => SetProperty(ref _headline, value); }
    public string Plan { get => _plan; private set => SetProperty(ref _plan, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Countdown { get => _countdown; private set => SetProperty(ref _countdown, value); }
    public string Details { get => _details; private set => SetProperty(ref _details, value); }
    public string Updated { get => _updated; private set => SetProperty(ref _updated, value); }
    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public double Usage { get => _usage; private set => SetProperty(ref _usage, value); }
    public string Badge { get => _badge; private set => SetProperty(ref _badge, value); }
    public IBrush BadgeBrush { get => _badgeBrush; private set => SetProperty(ref _badgeBrush, value); }
    public bool IsRefreshing => Volatile.Read(ref _refreshing) != 0;
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) { _logger.Info("UI refresh ignored because a refresh is already active."); return; }
        _logger.Info("UI refresh started.");
        Updated = "刷新中…";
        RaisePropertyChanged(nameof(IsRefreshing));
        try
        {
            var snapshot = await ReadOnWorkerAsync(cancellationToken).ConfigureAwait(true);
            if (_lastSuccessfulUsedPercent is int previous && snapshot.Primary.UsedPercent is int current && current < previous)
            {
                _logger.Info($"Primary used percent dropped from {previous}% to {current}%; starting one confirmation read after one second.");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
                snapshot = await ReadOnWorkerAsync(cancellationToken).ConfigureAwait(true);
            }
            ApplySnapshot(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { _logger.Info("UI refresh canceled."); }
        catch (Exception initialError) when (_lastSuccessfulUsedPercent is null)
        {
            _logger.Error("Initial UI refresh", initialError);
            try
            {
                _logger.Info("Initial UI refresh failed; retrying once after one second.");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
                ApplySnapshot(await ReadOnWorkerAsync(cancellationToken).ConfigureAwait(true));
            }
            catch (Exception retryError) { _logger.Error("Initial UI refresh retry", retryError); SetError(retryError); }
        }
        catch (Exception ex) { _logger.Error("UI refresh", ex); SetError(ex); }
        finally { Interlocked.Exchange(ref _refreshing, 0); RaisePropertyChanged(nameof(IsRefreshing)); _logger.Info("UI refresh finished."); }
    }
    private Task<RateLimitSnapshot> ReadOnWorkerAsync(CancellationToken cancellationToken) =>
        Task.Run(() => _provider.ReadAsync(cancellationToken), cancellationToken);
    private void ApplySnapshot(RateLimitSnapshot snapshot)
    {
        Headline = snapshot.RemainingPercent is { } remaining ? $"剩余 {remaining}%" : "限额未知";
        Plan = $"PLAN {(snapshot.PlanType ?? "unknown").ToUpperInvariant()}"; Usage = snapshot.Primary.UsedPercent ?? 0;
        Badge = snapshot.Primary.UsedPercent?.ToString("D3") ?? "---";
        BadgeBrush = (snapshot.Primary.UsedPercent ?? 0) switch { >= 85 => Brushes.LightCoral, >= 60 => Brushes.Gold, _ => Brushes.LightGreen };
        Summary = $"将于 {FormatResetMoment(snapshot.Primary.ResetsAt, false)} 重置";
        Countdown = $"周限额 {snapshot.Secondary.UsedPercent?.ToString() ?? "--"}% · {FormatResetMoment(snapshot.Secondary.ResetsAt, true)}";
        Details = $"Credits {(snapshot.Credits?.Unlimited == true ? "无限" : snapshot.Credits?.Balance ?? "0")} | 重置额度 {snapshot.RateLimitResetCredits?.ToString() ?? "0"}";
        Updated = $"{DateTime.Now:HH:mm:ss} 更新"; ErrorMessage = "";
        _lastSuccessfulUsedPercent = snapshot.Primary.UsedPercent;
        _logger.Info($"UI snapshot applied; primaryUsed={snapshot.Primary.UsedPercent?.ToString() ?? "unknown"}%, secondaryUsed={snapshot.Secondary.UsedPercent?.ToString() ?? "unknown"}%. ");
    }
    private void SetError(Exception exception)
    {
        Headline = "读取失败"; Badge = "!"; BadgeBrush = Brushes.LightCoral; ErrorMessage = exception.Message; Updated = "刷新失败";
        if (exception.Message.Contains("Codex CLI", StringComparison.OrdinalIgnoreCase))
        {
            Summary = "未找到 Codex CLI";
            Countdown = "请先安装 Codex CLI";
            Details = "Linux IDE 插件不会自动提供 CLI";
        }
    }
    private static string FormatResetMoment(long? epochSeconds, bool includeDate) => epochSeconds is null or <= 0 ? "未知" : (includeDate || DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).LocalDateTime.Date != DateTime.Today ? DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).LocalDateTime.ToString("M月d日 HH:mm") : DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).LocalDateTime.ToString("HH:mm"));
}
