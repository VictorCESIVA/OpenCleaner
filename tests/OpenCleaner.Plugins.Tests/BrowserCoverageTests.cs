using OpenCleaner.Plugins.System.Plugins;
using Xunit;

namespace OpenCleaner.Plugins.Tests;

public sealed class BrowserCoverageTests
{
    private static readonly string Local =
        Path.Combine(@"C:\Users\Exemple", "AppData", "Local");

    private static readonly string Roaming =
        Path.Combine(@"C:\Users\Exemple", "AppData", "Roaming");

    [Fact]
    public void ChromiumRootsCoverStableBetaDevAndCanaryChannels()
    {
        var roots = ChromeCleanerPlugin.GetChromiumProfileRoots(Local);

        Assert.Equal(13, roots.Count);
        Assert.Contains(roots, root => root.Name == "Chrome" &&
            root.Path == Path.Combine(Local, "Google", "Chrome", "User Data") && root.Process == "chrome");
        Assert.Contains(roots, root => root.Name == "Chrome Canary" &&
            root.Path == Path.Combine(Local, "Google", "Chrome SxS", "User Data"));
        Assert.Contains(roots, root => root.Name == "Edge Beta" &&
            root.Path == Path.Combine(Local, "Microsoft", "Edge Beta", "User Data") && root.Process == "msedge");
        Assert.Contains(roots, root => root.Name == "Edge Canary" &&
            root.Path == Path.Combine(Local, "Microsoft", "Edge SxS", "User Data"));
        Assert.Contains(roots, root => root.Name == "Brave Nightly" &&
            root.Path == Path.Combine(Local, "BraveSoftware", "Brave-Browser-Nightly", "User Data"));
        Assert.Contains(roots, root => root.Name == "Vivaldi Snapshot" &&
            root.Path == Path.Combine(Local, "Vivaldi Snapshot", "User Data"));
    }

    [Fact]
    public void ChromiumRootsAreUniqueAndStayUnderLocalAppData()
    {
        var roots = ChromeCleanerPlugin.GetChromiumProfileRoots(Local);

        Assert.Equal(roots.Count, roots.Select(root => root.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(roots.Count, roots.Select(root => root.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(roots, root =>
        {
            Assert.StartsWith(Local + Path.DirectorySeparatorChar, root.Path, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("User Data", root.Path, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(root.Process));
        });
    }

    [Fact]
    public void ChannelsOfOneEngineShareTheRunningProcess()
    {
        var roots = ChromeCleanerPlugin.GetChromiumProfileRoots(Local);

        Assert.All(roots.Where(root => root.Name.StartsWith("Chrome", StringComparison.Ordinal)),
            root => Assert.Equal("chrome", root.Process));
        Assert.All(roots.Where(root => root.Name.StartsWith("Edge", StringComparison.Ordinal)),
            root => Assert.Equal("msedge", root.Process));
        Assert.All(roots.Where(root => root.Name.StartsWith("Brave", StringComparison.Ordinal)),
            root => Assert.Equal("brave", root.Process));
        Assert.All(roots.Where(root => root.Name.StartsWith("Vivaldi", StringComparison.Ordinal)),
            root => Assert.Equal("vivaldi", root.Process));
    }

    [Fact]
    public void OperaRootsIncludeBetaAndDeveloperWithLocalAndRoamingData()
    {
        var roots = ChromeCleanerPlugin.GetOperaProfileRoots(Local, Roaming);

        Assert.Equal(4, roots.Count);
        Assert.Contains(roots, root => root.Name == "Opera Beta" &&
            root.LocalPath == Path.Combine(Local, "Opera Software", "Opera Beta") &&
            root.RoamingPath == Path.Combine(Roaming, "Opera Software", "Opera Beta"));
        Assert.Contains(roots, root => root.Name == "Opera Developer" &&
            root.LocalPath == Path.Combine(Local, "Opera Software", "Opera Developer"));
        Assert.Equal(4, roots.Select(root => root.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
