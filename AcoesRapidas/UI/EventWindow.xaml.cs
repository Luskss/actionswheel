using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AcoesRapidas.Calendar;
using AcoesRapidas.Input;

namespace AcoesRapidas.UI;

/// <summary>Janela rápida que cria um evento no Google Agenda, no Outlook ou em um Nextcloud.</summary>
public partial class EventWindow : Window
{
    private const string DateFormat = "dd/MM/yyyy";
    private const string TimeFormat = "HH:mm";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(10);

    private readonly string _server;
    private readonly NextcloudAccountStore _accounts;
    private readonly Action<string, bool> _notify;
    private readonly NextcloudClient? _client;
    private readonly CancellationTokenSource _closing = new();
    private readonly ThemePalette _palette = WindowsTheme.Current();

    private NextcloudAccount? _account;
    private EventDestination _destination = EventDestination.Google;
    private (EventDraft Draft, string Uid)? _lastAttempt;
    private bool _endEdited;
    private bool _isSettingEnd;
    private bool _isRestoringDestination;

    /// <param name="server">Endereço do Nextcloud. Vazio quando só Google e Outlook devem aparecer.</param>
    public EventWindow(string? server, NextcloudAccountStore accounts, Action<string, bool> notify)
    {
        _server = server?.Trim() ?? "";
        _accounts = accounts;
        _notify = notify;
        _client = HasNextcloud ? new NextcloudClient(_server) : null;

        InitializeComponent();
        FormTheme.Apply(this, _palette, _palette.Slice);
        FillSuggestedTimes();

        Loaded += OnLoaded;
    }

    private bool HasNextcloud => _server.Length > 0;

    private string SaveLabel => _destination switch
    {
        EventDestination.Google => "Abrir no Google Agenda",
        EventDestination.Outlook => "Abrir no Outlook",
        _ => "Criar evento",
    };

    protected override void OnClosed(EventArgs e)
    {
        _closing.Cancel();
        _closing.Dispose();
        _client?.Dispose();
        base.OnClosed(e);
    }

    private void FillSuggestedTimes()
    {
        var start = EventInput.SuggestStart(DateTime.Now);
        DateBox.Text = start.ToString(DateFormat, CultureInfo.InvariantCulture);
        StartBox.Text = start.ToString(TimeFormat, CultureInfo.InvariantCulture);
        SetEnd(EventInput.SuggestEnd(TimeOnly.FromDateTime(start)));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DestNextcloud.Visibility = HasNextcloud ? Visibility.Visible : Visibility.Hidden;

        // Sem escolha anterior, quem configurou um Nextcloud já começa nele.
        var remembered = EventDestinationStore.Load(
            EventDestinationStore.DefaultPath, HasNextcloud ? EventDestination.Nextcloud : EventDestination.Google);
        var start = remembered == EventDestination.Nextcloud && !HasNextcloud ? EventDestination.Google : remembered;

        // Marcar o botão dispara OnDestinationChecked, que monta o resto da janela.
        _isRestoringDestination = true;
        (start switch
        {
            EventDestination.Outlook => DestOutlook,
            EventDestination.Nextcloud => DestNextcloud,
            _ => DestGoogle,
        }).IsChecked = true;
        _isRestoringDestination = false;
    }

    private async void OnDestinationChecked(object sender, RoutedEventArgs e)
    {
        _destination = Enum.Parse<EventDestination>((string)((RadioButton)sender).Tag);
        if (!_isRestoringDestination)
        {
            // A escolha vale para a próxima vez mesmo que o evento não seja criado agora.
            EventDestinationStore.Save(EventDestinationStore.DefaultPath, _destination);
        }

        HideError();
        SaveButton.Content = SaveLabel;

        if (_destination != EventDestination.Nextcloud)
        {
            ConnectPanel.Visibility = Visibility.Collapsed;
            CalendarSection.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = true;
            ShowForm();
            return;
        }

        CalendarSection.Visibility = Visibility.Visible;
        _account ??= _accounts.Load(_server);
        if (_account is null)
        {
            ShowConnect(null);
            return;
        }

        ShowForm();
        if (SelectedCalendar() is null)
        {
            await LoadCalendarsAsync();
        }
        else
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void ShowConnect(string? message)
    {
        _account = null;
        if (_destination != EventDestination.Nextcloud)
        {
            return;
        }

        FormPanel.Visibility = Visibility.Collapsed;
        ConnectPanel.Visibility = Visibility.Visible;
        ConnectButton.IsEnabled = true;
        ConnectStatus.Text = message ?? "";
        SaveButton.IsEnabled = false;
        HideError();
    }

    private void ShowForm()
    {
        ConnectPanel.Visibility = Visibility.Collapsed;
        FormPanel.Visibility = Visibility.Visible;
        TitleBox.Focus();
    }

    private async void OnConnect(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        HideError();
        ConnectStatus.Text = "Abrindo o navegador…";

        try
        {
            var login = await _client!.StartLoginAsync(_closing.Token);
            NativeMethods.GrantForegroundToNextProcess();
            Process.Start(new ProcessStartInfo(login.BrowserUrl.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            ConnectStatus.Text = "Aguardando a autorização no navegador…";

            var account = await WaitForAuthorizationAsync(login);
            if (account is null)
            {
                ShowConnect("O tempo para autorizar acabou. Tente de novo.");
                return;
            }

            _account = account;
            _accounts.Save(account);
            ShowForm();
            Activate();
            await LoadCalendarsAsync();
        }
        catch (NextcloudException ex)
        {
            ShowConnect(ex.Message);
        }
        catch (Win32Exception ex)
        {
            ShowConnect($"Não foi possível abrir o navegador: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            // A janela foi fechada durante a autorização.
        }
    }

    private async Task<NextcloudAccount?> WaitForAuthorizationAsync(NextcloudLogin login)
    {
        var deadline = DateTime.UtcNow + LoginTimeout;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, _closing.Token);
            if (await _client!.PollLoginAsync(login, _closing.Token) is { } account)
            {
                return account;
            }
        }

        return null;
    }

    private async Task LoadCalendarsAsync()
    {
        if (_account is not { } account)
        {
            return;
        }

        CalendarPanel.Children.Clear();
        RetryButton.Visibility = Visibility.Collapsed;
        CalendarStatus.Text = "Carregando calendários…";
        SaveButton.IsEnabled = false;

        try
        {
            var calendars = await _client!.ListCalendarsAsync(account, _closing.Token);
            if (calendars.Count == 0)
            {
                CalendarStatus.Text = "Nenhum calendário desta conta aceita novos eventos.";
                RetryButton.Visibility = Visibility.Visible;
                return;
            }

            var selected = calendars.FirstOrDefault(c => c.Href == account.CalendarHref) ?? calendars[0];
            foreach (var calendar in calendars)
            {
                CalendarPanel.Children.Add(new RadioButton
                {
                    Style = (Style)FindResource("CalendarChoice"),
                    Content = new TextBlock { Text = calendar.Name },
                    Tag = calendar.Href,
                    IsChecked = calendar == selected,
                });
            }

            CalendarStatus.Text = "";
            SaveButton.IsEnabled = true;
        }
        catch (NextcloudException ex) when (ex.IsUnauthorized)
        {
            _accounts.Clear();
            ShowConnect(ex.Message);
        }
        catch (NextcloudException ex)
        {
            CalendarStatus.Text = ex.Message;
            RetryButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException)
        {
            // A janela foi fechada enquanto os calendários carregavam.
        }
    }

    private async void OnRetry(object sender, RoutedEventArgs e) => await LoadCalendarsAsync();

    private void OnToday(object sender, RoutedEventArgs e) =>
        DateBox.Text = DateTime.Today.ToString(DateFormat, CultureInfo.InvariantCulture);

    private void OnTomorrow(object sender, RoutedEventArgs e) =>
        DateBox.Text = DateTime.Today.AddDays(1).ToString(DateFormat, CultureInfo.InvariantCulture);

    private void OnAllDayChanged(object sender, RoutedEventArgs e) =>
        TimePanel.Visibility = AllDayToggle.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;

    private void OnEndChanged(object sender, TextChangedEventArgs e) => _endEdited |= !_isSettingEnd;

    private void OnStartLostFocus(object sender, KeyboardFocusChangedEventArgs e) => SyncEndWithStart();

    /// <summary>Enquanto o fim não foi editado, ele acompanha o início com uma hora de duração.</summary>
    private void SyncEndWithStart()
    {
        if (!_endEdited && EventInput.ParseTime(StartBox.Text) is { } start)
        {
            SetEnd(EventInput.SuggestEnd(start));
        }
    }

    private void SetEnd(TimeOnly end)
    {
        _isSettingEnd = true;
        EndBox.Text = end.ToString(TimeFormat, CultureInfo.InvariantCulture);
        _isSettingEnd = false;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_destination == EventDestination.Nextcloud)
        {
            await SaveToNextcloudAsync();
        }
        else
        {
            OpenInBrowser();
        }
    }

    /// <summary>Lê o formulário e mostra o que corrigir quando algo está errado.</summary>
    private EventDraft? ReadDraft()
    {
        // Com o Enter, o campo de início ainda está em foco e o fim não foi atualizado.
        SyncEndWithStart();

        var (draft, error) = EventInput.Read(
            TitleBox.Text, DateBox.Text, StartBox.Text, EndBox.Text, AllDayToggle.IsChecked == true,
            LocationBox.Text, DateOnly.FromDateTime(DateTime.Today));
        if (draft is null)
        {
            ShowError(error!);
        }

        return draft;
    }

    /// <summary>Google e Outlook recebem o evento pronto pelo navegador, e quem confirma é o usuário.</summary>
    private void OpenInBrowser()
    {
        if (ReadDraft() is not { } draft)
        {
            return;
        }

        var google = _destination == EventDestination.Google;
        var address = google
            ? CalendarLinks.Google(draft, TimeZoneInfo.Local)
            : CalendarLinks.Outlook(draft, TimeZoneInfo.Local);

        try
        {
            NativeMethods.GrantForegroundToNextProcess();
            Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            ShowError($"Não foi possível abrir o navegador: {ex.Message}");
            return;
        }

        _notify($"Evento \"{draft.Title}\" aberto no {(google ? "Google Agenda" : "Outlook")}. Confirme lá para salvar.", false);
        Close();
    }

    private async Task SaveToNextcloudAsync()
    {
        if (_account is not { } account || SelectedCalendar() is not { } calendarHref || ReadDraft() is not { } draft)
        {
            return;
        }

        // Repetir o mesmo evento depois de uma falha usa o mesmo identificador e não duplica.
        var uid = _lastAttempt is { } last && last.Draft == draft ? last.Uid : $"{Guid.NewGuid():N}@acoesrapidas";
        _lastAttempt = (draft, uid);

        HideError();
        SaveButton.IsEnabled = false;
        SaveButton.Content = "Criando…";

        try
        {
            var ics = IcsBuilder.Build(draft, uid, DateTime.UtcNow, TimeZoneInfo.Local);
            await _client!.CreateEventAsync(account, calendarHref, uid, ics, _closing.Token);

            _accounts.Save(account with { CalendarHref = calendarHref });
            _notify($"Evento \"{draft.Title}\" criado para {Describe(draft)}.", false);
            Close();
        }
        catch (NextcloudException ex) when (ex.IsUnauthorized)
        {
            _accounts.Clear();
            RestoreSaveButton();
            ShowConnect(ex.Message);
        }
        catch (NextcloudException ex)
        {
            ShowError(ex.Message);
            RestoreSaveButton();
        }
        catch (OperationCanceledException)
        {
            // A janela foi fechada durante o envio.
        }
    }

    private void RestoreSaveButton()
    {
        SaveButton.Content = SaveLabel;
        SaveButton.IsEnabled = true;
    }

    private string? SelectedCalendar() =>
        CalendarPanel.Children.OfType<RadioButton>().FirstOrDefault(choice => choice.IsChecked == true)?.Tag as string;

    private static string Describe(EventDraft draft)
    {
        var date = draft.Date.ToString("dd/MM", CultureInfo.InvariantCulture);
        return draft.Start is { } start
            ? $"{date} às {start.ToString(TimeFormat, CultureInfo.InvariantCulture)}"
            : date;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorBanner.Visibility = Visibility.Collapsed;
}
