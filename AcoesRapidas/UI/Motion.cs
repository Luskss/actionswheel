using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AcoesRapidas.UI;

/// <summary>
/// Animações do app. Respeitam a opção "Efeitos de animação" do Windows: quando ela está
/// desligada, cada mudança acontece de imediato.
/// </summary>
internal static class Motion
{
    public static readonly IEasingFunction Smooth = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction Pop = Freeze(new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction Spring = Freeze(new BackEase { Amplitude = 0.9, EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction Exit =Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });

    public static DoubleAnimation To(double value, int milliseconds, IEasingFunction? ease = null, int delay = 0)
    {
        var enabled = SystemParameters.ClientAreaAnimation;
        return new DoubleAnimation(value, TimeSpan.FromMilliseconds(enabled ? milliseconds : 0))
        {
            EasingFunction = ease ?? Smooth,
            BeginTime = TimeSpan.FromMilliseconds(enabled ? delay : 0),
        };
    }

    public static ColorAnimation ToColor(Color value, int milliseconds) =>
        new(value, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? milliseconds : 0))
        {
            EasingFunction = Smooth,
        };

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
