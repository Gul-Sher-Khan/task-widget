using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskWidget.Core;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT;

namespace TaskWidget;

public sealed partial class MainWindow : Window
{
    const double MarginDip = 28;
    const double DockWidthDip = 380;
    const double DockHeightDip = 44;
    const double DockTopDip = 8;

    MicaController? mica;
    double current;
    double from;
    double target;
    double widgetHeight;
    double landHeight;
    double animMs;
    long animStart;
    bool rendering;
    bool placed;
    bool finishing;
    int dragFrom = -1;
    bool driving;
    bool slide;
    RectInt32 fromRect;
    RectInt32 toRect;
    Action? animDone;

    public MainWindow(AppModel model)
    {
        Model = model;
        InitializeComponent();
        SettingsHost.Children.Add(new SettingsPanel(model));
        BannerHost.Children.Add(new BannerView(model));
        Title = "Task Widget";

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        if (Model.Docked)
        {
            Root.Visibility = Visibility.Collapsed;
            DockRoot.Visibility = Visibility.Visible;
        }

        Capture.PreviewKeyDown += Capture_PreviewKeyDown;
        List.PreviewKeyDown += List_PreviewKeyDown;
        List.RightTapped += List_RightTapped;
        List.DragItemsStarting += List_DragItemsStarting;
        List.DragItemsCompleted += List_DragItemsCompleted;
        Root.KeyDown += Root_KeyDown;
        ApplyUndoMotion();
        Host.SizeChanged += (_, _) => OnHostSize();
        Model.PropertyChanged += OnModelPropertyChanged;
        Closed += (_, _) =>
        {
            Model.PropertyChanged -= OnModelPropertyChanged;
            CompositionTarget.Rendering -= Tick;
            mica?.Dispose();
            Model.Dispose();
        };
    }

    public AppModel Model { get; }

void Done_Click(object sender, RoutedEventArgs e) => Model.ToggleDoneView();

    void Check_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskRow task)
            Model.Tick(task);
    }

    void Undo_Click(object sender, RoutedEventArgs e) => Model.Undo();

    void List_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && List.SelectedIndex >= 0)
        {
            if (e.Key == VirtualKey.Up)
                Model.MoveTo(List.SelectedIndex, List.SelectedIndex - 1);
            else if (e.Key == VirtualKey.Down)
                Model.MoveTo(List.SelectedIndex, List.SelectedIndex + 1);
            else
                ctrl = false;

            if (ctrl)
            {
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case VirtualKey.Up:
                Model.SelectUp();
                break;
            case VirtualKey.Down:
                Model.SelectDown();
                break;
            case VirtualKey.Space:
                Model.TickSelected();
                break;
            case VirtualKey.Delete:
                Model.DeleteSelected();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    void List_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not TaskRow task)
            return;

        var item = new MenuFlyoutItem
        {
            Text = "Delete",
            Icon = new FontIcon { Glyph = "\uE74D" },
        };
        item.Click += (_, _) => Model.Delete(task);
        var menu = new MenuFlyout();
        menu.Items.Add(item);
        menu.ShowAt(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
        e.Handled = true;
    }

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Root.XamlRoot is null || FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox)
            return;

        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (!ctrl)
            return;

        if (e.Key == VirtualKey.Z)
            Model.Undo();
        else if (e.Key == VirtualKey.Y)
            Model.Redo();
        else
            return;

        e.Handled = true;
    }

    void Resort_Click(object sender, RoutedEventArgs e) => Model.ReSort();

    void ApplyUndoMotion()
    {
        var visual = ElementCompositionPreview.GetElementVisual(UndoPill);
        var compositor = visual.Compositor;
        var show = compositor.CreateAnimationGroup();
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(Ms("UndoFadeInMs"));
        var rise = compositor.CreateScalarKeyFrameAnimation();
        rise.Target = "Translation.Y";
        rise.InsertKeyFrame(0, (float)Ms("UndoRisePx"));
        rise.InsertKeyFrame(1, 0, Ease(compositor, true));
        rise.Duration = TimeSpan.FromMilliseconds(Ms("UndoInMs"));
        show.Add(fade);
        show.Add(rise);
        var hide = compositor.CreateScalarKeyFrameAnimation();
        hide.Target = "Opacity";
        hide.InsertKeyFrame(1, 0);
        hide.Duration = TimeSpan.FromMilliseconds(Ms("UndoOutMs"));
        ElementCompositionPreview.SetIsTranslationEnabled(UndoPill, true);
        ElementCompositionPreview.SetImplicitShowAnimation(UndoPill, show);
        ElementCompositionPreview.SetImplicitHideAnimation(UndoPill, hide);
    }

    void List_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        dragFrom = e.Items.Count == 1 && e.Items[0] is TaskRow row ? Model.Tasks.IndexOf(row) : -1;
        if (dragFrom < 0)
            e.Cancel = true;
    }

    void List_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        if (dragFrom < 0 || e.Items.Count != 1 || e.Items[0] is not TaskRow row)
        {
            dragFrom = -1;
            return;
        }

        var to = Model.Tasks.IndexOf(row);
        var from = dragFrom;
        dragFrom = -1;
        Model.AcceptReorder(from, to);
    }

    void Collapse_Click(object sender, RoutedEventArgs e)
    {
        if (driving)
            return;
        Model.Dock();
    }

    void Dock_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (driving || !Model.Docked)
            return;
        Model.Expand();
        e.Handled = true;
    }

    void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.RestartRequested) && Model.RestartRequested)
        {
            if (DispatcherQueue.HasThreadAccess)
                FinishUpdate();
            else
                DispatcherQueue.TryEnqueue(FinishUpdate);
            return;
        }

        if (e.PropertyName != nameof(AppModel.Docked) || !placed)
            return;

        if (Model.Docked)
            PlayToDock();
        else
            PlayToWidget();
    }

    void Settings_Click(object sender, RoutedEventArgs e) => Model.ToggleSettings();

    // The installer replaces this process. A separate command waits, installs silently, then starts Task Widget again.
    void FinishUpdate()
    {
        if (finishing || Model.InstallerPath is not string installer || Environment.ProcessPath is not string app)
            return;

        finishing = true;
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 3 >nul & \"" + installer + "\" /VERYSILENT /NORESTART & start \"\" \"" + app + "\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        Close();
    }

    void Capture_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (shift.HasFlag(CoreVirtualKeyStates.Down))
            return;

        Model.UpdateDraft(Capture.Text);
        Model.CommitCapture();
        e.Handled = true;
    }

    void OnHostSize()
    {
        if (Host.XamlRoot is null)
            return;
        if (!Model.Docked && Root.ActualHeight <= 0)
            return;

        if (!placed)
        {
            placed = true;
            ApplyMica();
            if (Model.Docked)
            {
                Root.Visibility = Visibility.Collapsed;
                DockRoot.Visibility = Visibility.Visible;
                current = DockHeightDip;
                Place(DockClient());
                return;
            }

            current = Root.ActualHeight;
            widgetHeight = current;
            Place(WidgetClient(current));
            Capture.Focus(FocusState.Programmatic);
            return;
        }

        if (driving || Model.Docked)
            return;

        var next = Root.ActualHeight;
        if (Math.Abs(next - target) < 0.5 && rendering && !slide)
            return;
        if (Math.Abs(next - current) < 0.5)
            return;

        from = current;
        target = next;
        slide = false;
        animDone = null;
        animMs = next > current ? Ms("WidgetGrowMs") : Ms("WidgetShrinkMs");
        animStart = System.Diagnostics.Stopwatch.GetTimestamp();
        EnsureRendering();
    }

    // The backdrop cannot fade, so the content and the window height animate.
    void PlayToDock()
    {
        if (Host.XamlRoot is null)
            return;

        driving = true;
        widgetHeight = Root.ActualHeight > 0 ? Root.ActualHeight : current;
        AnimateContent(Root, show: false, Ms("WidgetContentOutMs"));
        AnimateRect(WidgetClient(widgetHeight), DockClient(), Ms("WidgetRollUpMs"), DockHeightDip, FinishDock);
    }

    void FinishDock()
    {
        var visual = ElementCompositionPreview.GetElementVisual(DockRoot);
        ElementCompositionPreview.SetIsTranslationEnabled(DockRoot, true);
        visual.Opacity = 0;
        Root.Visibility = Visibility.Collapsed;
        DockRoot.Visibility = Visibility.Visible;
        AnimateContent(DockRoot, show: true, Ms("DockInMs"));
        SpringIn(DockRoot);
        driving = false;
    }

    void PlayToWidget()
    {
        if (Host.XamlRoot is null)
            return;

        driving = true;
        Activate();
        AnimateContent(DockRoot, show: false, Ms("DockOutMs"));
        Root.Visibility = Visibility.Visible;
        Root.Measure(new Size(Root.Width, double.PositiveInfinity));
        Root.UpdateLayout();
        var height = Math.Max(Root.ActualHeight, Root.DesiredSize.Height);
        if (height <= 0)
            height = widgetHeight;
        AnimateContent(Root, show: true, Ms("WidgetContentInMs"));
        AnimateRect(DockClient(), WidgetClient(height), Ms("WidgetUnrollMs"), height, () =>
        {
            DockRoot.Visibility = Visibility.Collapsed;
            driving = false;
        });
        Capture.Focus(FocusState.Programmatic);
    }

    void Tick(object? sender, object e)
    {
        var p = Math.Min(1, System.Diagnostics.Stopwatch.GetElapsedTime(animStart).TotalMilliseconds / animMs);
        var eased = 1 - Math.Pow(1 - p, 3);
        if (slide)
            Place(p >= 1 ? toRect : Lerp(fromRect, toRect, eased));
        else
        {
            current = from + (target - from) * eased;
            Place(WidgetClient(current));
        }

        if (p < 1)
            return;

        CompositionTarget.Rendering -= Tick;
        rendering = false;
        if (slide)
        {
            current = landHeight;
            target = landHeight;
        }

        var done = animDone;
        animDone = null;
        done?.Invoke();
    }

    void AnimateRect(RectInt32 fromClient, RectInt32 toClient, double ms, double endHeightDip, Action done)
    {
        slide = true;
        fromRect = fromClient;
        toRect = toClient;
        landHeight = endHeightDip;
        animMs = ms;
        animDone = done;
        animStart = System.Diagnostics.Stopwatch.GetTimestamp();
        EnsureRendering();
    }

    void EnsureRendering()
    {
        if (rendering)
            return;
        rendering = true;
        CompositionTarget.Rendering += Tick;
    }

    void AnimateContent(UIElement element, bool show, double ms)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var compositor = visual.Compositor;
        var ease = Ease(compositor, show);
        float slidePx = -(float)Ms("WidgetSlidePx");
        var opacity = compositor.CreateScalarKeyFrameAnimation();
        opacity.InsertKeyFrame(0, show ? 0 : 1);
        opacity.InsertKeyFrame(1, show ? 1 : 0, ease);
        opacity.Duration = TimeSpan.FromMilliseconds(ms);
        var translation = compositor.CreateScalarKeyFrameAnimation();
        translation.InsertKeyFrame(0, show ? slidePx : 0);
        translation.InsertKeyFrame(1, show ? 0 : slidePx, ease);
        translation.Duration = TimeSpan.FromMilliseconds(ms);
        visual.StartAnimation("Opacity", opacity);
        visual.StartAnimation("Translation.Y", translation);
    }

    void SpringIn(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)(DockWidthDip / 2), 0, 0);
        var compositor = visual.Compositor;
        var spring = compositor.CreateSpringVector3Animation();
        float fromScale = (float)Ms("DockSpringFrom");
        spring.InitialValue = new Vector3(fromScale, fromScale, 1);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = (float)Ms("DockSpringDamping");
        spring.Period = TimeSpan.FromMilliseconds(Ms("DockSpringPeriodMs"));
        visual.StartAnimation("Scale", spring);
    }

    static CompositionEasingFunction Ease(Compositor compositor, bool arrive)
    {
        var resources = Application.Current.Resources;
        string prefix = arrive ? "EaseOut" : "EaseIn";
        float x1 = (float)(double)resources[$"{prefix}X1"];
        float y1 = (float)(double)resources[$"{prefix}Y1"];
        float x2 = (float)(double)resources[$"{prefix}X2"];
        float y2 = (float)(double)resources[$"{prefix}Y2"];
        return compositor.CreateCubicBezierEasingFunction(new Vector2(x1, y1), new Vector2(x2, y2));
    }

    static double Ms(string key) => (double)Application.Current.Resources[key];

    double Scale => Host.XamlRoot.RasterizationScale;

    RectInt32 WidgetClient(double heightDip)
    {
        var scale = Scale;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = (int)Math.Ceiling(Root.Width * scale);
        int height = (int)Math.Ceiling(heightDip * scale);
        int x = area.X + area.Width - width - (int)Math.Ceiling(MarginDip * scale);
        int y = area.Y + (int)Math.Ceiling(MarginDip * scale);
        return new RectInt32(x, y, width, height);
    }

    RectInt32 DockClient()
    {
        var scale = Scale;
        var area = DisplayArea.Primary.WorkArea;
        int width = (int)Math.Ceiling(DockWidthDip * scale);
        int height = (int)Math.Ceiling(DockHeightDip * scale);
        int x = area.X + (area.Width - width) / 2;
        int y = area.Y + (int)Math.Ceiling(DockTopDip * scale);
        return new RectInt32(x, y, width, height);
    }

    void Place(RectInt32 client)
    {
        int frameW = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        int frameH = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        AppWindow.MoveAndResize(new RectInt32(client.X, client.Y, client.Width + frameW, client.Height + frameH));
    }

    static RectInt32 Lerp(RectInt32 a, RectInt32 b, double t) => new(
        (int)Math.Round(a.X + (b.X - a.X) * t),
        (int)Math.Round(a.Y + (b.Y - a.Y) * t),
        (int)Math.Round(a.Width + (b.Width - a.Width) * t),
        (int)Math.Round(a.Height + (b.Height - a.Height) * t));

    void ApplyMica()
    {
        var config = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? SystemBackdropTheme.Dark
                : SystemBackdropTheme.Light,
        };
        try
        {
            mica = new MicaController { Kind = MicaKind.Base };
            mica.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
            mica.SetSystemBackdropConfiguration(config);
            Host.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        catch (COMException)
        {
            mica = null;
        }
    }
}
