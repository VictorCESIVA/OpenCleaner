using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests.Tools;

public sealed class StartupImpactEstimatorTests
{
    private const long MegaByte = 1024L * 1024;

    [Theory]
    [InlineData(100 * 1024, @"C:\Program Files\Application\app.exe", StartupImpact.Low)]
    [InlineData(5 * MegaByte, @"C:\Program Files\Application\app.exe", StartupImpact.Medium)]
    [InlineData(25 * MegaByte, @"C:\Program Files\Application\app.exe", StartupImpact.High)]
    [InlineData(100 * 1024, @"C:\Users\Alice\AppData\Local\Application\app.exe", StartupImpact.Medium)]
    [InlineData(6 * MegaByte, @"C:\Users\Alice\AppData\Local\Application\app.exe", StartupImpact.High)]
    [InlineData(100 * 1024, @"C:\Users\Alice\AppData\Local\Temp\app.exe", StartupImpact.High)]
    [InlineData(100 * 1024, @"C:\Users\Alice\Downloads\app.exe", StartupImpact.High)]
    [InlineData(100 * 1024, @"C:\ProgramData\Vendor\app.exe", StartupImpact.Medium)]
    public void ClassifyUsesSizeAndLocation(long size, string path, StartupImpact expected) =>
        Assert.Equal(expected, StartupImpactEstimator.Classify(size, path));

    [Fact]
    public void InvalidInputsAreUnknown()
    {
        Assert.Equal(StartupImpact.Unknown, StartupImpactEstimator.Classify(-1, @"C:\Program Files\a.exe"));
        Assert.Equal(StartupImpact.Unknown, StartupImpactEstimator.Classify(100, ""));
        Assert.Equal(StartupImpact.Unknown, StartupImpactEstimator.Estimate(null));
        Assert.Equal(StartupImpact.Unknown, StartupImpactEstimator.Estimate("   "));
        Assert.Equal(StartupImpact.Unknown, StartupImpactEstimator.Estimate(@"Z:\absent\" + Guid.NewGuid() + ".exe"));
        Assert.Equal(StartupImpact.Unknown, StartupImpactEstimator.Estimate("relative.exe"));
    }

    [Fact]
    public void TempExecutableIsHighEvenWhenSmall()
    {
        string file = Path.Combine(Path.GetTempPath(), "OpenCleaner-Impact-" + Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllBytes(file, new byte[64]);
        try
        {
            Assert.Equal(StartupImpact.High, StartupImpactEstimator.Estimate(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SystemExecutableIsClassifiedFromItsSize()
    {
        string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        if (!File.Exists(notepad)) return;

        StartupImpact impact = StartupImpactEstimator.Estimate(notepad);

        Assert.NotEqual(StartupImpact.Unknown, impact);
        Assert.NotEqual(StartupImpact.High, impact);
    }

    [Theory]
    [InlineData(StartupImpact.High, "élevé")]
    [InlineData(StartupImpact.Medium, "moyen")]
    [InlineData(StartupImpact.Low, "faible")]
    [InlineData(StartupImpact.Unknown, "inconnu")]
    public void LabelsAreFrench(StartupImpact impact, string expected) =>
        Assert.Equal(expected, StartupImpactEstimator.Label(impact));
}
