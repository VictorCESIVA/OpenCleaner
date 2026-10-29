using System.ComponentModel;
using System.Windows.Media;
using OpenCleaner.Core.Tools;
using OpenCleaner.UI.Localization;

namespace OpenCleaner.UI;

internal sealed class InstalledProgramRow : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public InstalledProgramRow(InstalledProgram program) => Program = program;

    private static string T(string key, params object?[] args) => LocalizationService.Instance.Get(key, args);

    public InstalledProgram Program { get; }
    public string Name => Program.Name;
    public string SourceLabel => T(Program.MachineWide ? "Uninstall.Row.Machine" : "Uninstall.Row.User");
    public string? IconPath => Program.IconPath;
    public bool CanUninstall => Program.CanUninstall;

    public string Meta
    {
        get
        {
            string? publisher = string.IsNullOrWhiteSpace(Program.Publisher) ? null : Program.Publisher.Trim();
            string? version = string.IsNullOrWhiteSpace(Program.Version) ? null : "v" + Program.Version.Trim();
            if (publisher == null && version == null) return T("Uninstall.NoPublisher");
            if (publisher == null) return version!;
            if (version == null) return publisher;
            return publisher + " · " + version;
        }
    }

    public string SizeLabel => string.IsNullOrEmpty(Program.SizeLabel) ? "—" : Program.SizeLabel;

    internal void RefreshLocalization()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SourceLabel)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Meta)));
    }

    public string Initials
    {
        get
        {
            string name = Program.Name.TrimStart();
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
