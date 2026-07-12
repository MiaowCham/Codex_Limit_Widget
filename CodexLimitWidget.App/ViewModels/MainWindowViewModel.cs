using CodexLimitWidget.Core;
using CodexLimitWidget.Core.Resources;
using Avalonia.Media;

namespace CodexLimitWidget.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IRateLimitProvider _provider;
    private readonly IAppLogger _logger;
    private int _refreshing;
    private int? _lastSuccessfulUsedPercent;
    private string _headline = Strings.Loading, _plan = "", _summary = "", _countdown = "", _details = "", _updated = "", _errorMessage = "", _badge = "---";
    private double _usage;
    private IBrush _badgeBrush = Brushes.LightGreen;
    private IBrush _usageBrush = Brushes.LightGreen;
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
    public IBrush UsageBrush { get => _usageBrush; private set => SetProperty(ref _usageBrush, value); }
    public bool IsRefreshing => Volatile.Read(ref _refreshing) != 0;
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) { _logger.Info("UI refresh ignored because a refresh is already active."); return; }
        _logger.Info("UI refresh started.");
        Updated = Strings.Get("Refreshing");
        RaisePropertyChanged(nameof(IsRefreshing));
        try
        {
            var snapshot = await ReadOnWorkerAsync(cancellationToken).ConfigureAwait(true);
            if (_lastSuccessfulUsedPercent is int previous && snapshot.Primary.UsedPercent is int current && current < previous)
            {
                _logger.Info($"Primary used percent dropped from {previous}% to {current}%; starting one confirmation read after one second.");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
                try
                {
                    snapshot = await ReadOnWorkerAsync(cancellationToken).ConfigureAwait(true);
                }
                catch (Exception confirmationError) when (confirmationError is not OperationCanceledException)
                {
                    _logger.Error("Primary used percent confirmation read", confirmationError);
                }
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
        Headline = snapshot.RemainingPercent is { } remaining ? Strings.Format("RemainingPercent", remaining) : Strings.Get("LimitUnknown");
        Plan = Strings.Format("Plan", (snapshot.PlanType ?? "unknown").ToUpperInvariant()); Usage = snapshot.Primary.UsedPercent ?? 0;
        Badge = snapshot.Primary.UsedPercent?.ToString("D3") ?? "---";
        BadgeBrush = UsageBrush = (snapshot.Primary.UsedPercent ?? 0) switch { >= 85 => Brushes.LightCoral, >= 60 => Brushes.Gold, _ => Brushes.LightGreen };
        Summary = Strings.Format("ResetAt", FormatResetMoment(snapshot.Primary.ResetsAt, false));
        Countdown = Strings.Format("WeeklyLimit", snapshot.Secondary.UsedPercent?.ToString() ?? "--", FormatResetMoment(snapshot.Secondary.ResetsAt, true));
        Details = Strings.Format("Details", snapshot.Credits?.Unlimited == true ? Strings.Get("Unlimited") : snapshot.Credits?.Balance ?? "0", snapshot.RateLimitResetCredits?.ToString() ?? "0");
        Updated = Strings.Format("UpdatedAt", DateTime.Now.ToString("T")); ErrorMessage = "";
        _lastSuccessfulUsedPercent = snapshot.Primary.UsedPercent;
        _logger.Info($"UI snapshot applied; primaryUsed={snapshot.Primary.UsedPercent?.ToString() ?? "unknown"}%, secondaryUsed={snapshot.Secondary.UsedPercent?.ToString() ?? "unknown"}%. ");
    }
    private void SetError(Exception exception)
    {
        Headline = Strings.Get("ReadFailure"); Badge = "!"; BadgeBrush = UsageBrush = Brushes.LightCoral; ErrorMessage = exception.Message; Updated = Strings.Get("RefreshFailed");
        if (exception.Message.Contains("Codex CLI", StringComparison.OrdinalIgnoreCase))
        {
            Summary = Strings.Get("CodexCliMissing");
            Countdown = Strings.Get("InstallCodexCli");
            Details = Strings.Get("LinuxIdeCliNote");
        }
    }
    private static string FormatResetMoment(long? epochSeconds, bool includeDate) => epochSeconds is null or <= 0 ? Strings.Unknown : (includeDate || DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).LocalDateTime.Date != DateTime.Today ? DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).LocalDateTime.ToString(Strings.Get("ResetTimeWithDateFormat")) : DateTimeOffset.FromUnixTimeSeconds(epochSeconds.Value).LocalDateTime.ToString(Strings.Get("ResetTimeFormat")));
}
