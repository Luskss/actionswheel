using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AcoesRapidas.Input;

namespace AcoesRapidas.UI;

/// <summary>
/// Janela pequena que desenha o anel animado. Ela ignora o mouse: quem recebe a interação é a
/// janela de tela cheia logo abaixo, que não precisa ser redesenhada a cada quadro.
/// </summary>
internal sealed class RingWindow : Window
{
    /// <summary>Distância do centro até a borda, com folga para o brilho e a animação.</summary>
    private const double Extent = 270;

    public RingWindow()
    {
        Title = "Ações rápidas";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = Extent * 2;
        Height = Extent * 2;

        Canvas.SetLeft(Ring, Extent);
        Canvas.SetTop(Ring, Extent);
        var surface = new Canvas();
        surface.Children.Add(Ring);
        Content = surface;

        SourceInitialized += (_, _) => NativeMethods.MakeClickThrough(new WindowInteropHelper(this).Handle);
    }

    public Canvas Ring { get; } = new();

    /// <summary>Exibe o anel centralizado em um ponto da tela, em pixels físicos.</summary>
    public void ShowAt(double screenX, double screenY, double scale)
    {
        var size = (int)Math.Ceiling(Extent * 2 * scale);
        var left = (int)Math.Round(screenX - (size / 2.0));
        var top = (int)Math.Round(screenY - (size / 2.0));
        var bounds = new NativeMethods.RECT { Left = left, Top = top, Right = left + size, Bottom = top + size };

        // A posição é aplicada de novo depois de exibir porque o WPF pode restaurar a anterior.
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.PlaceTopmost(hwnd, bounds);
        Show();
        NativeMethods.PlaceTopmost(hwnd, bounds);
    }
}
