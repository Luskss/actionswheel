using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using AcoesRapidas.Config;
using AcoesRapidas.Input;

namespace AcoesRapidas.UI;

/// <summary>Abertura, fechamento e interação do menu circular.</summary>
public partial class RadialMenuWindow : Window
{
    private const double ScreenMargin = 44;

    /// <summary>Tempo em que cliques são ignorados ao trocar de rodinha, para um clique duplo não escolher a opção seguinte.</summary>
    private const int LevelChangeClickGuardMs = 350;

    private readonly RingWindow _ringWindow = new();

    /// <summary>Rodinhas anteriores, da mais recente para a mais antiga, para o Esc e o centro voltarem.</summary>
    private readonly Stack<IReadOnlyList<ActionDefinition>> _levels = new();

    /// <summary>Nomes das listas abertas, da atual para a mais externa.</summary>
    private readonly Stack<string> _levelNames = new();

    private IReadOnlyList<ActionDefinition> _actions = [];
    private ThemePalette _palette = WindowsTheme.Current();
    private NativeMethods.RECT _screenBounds;
    private double _screenScale = 1;
    private Point _center;
    private int? _highlighted;
    private Point? _lastMousePosition;
    private long _clicksAllowedAt;
    private bool _leftPressed;
    private bool _isClosing;
    private int _closeGeneration;
    private ActionDefinition? _pendingAction;

    public RadialMenuWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    public event Action<ActionDefinition>? ActionChosen;

    public event Action<bool>? OpenChanged;

    public bool IsOpen { get; private set; }

    private Canvas Ring => _ringWindow.Ring;

    public void Open(IReadOnlyList<ActionDefinition> actions)
    {
        if (_isClosing)
        {
            FinishClose();
        }

        if (IsOpen)
        {
            return;
        }

        _levels.Clear();
        _levelNames.Clear();
        _actions = actions;
        _palette = WindowsTheme.Current();
        _highlighted = null;
        _lastMousePosition = null;
        _leftPressed = false;

        NativeMethods.GetCursorPos(out var cursor);
        var (bounds, dpi) = NativeMethods.GetMonitorAt(cursor);
        var scale = dpi / 96.0;
        _screenBounds = bounds;
        _screenScale = scale;
        var cursorPosition = new Point((cursor.X - bounds.Left) / scale, (cursor.Y - bounds.Top) / scale);

        var (centerX, centerY) = RadialMenuGeometry.ClampCenter(
            cursorPosition.X, cursorPosition.Y, bounds.Width / scale, bounds.Height / scale, OuterRadius + ScreenMargin);
        _center = new Point(centerX, centerY);
        BuildRing();
        PrepareOpenAnimation();

        // A posição é aplicada de novo depois de exibir porque o WPF pode restaurar a anterior.
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        NativeMethods.PlaceTopmost(hwnd, bounds);
        Show();
        NativeMethods.PlaceTopmost(hwnd, bounds);
        _ringWindow.ShowAt(bounds.Left + (centerX * scale), bounds.Top + (centerY * scale), scale);

        IsOpen = true;
        OpenChanged?.Invoke(true);
        PlayOpenAnimation();
        UpdateHighlight(cursorPosition);
    }

    public void CloseMenu()
    {
        if (IsOpen)
        {
            BeginClose(null);
        }
    }

    public void Toggle(IReadOnlyList<ActionDefinition> actions)
    {
        if (IsOpen)
        {
            CloseMenu();
        }
        else
        {
            Open(actions);
        }
    }

    /// <summary>Escolhe uma ação. Quando ela é uma lista, a rodinha troca pelas opções dela.</summary>
    public void Choose(int index)
    {
        if (!IsOpen || index < 0 || index >= _actions.Count)
        {
            return;
        }

        var action = _actions[index];
        if (action.IsMenu)
        {
            _levels.Push(_actions);
            _levelNames.Push(action.Label);
            SwitchLevel(action.Children!);
            return;
        }

        SetHighlight(index);
        BeginClose(action);
    }

    /// <summary>Anda pelas fatias com as setas, alcançando também as que não têm número.</summary>
    public void MoveHighlight(int step)
    {
        if (!IsOpen || RadialMenuGeometry.StepSlice(_highlighted, step, _actions.Count) is not { } next)
        {
            return;
        }

        MoveTracker(RadialMenuGeometry.SliceCenterAngle(next, _actions.Count));
        SetHighlight(next);
    }

    public void ChooseHighlighted()
    {
        if (_highlighted is { } index)
        {
            Choose(index);
        }
    }

    /// <summary>Volta para a rodinha anterior ou, na principal, fecha o menu.</summary>
    public void Back()
    {
        if (!IsOpen)
        {
            return;
        }

        if (_levels.Count == 0)
        {
            CloseMenu();
            return;
        }

        _levelNames.Pop();
        SwitchLevel(_levels.Pop());
    }

    private void SwitchLevel(IReadOnlyList<ActionDefinition> actions)
    {
        _actions = actions;
        _highlighted = null;
        _leftPressed = false;
        _clicksAllowedAt = Environment.TickCount64 + LevelChangeClickGuardMs;

        BuildRing();
        PrepareOpenAnimation();
        PlayOpenAnimation();

        NativeMethods.GetCursorPos(out var cursor);
        UpdateHighlight(new Point(
            (cursor.X - _screenBounds.Left) / _screenScale, (cursor.Y - _screenBounds.Top) / _screenScale));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // O Windows também avisa sem o mouse ter andado, o que desfaria a escolha feita pelas setas.
        var position = e.GetPosition(Surface);
        if (position != _lastMousePosition)
        {
            _lastMousePosition = position;
            UpdateHighlight(position);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _leftPressed = Environment.TickCount64 >= _clicksAllowedAt;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_leftPressed || !IsOpen)
        {
            return;
        }

        _leftPressed = false;
        UpdateHighlight(e.GetPosition(Surface));
        if (_highlighted is { } index)
        {
            Choose(index);
        }
        else
        {
            Back();
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Back();
    }

    private void BeginClose(ActionDefinition? action)
    {
        IsOpen = false;
        _isClosing = true;
        _pendingAction = action;
        OpenChanged?.Invoke(false);

        var generation = ++_closeGeneration;
        EventHandler completed = (_, _) =>
        {
            if (generation == _closeGeneration)
            {
                FinishClose();
            }
        };

        if (action is null)
        {
            PlayCloseAnimation(completed);
        }
        else
        {
            PlayConfirmAnimation(completed);
        }
    }

    private void FinishClose()
    {
        _closeGeneration++;
        _isClosing = false;
        _ringWindow.Hide();
        Hide();

        var action = _pendingAction;
        _pendingAction = null;
        if (action is not null)
        {
            ActionChosen?.Invoke(action);
        }
    }

    private void UpdateHighlight(Point position)
    {
        if (!IsOpen)
        {
            return;
        }

        var dx = position.X - _center.X;
        var dy = position.Y - _center.Y;
        MoveTracker(RadialMenuGeometry.AngleOf(dx, dy));
        SetHighlight(RadialMenuGeometry.SliceAt(dx, dy, _actions.Count, HubRadius));
    }

    private void SetHighlight(int? next)
    {
        if (next == _highlighted)
        {
            return;
        }

        var previous = _highlighted;
        _highlighted = next;

        if (previous is { } oldIndex)
        {
            AnimateSliceState(_slices[oldIndex], isActive: false);
        }

        if (next is { } newIndex)
        {
            AnimateSliceState(_slices[newIndex], isActive: true);
        }

        MoveSelector(previous, next);
        UpdateHubText(next is { } selected ? HubLabel(_actions[selected]) : null);
    }

    /// <summary>No centro, uma lista avisa quantas opções vai abrir.</summary>
    private static string HubLabel(ActionDefinition action)
    {
        if (!action.IsMenu)
        {
            return action.Label;
        }

        var count = action.Children!.Count;
        return $"{action.Label}\n{count} {(count == 1 ? "opção" : "opções")}";
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeNonActivating(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(PreventActivation);
    }

    private nint PreventActivation(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != NativeMethods.WM_MOUSEACTIVATE)
        {
            return 0;
        }

        handled = true;
        return NativeMethods.MA_NOACTIVATE;
    }
}
