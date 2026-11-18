using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
namespace AcoesRapidas.UI;

/// <summary>Tutorial em passos que ensina a usar a rodinha com as fatias do próprio usuário.</summary>
public partial class TutorialWindow : Window
{
    private const double DotSize = 8;

    private readonly IReadOnlyList<TutorialStep> _steps;
    private readonly ThemePalette _palette = WindowsTheme.Current();
    private int _current;

    private readonly string _finishLabel;

    public TutorialWindow(string title, IReadOnlyList<TutorialStep> steps, string finishLabel)
    {
        _steps = steps;
        _finishLabel = finishLabel;

        InitializeComponent();
        Title = title;
        FormTheme.Apply(this, _palette, _palette.Slice);

        foreach (var _ in _steps)
        {
            DotsPanel.Children.Add(new Ellipse { Width = DotSize, Height = DotSize, Margin = new Thickness(0, 0, 7, 0) });
        }

        Loaded += (_, _) => NextButton.Focus();
        ShowStep(0);
    }

    private bool IsLastStep => _current == _steps.Count - 1;

    private void ShowStep(int index)
    {
        _current = index;
        var step = _steps[index];

        StepCounter.Text = $"Passo {index + 1} de {_steps.Count}";
        StepTitle.Text = step.Title;
        StepBody.Text = step.Body;
        BuildKeys(step.Keys);

        Preview.HubCaption = step.Caption;
        Preview.Update(step.Wheel, step.Selected, _palette);

        BackButton.Visibility = index == 0 ? Visibility.Hidden : Visibility.Visible;
        SkipButton.Visibility = IsLastStep ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = IsLastStep ? _finishLabel : "Próximo";

        for (var i = 0; i < DotsPanel.Children.Count; i++)
        {
            ((Ellipse)DotsPanel.Children[i]).Fill = (System.Windows.Media.Brush)FindResource(i == index ? "Brush.Accent" : "Brush.Stroke");
        }

        StepPanel.BeginAnimation(OpacityProperty, null);
        StepPanel.Opacity = 0.2;
        StepPanel.BeginAnimation(OpacityProperty, Motion.To(1, 170));
    }

    private void BuildKeys(IReadOnlyList<TutorialKey> keys)
    {
        KeysGrid.Children.Clear();
        KeysGrid.RowDefinitions.Clear();

        for (var row = 0; row < keys.Count; row++)
        {
            KeysGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var keyCap = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("Brush.Input"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("Brush.Stroke"),
                BorderThickness = new Thickness(1, 1, 1, 2),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = keys[row].Key, FontWeight = FontWeights.SemiBold, FontSize = 12 },
            };
            Grid.SetRow(keyCap, row);
            KeysGrid.Children.Add(keyCap);

            var meaning = new TextBlock
            {
                Text = keys[row].Meaning,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 8),
            };
            Grid.SetRow(meaning, row);
            Grid.SetColumn(meaning, 2);
            KeysGrid.Children.Add(meaning);
        }
    }

    private void OnNext(object sender, RoutedEventArgs e) => GoForward();

    private void OnBack(object sender, RoutedEventArgs e) => GoBack();

    private void OnSkip(object sender, RoutedEventArgs e) => Close();

    private void GoForward()
    {
        if (IsLastStep)
        {
            Close();
        }
        else
        {
            ShowStep(_current + 1);
        }
    }

    private void GoBack()
    {
        if (_current > 0)
        {
            ShowStep(_current - 1);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Right when !IsLastStep:
                GoForward();
                e.Handled = true;
                break;
            case Key.Left:
                GoBack();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }
}
