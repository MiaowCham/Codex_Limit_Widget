using Avalonia;
using CodexLimitWidget.Core;
using System.Reflection;

namespace CodexLimitWidget.App;

internal static class Program
{
    public static int RefreshIntervalSeconds { get; private set; } = 60;
    public static string ApplicationVersion { get; } = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "未知";
    public static FileAppLogger Logger { get; } = new();
    [STAThread]
    public static int Main(string[] args)
    {
        Logger.Info($"Application starting; args={string.Join(' ', args)}.");
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception) Logger.Error("AppDomain unhandled exception", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Logger.Error("Unobserved task exception", eventArgs.Exception);
            eventArgs.SetObserved();
        };
        var index = Array.IndexOf(args, "--interval");
        if (index >= 0 && (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var seconds) || seconds is < 1 or > 86400))
        {
            Console.Error.WriteLine("--interval 必须是 1 到 86400 之间的秒数。");
            return 2;
        }
        if (index >= 0) RefreshIntervalSeconds = int.Parse(args[index + 1]);
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception exception) { Logger.Error("Application startup", exception); return 1; }
        Logger.Info("Application exited normally.");
        return 0;
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = false }).LogToTrace();
}
