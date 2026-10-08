using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskWidget.Core;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT;

namespace TaskWidget;

public sealed partial class MainWindow : Window
{
    const double MarginDip = 28;
    MicaController? mica;
    double current;
    double from;
    double target;
    double animMs;
    long animStart;
    bool rendering;
    bool placed;

    public MainWindow(AppModel model)
    {
        Model = model;
        InitializeComponent();
        SettingsHost.Children.Add(new SettingsPanel(model));
        Title = "Task Widget";

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        Capture.PreviewKeyDown += Capture_PreviewKeyDown;
        Root.SizeChanged += (_, _) => OnRootSize();
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= Tick;
            mica?.Dispose();
            Model.Dispose();
        };
    }

    public AppModel Model { get; }

    void Settings_Click(object sender, RoutedEventArgs e) => Model.ToggleSettings();

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

    void OnRootSize()
    {
        if (Root.XamlRoot is null || Root.ActualHeight <= 0)
            return;

        if (!placed)
        {
            placed = true;
            ApplyMica();
            current = Root.ActualHeight;
            Place(current);
            Capture.Focus(FocusState.Programmatic);
            return;
        }

        var next = Root.ActualHeight;
        if (Math.Abs(next - target) < 0.5 && rendering)
            return;
        if (Math.Abs(next - current) < 0.5)
            return;

        from = current;
        target = next;
        var grow = (double)Application.Current.Resources["WidgetGrowMs"];
        var shrink = (double)Application.Current.Resources["WidgetShrinkMs"];
        animMs = next > current ? grow : shrink;
        animStart = System.Diagnostics.Stopwatch.GetTimestamp();
        if (rendering)
            return;
        rendering = true;
        CompositionTarget.Rendering += Tick;
    }

    void Tick(object? sender, object e)
    {
        var p = Math.Min(1, System.Diagnostics.Stopwatch.GetElapsedTime(animStart).TotalMilliseconds / animMs);
        var eased = 1 - Math.Pow(1 - p, 3);
        current = from + (target - from) * eased;
        Place(current);
        if (p < 1)
            return;
        CompositionTarget.Rendering -= Tick;
        rendering = false;
    }

    void Place(double heightDip)
    {
        var scale = Root.XamlRoot.RasterizationScale;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = (int)Math.Ceiling(Root.Width * scale);
        int height = (int)Math.Ceiling(heightDip * scale);
        int frameW = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        int frameH = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        int x = area.X + area.Width - width - (int)Math.Ceiling(MarginDip * scale);
        int y = area.Y + (int)Math.Ceiling(MarginDip * scale);
        AppWindow.MoveAndResize(new RectInt32(x, y, width + frameW, height + frameH));
    }

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
