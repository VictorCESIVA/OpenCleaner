using OpenCleaner.Core.Tools;
using Xunit;

namespace OpenCleaner.Core.Tests;

public sealed class IconPathResolverTests
{
    [Fact]
    public void QuotedDisplayIconWithIndexIsNormalized()
    {
        string file = CreateTempFile(".ico");
        try
        {
            Assert.Equal(file, IconPathResolver.NormalizeDisplayIcon($"\"{file}\",0"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void UnquotedExecutableWithArgumentsIsExtracted()
    {
        string file = CreateTempFile(".exe");
        try
        {
            Assert.Equal(file, IconPathResolver.ExtractExecutable($"{file} --background"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void MissingIconLocationReturnsNull() =>
        Assert.Null(IconPathResolver.NormalizeDisplayIcon(@"Z:\missing\application.exe"));

    private static string CreateTempFile(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(),
            "OpenCleaner-Icon-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, []);
        return path;
    }
}
