namespace OpenCleaner.Core.Theming;

public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255)
{
    public static RgbaColor Black => new(0, 0, 0);
    public static RgbaColor White => new(255, 255, 255);
}

public readonly record struct HslColor(double H, double S, double L);

public static class ColorMath
{
    public static bool TryParse(string? value, out RgbaColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#') span = span[1..];
        if (span.Length is not (3 or 4 or 6 or 8)) return false;

        foreach (char c in span)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }

        color = span.Length switch
        {
            3 => new RgbaColor(
                (byte)(HexDigit(span[0]) * 17),
                (byte)(HexDigit(span[1]) * 17),
                (byte)(HexDigit(span[2]) * 17)),
            4 => new RgbaColor(
                (byte)(HexDigit(span[0]) * 17),
                (byte)(HexDigit(span[1]) * 17),
                (byte)(HexDigit(span[2]) * 17),
                (byte)(HexDigit(span[3]) * 17)),
            6 => new RgbaColor(
                ByteAt(span, 0),
                ByteAt(span, 2),
                ByteAt(span, 4)),
            _ => new RgbaColor(
                ByteAt(span, 2),
                ByteAt(span, 4),
                ByteAt(span, 6),
                ByteAt(span, 0))
        };
        return true;
    }

    public static RgbaColor Parse(string value) =>
        TryParse(value, out RgbaColor color)
            ? color
            : throw new FormatException($"'{value}' is not a valid hex color.");

    public static bool TryNormalizeHex(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;

        ReadOnlySpan<char> span = value.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#') span = span[1..];
        if (span.Length is not (3 or 6)) return false;

        if (!TryParse(value, out RgbaColor color)) return false;
        normalized = Format(color);
        return true;
    }

    public static string Format(RgbaColor color, bool includeAlpha = false) =>
        includeAlpha
            ? $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static string ToHex(RgbaColor color, bool includeAlpha = false) =>
        Format(color, includeAlpha);

    public static HslColor RgbToHsl(RgbaColor color)
    {
        double r = color.R / 255d;
        double g = color.G / 255d;
        double b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;
        double lightness = (max + min) / 2d;
        double hue = 0d;
        double saturation = 0d;

        if (delta > 0d)
        {
            saturation = delta / (1d - Math.Abs(2d * lightness - 1d));
            if (max == r) hue = 60d * (((g - b) / delta) % 6d);
            else if (max == g) hue = 60d * ((b - r) / delta + 2d);
            else hue = 60d * ((r - g) / delta + 4d);
            if (hue < 0d) hue += 360d;
        }

        return new HslColor(hue, saturation, lightness);
    }

    public static RgbaColor HslToRgb(double hue, double saturation, double lightness, byte alpha = 255)
    {
        hue = ((hue % 360d) + 360d) % 360d;
        saturation = Math.Clamp(saturation, 0d, 1d);
        lightness = Math.Clamp(lightness, 0d, 1d);

        double chroma = (1d - Math.Abs(2d * lightness - 1d)) * saturation;
        double x = chroma * (1d - Math.Abs((hue / 60d) % 2d - 1d));
        double m = lightness - chroma / 2d;

        (double r, double g, double b) = hue switch
        {
            < 60d => (chroma, x, 0d),
            < 120d => (x, chroma, 0d),
            < 180d => (0d, chroma, x),
            < 240d => (0d, x, chroma),
            < 300d => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return new RgbaColor(ToByte(r + m), ToByte(g + m), ToByte(b + m), alpha);
    }

    public static double RelativeLuminance(RgbaColor color) =>
        0.2126d * LinearChannel(color.R) +
        0.7152d * LinearChannel(color.G) +
        0.0722d * LinearChannel(color.B);

    public static double ContrastRatio(RgbaColor foreground, RgbaColor background)
    {
        double first = RelativeLuminance(foreground);
        double second = RelativeLuminance(background);
        double lighter = Math.Max(first, second);
        double darker = Math.Min(first, second);
        return (lighter + 0.05d) / (darker + 0.05d);
    }

    public static RgbaColor Blend(RgbaColor first, RgbaColor second, double firstWeight)
    {
        firstWeight = Math.Clamp(firstWeight, 0d, 1d);
        return new RgbaColor(
            BlendChannel(first.R, second.R, firstWeight),
            BlendChannel(first.G, second.G, firstWeight),
            BlendChannel(first.B, second.B, firstWeight),
            BlendChannel(first.A, second.A, firstWeight));
    }

    public static RgbaColor BlendToward(RgbaColor color, RgbaColor target, double amount) =>
        Blend(color, target, 1d - Math.Clamp(amount, 0d, 1d));

    public static RgbaColor WithLightness(RgbaColor color, double lightness)
    {
        HslColor hsl = RgbToHsl(color);
        return HslToRgb(hsl.H, hsl.S, lightness, color.A);
    }

    public static RgbaColor Lighten(RgbaColor color, double amount) =>
        WithLightness(color, RgbToHsl(color).L + amount);

    public static RgbaColor Darken(RgbaColor color, double amount) =>
        WithLightness(color, RgbToHsl(color).L - amount);

    public static RgbaColor ReadableForeground(RgbaColor background) =>
        ContrastRatio(RgbaColor.Black, background) >= ContrastRatio(RgbaColor.White, background)
            ? RgbaColor.Black
            : RgbaColor.White;

    public static RgbaColor EnsureContrast(RgbaColor foreground, RgbaColor background, double target = 4.5d)
    {
        target = Math.Max(1d, target);
        if (ContrastRatio(foreground, background) >= target) return foreground;

        HslColor hsl = RgbToHsl(foreground);
        double endpoint = RelativeLuminance(foreground) >= RelativeLuminance(background) ? 1d : 0d;
        RgbaColor candidate = HslToRgb(hsl.H, hsl.S, endpoint, foreground.A);

        if (ContrastRatio(candidate, background) < target)
        {
            double alternateEndpoint = endpoint == 1d ? 0d : 1d;
            RgbaColor alternate = HslToRgb(hsl.H, hsl.S, alternateEndpoint, foreground.A);
            return ContrastRatio(alternate, background) > ContrastRatio(candidate, background)
                ? alternate
                : candidate;
        }

        double low = hsl.L;
        double high = endpoint;
        for (int step = 0; step < 32; step++)
        {
            double middle = (low + high) / 2d;
            RgbaColor probe = HslToRgb(hsl.H, hsl.S, middle, foreground.A);
            if (ContrastRatio(probe, background) >= target) high = middle;
            else low = middle;
        }

        return HslToRgb(hsl.H, hsl.S, high, foreground.A);
    }

    private static byte ByteAt(ReadOnlySpan<char> span, int start) =>
        (byte)(HexDigit(span[start]) * 16 + HexDigit(span[start + 1]));

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1
    };

    private static byte ToByte(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0d, 1d) * 255d, MidpointRounding.AwayFromZero);

    private static byte BlendChannel(byte first, byte second, double firstWeight) =>
        (byte)Math.Round(first * firstWeight + second * (1d - firstWeight), MidpointRounding.AwayFromZero);

    private static double LinearChannel(byte channel)
    {
        double value = channel / 255d;
        return value <= 0.04045d
            ? value / 12.92d
            : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
    }
}
