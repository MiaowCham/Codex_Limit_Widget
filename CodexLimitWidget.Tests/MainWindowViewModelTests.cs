using CodexLimitWidget.App.ViewModels;
using CodexLimitWidget.Core;
using CodexLimitWidget.Core.Resources;
using Xunit;

namespace CodexLimitWidget.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task RefreshMapsSnapshotToDisplayProperties()
    {
        var vm = new MainWindowViewModel(new FakeProvider(CreateSnapshot()));
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(Strings.Format("RemainingPercent", 75), vm.Headline); Assert.Equal(Strings.Format("Plan", "PRO"), vm.Plan); Assert.Equal("025", vm.Badge); Assert.Equal(25, vm.Usage); Assert.StartsWith(Strings.Get("ResetAt").Split("{0}")[0], vm.Summary); Assert.Empty(vm.ErrorMessage);
    }
    [Fact]
    public async Task FailedRefreshKeepsLastSuccessfulData()
    {
        var provider = new FakeProvider(CreateSnapshot()); var vm = new MainWindowViewModel(provider);
        await vm.RefreshAsync(CancellationToken.None); provider.Exception = new InvalidOperationException("offline");
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(Strings.Get("ReadFailure"), vm.Headline); Assert.Equal(Strings.Format("Plan", "PRO"), vm.Plan); Assert.Equal(25, vm.Usage); Assert.Equal("offline", vm.ErrorMessage);
    }
    [Fact]
    public async Task FailedConfirmationReadAppliesFirstSuccessfulSnapshot()
    {
        var provider = new SequenceProvider(CreateSnapshot(), CreateSnapshot(10), new InvalidOperationException("offline"));
        var vm = new MainWindowViewModel(provider);
        await vm.RefreshAsync(CancellationToken.None);
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal(Strings.Format("RemainingPercent", 90), vm.Headline); Assert.Equal("010", vm.Badge); Assert.Equal(10, vm.Usage); Assert.Empty(vm.ErrorMessage);
    }
    private static RateLimitSnapshot CreateSnapshot(int usedPercent = 25) => new("codex", null, "pro", null, null, new(usedPercent, 300, null), new(50, null, null), null);
    private sealed class FakeProvider(RateLimitSnapshot snapshot) : IRateLimitProvider
    {
        public Exception? Exception { get; set; }
        public Task<RateLimitSnapshot> ReadAsync(CancellationToken cancellationToken) => Exception is { } ex ? Task.FromException<RateLimitSnapshot>(ex) : Task.FromResult(snapshot);
    }
    private sealed class SequenceProvider(params object[] results) : IRateLimitProvider
    {
        private readonly Queue<object> _results = new(results);
        public Task<RateLimitSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            var result = _results.Dequeue();
            return result is Exception error ? Task.FromException<RateLimitSnapshot>(error) : Task.FromResult((RateLimitSnapshot)result);
        }
    }
}
