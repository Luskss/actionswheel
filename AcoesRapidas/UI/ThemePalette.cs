using System.Windows.Media;

namespace AcoesRapidas.UI;

/// <summary>Tons da cor de destaque do Windows.</summary>
public readonly record struct AccentShades(Color Accent, Color Light, Color Dark);

public static class ColorMath
{
    public static Color Mix(Color from, Color to, double amount)
    {
        return Color.FromRgb(Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B));

        byte Channel(byte a, byte b) => (byte)Math.Round(a + ((b - a) * amount));
    }

    public static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    /// <summary>Razão de contraste da WCAG entre duas cores opacas.</summary>
    public static double Contrast(Color a, Color b)
    {
        var (lighter, darker) = (Luminance(a), Luminance(b));
        if (lighter < darker)
        {
            (lighter, darker) = (darker, lighter);
        }

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));

        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }
}

/// <summary>Cores da interface, derivadas do tema e da cor de destaque do Windows.</summary>
public sealed record ThemePalette
{
    private const double MinimumContrast = 4.5;

    private static readonly Color White = Colors.White;
    private static readonly Color Ink = Color.FromRgb(0x10, 0x13, 0x17);

    public required bool IsLight { get; init; }

    public required Color Accent { get; init; }

    public required Color Surface { get; init; }

    public required Color SurfaceStroke { get; init; }

    public required Color Slice { get; init; }

    public required Color Hub { get; init; }

    public required Color Text { get; init; }

    public required Color MutedText { get; init; }

    public required Color AccentStart { get; init; }

    public required Color AccentEnd { get; init; }

    public required Color OnAccent { get; init; }

    public required Color Glow { get; init; }

    public Color Danger { get; } = Color.FromRgb(0xC4, 0x2B, 0x1C);

    public Color OnDanger { get; } = Colors.White;

    public static ThemePalette Create(AccentShades shades, bool isLight)
    {
        var baseFill = isLight ? shades.Dark : shades.Light;
        var (start, end, onAccent) = ResolveAccentFill(baseFill, ColorMath.Mix(baseFill, shades.Accent, 0.45));

        return isLight
            ? new ThemePalette
            {
                IsLight = true,
                Accent = shades.Accent,
                Surface = Color.FromArgb(0xF4, 0xF8, 0xF9, 0xFB),
                SurfaceStroke = Color.FromArgb(0x24, 0x00, 0x00, 0x00),
                Slice = ColorMath.Mix(Color.FromRgb(0xEC, 0xEE, 0xF2), shades.Accent, 0.07),
                Hub = ColorMath.Mix(White, shades.Accent, 0.03),
                Text = Color.FromRgb(0x1A, 0x1D, 0x23),
                MutedText = Color.FromRgb(0x56, 0x5F, 0x6C),
                AccentStart = start,
                AccentEnd = end,
                OnAccent = onAccent,
                Glow = ColorMath.WithAlpha(shades.Accent, 0x50),
            }
            : new ThemePalette
            {
                IsLight = false,
                Accent = shades.Accent,
                Surface = Color.FromArgb(0xF2, 0x14, 0x17, 0x1D),
                SurfaceStroke = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF),
                Slice = ColorMath.Mix(Color.FromRgb(0x25, 0x2A, 0x33), shades.Accent, 0.08),
                Hub = ColorMath.Mix(Color.FromRgb(0x1A, 0x1E, 0x25), shades.Accent, 0.10),
                Text = Color.FromRgb(0xF3, 0xF5, 0xF8),
                MutedText = Color.FromRgb(0xAE, 0xB7, 0xC4),
                AccentStart = start,
                AccentEnd = end,
                OnAccent = onAccent,
                Glow = ColorMath.WithAlpha(shades.Light, 0x68),
            };
    }

    /// <summary>
    /// Escolhe o texto mais legível sobre o gradiente e ajusta o gradiente quando a cor
    /// de destaque tem um brilho intermediário.
    /// </summary>
    private static (Color Start, Color End, Color OnAccent) ResolveAccentFill(Color start, Color end)
    {
        var onAccent = WorstContrast(White, start, end) >= WorstContrast(Ink, start, end) ? White : Ink;
        var correction = onAccent == White ? Colors.Black : White;

        for (var step = 0; step < 24 && WorstContrast(onAccent, start, end) < MinimumContrast; step++)
        {
            start = ColorMath.Mix(start, correction, 0.08);
            end = ColorMath.Mix(end, correction, 0.08);
        }

        return (start, end, onAccent);
    }

    private static double WorstContrast(Color text, Color start, Color end) =>
        Math.Min(ColorMath.Contrast(text, start), ColorMath.Contrast(text, end));
}
