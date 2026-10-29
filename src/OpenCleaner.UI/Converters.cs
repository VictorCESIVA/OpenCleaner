using OpenCleaner.Contracts;
using OpenCleaner.UI.Localization;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace OpenCleaner.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  CONVERTISSEURS DE VALEUR partagés entre MainWindow et RestoreWindow
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Couleur de l'ellipse (pastille) selon RiskLevel</summary>
public class RiskColorConverter : IValueConverter
{
    public static readonly RiskColorConverter Instance = new();
    private static readonly Brush Safe = CreateBrush("#85E0B4");
    private static readonly Brush Recommended = CreateBrush("#F4C27A");
    private static readonly Brush Expert = CreateBrush("#F5A0A4");

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value switch
        {
            RiskLevel.Safe        => Safe,
            RiskLevel.Recommended => Recommended,
            RiskLevel.ExpertOnly  => Expert,
            _                     => Brushes.Gray
        };

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();

    internal static Brush CreateBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Fond du badge risque (semi-transparent)</summary>
public class RiskBgConverter : IValueConverter
{
    public static readonly RiskBgConverter Instance = new();
    private static readonly Brush Safe = RiskColorConverter.CreateBrush("#17372B");
    private static readonly Brush Recommended = RiskColorConverter.CreateBrush("#42321C");
    private static readonly Brush Expert = RiskColorConverter.CreateBrush("#44272F");

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value switch
        {
            RiskLevel.Safe        => Safe,
            RiskLevel.Recommended => Recommended,
            RiskLevel.ExpertOnly  => Expert,
            _                     => Brushes.Gray
        };

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Libellé court du badge risque</summary>
public class RiskLabelConverter : IValueConverter
{
    public static readonly RiskLabelConverter Instance = new();

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value switch
        {
            RiskLevel.Safe        => LocalizationService.Instance.Get("Risk.Low"),
            RiskLevel.Recommended => LocalizationService.Instance.Get("Risk.Review"),
            RiskLevel.ExpertOnly  => LocalizationService.Instance.Get("Risk.Expert"),
            _                     => "?"
        };

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Taille en octets → format lisible (Ko / Mo / Go)</summary>
public class BytesConverter : IValueConverter
{
    public static readonly BytesConverter Instance = new();

    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is not long bytes) return "—";
        return SizeFormatter.Format(bytes);
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Date → "il y a X minutes / heures / jours"</summary>
public class RelativeDateConverter : IValueConverter
{
    public static readonly RelativeDateConverter Instance = new();

    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is not DateTime dt) return "";
        var diff = dt.Kind == DateTimeKind.Utc ? DateTime.UtcNow - dt : DateTime.Now - dt;

        return diff.TotalMinutes < 2   ? LocalizationService.Instance.Get("Time.JustNow") :
               diff.TotalMinutes < 60  ? LocalizationService.Instance.Get("Time.MinutesAgo", (int)diff.TotalMinutes) :
               diff.TotalHours  < 24   ? LocalizationService.Instance.Get("Time.HoursAgo", (int)diff.TotalHours) :
               diff.TotalDays   < 30   ? LocalizationService.Instance.Get("Time.DaysAgo", (int)diff.TotalDays) :
                                         LocalizationService.Instance.Get("Time.MonthsAgo", (int)(diff.TotalDays / 30));
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Les sauvegardes sont stockées en UTC et affichées dans l'heure locale.</summary>
public class LocalDateConverter : IValueConverter
{
    public static readonly LocalDateConverter Instance = new();

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is DateTime date ? date.ToLocalTime().ToString("dd/MM/yyyy HH:mm", c) : "—";

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Keep the dashboard bounded for list virtualization; scroll the page on short displays.</summary>
public class DashboardHeightConverter : IMultiValueConverter
{
    public static readonly DashboardHeightConverter Instance = new();
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        var minimum = values.Length > 1 && values[1] is Visibility.Visible ? 940 : 820;
        if (values.Length > 2 && values[2] is Visibility.Visible) minimum += 80;
        return values.Length > 0 && values[0] is double height && double.IsFinite(height)
            ? Math.Max(minimum, height - 48) : minimum;
    }
    public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => throw new NotSupportedException();
}
