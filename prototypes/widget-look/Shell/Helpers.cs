// PROTOTYPE: x:Bind helper functions and the animated accordion shared by every variant.
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.Text;

namespace Look;

public static class F
{
    public static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility NV(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VN(int n) => n > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Both(bool a, bool b) => a && b ? Visibility.Visible : Visibility.Collapsed;
    public static bool Not(bool b) => !b;

    public static Brush Pri(Pri p) => Shell.Res($"Pri{p}Brush");
    public static Brush PriSoft(Pri p) => Shell.Res($"Pri{p}SoftBrush");

    // Priority is shown by shape as well as colour: High ▲, Medium ◆, Low ▼.
    public static Geometry PriShape(Pri p) => Geo(p switch
    {
        Look.Pri.High => "M6,1.2 L11.2,10.4 L0.8,10.4 Z",
        Look.Pri.Medium => "M6,0.8 L11.2,6 L6,11.2 L0.8,6 Z",
        _ => "M0.8,1.6 L11.2,1.6 L6,10.8 Z",
    });

    public static Brush PriBy(string p) => Pri(System.Enum.Parse<Pri>(p));
    public static Brush PriSoftBy(string p) => PriSoft(System.Enum.Parse<Pri>(p));
    public static Geometry ShapeBy(string p) => PriShape(System.Enum.Parse<Pri>(p));

    // Same shape under another name: x:Bind evaluates identical calls once and would share one Geometry between two Paths.
    public static Geometry PriShape2(Pri p) => PriShape(p);

    public static string PriWord(Pri p) => p.ToString();
    public static string EffWord(Eff e) => e.ToString();
    public static string EffSpan(Eff e) => e switch { Eff.Quick => "15m", Eff.Short => "1h", _ => "1h+" };

    // Pie-clock: a quarter, half or full disc.
    public static Geometry EffPie(Eff e) => Geo(e switch
    {
        Eff.Quick => "M6,6 L6,1 A5,5 0 0 1 11,6 Z",
        Eff.Short => "M6,6 L6,1 A5,5 0 0 1 6,11 Z",
        _ => "M6,1 A5,5 0 1 1 5.99,1 Z",
    });

    public static double EffDot(Eff e, int i) => (int)e >= i ? 1 : 0.22;

    public static TextDecorations Strike(bool b) => b ? TextDecorations.Strikethrough : TextDecorations.None;
    public static double Fade(bool b) => b ? 0.45 : 1;
    public static double DoneFade(bool done, bool striking) => done || striking ? 0.5 : 1;
    public static string Count(int n) => n.ToString();
    public static string Plural(int n, string one) => n == 1 ? $"1 {one}" : $"{n} {one}s";

    // A fresh Geometry per call: one instance cannot be shared between two Paths.
    static Geometry Geo(string d) => (Geometry)XamlReader.Load(
        $"<Geometry xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">{d}</Geometry>");
}

// Height-animated reveal for a Task's Details. Siblings slide because layout runs every frame.
public sealed class Accordion : ContentControl
{
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(Accordion), new PropertyMetadata(false, (d, e) => ((Accordion)d).Update((bool)e.NewValue)));

    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

    Storyboard running;

    public Accordion()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Visibility = Visibility.Collapsed;
        Loaded += (_, _) => { if (IsOpen) { Visibility = Visibility.Visible; Height = double.NaN; Opacity = 1; } };
    }

    void Update(bool open)
    {
        running?.Stop();
        double from = Visibility == Visibility.Visible ? ActualHeight : 0;
        Visibility = Visibility.Visible;
        Height = double.NaN;
        double width = (Parent as FrameworkElement)?.ActualWidth ?? 0;
        if (!IsLoaded || width <= 0)
        {
            Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
        double full = DesiredSize.Height;
        double to = open ? full : 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var h = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(open ? 220 : 170), EasingFunction = ease, EnableDependentAnimation = true };
        var o = new DoubleAnimation { To = open ? 1 : 0, Duration = TimeSpan.FromMilliseconds(open ? 260 : 120), EasingFunction = ease };
        Storyboard.SetTarget(h, this); Storyboard.SetTargetProperty(h, "Height");
        Storyboard.SetTarget(o, this); Storyboard.SetTargetProperty(o, "Opacity");
        var sb = new Storyboard();
        sb.Children.Add(h); sb.Children.Add(o);
        sb.Completed += (_, _) =>
        {
            if (running != sb) return;
            running = null;
            sb.Stop();
            Height = double.NaN;
            Opacity = open ? 1 : 0;
            Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        };
        running = sb;
        sb.Begin();
    }
}
