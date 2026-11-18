using System.IO;
using System.Security;
using System.Windows.Media;
using Microsoft.Win32;

namespace AcoesRapidas.UI;

/// <summary>Lê o tema e a cor de destaque atuais do Windows.</summary>
public static class WindowsTheme
{
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int PaletteLength = 32;
    private const int PaletteLightIndex = 1;
    private const int PaletteAccentIndex = 3;
    private const int PaletteDarkIndex = 4;

    private static readonly Color DefaultAccent = Color.FromRgb(0x00, 0x78, 0xD4);

    private static bool? _forcedIsLight;
    private static Color? _forcedAccent;

    /// <summary>Lido a cada uso, para acompanhar mudanças feitas com o app aberto.</summary>
    public static ThemePalette Current() => ThemePalette.Create(ReadAccent(), ReadIsLight());

    /// <summary>Aceita --tema=claro|escuro e --destaque=#RRGGBB para conferir outras aparências.</summary>
    public static void ApplyOverrides(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.Equals("--tema=claro", StringComparison.OrdinalIgnoreCase))
            {
                _forcedIsLight = true;
            }
            else if (arg.Equals("--tema=escuro", StringComparison.OrdinalIgnoreCase))
            {
                _forcedIsLight = false;
            }
            else if (arg.StartsWith("--destaque=", StringComparison.OrdinalIgnoreCase))
            {
                _forcedAccent = ParseHexColor(arg["--destaque=".Length..]);
            }
        }
    }

    /// <summary>Interpreta o valor AccentPalette do registro: 8 cores no formato RGBA.</summary>
    public static AccentShades? ParseAccentPalette(byte[]? palette)
    {
        if (palette is null || palette.Length < PaletteLength)
        {
            return null;
        }

        return new AccentShades(At(PaletteAccentIndex), At(PaletteLightIndex), At(PaletteDarkIndex));

        Color At(int index) => Color.FromRgb(palette[index * 4], palette[(index * 4) + 1], palette[(index * 4) + 2]);
    }

    /// <summary>Converte um valor do registro no formato 0xAABBGGRR.</summary>
    public static Color FromAbgr(int abgr) =>
        Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));

    public static AccentShades DeriveShades(Color accent) =>
        new(accent, ColorMath.Mix(accent, Colors.White, 0.45), ColorMath.Mix(accent, Colors.Black, 0.25));

    private static AccentShades ReadAccent()
    {
        if (_forcedAccent is { } forced)
        {
            return DeriveShades(forced);
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AccentKey);
            if (ParseAccentPalette(key?.GetValue("AccentPalette") as byte[]) is { } shades)
            {
                return shades;
            }

            if (key?.GetValue("AccentColorMenu") is int abgr)
            {
                return DeriveShades(FromAbgr(abgr));
            }
        }
        catch (Exception ex) when (ex is IOException or SecurityException or UnauthorizedAccessException)
        {
            // Sem acesso ao registro, o app segue com a cor padrão do Windows.
        }

        return DeriveShades(DefaultAccent);
    }

    private static bool ReadIsLight()
    {
        if (_forcedIsLight is { } forced)
        {
            return forced;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (Exception ex) when (ex is IOException or SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Color? ParseHexColor(string value)
    {
        var hex = value.TrimStart('#');
        return hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb)
            ? Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb)
            : null;
    }
}
