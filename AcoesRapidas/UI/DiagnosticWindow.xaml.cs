using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AcoesRapidas.Config;
using AcoesRapidas.Input;

namespace AcoesRapidas.UI;

/// <summary>
/// Mostra por tempo limitado o que o teclado envia. Letras, números e símbolos não são registrados.
/// </summary>
public partial class DiagnosticWindow : Window
{
    private const int DurationSeconds = 60;

    private readonly KeyboardHook _hook = new(passive: true);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly StreamWriter _log;
    private int _remainingSeconds = DurationSeconds;

    public DiagnosticWindow()
    {
        InitializeComponent();
        ApplyTheme(WindowsTheme.Current());

        Directory.CreateDirectory(ConfigStore.DefaultDirectory);
        var logPath = Path.Combine(ConfigStore.DefaultDirectory, "diagnostico.log");
        _log = new StreamWriter(logPath, append: false, Encoding.UTF8) { AutoFlush = true };

        _hook.RawKeyReceived += key => Dispatcher.BeginInvoke(() => Append(key));
        _timer.Tick += (_, _) => Tick();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _hook.Dispose();
            _log.Dispose();
            Application.Current.Shutdown();
        };

        UpdateStatus();
        _hook.Start();
        _timer.Start();
    }

    private void ApplyTheme(ThemePalette palette)
    {
        Background = new SolidColorBrush(palette.Hub);
        StatusText.Foreground = new SolidColorBrush(palette.Text);
        LogText.Background = new SolidColorBrush(palette.Slice);
        LogText.Foreground = new SolidColorBrush(palette.Text);
        LogText.BorderBrush = new SolidColorBrush(palette.SurfaceStroke);
        SourceInitialized += (_, _) =>
            NativeMethods.ApplyTitleBarTheme(new WindowInteropHelper(this).Handle, !palette.IsLight);
    }

    private void Append(RawKeyEvent key)
    {
        var state = key.IsDown ? "desceu" : "subiu ";
        var origin = key.IsInjected ? "injetada" : "física";
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {state}  {KeyLogFormatter.Describe(key.VirtualKey),-16} {origin}";
        _log.WriteLine(line);
        LogText.AppendText(line + Environment.NewLine);
        LogText.ScrollToEnd();
    }

    private void Tick()
    {
        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            Close();
            return;
        }

        UpdateStatus();
    }

    private void UpdateStatus() =>
        StatusText.Text = $"Pressione a tecla Copilot algumas vezes. Esta janela fecha em {_remainingSeconds} s.";
}
