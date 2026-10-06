// PROTOTYPE: wires the Widget, the Dock, the prototype control bar and the hotkey session together.
//
// Hotkey session (from the spike and "Capture flow and failure handling"): tap Ctrl+Shift raises the full Widget with the
// Capture box focused; tapping again commits the Capture box text and ends the session; Esc ends it without committing;
// clicking another window ends it. Ending a session returns to whatever the Widget was before: sunk to the bottom of
// the z-order on the desktop, or collapsed into the Dock.
using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Look;

public static class Shell
{
    public static readonly Store Store = new();
    public static WidgetWindow Widget;
    public static DockWindow Dock;
    public static ProtoBar Bar;

    public static readonly string[] VariantNames = { "A · Fluent list", "B · Cards", "C · Next up" };
    public static readonly string[] ThemeNames = { "System", "Light", "Dark" };
    public static readonly string[] BackdropNames = { "Mica", "Mica Alt", "Acrylic", "Solid" };
    public static int Variant, ThemeMode, Backdrop;
    public static bool Docked, Raised;
    static bool sessionFromDock;
    static DispatcherQueue dq;
    static readonly UISettings ui = new();

    public static void Start()
    {
        dq = DispatcherQueue.GetForCurrentThread();
        Store.Seed(9);
        // Start-up overrides, handy for screenshots: LOOK_VARIANT 0-2, LOOK_THEME 0-2, LOOK_BACKDROP 0-3.
        int.TryParse(Environment.GetEnvironmentVariable("LOOK_VARIANT"), out Variant);
        int.TryParse(Environment.GetEnvironmentVariable("LOOK_THEME"), out ThemeMode);
        int.TryParse(Environment.GetEnvironmentVariable("LOOK_BACKDROP"), out Backdrop);
        ui.ColorValuesChanged += (_, _) => dq.TryEnqueue(() => { if (ThemeMode == 0) Apply(); });

        Widget = new WidgetWindow();
        Dock = new DockWindow();
        Widget.Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && Raised) EndSession();
        };
        Native.OnTap = OnTap;
        Widget.Root.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && Raised) { EndSession(); e.Handled = true; }
        };

        Apply();
        Widget.Activate();
        Dock.Activate();
        Dock.AppWindow.Hide();
        After(50, () => Widget.Sink());
        if (Environment.GetEnvironmentVariable("LOOK_BUSY") == "1") { Store.Capture("Email Bilal the contract, he's waiting today"); Store.Attention = true; }
        if (Environment.GetEnvironmentVariable("LOOK_DOCKED") == "1") After(400, Collapse);

        Bar = new ProtoBar();
        Bar.Activate();
        Bar.Refresh();
    }

    public static ElementTheme Theme => ThemeMode switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ui.GetColorValue(UIColorType.Background).R < 128 ? ElementTheme.Dark : ElementTheme.Light,
    };

    public static Brush Res(string key)
    {
        var themes = Application.Current.Resources.MergedDictionaries[1].ThemeDictionaries;
        var dict = (ResourceDictionary)themes[Theme == ElementTheme.Dark ? "Dark" : "Light"];
        return (Brush)dict[key];
    }

    // Views are rebuilt on every variant or theme change so x:Bind'd token brushes pick up the new theme.
    public static void Apply()
    {
        Widget.ApplyLook(Theme, Backdrop);
        Dock.ApplyLook(Theme, Backdrop);
        Widget.SetView(Variant switch { 0 => new WidgetA(), 1 => new WidgetB(), _ => new WidgetC() });
        Dock.SetView(Variant switch { 0 => new DockA(), 1 => new DockB(), _ => new DockC() });
        Bar?.Refresh();
    }

    public static void After(int ms, Action a)
    {
        var t = dq.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(ms);
        t.IsRepeating = false;
        t.Tick += (_, _) => a();
        t.Start();
    }

    // ---- Widget <-> Dock ----
    public static void Collapse()
    {
        Docked = true;
        Raised = false;
        Widget.RollUp(() => Dock.ShowDock());
        Bar?.Refresh();
    }

    public static void Expand()
    {
        if (!Docked) return;
        Docked = false;
        sessionFromDock = false;
        Dock.HideDock(null);
        Widget.RollDown();
        Raised = true;
        Widget.Raise();
        Bar?.Refresh();
    }

    // ---- hotkey session ----
    static void OnTap()
    {
        if (Raised && Native.IsForeground(Widget.Hwnd))
        {
            CommitCapture();
            EndSession();
            return;
        }
        sessionFromDock = Docked;
        if (Docked)
        {
            Dock.HideDock(null);
            Widget.RollDown();
        }
        Raised = true;
        Widget.Raise();
        FocusCapture();
        Bar?.Refresh();
    }

    public static void FocusCapture()
    {
        var box = Widget.WidgetView?.CaptureBox;
        box?.Focus(FocusState.Programmatic);
        if (box != null) box.SelectionStart = box.Text.Length;
    }

    public static void CommitCapture()
    {
        var box = Widget.WidgetView?.CaptureBox;
        if (box == null || string.IsNullOrWhiteSpace(box.Text)) return;
        Store.Capture(box.Text);
        box.Text = "";
    }

    public static void EndSession()
    {
        Raised = false;
        if (sessionFromDock) { sessionFromDock = false; Collapse(); }
        else Widget.Sink();
        Bar?.Refresh();
    }
}
