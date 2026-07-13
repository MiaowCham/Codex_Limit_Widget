using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using CodexLimitWidget.Core.Resources;

namespace CodexLimitWidget.Core;

public interface IRateLimitProvider
{
    Task<RateLimitSnapshot> ReadAsync(CancellationToken cancellationToken);
}

public sealed class CodexAppServerRateLimitProvider : IRateLimitProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _version;
    private readonly IAppServerProcessFactory _processFactory;
    private readonly IAppLogger _logger;
    private System.Diagnostics.Process? _process;
    private Task<string>? _stderrTask;
    private int _nextId;
    private bool _disposed;

    public CodexAppServerRateLimitProvider(string version, IAppServerProcessFactory? processFactory = null, IAppLogger? logger = null) { _version = version; _processFactory = processFactory ?? new DefaultAppServerProcessFactory(); _logger = logger ?? new FileAppLogger(); }

    public async Task<RateLimitSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.StartNew();
        _logger.Info($"Rate-limit read requested; processRunning={_process is { HasExited: false }}.");
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                var snapshot = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                _logger.Info($"Rate-limit read succeeded in {elapsed.ElapsedMilliseconds} ms.");
                return snapshot;
            }
            catch (Exception firstError) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Error("Rate-limit first attempt", firstError);
                await StopAsync().ConfigureAwait(false);
                try
                {
                    var snapshot = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                    _logger.Info($"Rate-limit recovery read succeeded in {elapsed.ElapsedMilliseconds} ms.");
                    return snapshot;
                }
                catch (Exception retryError)
                {
                    _logger.Error("Rate-limit recovery attempt", retryError);
                    // Do not leave a process that timed out twice available for the
                    // ViewModel's delayed startup retry. A new call must start clean.
                    await StopAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<RateLimitSnapshot> ReadCoreAsync(CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited) await StartAsync(cancellationToken).ConfigureAwait(false);
        var response = await RequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
        if (response.TryGetProperty("error", out var error)) throw new InvalidOperationException(Strings.Format("ReadLimitFailed", error));
        if (!response.TryGetProperty("result", out var result)) throw new InvalidOperationException(Strings.Get("AppServerResultMissing"));
        return RateLimitSnapshot.FromJson(result);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Info("Starting Codex app-server process.");
        _process = _processFactory.Start();
        _logger.Info($"Codex app-server started; pid={_process.Id}.");
        _stderrTask = _process.StandardError.ReadToEndAsync();
        var elapsed = Stopwatch.StartNew();
        var init = await RequestAsync("initialize", new { clientInfo = new { name = "codex-limit-widget", version = _version }, capabilities = new { experimentalApi = true } }, cancellationToken).ConfigureAwait(false);
        if (!init.TryGetProperty("result", out _)) throw new InvalidOperationException(Strings.Get("AppServerInitializationFailed"));
        _logger.Info($"Codex app-server initialized in {elapsed.ElapsedMilliseconds} ms.");
    }

    private async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new InvalidOperationException(Strings.Get("AppServerNotStarted"));
        if (process.HasExited) throw new InvalidOperationException(Strings.Format("AppServerExited", process.ExitCode));
        var id = Interlocked.Increment(ref _nextId);
        var elapsed = Stopwatch.StartNew();
        _logger.Info($"RPC send: id={id}, method={method}.");
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { id, method, @params = parameters }, JsonOptions)).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            while (await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { } line)
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("id", out var responseId) && responseId.TryGetInt32(out var responseNumber) && responseNumber == id)
                    {
                        _logger.Info($"RPC response: id={id}, method={method}, elapsed={elapsed.ElapsedMilliseconds} ms.");
                        return doc.RootElement.Clone();
                    }
                }
                catch (JsonException exception) { _logger.Error($"Invalid app-server stdout for {method}", exception); }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(Strings.Format("AppServerTimeout", method));
        }
        throw new InvalidOperationException(Strings.Get("AppServerExitedWithoutCode"));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (_disposed) return; _disposed = true; await StopAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private async Task StopAsync()
    {
        if (_process is { } process)
        {
            _process = null;
            try { if (!process.HasExited) { _logger.Info($"Stopping Codex app-server pid={process.Id}."); process.Kill(true); } await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false); }
            catch { }
            finally { process.Dispose(); }
        }
        if (_stderrTask is { } stderr)
        {
            _stderrTask = null;
            try
            {
                if (await Task.WhenAny(stderr, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false) == stderr)
                {
                    var output = await stderr.ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(output)) _logger.Info($"Codex app-server stderr: {output[..Math.Min(output.Length, 2000)]}");
                }
                else
                {
                    _logger.Info("Timed out waiting for Codex app-server stderr to close; continuing shutdown.");
                    _ = stderr.ContinueWith(task => _ = task.Exception, TaskContinuationOptions.OnlyOnFaulted);
                }
            }
            catch (Exception exception) { _logger.Error("Collecting app-server stderr", exception); }
        }
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
