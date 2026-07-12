using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CodexLimitWidget.App;
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted() { if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow(); base.OnFrameworkInitializationCompleted(); }
    private MainWindow? MainWindow => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
    private void ShowWindow_Click(object? sender, EventArgs e) => ShowMainWindow();
    private void Refresh_Click(object? sender, EventArgs e) => MainWindow?.QueueRefreshFromTray();
    private void Exit_Click(object? sender, EventArgs e) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    private void ShowMainWindow() { var window = MainWindow; if (window is null) return; window.Show(); window.Activate(); }
}
