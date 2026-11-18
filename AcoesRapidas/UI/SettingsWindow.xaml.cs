using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AcoesRapidas.Config;
using AcoesRapidas.Input;
using Microsoft.Win32;

namespace AcoesRapidas.UI;

/// <summary>Janela para organizar e editar as ações da rodinha e das listas dentro dela.</summary>
public partial class SettingsWindow : Window
{
    private const string NewActionIcon = "E734";
    private const string RootTitle = "Rodinha";
    private const string DefaultMediaKey = "playpause";

    private static readonly (string Code, string Name)[] IconChoices =
    [
        ("E774", "Site"), ("E80F", "Início"), ("E968", "Rede"), ("E753", "Nuvem"),
        ("E7F4", "Monitor"), ("E756", "Terminal"), ("E943", "Código"), ("E90F", "Ferramenta"),
        ("E713", "Ajustes"), ("E8B7", "Pasta"), ("E8A5", "Documento"), ("E70B", "Anotação"),
        ("E8EF", "Calculadora"), ("E715", "E-mail"), ("E787", "Calendário"), ("E722", "Câmera"),
        ("E768", "Iniciar"), ("E71A", "Parar"), ("E72C", "Atualizar"), ("E7E8", "Energia"),
        ("E72E", "Cadeado"), ("EA18", "Escudo"), ("E734", "Estrela"), ("E946", "Informação"),
        ("E7FC", "Jogo"), ("E8D6", "Música"), ("E8F2", "Chat"), ("E716", "Pessoas"),
    ];

    private readonly ConfigStore _store;
    private readonly Action<ActionDefinition> _runAction;
    private readonly ThemePalette _palette = WindowsTheme.Current();
    private readonly string _originalSnapshot;
    private readonly RadioButton[] _mediaChoices;
    private readonly Dictionary<string, RadioButton> _iconButtons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Níveis acima do que está sendo editado, do mais próximo ao mais distante.</summary>
    private readonly Stack<(List<ActionDefinition> Drafts, int Selected)> _parents = new();

    private List<ActionDefinition> _drafts;
    private int _selected;
    private bool _isLoadingEditor;
    private bool _isSaved;

    public SettingsWindow(ConfigStore store, Action<ActionDefinition> runAction)
    {
        _store = store;
        _runAction = runAction;
        _drafts = store.Current.Actions.Select(Normalize).ToList();
        _originalSnapshot = Snapshot(_drafts);

        InitializeComponent();
        _mediaChoices = [MediaPlayPause, MediaNext, MediaPrevious, MediaStop];
        FormTheme.Apply(this, _palette, _palette.Hub);
        BuildIconChoices();

        Preview.SliceSelected += Select;
        Loaded += (_, _) => Root.BeginAnimation(OpacityProperty, Motion.To(1, 160));
        Root.Opacity = 0;

        Select(0);
    }

    /// <summary>Disparado depois que as alterações foram gravadas.</summary>
    public event Action? Saved;

    /// <summary>Disparado quando o usuário pede para ver o tutorial.</summary>
    public event Action? TutorialRequested;

    public event Action? DiscordTutorialRequested;

    private bool IsInList => _parents.Count > 0;

    private bool IsDirty => Snapshot(CurrentRoot()) != _originalSnapshot;

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_isSaved || !IsDirty)
        {
            return;
        }

        var answer = MessageBox.Show(
            this, "Descartar as alterações feitas na rodinha?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        e.Cancel = answer != MessageBoxResult.Yes;
    }

    private static string Snapshot(IEnumerable<ActionDefinition> actions) =>
        JsonSerializer.Serialize(actions.Select(Normalize));

    private static ActionDefinition Normalize(ActionDefinition action)
    {
        var type = action.Type.ToLowerInvariant();
        return action with
        {
            Label = action.Label.Trim(),
            Target = type == ActionTypes.Url ? AppLinks.ToAppAddress(action.Target) : action.Target.Trim(),
            Type = type,
            Icon = action.Icon.ToUpperInvariant(),
            IconPath = string.IsNullOrWhiteSpace(action.IconPath) ? null : action.IconPath.Trim(),
            Args = type == ActionTypes.Command && !string.IsNullOrWhiteSpace(action.Args) ? action.Args.Trim() : null,
            Children = type == ActionTypes.Menu ? action.Children?.Select(Normalize).ToList() : null,
        };
    }

    private static ActionDefinition NewAction() => new()
    {
        Label = "Nova ação",
        Icon = NewActionIcon,
        Type = ActionTypes.Url,
    };

    /// <summary>A rodinha principal com as alterações de todos os níveis, sem mexer no que está aberto.</summary>
    private List<ActionDefinition> CurrentRoot()
    {
        var level = _drafts;
        foreach (var (parent, selected) in _parents)
        {
            var copy = parent.ToList();
            copy[selected] = copy[selected] with { Children = level.ToList() };
            level = copy;
        }

        return level;
    }

    private void BuildIconChoices()
    {
        foreach (var (code, name) in IconChoices)
        {
            AddIconChoice(code, name);
        }
    }

    private RadioButton AddIconChoice(string code, string name)
    {
        var button = new RadioButton
        {
            Style = (Style)FindResource("IconChoice"),
            Content = char.ConvertFromUtf32(Convert.ToInt32(code, 16)),
            ToolTip = name,
            Tag = code,
        };
        AutomationProperties.SetName(button, name);
        button.Checked += OnIconChecked;

        _iconButtons[code] = button;
        IconPanel.Children.Add(button);
        return button;
    }

    private void Select(int index)
    {
        _selected = Math.Clamp(index, 0, _drafts.Count - 1);
        LoadEditor();
        RefreshWheel();
        Editor.BeginAnimation(OpacityProperty, null);
        Editor.Opacity = 0.35;
        Editor.BeginAnimation(OpacityProperty, Motion.To(1, 150));
    }

    private void LoadEditor()
    {
        var action = _drafts[_selected];
        _isLoadingEditor = true;

        LabelBox.Text = action.Label;
        TargetBox.Text = action.Target;
        ArgsBox.Text = action.Args ?? "";
        TypeUrl.IsChecked = action.Type == ActionTypes.Url;
        TypeCommand.IsChecked = action.Type == ActionTypes.Command;
        TypeToast.IsChecked = action.Type == ActionTypes.Toast;
        TypeSettings.IsChecked = action.Type == ActionTypes.Settings;
        TypeMenu.IsChecked = action.Type == ActionTypes.Menu;
        TypeMedia.IsChecked = action.Type == ActionTypes.Media;
        TypeEvent.IsChecked = action.Type == ActionTypes.Event;

        foreach (var choice in _mediaChoices)
        {
            choice.IsChecked = action.Type == ActionTypes.Media
                && string.Equals((string)choice.Tag, action.Target, StringComparison.OrdinalIgnoreCase);
        }

        LoadIconChoice(action);

        _isLoadingEditor = false;
        UpdateTypeFields(action);
    }

    private void LoadIconChoice(ActionDefinition action)
    {
        foreach (var button in _iconButtons.Values)
        {
            button.IsChecked = false;
        }

        var hasImage = AppIcons.LoadMask(action.IconPath) is not null;
        if (!hasImage)
        {
            var glyph = _iconButtons.TryGetValue(action.Icon, out var existing)
                ? existing
                : AddIconChoice(action.Icon, "Ícone personalizado");
            glyph.IsChecked = true;
        }

        UpdateIconPreview(action);
    }

    private void UpdateIconPreview(ActionDefinition action)
    {
        var mask = AppIcons.LoadMask(action.IconPath);
        IconPreview.OpacityMask = mask;
        IconPreview.Visibility = mask is null ? Visibility.Collapsed : Visibility.Visible;
        ClearIconButton.Visibility = string.IsNullOrWhiteSpace(action.IconPath) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateTypeFields(ActionDefinition action)
    {
        var (label, hint) = action.Type switch
        {
            ActionTypes.Url => (
                "Endereço",
                "Cole o link de um site, canal do Discord ou música do Spotify. Discord e Spotify abrem direto no app. Para um jogo da Steam, use steam://rungameid/ID."),
            ActionTypes.Command => ("Programa", "Nome ou caminho do programa, como notepad.exe."),
            ActionTypes.Toast => ("Mensagem", "Texto exibido na notificação."),
            ActionTypes.Media => ("", "Controla o Spotify e qualquer player de música, mesmo em segundo plano."),
            ActionTypes.Menu => ("", $"Ao escolher esta fatia, abre uma segunda rodinha com {action.Children?.Count ?? 0} opção(ões)."),
            ActionTypes.Event => (
                "Endereço do Nextcloud (opcional)",
                "Abre uma janela para criar o evento no Google Agenda ou no Outlook, que abrem no navegador já preenchidos. Para oferecer também o seu Nextcloud, informe o endereço dele. No primeiro uso, o app pede autorização."),
            _ => ("", "Abre esta janela de configurações."),
        };

        var usesTarget = action.Type is ActionTypes.Url or ActionTypes.Command or ActionTypes.Toast or ActionTypes.Event;
        TargetLabel.Text = label;
        AutomationProperties.SetName(TargetBox, label);
        TypeHint.Text = hint;
        TargetPanel.Visibility = usesTarget ? Visibility.Visible : Visibility.Collapsed;
        ArgsPanel.Visibility = action.Type == ActionTypes.Command ? Visibility.Visible : Visibility.Collapsed;
        BrowseButton.Visibility = action.Type == ActionTypes.Command ? Visibility.Visible : Visibility.Collapsed;
        MediaPanel.Visibility = action.Type == ActionTypes.Media ? Visibility.Visible : Visibility.Collapsed;
        MenuPanel.Visibility = action.Type == ActionTypes.Menu ? Visibility.Visible : Visibility.Collapsed;
        TestButton.IsEnabled = action.Type is ActionTypes.Url or ActionTypes.Command or ActionTypes.Toast or ActionTypes.Media or ActionTypes.Event;
    }

    private void RefreshWheel()
    {
        var maximum = IsInList ? ConfigStore.MaxChildren : ConfigStore.MaxActions;
        var minimum = IsInList ? 1 : ConfigStore.MinActions;

        Preview.MaxSlices = maximum;
        Preview.Update(_drafts.ToList(), _selected, _palette);
        AddButton.IsEnabled = _drafts.Count < maximum;
        AddButton.ToolTip = AddButton.IsEnabled
            ? "Adicionar ação"
            : $"Esta rodinha já tem o máximo de {maximum} ações. Exclua uma ou agrupe algumas em uma lista.";
        RemoveButton.IsEnabled = _drafts.Count > minimum;
        MoveBackButton.IsEnabled = MoveForwardButton.IsEnabled = _drafts.Count > 1;
        SaveButton.IsEnabled = IsDirty;

        BackButton.Visibility = IsInList ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = string.Join(
            " › ", new[] { RootTitle }.Concat(_parents.Reverse().Select(p => p.Drafts[p.Selected].Label)));
    }

    private void UpdateDraft(Func<ActionDefinition, ActionDefinition> change)
    {
        if (_isLoadingEditor)
        {
            return;
        }

        _drafts[_selected] = change(_drafts[_selected]);
        HideError();
        RefreshWheel();
    }

    private void OnLabelChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDraft(action => action with { Label = LabelBox.Text });
        UpdateTypeFields(_drafts[_selected]);
    }

    private void OnTargetChanged(object sender, TextChangedEventArgs e) =>
        UpdateDraft(action => action with { Target = TargetBox.Text });

    /// <summary>Mostra o endereço do app assim que o usuário termina de colar um link.</summary>
    private void OnTargetLostFocus(object sender, RoutedEventArgs e)
    {
        if (_drafts[_selected].Type == ActionTypes.Url)
        {
            TargetBox.Text = AppLinks.ToAppAddress(TargetBox.Text);
        }
    }

    private void OnArgsChanged(object sender, TextChangedEventArgs e) =>
        UpdateDraft(action => action with { Args = ArgsBox.Text });

    private void OnIconChecked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingEditor)
        {
            return;
        }

        // Escolher um ícone da lista substitui o ícone de programa.
        UpdateDraft(action => action with { Icon = (string)((RadioButton)sender).Tag, IconPath = null });
        UpdateIconPreview(_drafts[_selected]);
    }

    private void OnMediaChecked(object sender, RoutedEventArgs e) =>
        UpdateDraft(action => action with { Target = (string)((RadioButton)sender).Tag });

    private void OnTypeChecked(object sender, RoutedEventArgs e)
    {
        if (_isLoadingEditor)
        {
            return;
        }

        var type = sender == TypeUrl ? ActionTypes.Url
            : sender == TypeCommand ? ActionTypes.Command
            : sender == TypeToast ? ActionTypes.Toast
            : sender == TypeMenu ? ActionTypes.Menu
            : sender == TypeMedia ? ActionTypes.Media
            : sender == TypeEvent ? ActionTypes.Event
            : ActionTypes.Settings;

        UpdateDraft(action => action with
        {
            Type = type,
            Target = TargetFor(action, type),
            Children = type == ActionTypes.Menu && action.Children is not { Count: > 0 } ? [NewAction()] : action.Children,
        });
        LoadEditor();
    }

    /// <summary>Ao entrar ou sair dos tipos mídia e evento, o destino de um tipo não serve para o outro.</summary>
    private static string TargetFor(ActionDefinition action, string newType)
    {
        if (newType == ActionTypes.Media)
        {
            return ConfigStore.MediaKeys.ContainsKey(action.Target) ? action.Target : DefaultMediaKey;
        }

        if (newType == ActionTypes.Event)
        {
            return ConfigStore.IsAllowedNextcloudServer(action.Target) ? action.Target : "";
        }

        return action.Type is ActionTypes.Media or ActionTypes.Event ? "" : action.Target;
    }

    private void OnMoveBack(object sender, RoutedEventArgs e) => Move(-1);

    private void OnMoveForward(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int step)
    {
        var target = (_selected + step + _drafts.Count) % _drafts.Count;
        (_drafts[_selected], _drafts[target]) = (_drafts[target], _drafts[_selected]);
        _selected = target;
        HideError();
        RefreshWheel();
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        _drafts.Insert(_selected + 1, NewAction());
        HideError();
        Select(_selected + 1);
        LabelBox.Focus();
        LabelBox.SelectAll();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (_drafts[_selected] is { IsMenu: true } list)
        {
            var answer = MessageBox.Show(
                this,
                $"Excluir a lista \"{list.Label}\" e as {list.Children!.Count} opções dentro dela?",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        _drafts.RemoveAt(_selected);
        HideError();
        Select(Math.Min(_selected, _drafts.Count - 1));
    }

    private void OnEditList(object sender, RoutedEventArgs e)
    {
        var action = _drafts[_selected];
        _parents.Push((_drafts, _selected));
        _drafts = (action.Children ?? [NewAction()]).ToList();
        HideError();
        Select(0);
    }

    private void OnBack(object sender, RoutedEventArgs e) => GoBack();

    /// <summary>Devolve as opções editadas para a ação que abre a lista e volta um nível.</summary>
    private void GoBack()
    {
        var (parent, selected) = _parents.Pop();
        parent[selected] = parent[selected] with { Children = _drafts.ToList() };
        _drafts = parent;
        HideError();
        Select(selected);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher programa",
            Filter = "Programas (*.exe;*.bat;*.cmd)|*.exe;*.bat;*.cmd|Todos os arquivos (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            TargetBox.Text = dialog.FileName;
        }
    }

    private void OnPickIcon(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher programa ou imagem para o ícone",
            Filter = "Programas e imagens|*.exe;*.lnk;*.ico;*.png;*.jpg;*.jpeg;*.bmp|Todos os arquivos (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (AppIcons.LoadMask(dialog.FileName) is null)
        {
            ShowError("Não foi possível ler um ícone deste arquivo.");
            return;
        }

        UpdateDraft(action => action with { IconPath = dialog.FileName });
        LoadIconChoice(_drafts[_selected]);
    }

    private void OnClearIcon(object sender, RoutedEventArgs e)
    {
        UpdateDraft(action => action with { IconPath = null });
        LoadIconChoice(_drafts[_selected]);
    }

    private void OnTest(object sender, RoutedEventArgs e)
    {
        var action = Normalize(_drafts[_selected]);
        if (TryValidate(action, _selected))
        {
            _runAction(action);
        }
    }

    private void OnHelp(object sender, RoutedEventArgs e) => TutorialRequested?.Invoke();

    private void OnDiscordHelp(object sender, RoutedEventArgs e) => DiscordTutorialRequested?.Invoke();

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnSave(object sender, RoutedEventArgs e)
    {
        while (IsInList)
        {
            GoBack();
        }

        var actions = _drafts.Select(Normalize).ToList();
        for (var i = 0; i < actions.Count; i++)
        {
            if (!TryValidate(actions[i], i))
            {
                return;
            }
        }

        if (_store.Save(new AppConfig { Actions = actions }) is { } error)
        {
            ShowError(error);
            return;
        }

        _isSaved = true;
        Saved?.Invoke();
        Close();
    }

    private bool TryValidate(ActionDefinition action, int index)
    {
        try
        {
            ConfigStore.ValidateAction(action, index + 1);
            return true;
        }
        catch (ConfigException ex)
        {
            if (index != _selected)
            {
                Select(index);
            }

            ShowError(ex.Message);
            return false;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.ToolTip = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorBanner.Visibility = Visibility.Collapsed;
}
