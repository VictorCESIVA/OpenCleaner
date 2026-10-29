using Moq;
using OpenCleaner.Contracts;
using OpenCleaner.Core;
using OpenCleaner.Core.Scheduler;
using Xunit;

namespace OpenCleaner.Core.Tests;

public sealed class AutomaticCleaningTests
{
    private static CleanableItem Item(RiskLevel risk = RiskLevel.Safe, ItemType type = ItemType.File) =>
        new(Guid.NewGuid().ToString("N"), Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), 100,
            "test item", risk, type, ParentPluginId: "test.plugin");
    private static Mock<ICleanerPlugin> Plugin()
    {
        var plugin = new Mock<ICleanerPlugin>();
        plugin.SetupGet(p => p.Id).Returns("test.plugin");
        plugin.SetupGet(p => p.Name).Returns("Test plugin");
        plugin.SetupGet(p => p.IsAvailable).Returns(true);
        plugin.SetupGet(p => p.Category).Returns(PluginCategory.System);
        return plugin;
    }

    [Fact]
    public async Task SmartCleanOnlyPassesExplicitlySafeFileItemsAndUsesActualCounters()
    {
        var plugin = Plugin();
        var safe = Item();
        var items = new List<CleanableItem> { safe, Item(RiskLevel.ExpertOnly), Item((RiskLevel)99),
            Item(RiskLevel.Recommended), Item(type: ItemType.Directory), Item(type: ItemType.RegistryKey),
            Item() with { ParentPluginId = "other.plugin" } };
        plugin.Setup(p => p.CleanAsync(It.IsAny<IEnumerable<CleanableItem>>(), It.IsAny<IBackupManager>(),
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(true, "done", Guid.NewGuid(), ItemsProcessed: 1, BytesRecovered: 0))
            .Callback<IEnumerable<CleanableItem>, IBackupManager, IProgress<double>?, CancellationToken>((actual, _, _, _) =>
                Assert.Equal(new[] { safe }, actual));
        var result = await new SmartCleanService(Mock.Of<IBackupManager>()).ExecuteSmartCleanAsync([new(plugin.Object, items, 500)]);
        Assert.Equal(1, result.ItemsCleaned);
        Assert.Equal(0, result.BytesFreed);
    }

    [Fact]
    public async Task SmartCleanPreservesPartialCountersOnFailure()
    {
        var plugin = Plugin();
        plugin.Setup(p => p.CleanAsync(It.IsAny<IEnumerable<CleanableItem>>(), It.IsAny<IBackupManager>(),
                It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationResult(false, "partial failure", Guid.NewGuid(), ItemsProcessed: 1, BytesRecovered: 42));
        var result = await new SmartCleanService(Mock.Of<IBackupManager>()).ExecuteSmartCleanAsync([new(plugin.Object, [Item(), Item()], 200)]);
        Assert.False(result.Success);
        Assert.Equal(1, result.ItemsCleaned);
        Assert.Equal(42, result.BytesFreed);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task QuickScanFiltersUnsafeItemsBeforeRankingPlugins()
    {
        var plugin = Plugin();
        plugin.Setup(p => p.AnalyzeAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Item(), Item(RiskLevel.Recommended), Item(type: ItemType.Directory),
                Item(RiskLevel.ExpertOnly) with { Size = long.MaxValue } });
        var results = await new SmartCleanService(Mock.Of<IBackupManager>()).QuickScanAsync([plugin.Object]);
        Assert.Single(Assert.Single(results).Items);
        Assert.Equal(100, results[0].TotalSize);
    }

    [Fact]
    public async Task QuickScanCancellationPropagatesEvenForUncooperativePlugin()
    {
        var plugin = Plugin();
        var stalled = new TaskCompletionSource<IReadOnlyList<CleanableItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        plugin.Setup(p => p.AnalyzeAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>())).Returns(stalled.Task);
        using var cancellation = new CancellationTokenSource();
        var scan = new SmartCleanService(Mock.Of<IBackupManager>()).QuickScanAsync([plugin.Object], cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scan);
        stalled.SetResult([]);
    }

    [Fact]
    public void OfflineServiceWorkerContentIsExcludedByDefault() => Assert.False(BrowserCleanOptions.IncludeServiceWorkers);

    [Fact]
    public async Task QuickScanRetainsEmptySuccessAndPluginFailure()
    {
        var failure = Plugin();
        failure.Setup(p => p.AnalyzeAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("access denied"));
        var empty = Plugin();
        empty.SetupGet(p => p.Id).Returns("test.empty");
        empty.Setup(p => p.AnalyzeAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var service = new SmartCleanService(Mock.Of<IBackupManager>());
        var results = await service.QuickScanAsync([failure.Object, empty.Object]);
        Assert.Equal(2, results.Length);
        Assert.Null(Assert.Single(results, r => r.Plugin.Id == "test.empty").Error);
        Assert.Contains("access denied", Assert.Single(results, r => r.Plugin.Id == "test.plugin").Error);
        var result = await service.ExecuteSmartCleanAsync(results);
        Assert.False(result.Success);
        Assert.Single(result.Errors);
        Assert.Equal(0, result.ItemsCleaned);
        failure.Verify(p => p.CleanAsync(It.IsAny<IEnumerable<CleanableItem>>(), It.IsAny<IBackupManager>(),
            It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PluginCancellationIsVisibleAsScanFailure()
    {
        var plugin = Plugin();
        plugin.Setup(p => p.AnalyzeAsync(It.IsAny<IProgress<double>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var result = Assert.Single(await new SmartCleanService(Mock.Of<IBackupManager>()).QuickScanAsync([plugin.Object]));
        Assert.Contains("annulée", result.Error);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void ScheduleRejectsMalformedOrMissingValues()
    {
        Assert.Throws<ArgumentException>(() => new ScheduleConfig { IsEnabled = true, Days = [] }.Validate());
        Assert.Throws<ArgumentException>(() => new ScheduleConfig { Days = [(DayOfWeek)99] }.Validate());
        Assert.Throws<ArgumentException>(() => new ScheduleConfig { PluginIds = ["plugin\" --other-flag"] }.Validate());
        Assert.Throws<ArgumentException>(() => new ScheduleConfig { PluginIds = ["system.windowsupdate"] }.Validate());
        Assert.Throws<ArgumentException>(() => new ScheduleConfig { PluginIds = ["registry.safe"] }.Validate());
        Assert.Throws<ArgumentException>(() => new ScheduleConfig { PluginIds = null! }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScheduleConfig { TimeOfDay = TimeSpan.FromDays(1) }.Validate());
        new ScheduleConfig { IsEnabled = true, PluginIds = ["system.temp", "browser.chrome"] }.Validate();
        new ScheduleConfig { IsEnabled = true, PluginIds = ["applications.caches"] }.Validate();
    }
}
