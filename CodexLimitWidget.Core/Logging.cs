namespace CodexLimitWidget.Core;

public interface IAppLogger
{
    void Info(string message);
    void Error(string source, Exception exception);
}

public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();
    private NullAppLogger() { }
    public void Info(string message) { }
    public void Error(string source, Exception exception) { }
}

public sealed class FileAppLogger : IAppLogger
{
    private static readonly object WriteLock = new();
    private readonly string _path;
    public string LogPath => _path;
    public FileAppLogger(string applicationName = "CodexLimitWidget")
    {
        var directory = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), applicationName, "Logs")
            : OperatingSystem.IsMacOS()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", applicationName)
                : Path.Combine(Environment.GetEnvironmentVariable("XDG_STATE_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"), applicationName, "Logs");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "widget.log");
    }
    public void Info(string message) => Write("INFO", message);
    public void Error(string source, Exception exception) => Write("ERROR", $"{source}{Environment.NewLine}{exception}");
    private void Write(string level, string message)
    {
        var line = $"[{DateTimeOffset.Now:O}] [T{Environment.CurrentManagedThreadId}] {level} {message}{Environment.NewLine}";
        try { lock (WriteLock) File.AppendAllText(_path, line); } catch { }
        System.Diagnostics.Debug.Write(line);
    }
}
