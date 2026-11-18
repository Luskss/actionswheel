using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AcoesRapidas.Input;

namespace AcoesRapidas.UI;

public partial class ToastWindow : Window
{
    private const string InfoGlyph = "";
    private const string ErrorGlyph = "";

    private readonly DispatcherTimer _timer;
    private bool _isDismissing;

    public ToastWindow(string message, bool isError)
    {
        InitializeComponent();
        ApplyTheme(WindowsTheme.Current(), isError);
        MessageText.Text = message;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 7 : 3.5) };
        _timer.Tick += (_, _) => Dismiss();

        SourceInitialized += (_, _) => NativeMethods.MakeNonActivating(new WindowInteropHelper(this).Handle);
        Loaded += (_, _) => Present();
        Closed += (_, _) => _timer.Stop();
    }

    private void ApplyTheme(ThemePalette palette, bool isError)
    {
        var card = new SolidColorBrush(palette.Hub);
        ShadowPlate.Background = card;
        Card.Background = card;
        Card.BorderBrush = new SolidColorBrush(palette.SurfaceStroke);
        MessageText.Foreground = new SolidColorBrush(palette.Text);

        var fill = isError ? palette.Danger : palette.AccentStart;
        IconDisc.Fill = new SolidColorBrush(fill);
        IconGlyph.Text = isError ? ErrorGlyph : InfoGlyph;
        IconGlyph.Foreground = new SolidColorBrush(isError ? palette.OnDanger : palette.OnAccent);
    }

    private void Present()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth;
        Top = area.Bottom - ActualHeight;

        Root.BeginAnimation(OpacityProperty, Motion.To(1, 180));
        Shift.BeginAnimation(TranslateTransform.YProperty, Motion.To(0, 280, Motion.Pop));
        _timer.Start();
    }

    private void Dismiss()
    {
        if (_isDismissing)
        {
            return;
        }

        _isDismissing = true;
        _timer.Stop();

        var fade = Motion.To(0, 160, Motion.Exit);
        fade.Completed += (_, _) => Close();
        Shift.BeginAnimation(TranslateTransform.YProperty, Motion.To(10, 160, Motion.Exit));
        Root.BeginAnimation(OpacityProperty, fade);
    }
}
