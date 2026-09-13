namespace CodexLimitWidget.Core;

/// <summary>Opt-in switches for reproducing difficult local installation states.</summary>
public static class DebugOptions
{
    public static bool ForceCliMissing { get; private set; }
    public static bool SkipBundledCli { get; private set; }
    public static string? CliPathOverride { get; private set; }

    public static void Configure(IEnumerable<string> arguments)
    {
        var args = arguments.ToArray();
        ForceCliMissing = args.Contains("--debug-cli-missing", StringComparer.OrdinalIgnoreCase);
        SkipBundledCli = args.Contains("--debug-skip-bundled-cli", StringComparer.OrdinalIgnoreCase);
        var index = Array.FindIndex(args, arg => arg.Equals("--debug-cli-path", StringComparison.OrdinalIgnoreCase));
        CliPathOverride = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
