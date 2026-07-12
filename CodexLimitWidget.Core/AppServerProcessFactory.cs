using System.Diagnostics;
using CodexLimitWidget.Core.Resources;

namespace CodexLimitWidget.Core;

public interface IAppServerProcessFactory
{
    Process Start();
}

public sealed class DefaultAppServerProcessFactory : IAppServerProcessFactory
{
    public Process Start()
    {
        var command = ResolveCodexCommand();
        var startInfo = new ProcessStartInfo
        {
            FileName = command.FileName,
            Arguments = command.Arguments,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        try { return Process.Start(startInfo) ?? throw new InvalidOperationException(Strings.Get("AppServerStartFailed")); }
        catch (Exception ex) { throw new InvalidOperationException(Strings.Get("CodexCliNotFound"), ex); }
    }

    private static (string FileName, string Arguments) ResolveCodexCommand()
    {
        if (!OperatingSystem.IsWindows())
        {
            var pathEntries = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
            var candidates = pathEntries.Select(directory => Path.Combine(directory.Trim(), "codex"))
                .Concat(new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "codex"),
                    "/usr/local/bin/codex",
                    "/opt/homebrew/bin/codex",
                    "/usr/bin/codex",
                });
            var found = candidates.FirstOrDefault(File.Exists);
            return (found ?? "codex", "app-server --listen stdio://");
        }
        var path = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries) ?? [];
        // npm 的 Windows shim 是 .cmd；直接以 node.exe 运行其入口，避免 cmd.exe
        // 在受限环境中被 UAC/企业策略拒绝（错误 740）。
        foreach (var directory in path)
        {
            var baseDirectory = directory.Trim();
            var node = Path.Combine(baseDirectory, "node.exe");
            var script = Path.Combine(baseDirectory, "node_modules", "@openai", "codex", "bin", "codex.js");
            if (File.Exists(node) && File.Exists(script)) return (node, $"\"{script}\" app-server --listen stdio://");
        }
        foreach (var directory in path)
        {
            var found = Path.Combine(directory.Trim(), "codex.exe");
            if (File.Exists(found)) return (found, "app-server --listen stdio://");
        }
        return ("codex.exe", "app-server --listen stdio://");
    }
}
