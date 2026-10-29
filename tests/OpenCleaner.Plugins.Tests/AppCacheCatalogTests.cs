using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCleaner.Core.Security;
using OpenCleaner.Plugins.System.Plugins;
using Xunit;

namespace OpenCleaner.Plugins.Tests;

public sealed class AppCacheCatalogTests
{
    private static readonly HashSet<string> ForbiddenSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Documents", "Desktop", "Downloads", "Images", "Pictures", "Videos", "Music",
        "node_modules", ".git", ".ssh", "Desktop"
    };

    [Fact]
    public void CatalogCoversTheExpectedApplications()
    {
        var targets = AppCacheCleanerPlugin.CacheTargets;

        Assert.Equal(21, targets.Count);
        Assert.Equal(8, targets.Select(target => target.AppName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(targets, target => target.AppName == "Slack" &&
            target.Segments.SequenceEqual(["Slack", "Cache"]));
        Assert.Contains(targets, target => target.AppName == "Microsoft Store" &&
            target.Segments.SequenceEqual(["Packages", "Microsoft.WindowsStore_8wekyb3d8bbwe", "LocalCache"]));
        Assert.Contains(targets, target => target.AppName == "OneDrive" &&
            target.Segments.SequenceEqual(["Microsoft", "OneDrive", "logs"]));
        Assert.Contains(targets, target => target.AppName == "Rapports de plantage");
    }

    [Fact]
    public void EveryTargetIsRelativeAndContainsNoUserFolder()
    {
        Assert.All(AppCacheCleanerPlugin.CacheTargets, target =>
        {
            Assert.False(string.IsNullOrWhiteSpace(target.AppName));
            Assert.False(string.IsNullOrWhiteSpace(target.Description));
            Assert.True(target.MinAge > TimeSpan.Zero);
            Assert.NotEmpty(target.Segments);
            Assert.All(target.Segments, segment =>
            {
                Assert.False(string.IsNullOrWhiteSpace(segment));
                Assert.Equal(segment, segment.Trim());
                Assert.DoesNotContain(':', segment);
                Assert.DoesNotContain('\\', segment);
                Assert.DoesNotContain('/', segment);
                Assert.NotEqual("..", segment);
                Assert.DoesNotContain(segment, ForbiddenSegments);
            });
        });
    }

    [Fact]
    public void TargetsAreUniqueAndBelongToAKnownRoot()
    {
        var targets = AppCacheCleanerPlugin.CacheTargets;
        var keys = targets.Select(target =>
            target.Root + "|" + string.Join('\\', target.Segments)).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(targets, target => Assert.True(Enum.IsDefined(target.Root)));
    }

    [Fact]
    public void OnlyElectronAppsCarryAProcessGuard()
    {
        var guarded = AppCacheCleanerPlugin.CacheTargets
            .Where(target => target.ProcessName != null)
            .Select(target => target.AppName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(new[] { "Notion", "Signal", "Slack", "WhatsApp", "Zoom" }, guarded);
    }

    [Fact]
    public void PluginIdentityIsReadOnlyAndApplicationScoped()
    {
        var plugin = new AppCacheCleanerPlugin(Mock.Of<IFileGuardian>(),
            NullLogger<AppCacheCleanerPlugin>.Instance);

        Assert.Equal("applications.caches", plugin.Id);
        Assert.Equal(OpenCleaner.Contracts.PluginCategory.Applications, plugin.Category);
        Assert.Equal(OpenCleaner.Contracts.RiskLevel.Safe, plugin.MaxRiskLevel);
        Assert.True(plugin.IsAvailable);
        Assert.False(plugin.RequiresAdmin);
    }
}
