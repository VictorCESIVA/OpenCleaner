using System.ComponentModel;
using System.Threading;
using System.Windows;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

// One scan or cleanup at a time, including operations in secondary windows.
internal sealed class UiOperation : IDisposable
{
    private static UiOperation? _current;
    private readonly Window _owner;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _disposed;
    public CancellationToken Token => _cancellation.Token;
    public static bool IsBusy => _current != null;
    public static event Action? StateChanged;

    private UiOperation(Window owner)
    {
        _owner = owner;
        owner.Closing += OnClosing;
    }

    public static bool TryBegin(Window owner, out UiOperation operation)
    {
        if (_current != null)
        {
            MessageBox.Show(owner, LocalizationService.Instance.Get("App.Busy.Message"),
                LocalizationService.Instance.Get("App.Busy.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            operation = null!;
            return false;
        }
        operation = new UiOperation(owner);
        _current = operation;
        StateChanged?.Invoke();
        return true;
    }

    public static void CancelCurrent() => _current?._cancellation.Cancel();

    public IProgress<T> CreateProgress<T>(Action<T> update) => new Progress<T>(value =>
    {
        if (!_disposed) update(value);
    });

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        _cancellation.Cancel();
    }

    public void Dispose()
    {
        _disposed = true;
        _owner.Closing -= OnClosing;
        if (ReferenceEquals(_current, this)) _current = null;
        StateChanged?.Invoke();
        _cancellation.Dispose();
    }
}
