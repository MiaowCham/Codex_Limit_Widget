using CodexLimitWidget.Core;
using CodexLimitWidget.Core.Resources;

var version = ApplicationVersion.FromAssembly(typeof(Program).Assembly);
var language = ParseLanguage(args);
if (language is not null && !Localization.TrySetCulture(language))
{
    Console.Error.WriteLine(Strings.Format("InvalidLanguage", language));
    return 2;
}
if (args.Length == 0 || args[0] is "--help" or "-h") return Help();
var interval = ParseInterval(args);
if (interval is null) return 2;
await using var provider = new CodexAppServerRateLimitProvider(version);
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
try
{
    if (args[0].Equals("status", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine((await provider.ReadAsync(cancel.Token)).FormatMultiline()); return 0; }
    if (!args[0].Equals("watch", StringComparison.OrdinalIgnoreCase)) return Help();
    while (!cancel.IsCancellationRequested) { try { Console.WriteLine((await provider.ReadAsync(cancel.Token)).FormatMultiline()); } catch (Exception ex) { Console.Error.WriteLine(Strings.Format("ReadFailed", ex.Message)); } await Task.Delay(TimeSpan.FromSeconds(interval.Value), cancel.Token); }
    return 0;
}
catch (OperationCanceledException) { return 0; }
catch (Exception ex) { Console.Error.WriteLine(Strings.Format("ReadFailed", ex.Message)); return 1; }

int Help() { Console.WriteLine(Strings.Format("CliHelp", version)); return 0; }
int? ParseInterval(string[] arguments) { var i = Array.IndexOf(arguments, "--interval"); if (i < 0) return 60; return i + 1 < arguments.Length && int.TryParse(arguments[i + 1], out var value) && value is >= 1 and <= 86400 ? value : null; }
string? ParseLanguage(string[] arguments) { var i = Array.IndexOf(arguments, "--language"); return i < 0 ? null : i + 1 < arguments.Length ? arguments[i + 1] : ""; }
