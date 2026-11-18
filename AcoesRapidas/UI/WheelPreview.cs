using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AcoesRapidas.Config;

namespace AcoesRapidas.UI;

/// <summary>Prévia interativa da rodinha: clicar em uma fatia seleciona a ação para edição.</summary>
internal sealed class WheelPreview : FrameworkElement
{
    public static readonly DependencyProperty SelectorAngleProperty = DependencyProperty.Register(
        nameof(SelectorAngle),
        typeof(double),
        typeof(WheelPreview),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double EdgeMargin = 20;
    private const double TabReach = 11;
    private const double TabHalfWidth = 20;
    private const double TabCornerRadius = 4;
    private const double InnerRatio = 0.36;
    private const double HubRatio = 0.30;
    private const double CornerRadius = 4;
    private const double SliceGap = 3;
    private const double ImageIconSize = 22;

    private static readonly Typeface IconTypeface = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Typeface TextTypeface = new(
        new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    private IReadOnlyList<ActionDefinition> _actions = [];
    private ThemePalette _palette = WindowsTheme.Current();
    private int _selected;
    private double _targetAngle;

    public WheelPreview()
    {
        Focusable = true;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
    }

    public event Action<int>? SliceSelected;

    public double SelectorAngle
    {
        get => (double)GetValue(SelectorAngleProperty);
        set => SetValue(SelectorAngleProperty, value);
    }

    /// <summary>Limite de fatias do nível que está sendo mostrado.</summary>
    public int MaxSlices { get; set; } = ConfigStore.MaxActions;

    /// <summary>Texto do centro. Sem ele, o centro mostra quantas fatias existem.</summary>
    public string? HubCaption { get; set; }

    public void Update(IReadOnlyList<ActionDefinition> actions, int selected, ThemePalette palette)
    {
        var keepsLayout = actions.Count == _actions.Count;
        _actions = actions;
        _palette = palette;
        _selected = selected;

        var target = RadialMenuGeometry.SliceCenterAngle(selected, actions.Count);
        if (keepsLayout)
        {
            _targetAngle += RadialMenuGeometry.ShortestRotation(_targetAngle, target);
            BeginAnimation(SelectorAngleProperty, Motion.To(_targetAngle, 170));
        }
        else
        {
            _targetAngle = target;
            BeginAnimation(SelectorAngleProperty, null);
            SelectorAngle = target;
        }

        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.Key switch
        {
            Key.Right or Key.Down => 1,
            Key.Left or Key.Up => -1,
            _ => 0,
        };

        if (step != 0 && _actions.Count > 0)
        {
            SliceSelected?.Invoke((_selected + step + _actions.Count) % _actions.Count);
            e.Handled = true;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();

        var position = e.GetPosition(this);
        var outer = OuterRadius();
        var index = RadialMenuGeometry.SliceAt(
            position.X - (ActualWidth / 2), position.Y - (ActualHeight / 2), _actions.Count, outer * HubRatio);
        if (index is { } slice)
        {
            SliceSelected?.Invoke(slice);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_actions.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var outer = OuterRadius();
        var inner = outer * InnerRatio;
        dc.PushTransform(new TranslateTransform(ActualWidth / 2, ActualHeight / 2));

        if (IsKeyboardFocused)
        {
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(_palette.AccentStart), 2), new Point(0, 0), outer + 5, outer + 5);
        }

        DrawListTabs(dc, outer);
        dc.DrawEllipse(new SolidColorBrush(_palette.Surface), null, new Point(0, 0), outer, outer);
        var sliceOuter = outer - 6;

        var sliceBrush = new SolidColorBrush(_palette.Slice);
        var slicePen = CreateCornerPen(sliceBrush);
        for (var i = 0; i < _actions.Count; i++)
        {
            var angle = RadialMenuGeometry.SliceCenterAngle(i, _actions.Count);
            dc.DrawGeometry(sliceBrush, slicePen, SliceShape.Create(angle, _actions.Count, inner, sliceOuter, CornerRadius, SliceGap));
        }

        var accentBrush = new SolidColorBrush(_palette.AccentStart);
        dc.PushTransform(new RotateTransform(SelectorAngle));
        dc.DrawGeometry(accentBrush, CreateCornerPen(accentBrush), SliceShape.Create(0, _actions.Count, inner, sliceOuter, CornerRadius, SliceGap));
        dc.Pop();

        for (var i = 0; i < _actions.Count; i++)
        {
            DrawSliceContent(dc, i, (inner + sliceOuter) / 2);
        }

        dc.DrawEllipse(new SolidColorBrush(_palette.Hub), new Pen(new SolidColorBrush(_palette.SurfaceStroke), 1), new Point(0, 0), outer * HubRatio, outer * HubRatio);
        var captionWidth = outer * HubRatio * 1.7;
        var caption = CreateText(HubCaption ?? $"{_actions.Count} de {MaxSlices}", TextTypeface, 12, _palette.MutedText, captionWidth, 2);
        caption.TextAlignment = TextAlignment.Center;
        dc.DrawText(caption, new Point(-captionWidth / 2, -caption.Height / 2));

        dc.Pop();
    }

    private static Pen CreateCornerPen(Brush brush) =>
        new(brush, CornerRadius * 2) { LineJoin = PenLineJoin.Round };

    private double OuterRadius() => (Math.Min(ActualWidth, ActualHeight) / 2) - EdgeMargin;

    /// <summary>Desenha a aba que indica as fatias com subitens, como na rodinha real.</summary>
    private void DrawListTabs(DrawingContext dc, double outer)
    {
        var brush = new SolidColorBrush(_palette.AccentStart);
        var pen = new Pen(brush, TabCornerRadius * 2) { LineJoin = PenLineJoin.Round };
        var tab = SliceShape.CreateTab(0, TabHalfWidth, outer - 4, outer + TabReach, TabCornerRadius);

        for (var i = 0; i < _actions.Count; i++)
        {
            if (!_actions[i].IsMenu)
            {
                continue;
            }

            dc.PushTransform(new RotateTransform(RadialMenuGeometry.SliceCenterAngle(i, _actions.Count)));
            dc.DrawGeometry(brush, pen, tab);
            var chevron = CreateText("", IconTypeface, 9, _palette.OnAccent, 16);
            dc.DrawText(chevron, new Point(-chevron.Width / 2, -outer - (TabReach / 2) - (chevron.Height / 2)));
            dc.Pop();
        }
    }

    private void DrawSliceContent(DrawingContext dc, int index, double radius)
    {
        var action = _actions[index];
        var color = index == _selected ? _palette.OnAccent : _palette.Text;
        var angle = RadialMenuGeometry.SliceCenterAngle(index, _actions.Count);
        var (x, y) = RadialMenuGeometry.PointOnCircle(angle, radius);
        var labelWidth = Math.Min(2 * Math.PI * radius / _actions.Count * 0.96, 100);

        var mask = AppIcons.LoadMask(action.IconPath);
        var glyph = mask is null ? CreateText(action.IconGlyph, IconTypeface, 20, color, 40) : null;
        var iconHeight = glyph?.Height ?? ImageIconSize;
        var label = CreateText(action.Label, TextTypeface, 11, color, labelWidth, 2);
        label.TextAlignment = TextAlignment.Center;

        var top = y - ((iconHeight + label.Height + 2) / 2);
        if (mask is not null)
        {
            dc.PushOpacityMask(mask);
            dc.DrawRectangle(
                new SolidColorBrush(color), null, new Rect(x - (ImageIconSize / 2), top, ImageIconSize, ImageIconSize));
            dc.Pop();
        }
        else if (glyph is not null)
        {
            dc.DrawText(glyph, new Point(x - (glyph.Width / 2), top));
        }

        dc.DrawText(label, new Point(x - (labelWidth / 2), top + iconHeight + 2));
    }

    private FormattedText CreateText(string text, Typeface typeface, double size, Color color, double maxWidth, int maxLines = 1) =>
        new(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = maxLines,
            Trimming = TextTrimming.CharacterEllipsis,
        };
}
