using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace TaskWidget;

// Height-animated reveal for a Task's Details. Siblings slide because layout runs every frame.
public sealed partial class Accordion : ContentControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(Accordion), new PropertyMetadata(false, (d, e) => ((Accordion)d).Update((bool)e.NewValue)));

    Storyboard? running;

    public Accordion()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Visibility = Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            if (!IsOpen)
                return;
            Visibility = Visibility.Visible;
            Height = double.NaN;
            Opacity = 1;
        };
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    void Update(bool open)
    {
        running?.Stop();
        var from = Visibility == Visibility.Visible ? ActualHeight : 0;
        Visibility = Visibility.Visible;
        Height = double.NaN;
        var width = (Parent as FrameworkElement)?.ActualWidth ?? 0;
        if (!IsLoaded || width <= 0)
        {
            Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        var full = DesiredSize.Height;
        var to = open ? full : 0;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var height = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromMilliseconds(Ms(open ? "AccordionOpenMs" : "AccordionCloseMs")),
            EasingFunction = ease,
            EnableDependentAnimation = true,
        };
        var opacity = new DoubleAnimation
        {
            To = open ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(Ms(open ? "AccordionFadeInMs" : "AccordionFadeOutMs")),
            EasingFunction = ease,
        };
        Storyboard.SetTarget(height, this);
        Storyboard.SetTargetProperty(height, "Height");
        Storyboard.SetTarget(opacity, this);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        var board = new Storyboard();
        board.Children.Add(height);
        board.Children.Add(opacity);
        board.Completed += (_, _) =>
        {
            if (running != board)
                return;
            running = null;
            board.Stop();
            Height = double.NaN;
            Opacity = open ? 1 : 0;
            Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        };
        running = board;
        board.Begin();
    }

    static double Ms(string key) => (double)Application.Current.Resources[key];
}
