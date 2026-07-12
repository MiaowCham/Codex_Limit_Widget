using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Diagnostics;
using CodexLimitWidget.Core.Resources;

namespace CodexLimitWidget.App;
public partial class App : Application
{
    private const string RepositoryUrl = "https://github.com/MiaowCham/Codex_Limit_Widget";
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
    private MainWindow? MainWindow => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
    private void ToggleWindow_Click(object? sender, EventArgs e)
    {
        var visible = MainWindow?.ToggleVisibilityFromTray() ?? false;
        if (sender is NativeMenuItem item) item.Header = visible ? Strings.TrayHideWindow : Strings.Get("TrayShowWindow");
    }
    private void ToggleTopmost_Click(object? sender, EventArgs e)
    {
        var topmost = MainWindow?.ToggleTopmostFromTray() ?? false;
        if (sender is NativeMenuItem item) item.Header = topmost ? Strings.TooltipDisableTopmost : Strings.TooltipEnableTopmost;
    }
    private void Refresh_Click(object? sender, EventArgs e) => MainWindow?.QueueRefreshFromTray();
    private void OpenRepository_Click(object? sender, EventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true }); }
        catch (Exception exception) { Program.Logger.Error("Opening repository URL", exception); }
    }
    private void Exit_Click(object? sender, EventArgs e) => (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
}
