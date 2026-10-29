using System.ComponentModel;
using System.Windows.Media;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

internal sealed class StartupItemRow : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public StartupItemRow(StartupItem item) => Item = item;

    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public StartupItem Item { get; }
    public string Name => Item.Name;
    public string Command => Item.Command;
    public string StatusLabel => T(Item.IsEnabled ? "Startup.Row.Enabled" : "Startup.Row.Disabled");
    public string LocationLabel => T(Item.Location switch
    {
        StartupItemLocation.CurrentUserRun => "Startup.Location.UserRun",
        StartupItemLocation.CurrentUserRunOnce => "Startup.Location.UserRunOnce",
        StartupItemLocation.LocalMachineRun => "Startup.Location.MachineRun",
        StartupItemLocation.LocalMachineRunOnce => "Startup.Location.MachineRunOnce",
        StartupItemLocation.CurrentUserStartup => "Startup.Location.UserStartup",
        StartupItemLocation.CommonStartup => "Startup.Location.CommonStartup",
        StartupItemLocation.DisabledByOpenCleanerMachine => "Startup.Location.DisabledMachine",
        _ => "Startup.Location.Disabled"
    });
    public bool IsEnabled => Item.IsEnabled;
    public bool CanToggle => Item.CanToggle;
    public bool IsMachine => Item.IsMachine;
    public string? IconPath => Item.IconPath;

    public StartupImpact Impact => Item.Impact;
    public string ImpactLabel => T("Startup.Impact.Label", T(Item.Impact switch
    {
        StartupImpact.High => "Startup.Impact.High",
        StartupImpact.Medium => "Startup.Impact.Medium",
        StartupImpact.Low => "Startup.Impact.Low",
        _ => "Startup.Impact.Unknown"
    }));
    public string ImpactToolTip => T("Startup.Impact.Help");

    internal void RefreshLocalization()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LocationLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImpactLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ImpactToolTip)));
    }

    public Brush ImpactBrush => (Brush)System.Windows.Application.Current.FindResource(Item.Impact switch
    {
        StartupImpact.High => "RiskExpert",
        StartupImpact.Medium => "RiskRecommended",
        StartupImpact.Low => "RiskSafe",
        _ => "TextSecond"
    });

    public string Initials
    {
        get
        {
            string name = Item.Name.TrimStart();
            return name.Length == 0 ? "?" : char.ToUpperInvariant(name[0]).ToString();
        }
    }

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadIconAsync()
    {
        ImageSource? icon = await ShellIconLoader.GetAsync(IconPath);
        if (icon != null) Icon = icon;
    }
}
