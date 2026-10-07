// PROTOTYPE: the Widget and Dock windows. Code-only; each hosts whichever variant view is current.
using System;
using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace Look;

public interface IWidgetView { TextBox CaptureBox { get; } }

public enum DockSpot { TopCenter, TopRight }
public interface IDockView { DockSpot Spot { get; } double TopOffset { get; } bool OpensCapture { get; } }

public class HostWindow : Window
{
    public readonly nint Hwnd;
    public readonly Grid Root = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    protected readonly ScrollViewer Sizer = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Disabled,
        ZoomMode = ZoomMode.Disabled,
        VerticalAlignment = VerticalAlignment.Top,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    ISystemBackdropControllerWithTargets controller;
    SystemBackdropConfiguration config;
    protected FrameworkElement View;

    public HostWindow(bool noActivate)
    {
        Hwnd = WindowNative.GetWindowHandle(this);
        var p = OverlappedPresenter.Create();
        p.SetBorderAndTitleBar(true, false);
        p.IsResizable = false; p.IsMaximizable = false; p.IsMinimizable = false;
        AppWindow.SetPresenter(p);
        AppWindow.IsShownInSwitchers = false;
        Native.ToolWindow(Hwnd, noActivate);
        Root.Children.Add(Sizer);
        Content = Root;
        ElementCompositionPreview.SetIsTranslationEnabled(Sizer, true);
    }

    public double Scale => Native.Scale(Hwnd);

    // AppWindow.ResizeClient reserves a caption this window does not have, so size the frame ourselves.
    protected void SetClient(SizeInt32 c)
    {
        int dw = AppWindow.Size.Width - AppWindow.ClientSize.Width, dh = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        AppWindow.Resize(new SizeInt32(c.Width + dw, c.Height + dh));
    }
    protected RectInt32 WorkArea => DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

    public virtual void SetView(FrameworkElement view)
    {
        View = view;
        Sizer.Content = view;
        view.SizeChanged += (_, e) => OnViewSize(e.NewSize.Width, e.NewSize.Height);
    }

    protected virtual void OnViewSize(double w, double h) { }

    public void ApplyLook(ElementTheme theme, int backdrop)
    {
        Root.RequestedTheme = theme;
        if (controller != null) { controller.RemoveAllSystemBackdropTargets(); controller.Dispose(); controller = null; }
        Root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        config = new SystemBackdropConfiguration
        {
            IsInputActive = true, // kept live when inactive: the resting look must not change on focus loss
            Theme = theme == ElementTheme.Dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light,
        };
        controller = backdrop switch
        {
            0 => new MicaController { Kind = MicaKind.Base },
            1 => new MicaController { Kind = MicaKind.BaseAlt },
            2 => new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base },
            _ => null,
        };
        if (controller == null) Root.Background = Shell.Res("SurfaceSolidBrush");
        else
        {
            controller.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            controller.SetSystemBackdropConfiguration(config);
        }
    }

    // Fade/slide the content (the backdrop itself can't be faded on a WinUI 3 window).
    public void AnimateContent(bool show, double ms, Action done = null)
    {
        var v = ElementCompositionPreview.GetElementVisual(Sizer);
        var c = v.Compositor;
        var ease = show ? Shell.EaseOut(c) : Shell.EaseIn(c);
        float slide = -(float)Shell.Num("WidgetSlidePx");
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        var o = c.CreateScalarKeyFrameAnimation();
        o.InsertKeyFrame(0, show ? 0 : 1); o.InsertKeyFrame(1, show ? 1 : 0, ease);
        o.Duration = TimeSpan.FromMilliseconds(ms);
        var t = c.CreateScalarKeyFrameAnimation();
        t.InsertKeyFrame(0, show ? slide : 0); t.InsertKeyFrame(1, show ? 0 : slide, ease);
        t.Duration = TimeSpan.FromMilliseconds(ms);
        v.StartAnimation("Opacity", o);
        v.StartAnimation("Translation.Y", t);
        batch.End();
        if (done != null) batch.Completed += (_, _) => done();
    }
}

public sealed class WidgetWindow : HostWindow
{
    public bool PinBottom = true;
    public bool Hidden;
    double target, from, current, animMs;
    long animStart;
    Action animDone;
    bool rendering, suppressSize;

    public WidgetWindow() : base(noActivate: false)
    {
        Native.PinBottom = () => PinBottom;
        Native.InstallWidget(Hwnd);
        Closed += (_, _) => Native.Uninstall();
    }

    public IWidgetView WidgetView => View as IWidgetView;

    public override void SetView(FrameworkElement view)
    {
        base.SetView(view);
        double s = Scale, w = view.Width;
        var wa = WorkArea;
        current = target = Math.Max(current, 120);
        AppWindow.Move(new PointInt32(wa.X + wa.Width - (int)((w + 28) * s), wa.Y + (int)(28 * s)));
        SetClient(new SizeInt32((int)Math.Ceiling(w * s), (int)(current * s)));
    }

    protected override void OnViewSize(double w, double h)
    {
        if (suppressSize || Hidden) return;
        AnimateHeight(h, Shell.Ms(h > current ? "WidgetGrowMs" : "WidgetShrinkMs"), null);
    }

    public void AnimateHeight(double to, double ms, Action done)
    {
        from = current; target = to; animMs = ms; animDone = done;
        animStart = Stopwatch.GetTimestamp();
        if (rendering) return;
        rendering = true;
        CompositionTarget.Rendering += Tick;
    }

    void Tick(object s, object e)
    {
        double p = Math.Min(1, Stopwatch.GetElapsedTime(animStart).TotalMilliseconds / animMs);
        double eased = 1 - Math.Pow(1 - p, 3);
        current = from + (target - from) * eased;
        double sc = Scale;
        SetClient(new SizeInt32((int)Math.Ceiling(View.Width * sc), (int)Math.Ceiling(current * sc)));
        if (p < 1) return;
        CompositionTarget.Rendering -= Tick;
        rendering = false;
        var d = animDone; animDone = null; d?.Invoke();
    }

    // Collapse: content fades, the window rolls up toward the Dock, then hides.
    public void RollUp(Action done)
    {
        suppressSize = true;
        AnimateContent(false, Shell.Ms("WidgetContentOutMs"));
        AnimateHeight(36, Shell.Ms("WidgetRollUpMs"), () => { AppWindow.Hide(); Hidden = true; suppressSize = false; done?.Invoke(); });
    }

    public void RollDown()
    {
        Hidden = false;
        current = 36;
        double sc = Scale;
        SetClient(new SizeInt32((int)Math.Ceiling(View.Width * sc), (int)(current * sc)));
        AppWindow.Show(false);
        AnimateContent(true, Shell.Ms("WidgetContentInMs"));
        AnimateHeight(View.ActualHeight, Shell.Ms("WidgetUnrollMs"), null);
    }

    public void Raise()
    {
        PinBottom = false;
        Native.Foreground(Hwnd);
    }

    public void Sink()
    {
        Native.Topmost(Hwnd, false);
        PinBottom = true;
        Native.Bottom(Hwnd);
    }
}

public sealed class DockWindow : HostWindow
{
    public DockWindow() : base(noActivate: true)
    {
        Root.PointerPressed += (_, _) => Shell.Expand((View as IDockView)?.OpensCapture ?? false);
    }

    public override void SetView(FrameworkElement view)
    {
        base.SetView(view);
        if (view.ActualWidth > 0) OnViewSize(view.ActualWidth, view.ActualHeight);
    }

    protected override void OnViewSize(double w, double h)
    {
        if (w <= 0 || h <= 0 || View is not IDockView dv) return;
        double s = Scale;
        var wa = WorkArea;
        int pw = (int)Math.Ceiling(w * s), ph = (int)Math.Ceiling(h * s);
        int x = dv.Spot == DockSpot.TopCenter ? wa.X + (wa.Width - pw) / 2 : wa.X + wa.Width - pw - (int)(12 * s);
        AppWindow.Move(new PointInt32(x, wa.Y + (int)(dv.TopOffset * s)));
        SetClient(new SizeInt32(pw, ph));
    }

    public void ShowDock()
    {
        AppWindow.Show(false);
        Native.Topmost(Hwnd, true, show: true);
        var v = ElementCompositionPreview.GetElementVisual(Sizer);
        v.CenterPoint = new Vector3((float)(View.ActualWidth / 2), 0, 0);
        var c = v.Compositor;
        var spring = c.CreateSpringVector3Animation();
        float from = (float)Shell.Num("DockSpringFrom");
        spring.InitialValue = new Vector3(from, from, 1);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = (float)Shell.Num("DockSpringDamping");
        spring.Period = TimeSpan.FromMilliseconds(Shell.Ms("DockSpringPeriodMs"));
        v.StartAnimation("Scale", spring);
        AnimateContent(true, Shell.Ms("DockInMs"));
    }

    public void HideDock(Action done) => AnimateContent(false, Shell.Ms("DockOutMs"), () => { AppWindow.Hide(); done?.Invoke(); });
}
