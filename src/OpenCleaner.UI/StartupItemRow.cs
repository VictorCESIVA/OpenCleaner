using System.ComponentModel;
using System.Windows.Media;
using OpenCleaner.Core.Tools;

namespace OpenCleaner.UI;

internal sealed class StartupItemRow : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public StartupItemRow(StartupItem item) => Item = item;

    public StartupItem Item { get; }
    public string Name => Item.Name;
    public string Command => Item.Command;
    public string StatusLabel => Item.StatusLabel;
    public string LocationLabel => Item.LocationLabel;
    public bool IsEnabled => Item.IsEnabled;
    public bool CanToggle => Item.CanToggle;
    public string? IconPath => Item.IconPath;

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
