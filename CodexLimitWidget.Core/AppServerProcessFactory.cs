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
        return ResolveWindowsCodexCommand(
            path,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
    }

    internal static (string FileName, string Arguments) ResolveWindowsCodexCommand(
        IEnumerable<string> pathEntries,
        string applicationData,
        string localApplicationData,
        string programFiles,
        string programFilesX86)
    {
        var path = pathEntries
            .Select(directory => directory.Trim().Trim('"'))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var npmDirectories = path
            .Concat(CombineWhenRooted(applicationData, "npm"))
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var nodeCandidates = path.Select(directory => Path.Combine(directory, "node.exe"))
            .Concat(CombineWhenRooted(programFiles, "nodejs", "node.exe"))
            .Concat(CombineWhenRooted(programFilesX86, "nodejs", "node.exe"))
            .Concat(CombineWhenRooted(localApplicationData, "Programs", "nodejs", "node.exe"))
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var node = nodeCandidates.FirstOrDefault(File.Exists);
        // npm 的 Windows shim 是 .cmd；直接以 node.exe 运行其入口，避免 cmd.exe
        // 在受限环境中被 UAC/企业策略拒绝（错误 740）。
        if (node is not null)
        {
            foreach (var directory in npmDirectories)
            {
                var script = Path.Combine(directory, "node_modules", "@openai", "codex", "bin", "codex.js");
                if (File.Exists(script)) return (node, $"\"{script}\" app-server --listen stdio://");
            }
        }

        foreach (var directory in path)
        {
            var found = Path.Combine(directory, "codex.exe");
            if (File.Exists(found)) return (found, "app-server --listen stdio://");
        }
        return ("codex.exe", "app-server --listen stdio://");
    }

    private static IEnumerable<string> CombineWhenRooted(string root, params string[] segments)
    {
        if (!string.IsNullOrWhiteSpace(root)) yield return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}
