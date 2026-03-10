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

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (RiskLevel)value switch
        {
            RiskLevel.Safe        => new SolidColorBrush(Color.FromRgb(0x10, 0x7C, 0x10)),
            RiskLevel.Recommended => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x00)),
            RiskLevel.ExpertOnly  => new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)),
            _                     => Brushes.Gray
        };

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Fond du badge risque (semi-transparent)</summary>
public class RiskBgConverter : IValueConverter
{
    public static readonly RiskBgConverter Instance = new();

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (RiskLevel)value switch
        {
            RiskLevel.Safe        => new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x7C, 0x10)),
            RiskLevel.Recommended => new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0x8C, 0x00)),
            RiskLevel.ExpertOnly  => new SolidColorBrush(Color.FromArgb(0xCC, 0xD1, 0x34, 0x38)),
            _                     => Brushes.Gray
        };

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}

/// <summary>Libellé court du badge risque</summary>
public class RiskLabelConverter : IValueConverter
{
    public static readonly RiskLabelConverter Instance = new();

    public object Convert(object value, Type t, object p, CultureInfo c) =>
        (RiskLevel)value switch
        {
            RiskLevel.Safe        => "Safe",
            RiskLevel.Recommended => "Warn",
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
        string[] s = ["o", "Ko", "Mo", "Go"];
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
        var diff = DateTime.Now - dt;

        return diff.TotalMinutes < 2   ? "à l'instant" :
               diff.TotalMinutes < 60  ? $"il y a {(int)diff.TotalMinutes} min" :
               diff.TotalHours  < 24   ? $"il y a {(int)diff.TotalHours} h" :
               diff.TotalDays   < 30   ? $"il y a {(int)diff.TotalDays} j" :
                                         $"il y a {(int)(diff.TotalDays / 30)} mois";
    }

    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
