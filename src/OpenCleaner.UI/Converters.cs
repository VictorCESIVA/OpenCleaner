using OpenCleaner.Contracts;
using System.Globalization;
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
            RiskLevel.Safe        => "Faible",
            RiskLevel.Recommended => "À vérifier",
            RiskLevel.ExpertOnly  => "Expert",
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
        string[] s = ["o", "Ko", "Mo", "Go", "To"];
        int i = 0;
        double d = bytes;
        while (d >= 1024 && i < s.Length - 1) { d /= 1024; i++; }
        return $"{d:0.#} {s[i]}";
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

        return diff.TotalMinutes < 2   ? "à l'instant" :
               diff.TotalMinutes < 60  ? $"il y a {(int)diff.TotalMinutes} min" :
               diff.TotalHours  < 24   ? $"il y a {(int)diff.TotalHours} h" :
               diff.TotalDays   < 30   ? $"il y a {(int)diff.TotalDays} j" :
                                         $"il y a {(int)(diff.TotalDays / 30)} mois";
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
public class DashboardHeightConverter : IValueConverter
{
    public static readonly DashboardHeightConverter Instance = new();
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is double height && double.IsFinite(height) ? Math.Max(740, height - 48) : 740;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
