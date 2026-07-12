using Avalonia;
using CodexLimitWidget.Core;
using CodexLimitWidget.Core.Resources;
using System.Reflection;

namespace CodexLimitWidget.App;

internal static class Program
{
    public static int RefreshIntervalSeconds { get; private set; } = 60;
    public static string ApplicationVersion { get; } = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? Strings.Unknown;
    public static FileAppLogger Logger { get; } = new();
    [STAThread]
    public static int Main(string[] args)
    {
        var language = ParseLanguage(args);
        if (language is not null && !Localization.TrySetCulture(language))
        {
            Console.Error.WriteLine(Strings.Format("InvalidLanguage", language));
            return 2;
        }
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
            Console.Error.WriteLine(Strings.Get("InvalidInterval"));
            return 2;
        }
        if (index >= 0) RefreshIntervalSeconds = int.Parse(args[index + 1]);
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception exception) { Logger.Error("Application startup", exception); return 1; }
        Logger.Info("Application exited normally.");
        return 0;
    }
    private static string? ParseLanguage(string[] arguments) { var index = Array.IndexOf(arguments, "--language"); return index < 0 ? null : index + 1 < arguments.Length ? arguments[index + 1] : ""; }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = false }).LogToTrace();
}
