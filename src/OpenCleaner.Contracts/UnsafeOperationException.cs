namespace OpenCleaner.Contracts;

public class UnsafeOperationException : InvalidOperationException
{
    public string? AttemptedPath { get; }

    public UnsafeOperationException(string message, string? attemptedPath = null)
        : base(message)
    {
        AttemptedPath = attemptedPath;
    }
}
