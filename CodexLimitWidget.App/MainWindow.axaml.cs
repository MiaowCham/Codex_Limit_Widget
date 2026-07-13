using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexLimitWidget.Core;
using CodexLimitWidget.App.ViewModels;
using CodexLimitWidget.Core.Resources;

namespace CodexLimitWidget.App;

public partial class MainWindow : Window
{
    private readonly CodexAppServerRateLimitProvider _provider;
    private readonly FileAppLogger _logger;
    private readonly MainWindowViewModel _viewModel;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _closing = new();
    private const uint WmNcLeftButtonDown = 0x00A1;
    private const nuint HtCaption = 2;
    private const string TiboProfileUrl = "https://x.com/thsottiaux";
    public MainWindow() { InitializeComponent(); _logger = Program.Logger; _logger.Info("Main window constructed."); _provider = new CodexAppServerRateLimitProvider(Program.ApplicationVersion, logger: _logger); _viewModel = new MainWindowViewModel(_provider, _logger); DataContext = _viewModel; _timer = new DispatcherTimer(TimeSpan.FromSeconds(Program.RefreshIntervalSeconds), DispatcherPriority.Background, (_, _) => QueueRefresh("timer")); Opened += (_, _) => { _logger.Info("Main window opened."); PositionAtTopRight(); _timer.Start(); QueueRefresh("startup"); }; SizeChanged += (_, _) => PositionAtTopRight(); Closed += async (_, _) => { _logger.Info("Main window closing."); _timer.Stop(); _closing.Cancel(); await _provider.DisposeAsync(); _closing.Dispose(); }; }
    private void QueueRefresh(string source)
    {
        _logger.Info($"Refresh queued by {source}.");
        _ = RefreshInBackgroundAsync();
    }
    internal void QueueRefreshFromTray() => QueueRefresh("tray");
    internal bool ToggleVisibilityFromTray()
    {
        if (IsVisible)
        {
            Hide();
            _logger.Info("Main window hidden from tray menu.");
            return false;
        }
        Show();
        Activate();
        _logger.Info("Main window shown from tray menu.");
        return true;
    }
    internal bool ToggleTopmostFromTray()
    {
        Topmost = !Topmost;
        PinIcon.Stroke = Topmost ? Brushes.White : Brush.Parse("#94A3B8");
        ToolTip.SetTip(PinButton, Topmost ? Strings.TooltipDisableTopmost : Strings.TooltipEnableTopmost);
        _logger.Info($"Topmost changed from tray menu; enabled={Topmost}.");
        return Topmost;
    }
    private async Task RefreshInBackgroundAsync()
    {
        try { await _viewModel.RefreshAsync(_closing.Token); }
        catch (Exception exception) { _logger.Error("Unexpected window refresh failure", exception); }
    }
    private void Refresh_Click(object? sender, RoutedEventArgs e) => QueueRefresh("button");
    private void Pin_Click(object? sender, RoutedEventArgs e)
    {
        ToggleTopmostFromTray();
    }
    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
    private void OpenTiboProfile_Click(object? sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(TiboProfileUrl) { UseShellExecute = true }); }
        catch (Exception exception) { _logger.Error("Opening Tibo X profile", exception); }
    }
    private void Surface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var isButton = IsButtonSource(e.Source);
        var leftPressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        if (!isButton && leftPressed)
        {
            if (OperatingSystem.IsWindows())
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle == IntPtr.Zero)
                {
                    BeginMoveDrag(e);
                }
                else
                {
                    ReleaseCapture();
                    SendMessage(handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
                }
            }
            else
            {
                BeginMoveDrag(e);
            }
            e.Handled = true;
        }
    }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint windowHandle, uint message, nuint wordParameter, nint longParameter);
    private static bool IsButtonSource(object? source) => source is Visual visual && (visual is Button || visual.FindAncestorOfType<Button>() is not null);
    private void PositionAtTopRight() { var area = Screens.Primary?.WorkingArea; if (area is { } workArea) Position = new PixelPoint(workArea.Right - (int)(Bounds.Width * RenderScaling), workArea.Y + 12); }
}
