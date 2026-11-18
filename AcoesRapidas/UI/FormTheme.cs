using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using AcoesRapidas.Input;

namespace AcoesRapidas.UI;

/// <summary>Aplica as cores do Windows às janelas de formulário do app.</summary>
internal static class FormTheme
{
    public static void Apply(Window window, ThemePalette palette, Color inputColor)
    {
        SetBrush("Brush.Window", palette.Hub);
        SetBrush("Brush.Panel", palette.Slice);
        SetBrush("Brush.Input", inputColor);
        SetBrush("Brush.Hover", ColorMath.Mix(palette.Hub, palette.Text, 0.1));
        SetBrush("Brush.Stroke", ColorMath.Mix(palette.Slice, palette.Text, 0.28));
        SetBrush("Brush.Text", palette.Text);
        SetBrush("Brush.Muted", palette.MutedText);
        SetBrush("Brush.Accent", palette.AccentStart);
        SetBrush("Brush.OnAccent", palette.OnAccent);
        SetBrush("Brush.Danger", palette.Danger);
        SetBrush("Brush.OnDanger", palette.OnDanger);

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            NativeMethods.ApplyTitleBarTheme(hwnd, !palette.IsLight);
            NativeMethods.ApplyCaptionColor(hwnd, palette.Hub.R, palette.Hub.G, palette.Hub.B);
        };

        void SetBrush(string key, Color color) => window.Resources[key] = new SolidColorBrush(color);
    }
}
