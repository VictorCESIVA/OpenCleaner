namespace OpenCleaner.Contracts;

public sealed record FileEntry(
    string Path,
    long Size,
    DateTime LastAccessTime,
    string ContentHash)
{
    public bool Exists => System.IO.File.Exists(Path);
}
