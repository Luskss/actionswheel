using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using AcoesRapidas.Actions;
using AcoesRapidas.Calendar;
using AcoesRapidas.Config;
using AcoesRapidas.Input;
using AcoesRapidas.UI;
using Microsoft.Win32;

namespace AcoesRapidas;

public partial class App : Application
{
    private const string SingleInstanceName = @"Local\AcoesRapidas.InstanciaUnica";

    private readonly ConfigStore _config = new(ConfigStore.DefaultPath);
    private readonly NextcloudAccountStore _nextcloudAccounts = new(Path.Combine(ConfigStore.DefaultDirectory, "nextcloud.dat"));
    private Mutex? _singleInstance;
    private KeyboardHook? _hook;
    private TrayIcon? _tray;
    private RadialMenuWindow? _menu;
    private ToastWindow? _toast;
    private ActionRunner? _runner;
    private SettingsWindow? _settings;
    private EventWindow? _eventWindow;
    private TutorialWindow? _tutorial;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        WindowsTheme.ApplyOverrides(e.Args);

        if (e.Args.Contains("--diagnostico"))
        {
            new DiagnosticWindow().Show();
            return;
        }

        _singleInstance = new Mutex(true, SingleInstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            ShowToast($"Erro inesperado: {args.Exception.Message}", true);
            args.Handled = true;
        };

        _runner = new ActionRunner(ShowToast, OpenSettings, OpenEventWindow);
        _menu = new RadialMenuWindow();
        _menu.ActionChosen += _runner.Run;
        _tray = new TrayIcon(OpenSettings, OpenTutorial, OpenDiscordTutorial, ReloadConfig, OpenConfig, Shutdown);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        ReportConfigError(_config.Load());
        StartHook();

        if (e.Args.Contains("--mostrar-menu"))
        {
            _menu.Open(_config.Current.Actions);
        }

        if (e.Args.Contains("--configuracoes"))
        {
            OpenSettings();
        }

        if (e.Args.Contains("--tutorial-discord"))
        {
            OpenDiscordTutorial();
        }
        else if (e.Args.Contains("--tutorial") || (e.Args.Length == 0 && IsFirstTutorial()))
        {
            OpenTutorial();
        }
    }

    /// <summary>Registra que o tutorial já apareceu, para ele abrir sozinho uma única vez.</summary>
    private static bool IsFirstTutorial()
    {
        var marker = Path.Combine(ConfigStore.DefaultDirectory, "tutorial-visto");
        try
        {
            if (File.Exists(marker))
            {
                return false;
            }

            Directory.CreateDirectory(ConfigStore.DefaultDirectory);
            File.WriteAllText(marker, "");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sem poder registrar, é melhor não insistir no tutorial a cada início.
            return false;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hook?.Dispose();
        _tray?.Dispose();
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
        {
            Dispatcher.BeginInvoke(() => _tray?.ApplyTheme(WindowsTheme.Current()));
        }
    }

    private void StartHook()
    {
        var hook = new KeyboardHook();
        hook.CopilotKeyPressed += () => Dispatcher.BeginInvoke(() => _menu?.Toggle(_config.Current.Actions));
        hook.MenuCommandReceived += command => Dispatcher.BeginInvoke(() => HandleMenuCommand(command));
        _menu!.OpenChanged += isOpen => hook.MenuOpen = isOpen;

        try
        {
            hook.Start();
            _hook = hook;
        }
        catch (InvalidOperationException ex)
        {
            ShowToast(ex.Message, true);
        }
    }

    private void HandleMenuCommand(MenuCommand command)
    {
        if (command.Index is { } index)
        {
            _menu?.Choose(index);
        }
        else if (command.Step != 0)
        {
            _menu?.MoveHighlight(command.Step);
        }
        else if (command.Confirm)
        {
            _menu?.ChooseHighlighted();
        }
        else
        {
            _menu?.Back();
        }
    }

    private void OpenSettings()
    {
        NativeMethods.GrantForegroundToNextProcess();
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        ReportConfigError(_config.Load());
        _settings = new SettingsWindow(_config, action => _runner?.Run(action));
        _settings.Saved += () => ShowToast("Rodinha atualizada.", false);
        _settings.TutorialRequested += OpenTutorial;
        _settings.DiscordTutorialRequested += OpenDiscordTutorial;
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    private void OpenTutorial() =>
        ShowTutorial("Como usar a rodinha", TutorialContent.Build(TutorialContent.Sample), "Começar a usar");

    private void OpenDiscordTutorial() =>
        ShowTutorial("Como adicionar canais do Discord", TutorialContent.BuildDiscord(TutorialContent.Sample), "Concluir");

    private void ShowTutorial(string title, IReadOnlyList<TutorialStep> steps, string finishLabel)
    {
        NativeMethods.GrantForegroundToNextProcess();

        // Só um tutorial por vez: pedir outro troca o que estava aberto.
        _tutorial?.Close();

        _tutorial = new TutorialWindow(title, steps, finishLabel);
        _tutorial.Closed += (_, _) => _tutorial = null;
        _tutorial.Show();
        _tutorial.Activate();
    }

    private void OpenEventWindow(ActionDefinition action)
    {
        NativeMethods.GrantForegroundToNextProcess();
        if (_eventWindow is not null)
        {
            _eventWindow.Activate();
            return;
        }

        _eventWindow = new EventWindow(action.Target, _nextcloudAccounts, ShowToast);
        _eventWindow.Closed += (_, _) => _eventWindow = null;
        _eventWindow.Show();
        _eventWindow.Activate();
    }

    private void ReloadConfig()
    {
        var error = _config.Load();
        if (error is null)
        {
            ShowToast("Configuração recarregada.", false);
        }
        else
        {
            ReportConfigError(error);
        }
    }

    private void ReportConfigError(string? error)
    {
        if (error is not null)
        {
            ShowToast($"{error} A última configuração válida continua em uso.", true);
        }
    }

    private void OpenConfig()
    {
        try
        {
            var startInfo = new ProcessStartInfo("notepad.exe") { UseShellExecute = true };
            startInfo.ArgumentList.Add(_config.FilePath);
            Process.Start(startInfo)?.Dispose();
        }
        catch (Win32Exception ex)
        {
            ShowToast($"Não foi possível abrir a configuração: {ex.Message}", true);
        }
    }

    private void ShowToast(string message, bool isError)
    {
        _toast?.Close();
        _toast = new ToastWindow(message, isError);
        _toast.Show();
    }
}
