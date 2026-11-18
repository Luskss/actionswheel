using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AcoesRapidas.Config;

namespace AcoesRapidas.UI;

/// <summary>Construção dos elementos visuais do menu circular.</summary>
public partial class RadialMenuWindow
{
    private const double OuterRadius = 196;
    private const double InnerRadius = 72;
    private const double HubRadius = 58;
    private const double TrackerRadius = 65;
    private const double HaloRadius = OuterRadius + 46;
    private const double BackingRadius = OuterRadius + 10;
    private const double SelectorGrowth = 5;
    private const double CornerRadius = 5;
    private const double SliceGap = 3;
    private const double IconSize = 26;
    private const int MaxLabelLines = 2;
    private const double TabInnerEdge = BackingRadius - 4;
    private const double TabOuterEdge = BackingRadius + 18;
    private const double TabHalfWidth = 27;
    private const double TabCornerRadius = 6;
    private const string TabChevron = "";

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");

    private readonly List<SliceVisual> _slices = [];
    private ScaleTransform _ringScale = new();
    private RotateTransform _ringRotate = new();
    private ScaleTransform _haloScale = new();
    private ScaleTransform _hubScale = new();
    private TranslateTransform _hubTextShift = new();
    private RotateTransform _selectorRotate = new();
    private RotateTransform _trackerRotate = new();
    private Ellipse? _halo;
    private Canvas? _tracker;
    private Path? _selector;
    private TextBlock? _hubText;
    private SolidColorBrush? _hubTextBrush;
    private double _selectorAngle;

    /// <summary>Com o mouse no centro, diz o que o clique ali vai fazer.</summary>
    private string IdleHint => _levelNames.Count switch
    {
        0 => "Clique aqui para fechar",
        1 => "Clique aqui para voltar",
        _ => $"Voltar para {_levelNames.ElementAt(1)}",
    };

    private sealed record SliceVisual(
        Path Shape,
        ScaleTransform ShapeScale,
        FrameworkElement Content,
        ScaleTransform ContentScale,
        TranslateTransform ContentShift,
        SolidColorBrush TextBrush,
        SolidColorBrush NumberBrush,
        double DirectionX,
        double DirectionY,
        ListTab? Tab);

    /// <summary>Aba que salta da borda nas fatias que abrem outra rodinha.</summary>
    private sealed record ListTab(FrameworkElement Root, TranslateTransform Shift, ScaleTransform Scale);

    private void BuildRing()
    {
        Ring.Children.Clear();
        _slices.Clear();

        _ringScale = new ScaleTransform(1, 1);
        _ringRotate = new RotateTransform(0);
        var ringTransform = new TransformGroup();
        ringTransform.Children.Add(_ringScale);
        ringTransform.Children.Add(_ringRotate);
        Ring.RenderTransform = ringTransform;

        AddHalo();

        // As abas ficam atrás do fundo para parecerem sair de dentro do círculo.
        var tabs = Enumerable.Range(0, _actions.Count).Select(AddListTab).ToList();
        AddBacking();

        var shapes = Enumerable.Range(0, _actions.Count).Select(AddSliceShape).ToList();
        AddSelector();
        for (var i = 0; i < _actions.Count; i++)
        {
            _slices.Add(AddSliceContent(i, shapes[i].Shape, shapes[i].Scale, tabs[i]));
        }

        AddHub();
        AddTracker();
    }

    private void AddHalo()
    {
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(_palette.Glow, 0.72));
        brush.GradientStops.Add(new GradientStop(ColorMath.WithAlpha(_palette.Glow, 0), 1));
        brush.Freeze();

        _haloScale = new ScaleTransform(1, 1);
        _halo = new Ellipse
        {
            Fill = brush,
            CacheMode = new BitmapCache(),
            RenderTransform = _haloScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false,
        };
        PlaceCentered(_halo, new Point(0, 0), HaloRadius * 2, HaloRadius * 2);
        Ring.Children.Add(_halo);
    }

    private ListTab? AddListTab(int index)
    {
        if (!_actions[index].IsMenu)
        {
            return null;
        }

        var brush = Solid(_palette.AccentStart);
        var shape = new Path
        {
            Data = SliceShape.CreateTab(0, TabHalfWidth, TabInnerEdge, TabOuterEdge, TabCornerRadius),
            Fill = brush,
            Stroke = brush,
            StrokeThickness = TabCornerRadius * 2,
            StrokeLineJoin = PenLineJoin.Round,
        };

        var chevron = new TextBlock
        {
            Text = TabChevron,
            FontFamily = IconFont,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = Solid(_palette.OnAccent),
            TextAlignment = TextAlignment.Center,
        };
        PlaceCentered(chevron, new Point(0, -(BackingRadius + TabOuterEdge) / 2), 24, 20);

        // O painel gira até a fatia; dentro dele, o eixo Y negativo sempre aponta para fora do círculo.
        var shift = new TranslateTransform();
        var scale = new ScaleTransform(1, 1);
        var motion = new TransformGroup();
        motion.Children.Add(scale);
        motion.Children.Add(shift);

        var slider = new Canvas { RenderTransform = motion };
        slider.Children.Add(shape);
        slider.Children.Add(chevron);

        var root = new Canvas
        {
            RenderTransform = new RotateTransform(RadialMenuGeometry.SliceCenterAngle(index, _actions.Count)),
            IsHitTestVisible = false,
        };
        root.Children.Add(slider);
        Ring.Children.Add(root);

        return new ListTab(root, shift, scale);
    }

    private void AddBacking()
    {
        var backing = new Ellipse
        {
            Fill = Solid(_palette.Surface),
            Stroke = Solid(_palette.SurfaceStroke),
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        PlaceCentered(backing, new Point(0, 0), BackingRadius * 2, BackingRadius * 2);
        Ring.Children.Add(backing);
    }

    private (Path Shape, ScaleTransform Scale) AddSliceShape(int index)
    {
        var angle = RadialMenuGeometry.SliceCenterAngle(index, _actions.Count);
        var brush = Solid(_palette.Slice);
        var scale = new ScaleTransform(1, 1);
        var shape = new Path
        {
            Data = CreateSliceGeometry(angle, OuterRadius),
            Fill = brush,
            Stroke = brush,
            StrokeThickness = CornerRadius * 2,
            StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = scale,
            IsHitTestVisible = false,
        };
        Ring.Children.Add(shape);
        return (shape, scale);
    }

    private void AddSelector()
    {
        var outerEdge = OuterRadius + SelectorGrowth;
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, -outerEdge),
            EndPoint = new Point(0, -InnerRadius),
        };
        brush.GradientStops.Add(new GradientStop(_palette.AccentStart, 0));
        brush.GradientStops.Add(new GradientStop(_palette.AccentEnd, 1));
        brush.Freeze();

        _selectorAngle = 0;
        _selectorRotate = new RotateTransform(0);
        _selector = new Path
        {
            Data = CreateSliceGeometry(0, outerEdge),
            Fill = brush,
            Stroke = brush,
            StrokeThickness = CornerRadius * 2,
            StrokeLineJoin = PenLineJoin.Round,
            RenderTransform = _selectorRotate,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        Ring.Children.Add(_selector);
    }

    private SliceVisual AddSliceContent(int index, Path shape, ScaleTransform shapeScale, ListTab? tab)
    {
        var action = _actions[index];
        var angle = RadialMenuGeometry.SliceCenterAngle(index, _actions.Count);
        var (directionX, directionY) = RadialMenuGeometry.PointOnCircle(angle, 1);
        var textBrush = new SolidColorBrush(_palette.Text);
        var numberBrush = new SolidColorBrush(_palette.MutedText);

        // A décima ação mostra "0", como no teclado. Depois dela não há mais teclas de atalho.
        var shortcut = index < 10 ? ((index + 1) % 10).ToString() : "";

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = shortcut,
            FontFamily = TextFont,
            FontSize = 10,
            Foreground = numberBrush,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 3),
        });
        stack.Children.Add(CreateIcon(action, textBrush));
        stack.Children.Add(new TextBlock
        {
            Text = action.Label,
            FontFamily = TextFont,
            FontSize = _actions.Count switch { >= 12 => 10.5, 11 => 11, _ => 12 },
            FontWeight = FontWeights.Medium,
            Foreground = textBrush,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 16 * MaxLabelLines,
        });

        var contentScale = new ScaleTransform(1, 1);
        var contentShift = new TranslateTransform();
        var transform = new TransformGroup();
        transform.Children.Add(contentScale);
        transform.Children.Add(contentShift);

        var host = new Grid
        {
            RenderTransform = transform,
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false,
        };
        host.Children.Add(stack);

        var contentRadius = (InnerRadius + OuterRadius) / 2;
        var arcLength = 2 * Math.PI * contentRadius / _actions.Count;
        PlaceCentered(host, ToPoint(angle, contentRadius), Math.Min(arcLength - 2, 112), 96);
        Ring.Children.Add(host);

        return new SliceVisual(
            shape, shapeScale, host, contentScale, contentShift, textBrush, numberBrush, directionX, directionY, tab);
    }

    /// <summary>
    /// Usa o ícone do programa quando a ação tem um, senão o ícone da fonte do Windows.
    /// Os dois são pintados com o mesmo pincel e mudam de cor juntos na seleção.
    /// </summary>
    private static FrameworkElement CreateIcon(ActionDefinition action, Brush glyphBrush)
    {
        var margin = new Thickness(0, 0, 0, 5);
        if (AppIcons.LoadMask(action.IconPath) is { } mask)
        {
            return new Rectangle
            {
                Fill = glyphBrush,
                OpacityMask = mask,
                Width = IconSize,
                Height = IconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = margin,
            };
        }

        return new TextBlock
        {
            Text = action.IconGlyph,
            FontFamily = IconFont,
            FontSize = 24,
            Foreground = glyphBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = margin,
        };
    }

    private void AddHub()
    {
        _hubScale = new ScaleTransform(1, 1);
        var disc = new Ellipse
        {
            Fill = Solid(_palette.Hub),
            Stroke = Solid(_palette.SurfaceStroke),
            StrokeThickness = 1,
            RenderTransform = _hubScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false,
        };
        PlaceCentered(disc, new Point(0, 0), HubRadius * 2, HubRadius * 2);
        Ring.Children.Add(disc);

        _hubTextBrush = new SolidColorBrush(_palette.MutedText);
        _hubTextShift = new TranslateTransform();
        _hubText = new TextBlock
        {
            Text = IdleHint,
            Foreground = _hubTextBrush,
            FontFamily = TextFont,
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = _hubTextShift,
        };

        var host = new Grid
        {
            RenderTransform = _hubScale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false,
        };
        host.Children.Add(_hubText);
        PlaceCentered(host, new Point(0, 0), (HubRadius * 2) - 20, (HubRadius * 2) - 32);
        Ring.Children.Add(host);
    }

    private void AddTracker()
    {
        _trackerRotate = new RotateTransform(0);
        var dot = new Ellipse { Fill = Solid(_palette.AccentStart) };
        PlaceCentered(dot, new Point(0, -TrackerRadius), 6, 6);

        // O ponto gira em torno do centro do anel, que é a origem deste painel.
        _tracker = new Canvas
        {
            RenderTransform = _trackerRotate,
            Opacity = 0,
            IsHitTestVisible = false,
        };
        _tracker.Children.Add(dot);
        Ring.Children.Add(_tracker);
    }

    private Geometry CreateSliceGeometry(double centerAngle, double outerEdge) =>
        SliceShape.Create(centerAngle, _actions.Count, InnerRadius, outerEdge, CornerRadius, SliceGap);

    private static Point ToPoint(double angle, double radius)
    {
        var (x, y) = RadialMenuGeometry.PointOnCircle(angle, radius);
        return new Point(x, y);
    }

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void PlaceCentered(FrameworkElement element, Point center, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        Canvas.SetLeft(element, center.X - (width / 2));
        Canvas.SetTop(element, center.Y - (height / 2));
    }
}
