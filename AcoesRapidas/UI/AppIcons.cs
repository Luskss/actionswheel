using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AcoesRapidas.UI;

/// <summary>
/// Carrega o ícone de um programa ou uma imagem como máscara monocromática, para ser pintada
/// com a cor do tema. Deve ser chamado na thread da interface.
/// </summary>
internal static class AppIcons
{
    private const int ImagePixelWidth = 64;
    private const int BytesPerPixel = 4;

    private static readonly Dictionary<string, Brush> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ProgramExtensions = [".exe", ".lnk", ".dll", ".url"];

    /// <summary>Devolve a máscara do ícone ou nulo quando o arquivo não existe ou não tem imagem.</summary>
    public static Brush? LoadMask(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        if (Cache.TryGetValue(expanded, out var cached))
        {
            return cached;
        }

        // Arquivos que ainda não existem não ficam em cache, para aparecerem assim que forem instalados.
        var mask = TryLoad(expanded);
        if (mask is not null)
        {
            Cache[expanded] = mask;
        }

        return mask;
    }

    private static Brush? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var source = ProgramExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                ? FromProgram(path)
                : FromImage(path);

            return source is null ? null : CreateMaskBrush(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static BitmapSource? FromProgram(string path)
    {
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        return icon is null
            ? null
            : Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    private static BitmapImage FromImage(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.DecodePixelWidth = ImagePixelWidth;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        return image;
    }

    private static ImageBrush CreateMaskBrush(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * BytesPerPixel;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var mask = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            IconStylizer.CreateMask(pixels),
            stride);
        mask.Freeze();

        var brush = new ImageBrush(mask) { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        return brush;
    }
}
