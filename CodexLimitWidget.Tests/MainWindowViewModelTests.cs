using CodexLimitWidget.App.ViewModels;
using CodexLimitWidget.Core;
using Xunit;

namespace CodexLimitWidget.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task RefreshMapsSnapshotToDisplayProperties()
    {
        var vm = new MainWindowViewModel(new FakeProvider(CreateSnapshot()));
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("剩余 75%", vm.Headline); Assert.Equal("PLAN PRO", vm.Plan); Assert.Equal("025", vm.Badge); Assert.Equal(25, vm.Usage); Assert.Contains("将于", vm.Summary); Assert.Empty(vm.ErrorMessage);
    }
    [Fact]
    public async Task FailedRefreshKeepsLastSuccessfulData()
    {
        var provider = new FakeProvider(CreateSnapshot()); var vm = new MainWindowViewModel(provider);
        await vm.RefreshAsync(CancellationToken.None); provider.Exception = new InvalidOperationException("offline");
        await vm.RefreshAsync(CancellationToken.None);
        Assert.Equal("读取失败", vm.Headline); Assert.Equal("PLAN PRO", vm.Plan); Assert.Equal(25, vm.Usage); Assert.Equal("offline", vm.ErrorMessage);
    }
    private static RateLimitSnapshot CreateSnapshot() => new("codex", null, "pro", null, null, new(25, 300, null), new(50, null, null), null);
    private sealed class FakeProvider(RateLimitSnapshot snapshot) : IRateLimitProvider
    {
        public Exception? Exception { get; set; }
        public Task<RateLimitSnapshot> ReadAsync(CancellationToken cancellationToken) => Exception is { } ex ? Task.FromException<RateLimitSnapshot>(ex) : Task.FromResult(snapshot);
    }
}
