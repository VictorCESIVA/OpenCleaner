using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

internal enum SoftwareUpdateStatus
{
    Ready,
    Unknown,
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Skipped
}

internal sealed class SoftwareUpdateRow : INotifyPropertyChanged
{
    private static readonly HashSet<string> UnknownVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        "", "unknown", "inconnu", "inconnue", "inconnu(e)", "?", "n/a", "n/d", "-"
    };

    private bool _isSelected;
    private SoftwareUpdateStatus _status;
    private int _exitCode;
    private string _detail = "";

    public SoftwareUpdateRow(WingetUpgradeItem item, bool isSelected)
    {
        Item = item;
        _isSelected = isSelected;
        _status = item.HasUnknownVersion ? SoftwareUpdateStatus.Unknown : SoftwareUpdateStatus.Ready;
    }

    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public WingetUpgradeItem Item { get; }
    public string Id => Item.Id;
    public string Source => Item.Source;
    public string Name => Item.Name;
    public string NameToolTip => Item.Name;
    public bool HasUnknownVersion => Item.HasUnknownVersion;
    public bool IsStoreSource =>
        string.Equals(Source, WingetUpgradeService.StoreSource, StringComparison.OrdinalIgnoreCase);

    public string CurrentVersionLabel => VersionLabel(Item.CurrentVersion);
    public string AvailableVersionLabel => VersionLabel(Item.AvailableVersion);
    public string SourceLabel => T(IsStoreSource ? "Updates.Source.Store" : "Updates.Source.Winget");
    public string SelectionAutomationName => T("Updates.SelectionAutomation", Name);
    public string AutomationName => T("Updates.RowAutomation", Name, StatusText);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            Raise(nameof(IsSelected));
        }
    }

    public bool IsSelectable => _status != SoftwareUpdateStatus.Succeeded;
    public SoftwareUpdateStatus StatusKind => _status;

    public string StatusText => _status switch
    {
        SoftwareUpdateStatus.Unknown => T("Updates.Row.Unknown"),
        SoftwareUpdateStatus.Running => T("Updates.Row.Running"),
        SoftwareUpdateStatus.Succeeded => T("Updates.Row.Succeeded"),
        SoftwareUpdateStatus.Failed when _exitCode > 0 => T("Updates.Row.FailedCode", _exitCode),
        SoftwareUpdateStatus.Failed => T("Updates.Row.Failed"),
        SoftwareUpdateStatus.Cancelled => T("Updates.Row.Cancelled"),
        SoftwareUpdateStatus.Skipped => T("Updates.Row.Skipped"),
        _ => T("Updates.Row.Ready")
    };

    public string StatusIcon => _status switch
    {
        SoftwareUpdateStatus.Unknown => "\uE7BA",
        SoftwareUpdateStatus.Running => "\uE895",
        SoftwareUpdateStatus.Succeeded => "\uE73E",
        SoftwareUpdateStatus.Failed => "\uE783",
        SoftwareUpdateStatus.Cancelled => "\uE711",
        SoftwareUpdateStatus.Skipped => "\uE738",
        _ => "\uE895"
    };

    public Brush StatusBrush => Resource(_status switch
    {
        SoftwareUpdateStatus.Unknown => "WarnColor",
        SoftwareUpdateStatus.Running => "Accent",
        SoftwareUpdateStatus.Succeeded => "RiskSafe",
        SoftwareUpdateStatus.Failed => "RiskExpert",
        _ => "TextSecond"
    });

    public bool HasError => _status == SoftwareUpdateStatus.Failed && _detail.Length > 0;
    public string ErrorText => HasError ? _detail : "";

    internal void ApplyRunning() => SetStatus(SoftwareUpdateStatus.Running);

    internal void ApplySucceeded() => SetStatus(SoftwareUpdateStatus.Succeeded);

    internal void ApplyFailed(int exitCode, string? message)
    {
        _exitCode = exitCode;
        _detail = Normalize(message);
        SetStatus(SoftwareUpdateStatus.Failed);
    }

    internal void ApplyCancelled() => SetStatus(SoftwareUpdateStatus.Cancelled);

    internal void ApplySkipped() => SetStatus(SoftwareUpdateStatus.Skipped);

    internal void PrepareForUpgrade()
    {
        _exitCode = 0;
        _detail = "";
        SetStatus(HasUnknownVersion ? SoftwareUpdateStatus.Unknown : SoftwareUpdateStatus.Ready);
    }

    internal void RefreshLocalization()
    {
        Raise(nameof(SourceLabel));
        Raise(nameof(SelectionAutomationName));
        Raise(nameof(CurrentVersionLabel));
        Raise(nameof(AvailableVersionLabel));
        Raise(nameof(StatusText));
        Raise(nameof(StatusIcon));
        Raise(nameof(StatusBrush));
        Raise(nameof(IsSelectable));
        Raise(nameof(HasError));
        Raise(nameof(ErrorText));
        Raise(nameof(AutomationName));
    }

    private void SetStatus(SoftwareUpdateStatus status)
    {
        _status = status;
        Raise(nameof(StatusKind));
        Raise(nameof(StatusText));
        Raise(nameof(StatusIcon));
        Raise(nameof(StatusBrush));
        Raise(nameof(IsSelectable));
        Raise(nameof(HasError));
        Raise(nameof(ErrorText));
        Raise(nameof(AutomationName));
    }

    private static string VersionLabel(string version) =>
        IsUnknownVersion(version) ? T("Updates.UnknownVersion") : version;

    private static bool IsUnknownVersion(string version) => UnknownVersions.Contains(version.Trim());

    private static string Normalize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "";
        string trimmed = message.Trim();
        return trimmed.Length <= 600 ? trimmed : trimmed[..600];
    }

    private static Brush Resource(string key) =>
        Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;

    private void Raise(string property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    public event PropertyChangedEventHandler? PropertyChanged;
}
