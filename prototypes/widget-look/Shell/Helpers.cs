// PROTOTYPE: x:Bind helper functions and the animated accordion shared by every variant.
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.Text;

namespace Look;

public static class F
{
    public static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility NV(bool b) => b ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility VN(int n) => n > 0 ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Both(bool a, bool b) => a && b ? Visibility.Visible : Visibility.Collapsed;
    public static bool Not(bool b) => !b;
    public static Visibility All3(bool a, bool b, bool c) => a && b && c ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility VS(string s) => string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;

    public static Brush Pri(Pri p) => Shell.Res($"Pri{p}Brush");

    // Priority is shown by shape as well as colour: High ▲, Medium ◆, Low ▼.
    public static Geometry PriShape(Pri p) => Geo(p switch
    {
        Look.Pri.High => "M6,1.2 L11.2,10.4 L0.8,10.4 Z",
        Look.Pri.Medium => "M6,0.8 L11.2,6 L6,11.2 L0.8,6 Z",
        _ => "M0.8,1.6 L11.2,1.6 L6,10.8 Z",
    });

    public static Brush PriBy(string p) => Pri(System.Enum.Parse<Pri>(p));
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
    public static string Plus(int n) => "+" + n;
    public static double Pct(double f) => f * 100;
    public static string Plural(int n, string one) => n == 1 ? $"1 {one}" : $"{n} {one}s";

    // ---- Round 4 ----
    public static double Dim(bool dimmed) => dimmed ? Shell.Num("DimOpacity") : 1;
    public static Visibility NoBanner(Banner b) => b == Look.Banner.None ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Is(Banner b, string which) => b.ToString() == which ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Any2(bool a, bool b) => a || b ? Visibility.Visible : Visibility.Collapsed;
    // The empty list: the welcome card before the first sign-in, otherwise just the hotkey line.
    public static Visibility EmptyHint(bool empty, bool welcome, bool open, bool tasks) => empty && !welcome && open && tasks ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility WelcomeV(bool welcome, bool open, bool tasks) => welcome && open && tasks ? Visibility.Visible : Visibility.Collapsed;
    public static string TapLine(string hotkey) => $"Tap {hotkey.Replace(" + ", "+")} anywhere to capture";
    public static bool Both2(bool a, bool b) => a && b;
    public static string CapturePlaceholder(bool waiting) => waiting ? "Waiting for dictation…" : "Capture a thought…";
    public static string ReadOnlyTip(bool ro) => ro ? "Saved by a newer Task Widget. Update to add Captures." : null;
    // Read-only rows: Done Tasks stay greyed the same way, so only the newer-version state disables controls.
    public static bool Editable(bool _) => Shell.Prefs.Editable;
    public static Visibility PriV(bool pending, bool dimmed) => pending || (dimmed && Shell.Prefs.VB) ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility DimRing(bool dimmed) => dimmed && Shell.Prefs.VB ? Visibility.Visible : Visibility.Collapsed;
    public static bool DimRingOn(bool dimmed) => dimmed && Shell.Prefs.VB;
    public static Visibility DimShimmer(bool dimmed) => dimmed && Shell.Prefs.VC ? Visibility.Visible : Visibility.Collapsed;

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
        var h = new DoubleAnimation { From = from, To = to, Duration = TimeSpan.FromMilliseconds(Shell.Ms(open ? "AccordionOpenMs" : "AccordionCloseMs")), EasingFunction = ease, EnableDependentAnimation = true };
        var o = new DoubleAnimation { To = open ? 1 : 0, Duration = TimeSpan.FromMilliseconds(Shell.Ms(open ? "AccordionFadeInMs" : "AccordionFadeOutMs")), EasingFunction = ease };
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

// Round 4 motion helpers, all driven by Motion.xaml tokens.
public static class Fx
{
    // Enter="True": fades in while sliding down EnterSlidePx whenever shown; a quick fade when hidden.
    public static readonly DependencyProperty EnterProperty = DependencyProperty.RegisterAttached(
        "Enter", typeof(bool), typeof(Fx), new PropertyMetadata(false, (d, e) => { if ((bool)e.NewValue) ApplyEnter((UIElement)d); }));
    public static bool GetEnter(UIElement e) => (bool)e.GetValue(EnterProperty);
    public static void SetEnter(UIElement e, bool v) => e.SetValue(EnterProperty, v);

    static void ApplyEnter(UIElement el)
    {
        var c = ElementCompositionPreview.GetElementVisual(el).Compositor;
        ElementCompositionPreview.SetIsTranslationEnabled(el, true);
        var ms = TimeSpan.FromMilliseconds(Shell.Ms("EnterMs"));
        var show = c.CreateAnimationGroup();
        var fade = c.CreateScalarKeyFrameAnimation(); fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0); fade.InsertKeyFrame(1, 1, Shell.EaseOut(c)); fade.Duration = ms;
        var slide = c.CreateScalarKeyFrameAnimation(); slide.Target = "Translation.Y";
        slide.InsertKeyFrame(0, -(float)Shell.Num("EnterSlidePx")); slide.InsertKeyFrame(1, 0, Shell.EaseOut(c)); slide.Duration = ms;
        show.Add(fade); show.Add(slide);
        var hide = c.CreateScalarKeyFrameAnimation(); hide.Target = "Opacity";
        hide.InsertKeyFrame(1, 0, Shell.EaseIn(c)); hide.Duration = TimeSpan.FromMilliseconds(Shell.Ms("LeaveMs"));
        ElementCompositionPreview.SetImplicitShowAnimation(el, show);
        ElementCompositionPreview.SetImplicitHideAnimation(el, hide);
    }

    // DimFade="True": Opacity changes (Re-interpret dimming) animate over DimMs.
    public static readonly DependencyProperty DimFadeProperty = DependencyProperty.RegisterAttached(
        "DimFade", typeof(bool), typeof(Fx), new PropertyMetadata(false, (d, e) =>
        {
            if ((bool)e.NewValue) ((UIElement)d).OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(Shell.Ms("DimMs")) };
        }));
    public static bool GetDimFade(UIElement e) => (bool)e.GetValue(DimFadeProperty);
    public static void SetDimFade(UIElement e, bool v) => e.SetValue(DimFadeProperty, v);

    // Pulse="True": opacity breathes 1 → 0.35 → 1 every DictationPulseMs while the element is in the tree.
    public static readonly DependencyProperty PulseProperty = DependencyProperty.RegisterAttached(
        "Pulse", typeof(bool), typeof(Fx), new PropertyMetadata(false, (d, e) =>
        {
            if (!(bool)e.NewValue) return;
            var el = (UIElement)d;
            var v = ElementCompositionPreview.GetElementVisual(el);
            var a = v.Compositor.CreateScalarKeyFrameAnimation();
            a.InsertKeyFrame(0, 1); a.InsertKeyFrame(0.5f, 0.35f); a.InsertKeyFrame(1, 1);
            a.Duration = TimeSpan.FromMilliseconds(Shell.Ms("DictationPulseMs"));
            a.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
            v.StartAnimation("Opacity", a);
        }));
    public static bool GetPulse(UIElement e) => (bool)e.GetValue(PulseProperty);
    public static void SetPulse(UIElement e, bool v) => e.SetValue(PulseProperty, v);

    // Drain="True": scales X from 1 to 0 (right edge to the left) over DictationWaitMs, restarting each time it's shown.
    public static readonly DependencyProperty DrainProperty = DependencyProperty.RegisterAttached(
        "Drain", typeof(bool), typeof(Fx), new PropertyMetadata(false, (d, e) =>
        {
            if (!(bool)e.NewValue) return;
            var el = (FrameworkElement)d;
            void Start()
            {
                if (el.Visibility != Visibility.Visible) return;
                var v = ElementCompositionPreview.GetElementVisual(el);
                var a = v.Compositor.CreateScalarKeyFrameAnimation();
                a.InsertKeyFrame(0, 1); a.InsertKeyFrame(1, 0, v.Compositor.CreateLinearEasingFunction());
                a.Duration = TimeSpan.FromMilliseconds(Shell.Ms("DictationWaitMs"));
                v.CenterPoint = new System.Numerics.Vector3(0, 0, 0);
                v.StartAnimation("Scale.X", a);
            }
            el.Loaded += (_, _) => Start();
            el.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Start());
        }));
    public static bool GetDrain(UIElement e) => (bool)e.GetValue(DrainProperty);
    public static void SetDrain(UIElement e, bool v) => e.SetValue(DrainProperty, v);
}

// Variant C: a soft highlight that sweeps across a dimmed row every ShimmerMs.
public sealed class Shimmer : Grid
{
    readonly Microsoft.UI.Xaml.Shapes.Rectangle band = new() { Width = 120, HorizontalAlignment = HorizontalAlignment.Left };

    public Shimmer()
    {
        IsHitTestVisible = false;
        var stops = new GradientStopCollection
        {
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0, 255, 255, 255), Offset = 0 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(28, 255, 255, 255), Offset = 0.5 },
            new GradientStop { Color = Microsoft.UI.ColorHelper.FromArgb(0, 255, 255, 255), Offset = 1 },
        };
        band.Fill = new LinearGradientBrush(stops, 0);
        Children.Add(band);
        ElementCompositionPreview.SetIsTranslationEnabled(band, true);
        SizeChanged += (_, e) => Start(e.NewSize.Width);
    }

    void Start(double w)
    {
        if (w <= 0) return;
        var v = ElementCompositionPreview.GetElementVisual(band);
        var a = v.Compositor.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0, -120); a.InsertKeyFrame(1, (float)w, v.Compositor.CreateLinearEasingFunction());
        a.Duration = TimeSpan.FromMilliseconds(Shell.Ms("ShimmerMs"));
        a.IterationBehavior = Microsoft.UI.Composition.AnimationIterationBehavior.Forever;
        v.StartAnimation("Translation.X", a);
    }
}
