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
    public static readonly Prefs Prefs = new();
    public static WidgetWindow Widget;
    public static DockWindow Dock;
    public static ProtoBar Bar;

    public static readonly string[] ThemeNames = { "System", "Light", "Dark" };
    public static readonly string[] BackdropNames = { "Mica", "Mica Alt", "Acrylic", "Solid" };
    public static bool Docked, Raised;
    static bool sessionFromDock, applyQueued;
    static DispatcherQueue dq;
    static readonly UISettings ui = new();
    static readonly AccessibilitySettings a11y = new();
    static readonly System.Collections.Generic.Dictionary<object, object> darkOriginals = new();
    static bool previewApplied;

    public static void Start()
    {
        dq = DispatcherQueue.GetForCurrentThread();
        Store.Seed(9);
        // Start-up overrides, handy for screenshots: LOOK_THEME 0-2, LOOK_BACKDROP 0-3, LOOK_CONTRAST=1,
        // LOOK_DOCKED=1, LOOK_BUSY=1, LOOK_SETTINGS=1, LOOK_NOBAR=1, LOOK_UPDATE=1 (an update is available).
        Prefs.ContrastPreview = Env("LOOK_CONTRAST") == 1;
        Prefs.Theme = Env("LOOK_THEME");
        Prefs.Backdrop = Env("LOOK_BACKDROP");
        Prefs.ShowSettings = Env("LOOK_SETTINGS") == 1;
        if (Env("LOOK_UPDATE") == 1) { Prefs.Update = Prefs.Upd.Available; Prefs.CheckedWhen = "Checked just now"; Store.Attention = true; }
        // Round 4: LOOK_VARIANT 0-2 (A/B/C), LOOK_SCENE 0-7 (see Scenes).
        Prefs.Variant = Env("LOOK_VARIANT");
        Scene(Env("LOOK_SCENE"), apply: false);
        // LOOK_SIGNIN 0-3: Idle, Waiting, Failed, NotEligible (for screenshots of the sign-in states).
        Prefs.SignInCause = Causes[0];
        Prefs.SignIn = (SignInState)Env("LOOK_SIGNIN");
        Prefs.ShowSettings = Env("LOOK_SETTINGS") == 1; // Scene() closes Settings, so re-apply the override
        // Fires for dark/light, accent and contrast-theme changes alike (HighContrastChanged is unavailable unpackaged).
        ui.ColorValuesChanged += (_, _) => dq.TryEnqueue(Apply);
        Prefs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Prefs.Theme) or nameof(Prefs.Backdrop)) QueueApply();
        };

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
        if (Env("LOOK_BUSY") == 1) { Store.Capture("Email Bilal the contract, he's waiting today"); Store.Attention = true; }
        if (Env("LOOK_DOCKED") == 1) After(400, Collapse);
        if (Env("LOOK_DICTATION") == 1) After(800, WaitForDictation); // Round 4: screenshot "Waiting for dictation…"

        if (Env("LOOK_NOBAR") == 1) return;
        Bar = new ProtoBar();
        Bar.Activate();
        Bar.Refresh();
    }

    static int Env(string name) => int.TryParse(Environment.GetEnvironmentVariable(name), out int v) ? v : 0;

    // ---- contrast ----
    public static bool SystemContrast => a11y.HighContrast;
    public static bool Contrast => SystemContrast || Prefs.ContrastPreview;

    // Prototype only: lay the ContrastPreview tokens over the Dark ones (and restore them when it's switched off).
    static void ApplyContrastPreview()
    {
        var tokens = Application.Current.Resources.MergedDictionaries[1];
        var dark = (ResourceDictionary)tokens.ThemeDictionaries["Dark"];
        var preview = (ResourceDictionary)tokens["ContrastPreview"];
        bool on = Prefs.ContrastPreview && !SystemContrast;
        if (on == previewApplied) return;
        foreach (var kv in preview)
        {
            if (on)
            {
                darkOriginals[kv.Key] = dark.TryGetValue(kv.Key, out var orig) ? orig : null;
                dark[kv.Key] = kv.Value;
            }
            else if (darkOriginals.TryGetValue(kv.Key, out var orig))
            {
                if (orig != null) dark[kv.Key] = orig; else dark.Remove(kv.Key);
            }
        }
        previewApplied = on;
    }

    // ---- motion tokens (Motion.xaml); Slow-mo stretches every duration 5x ----
    public static double Num(string key) => (double)Application.Current.Resources.MergedDictionaries[2][key];
    public static double Ms(string key) => Num(key) * (Prefs.SlowMo ? 5 : 1);

    public static Microsoft.UI.Composition.CompositionEasingFunction EaseOut(Microsoft.UI.Composition.Compositor c) =>
        c.CreateCubicBezierEasingFunction(new((float)Num("EaseOutX1"), (float)Num("EaseOutY1")), new((float)Num("EaseOutX2"), (float)Num("EaseOutY2")));

    public static Microsoft.UI.Composition.CompositionEasingFunction EaseIn(Microsoft.UI.Composition.Compositor c) =>
        c.CreateCubicBezierEasingFunction(new((float)Num("EaseInX1"), (float)Num("EaseInY1")), new((float)Num("EaseInX2"), (float)Num("EaseInY2")));

    public static ElementTheme Theme => Prefs.ContrastPreview && !SystemContrast ? ElementTheme.Dark : Prefs.Theme switch
    {
        1 => ElementTheme.Light,
        2 => ElementTheme.Dark,
        _ => ui.GetColorValue(UIColorType.Background).R < 128 ? ElementTheme.Dark : ElementTheme.Light,
    };

    public static Brush Res(string key)
    {
        var themes = Application.Current.Resources.MergedDictionaries[1].ThemeDictionaries;
        var dict = (ResourceDictionary)themes[SystemContrast ? "HighContrast" : Theme == ElementTheme.Dark ? "Dark" : "Light"];
        return (Brush)dict[key];
    }

    // A change made from inside a view (e.g. the Settings theme box) rebuilds the views, so defer it a tick.
    static void QueueApply()
    {
        if (applyQueued) return;
        applyQueued = true;
        dq.TryEnqueue(() => { applyQueued = false; Apply(); });
    }

    // Views are rebuilt on every design or theme change so token brushes and icon sets pick up the change.
    public static void Apply()
    {
        ApplyContrastPreview();
        int backdrop = Contrast ? 3 : Prefs.Backdrop; // contrast themes: no backdrop, system window colour
        Widget.ApplyLook(Theme, backdrop);
        Dock.ApplyLook(Theme, backdrop);
        Widget.SetView(new WidgetA());
        Dock.SetView(new DockCapture());
        Bar?.Refresh();
    }

    // Timers are held until they tick: nothing else roots a DispatcherQueueTimer, and a collected one never fires.
    static readonly System.Collections.Generic.HashSet<DispatcherQueueTimer> timers = new();

    public static void After(int ms, Action a)
    {
        var t = dq.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(ms);
        t.IsRepeating = false;
        t.Tick += (_, _) => { timers.Remove(t); a(); };
        timers.Add(t);
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

    public static void Expand(bool focusCapture = false)
    {
        if (!Docked) return;
        Docked = false;
        sessionFromDock = false;
        Dock.HideDock(null);
        Widget.RollDown();
        Raised = true;
        Widget.Raise();
        if (focusCapture) { Prefs.ShowSettings = false; Store.ShowDone = false; FocusCapture(); }
        Bar?.Refresh();
    }

    // ---- hotkey session ----
    static void OnTap()
    {
        if (Prefs.Recording) return; // the Settings hotkey recorder is listening
        if (Prefs.WaitingDictation) return;
        if (Raised && Native.IsForeground(Widget.Hwnd))
        {
            // Round 4: an empty box waits up to DictationWaitMs for Wispr Flow's paste, then commits and docks.
            if (string.IsNullOrWhiteSpace(Widget.WidgetView?.CaptureBox?.Text) && !Prefs.ReadOnly) { WaitForDictation(); return; }
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
        Prefs.ShowSettings = false;
        Store.ShowDone = false;
        Raised = true;
        Widget.Raise();
        FocusCapture();
        Bar?.Refresh();
    }

    public static void FocusCapture()
    {
        var box = Widget.WidgetView?.CaptureBox;
        if (box == null) return;
        box.DispatcherQueue.TryEnqueue(() => { box.Focus(FocusState.Programmatic); box.SelectionStart = box.Text.Length; });
    }

    public static void CommitCapture()
    {
        var box = Widget.WidgetView?.CaptureBox;
        if (box == null || string.IsNullOrWhiteSpace(box.Text)) return;
        if (Prefs.ReadOnly) return; // saved by a newer version: the draft stays in the box
        Store.Capture(box.Text);
        box.Text = "";
    }

    // ---- Round 4: "Waiting for dictation…" ----
    static int dictationToken;

    public static void WaitForDictation()
    {
        var box = Widget.WidgetView?.CaptureBox;
        if (box == null) return;
        int token = ++dictationToken;
        Prefs.WaitingDictation = true;
        void Done(bool commit)
        {
            if (token != dictationToken || !Prefs.WaitingDictation) return;
            box.TextChanged -= Changed;
            Prefs.WaitingDictation = false;
            if (commit) CommitCapture();
            EndSession();
        }
        void Changed(object s, TextChangedEventArgs e) { if (!string.IsNullOrWhiteSpace(box.Text)) Done(true); }
        box.TextChanged += Changed;
        After((int)Ms("DictationWaitMs"), () => Done(false));
    }

    // Control bar: raise the Widget as the hotkey would, wait, and (optionally) paste a dictation partway through.
    public static void SimulateDictation()
    {
        if (!Raised) OnTap();
        Widget.WidgetView.CaptureBox.Text = "";
        After(150, () =>
        {
            WaitForDictation();
            if (Prefs.DictationArrives)
                After((int)(Ms("DictationWaitMs") * 0.4), () =>
                {
                    if (Prefs.WaitingDictation) Widget.WidgetView.CaptureBox.Text = "Book a table for Friday dinner, somewhere near the office";
                });
        });
    }

    // ---- Round 4: sign-in (one flow; the welcome card, the signed-out banner and Settings share its state) ----
    static int signInToken, causeIndex;
    static readonly string[] Causes =
    {
        "Timed out after 5 minutes.",
        "Access was denied in the browser.",
        "The browser returned an unexpected state.",
        "Couldn't exchange the sign-in code for tokens.",
    };

    public static void StartSignIn()
    {
        int token = ++signInToken;
        Prefs.SignIn = SignInState.Waiting;
        After(2500 * (Prefs.SlowMo ? 5 : 1), () =>
        {
            if (token != signInToken || !Prefs.SignInWaiting) return;
            switch (Prefs.SignInOutcome)
            {
                case 0:
                    Prefs.SignIn = SignInState.Idle;
                    Prefs.SignedIn = true;
                    Prefs.Welcomed = true;
                    Store.RunWaiting();
                    if (Docked) Expand(); else { Raised = true; Widget.Raise(); } // the Widget comes to the front
                    break;
                case 1:
                    Prefs.SignInCause = Causes[causeIndex++ % Causes.Length];
                    Prefs.SignIn = SignInState.Failed;
                    break;
                default:
                    Prefs.SignIn = SignInState.NotEligible;
                    break;
            }
        });
    }

    public static void CancelSignIn()
    {
        signInToken++;
        Prefs.SignInCause = "You cancelled sign-in.";
        Prefs.SignIn = SignInState.Failed;
    }

    // ---- Round 4: scenes for the control bar ----
    public static readonly string[] Scenes =
        { "Everyday", "First run", "Empty, signed in", "Newer version", "Signed out", "Recovered", "Started empty", "Capture states" };
    public static int SceneIndex;

    public static void Scene(int i, bool apply = true)
    {
        SceneIndex = i;
        var P = Prefs;
        signInToken++;
        P.SignIn = SignInState.Idle;
        P.ReadOnly = false; P.Recovered = false; P.StartedEmpty = false; P.Offline = false;
        P.ShowSettings = false; Store.ShowDone = false;
        P.HoldPending = false;
        P.SignedIn = i is not (1 or 4);
        P.Welcomed = i != 1;
        Store.Seed(i is 1 or 2 or 6 ? 0 : 9);
        Store.ReleaseHeld();
        Store.Attention = false;
        if (i == 3) P.ReadOnly = true;
        if (i == 5) P.Recovered = true;
        if (i == 6) P.StartedEmpty = true;
        if (i == 7) Store.SeedCaptureStates();
        if (apply) Apply();
    }

    public static void Note(string text) => Bar?.Note(text);
    public static void EndSession()
    {
        Raised = false;
        if (sessionFromDock) { sessionFromDock = false; Collapse(); }
        else Widget.Sink();
        Bar?.Refresh();
    }
}
