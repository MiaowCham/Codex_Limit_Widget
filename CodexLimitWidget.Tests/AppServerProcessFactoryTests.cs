using CodexLimitWidget.Core;
using Xunit;

namespace CodexLimitWidget.Tests;

public sealed class AppServerProcessFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"codex-limit-widget-{Guid.NewGuid():N}");

    [Fact]
    public void ResolvesNodeAndCodexScriptFromDifferentPathDirectories()
    {
        var nodeDirectory = CreateFile("node", "node.exe");
        var npmDirectory = CreateFile("npm", "node_modules", "@openai", "codex", "bin", "codex.js");

        var command = DefaultAppServerProcessFactory.ResolveWindowsCodexCommand(
            [Path.GetDirectoryName(nodeDirectory)!, Path.Combine(_root, "npm")], "", "", "", "");

        Assert.Equal(nodeDirectory, command.FileName);
        Assert.Equal($"\"{npmDirectory}\" app-server --listen stdio://", command.Arguments);
    }

    [Fact]
    public void ResolvesDesktopCodexInstallationWhenItIsNotOnPath()
    {
        var root = Path.Combine(_root, "LocalAppData", "Programs", "Codex");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "codex.exe"), "");
        var command = DefaultAppServerProcessFactory.ResolveWindowsCodexCommand(
            [], Path.Combine(_root, "AppData"), Path.Combine(_root, "LocalAppData"),
            Path.Combine(_root, "ProgramFiles"), Path.Combine(_root, "ProgramFilesX86"));
        Assert.Equal(Path.Combine(root, "codex.exe"), command.FileName);
    }

    [Fact]
    public void ResolvesStandardNpmDirectoryWhenGuiPathDoesNotContainIt()
    {
        var node = CreateFile("ProgramFiles", "nodejs", "node.exe");
        var script = CreateFile("AppData", "npm", "node_modules", "@openai", "codex", "bin", "codex.js");

        var command = DefaultAppServerProcessFactory.ResolveWindowsCodexCommand(
            [], Path.Combine(_root, "AppData"), Path.Combine(_root, "LocalAppData"),
            Path.Combine(_root, "ProgramFiles"), Path.Combine(_root, "ProgramFilesX86"));

        Assert.Equal(node, command.FileName);
        Assert.Equal($"\"{script}\" app-server --listen stdio://", command.Arguments);
    }

    private string CreateFile(params string[] segments)
    {
        var path = Path.Combine(new[] { _root }.Concat(segments).ToArray());
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
