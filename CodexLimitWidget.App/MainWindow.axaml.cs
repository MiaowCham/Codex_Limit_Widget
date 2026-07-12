using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using System.Runtime.InteropServices;
using CodexLimitWidget.Core;
using CodexLimitWidget.App.ViewModels;

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
    public MainWindow() { InitializeComponent(); _logger = Program.Logger; _logger.Info("Main window constructed."); _provider = new CodexAppServerRateLimitProvider(Program.ApplicationVersion, logger: _logger); _viewModel = new MainWindowViewModel(_provider, _logger); DataContext = _viewModel; _timer = new DispatcherTimer(TimeSpan.FromSeconds(Program.RefreshIntervalSeconds), DispatcherPriority.Background, (_, _) => QueueRefresh("timer")); Opened += (_, _) => { _logger.Info("Main window opened."); PositionAtTopRight(); _timer.Start(); QueueRefresh("startup"); }; Closed += async (_, _) => { _logger.Info("Main window closing."); _timer.Stop(); _closing.Cancel(); await _provider.DisposeAsync(); _closing.Dispose(); }; }
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
        ToolTip.SetTip(PinButton, Topmost ? "取消置顶" : "启用置顶");
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
    private void Surface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        var isButton = IsButtonSource(e.Source);
        var leftPressed = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        _logger.Info($"Drag pointer pressed; position={point.X:F1},{point.Y:F1}; left={leftPressed}; buttonSource={isButton}.");
        if (!isButton && leftPressed)
        {
            if (OperatingSystem.IsWindows())
            {
                var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                if (handle == IntPtr.Zero)
                {
                    _logger.Info("Native Windows drag unavailable because the platform handle is missing; using Avalonia fallback.");
                    BeginMoveDrag(e);
                }
                else
                {
                    _logger.Info($"Native Windows drag started; hwnd=0x{handle.ToInt64():X}; window={Position.X},{Position.Y}.");
                    ReleaseCapture();
                    SendMessage(handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
                    _logger.Info($"Native Windows drag ended; window={Position.X},{Position.Y}.");
                }
            }
            else
            {
                _logger.Info("Avalonia native drag started for non-Windows platform.");
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
