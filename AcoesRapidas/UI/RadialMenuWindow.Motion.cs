using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AcoesRapidas.UI;

/// <summary>Animações do menu circular.</summary>
public partial class RadialMenuWindow
{
    private const int SliceStaggerMs = 26;
    private const double ActiveContentScale = 1.1;
    private const double ActiveContentShift = 5;
    private const double IntroContentShift = 22;
    private const double TrackerStepDegrees = 4;
    private const double TabHiddenShift = 26;
    private const double TabActiveShift = -11;
    private const double TabActiveWidth = 1.18;

    /// <summary>Define o estado inicial antes de a janela aparecer, para não piscar.</summary>
    private void PrepareOpenAnimation()
    {
        Ring.BeginAnimation(OpacityProperty, null);
        Ring.Opacity = 0;
        _ringScale.ScaleX = _ringScale.ScaleY = 0.78;
        _ringRotate.Angle = -14;
        _haloScale.ScaleX = _haloScale.ScaleY = 0.82;
        _hubScale.ScaleX = _hubScale.ScaleY = 0.6;

        foreach (var slice in _slices)
        {
            slice.Shape.Opacity = 0;
            slice.ShapeScale.ScaleX = slice.ShapeScale.ScaleY = 0.84;
            slice.Content.Opacity = 0;
            slice.ContentShift.X = -slice.DirectionX * IntroContentShift;
            slice.ContentShift.Y = -slice.DirectionY * IntroContentShift;

            if (slice.Tab is { } tab)
            {
                tab.Root.Opacity = 0;
                tab.Shift.Y = TabHiddenShift;
            }
        }
    }

    private void PlayOpenAnimation()
    {
        Ring.BeginAnimation(OpacityProperty, Motion.To(1, 110));
        AnimateScale(_ringScale, 1, 300, Motion.Pop);
        _ringRotate.BeginAnimation(RotateTransform.AngleProperty, Motion.To(0, 300));
        AnimateScale(_haloScale, 1, 380);
        AnimateScale(_hubScale, 1, 280, Motion.Pop, 40);

        for (var i = 0; i < _slices.Count; i++)
        {
            var slice = _slices[i];
            var delay = 40 + (i * SliceStaggerMs);
            slice.Shape.BeginAnimation(OpacityProperty, Motion.To(1, 170, delay: delay));
            AnimateScale(slice.ShapeScale, 1, 240, Motion.Pop, delay);
            slice.Content.BeginAnimation(OpacityProperty, Motion.To(1, 170, delay: delay + 30));
            slice.ContentShift.BeginAnimation(TranslateTransform.XProperty, Motion.To(0, 260, Motion.Pop, delay + 30));
            slice.ContentShift.BeginAnimation(TranslateTransform.YProperty, Motion.To(0, 260, Motion.Pop, delay + 30));

            if (slice.Tab is { } tab)
            {
                // A aba salta depois que a fatia já apareceu.
                tab.Root.BeginAnimation(OpacityProperty, Motion.To(1, 90, delay: delay + 150));
                tab.Shift.BeginAnimation(TranslateTransform.YProperty, Motion.To(0, 340, Motion.Spring, delay + 150));
            }
        }
    }

    private void PlayCloseAnimation(EventHandler completed)
    {
        AnimateScale(_ringScale, 0.86, 130, Motion.Exit);
        _ringRotate.BeginAnimation(RotateTransform.AngleProperty, Motion.To(10, 130, Motion.Exit));

        var fade = Motion.To(0, 130, Motion.Exit);
        fade.Completed += completed;
        Ring.BeginAnimation(OpacityProperty, fade);
    }

    private void PlayConfirmAnimation(EventHandler completed)
    {
        AddRipple();
        if (_highlighted is { } index)
        {
            AnimateScale(_slices[index].ContentScale, 1.22, 150, Motion.Pop);
        }

        AnimateScale(_ringScale, 1.05, 210);

        var fade = Motion.To(0, 170, Motion.Exit, 50);
        fade.Completed += completed;
        Ring.BeginAnimation(OpacityProperty, fade);
    }

    private void AddRipple()
    {
        var scale = new ScaleTransform(1, 1);
        var ripple = new Ellipse
        {
            Stroke = Solid(_palette.AccentStart),
            StrokeThickness = 3,
            RenderTransform = scale,
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false,
        };
        PlaceCentered(ripple, new Point(0, 0), HubRadius * 2, HubRadius * 2);
        Ring.Children.Add(ripple);

        AnimateScale(scale, (OuterRadius + 30) / HubRadius, 230);
        ripple.BeginAnimation(OpacityProperty, Motion.To(0, 230));
    }

    private void AnimateSliceState(SliceVisual slice, bool isActive)
    {
        AnimateScale(slice.ContentScale, isActive ? ActiveContentScale : 1, 170, Motion.Pop);

        var shift = isActive ? ActiveContentShift : 0;
        slice.ContentShift.BeginAnimation(TranslateTransform.XProperty, Motion.To(slice.DirectionX * shift, 170));
        slice.ContentShift.BeginAnimation(TranslateTransform.YProperty, Motion.To(slice.DirectionY * shift, 170));

        slice.TextBrush.BeginAnimation(
            SolidColorBrush.ColorProperty, Motion.ToColor(isActive ? _palette.OnAccent : _palette.Text, 130));
        slice.NumberBrush.BeginAnimation(
            SolidColorBrush.ColorProperty, Motion.ToColor(isActive ? _palette.OnAccent : _palette.MutedText, 130));

        if (slice.Tab is { } tab)
        {
            tab.Shift.BeginAnimation(TranslateTransform.YProperty, Motion.To(isActive ? TabActiveShift : 0, 240, Motion.Spring));
            tab.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(isActive ? TabActiveWidth : 1, 240, Motion.Spring));
        }
    }

    private void MoveSelector(int? previous, int? next)
    {
        if (_selector is null || _tracker is null)
        {
            return;
        }

        if (next is not { } index)
        {
            _selector.BeginAnimation(OpacityProperty, Motion.To(0, 120));
            _tracker.BeginAnimation(OpacityProperty, Motion.To(0, 120));
            return;
        }

        var target = RadialMenuGeometry.SliceCenterAngle(index, _actions.Count);
        if (previous is null)
        {
            // A seleção surge já na fatia certa, sem girar desde a posição anterior.
            _selectorAngle = target;
            _selectorRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            _selectorRotate.Angle = target;
        }
        else
        {
            _selectorAngle += RadialMenuGeometry.ShortestRotation(_selectorAngle, target);
            _selectorRotate.BeginAnimation(RotateTransform.AngleProperty, Motion.To(_selectorAngle, 170));
        }

        _selector.BeginAnimation(OpacityProperty, Motion.To(1, 110));
        _tracker.BeginAnimation(OpacityProperty, Motion.To(1, 110));
    }

    /// <summary>Ignora movimentos mínimos para não redesenhar o anel a cada pixel do mouse.</summary>
    private void MoveTracker(double angle)
    {
        if (Math.Abs(RadialMenuGeometry.ShortestRotation(_trackerRotate.Angle, angle)) >= TrackerStepDegrees)
        {
            _trackerRotate.Angle = angle;
        }
    }

    private void UpdateHubText(string? label)
    {
        if (_hubText is null || _hubTextBrush is null)
        {
            return;
        }

        _hubText.Text = label ?? IdleHint;
        _hubTextBrush.BeginAnimation(
            SolidColorBrush.ColorProperty, Motion.ToColor(label is null ? _palette.MutedText : _palette.Text, 120));

        _hubTextShift.Y = 5;
        _hubTextShift.BeginAnimation(TranslateTransform.YProperty, Motion.To(0, 150));
        _hubText.Opacity = 0;
        _hubText.BeginAnimation(OpacityProperty, Motion.To(1, 150));
    }

    private static void AnimateScale(ScaleTransform transform, double value, int milliseconds, IEasingFunction? ease = null, int delay = 0)
    {
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, Motion.To(value, milliseconds, ease, delay));
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, Motion.To(value, milliseconds, ease, delay));
    }
}
