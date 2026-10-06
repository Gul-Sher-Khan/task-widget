// PROTOTYPE: throwaway stack spike for "Stack memory and animation spike". Not production code.
// Docked = small strip pinned to the bottom of the z-order. Tapping Ctrl+Shift (or Ctrl+Alt+T) expands, raises and focuses
// the Capture box; Esc docks again. F2 cycles the backdrop. Env: SPIKE_TASKS (default 30), SPIKE_BACKDROP (0-4).
// On the first rendered frame with the Capture box focused it appends "<qpc-now> <qpc-at-WM_HOTKEY>" to
// %TEMP%\stack-spike-latency.log so the harness can measure hotkey-to-focused latency.
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT;
using WinRT.Interop;

namespace Spike;

public sealed partial class TaskItem
{
    public string Title { get; set; }
    public string Effort { get; set; }
    public Brush Dot { get; set; }
}

public sealed partial class MainWindow : Window
{
    const int W = 380, DockH = 72, ExpH = 620;
    static readonly string[] Titles =
    {
        "Reply to Sana about the Q3 budget", "Book dentist appointment", "Fix flaky login test",
        "Draft onboarding email for new hire", "Renew domain before it lapses", "Review Ali's PR on caching",
        "Buy groceries for the weekend", "Call the bank about the card", "Write release notes for 0.4",
        "Clean up the Downloads folder", "Update the team roadmap slide", "Send invoice to the client",
        "Pick up laundry", "Read the WebView2 perf article", "Plan Friday's demo",
    };
    static readonly string[] Efforts = { "Quick", "Short", "Long" };
    static readonly Brush[] Dots =
    {
        new SolidColorBrush(ColorHelper.FromArgb(255, 229, 72, 77)),
        new SolidColorBrush(ColorHelper.FromArgb(255, 245, 165, 36)),
        new SolidColorBrush(ColorHelper.FromArgb(255, 139, 141, 152)),
    };
    static readonly string[] BackdropNames = { "Mica", "Mica Alt", "Mica kept active", "Desktop Acrylic", "Solid" };
    static readonly string LogPath = Path.Combine(Path.GetTempPath(), "stack-spike-latency.log");
    static MainWindow instance;

    readonly ObservableCollection<TaskItem> tasks = new();
    readonly nint hwnd;
    bool expanded, pinBottom, rendering, pendingMark;
    long hotkeyQpc;
    int backdrop;
    MicaController micaController;

    // window height animation
    double animFrom, animTo, animMs;
    long animStart;
    int animFrames;
    double lastFps, lastAnimMs;
    Action animDone;

    public MainWindow()
    {
        InitializeComponent();
        instance = this;
        hwnd = WindowNative.GetWindowHandle(this);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, GetWindowLongPtrW(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW);

        double s = Scale;
        var wa = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(
            wa.X + wa.Width - (int)((W + 16) * s), wa.Y + (int)(16 * s), (int)(W * s), (int)(DockH * s)));

        int n = int.TryParse(Environment.GetEnvironmentVariable("SPIKE_TASKS"), out var t) ? t : 30;
        for (int i = 0; i < n; i++) tasks.Add(MakeTask(i));
        List.ItemsSource = tasks;

        int.TryParse(Environment.GetEnvironmentVariable("SPIKE_BACKDROP"), out backdrop);
        SetBackdrop(backdrop);

        Root.PreviewKeyDown += OnPreviewKeyDown;
        Capture.KeyDown += OnCaptureKeyDown;
        ElementCompositionPreview.SetIsTranslationEnabled(List, true);

        unsafe { SetWindowSubclass(hwnd, &SubclassProc, 1, 0); }
        int ok = RegisterHotKey(hwnd, 1, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)VirtualKey.T);
        Debug($"RegisterHotKey ok={ok} err={Marshal.GetLastWin32Error()}");
        // Tap Ctrl+Shift (press both, release both, no other key) can't be a RegisterHotKey combo,
        // so it is detected with a low-level keyboard hook.
        unsafe { keyboardHook = SetWindowsHookExW(WH_KEYBOARD_LL, &KeyboardProc, GetModuleHandleW(0), 0); }
        Debug($"keyboard hook {(keyboardHook != 0 ? "installed" : "FAILED")}");

        Activated += (_, e) =>
        {
            if (!expanded && !pinBottom) Dock(animate: false);
            // Clicking any other window docks the Widget, so there's no Esc to press.
            else if (expanded && e.WindowActivationState == WindowActivationState.Deactivated) Dock(animate: true);
        };
        Closed += (_, _) => { UnregisterHotKey(hwnd, 1); UnhookWindowsHookEx(keyboardHook); };
        UpdateStats();
    }

    double Scale => GetDpiForWindow(hwnd) / 96.0;

    static void Debug(string line) =>
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "stack-spike-debug.log"), $"{DateTime.Now:HH:mm:ss.fff} {line}\n");

    static TaskItem MakeTask(int i) => new()
    {
        Title = Titles[i % Titles.Length],
        Effort = Efforts[i % 3],
        Dot = Dots[(i / 3) % 3],
    };

    void UpdateStats() =>
        Stats.Text = $"{BackdropNames[backdrop]} · {tasks.Count} Tasks · last anim {lastFps:0} fps / {lastAnimMs:0} ms · F2 backdrop";

    void SetBackdrop(int mode)
    {
        micaController?.Dispose();
        micaController = null;
        SystemBackdrop = null;
        Root.Background = new SolidColorBrush(Colors.Transparent);
        switch (mode)
        {
            case 0: SystemBackdrop = new MicaBackdrop(); break;
            case 1: SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt }; break;
            case 2:
                micaController = new MicaController();
                micaController.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                micaController.SetSystemBackdropConfiguration(new SystemBackdropConfiguration { IsInputActive = true });
                break;
            case 3: SystemBackdrop = new DesktopAcrylicBackdrop(); break;
            default: Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"]; break;
        }
    }

    void OnHotkey(string source)
    {
        // Toggle: pressing it again while open commits whatever is in the Capture box, then docks.
        if (expanded && GetForegroundWindow() == hwnd)
        {
            CommitCapture();
            Dock(animate: true);
            Debug($"{source}: toggle closed");
            return;
        }
        hotkeyQpc = Stopwatch.GetTimestamp();
        pinBottom = false;
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        bool fg = SetForegroundWindow(hwnd) != 0 && GetForegroundWindow() == hwnd;
        if (!fg)
        {
            // Foreground lock: a hook (unlike RegisterHotKey) doesn't grant foreground rights.
            // The usual workaround is a synthetic Alt tap, which counts as fresh input from us.
            keybd_event(0x12, 0, KEYEVENTF_EXTENDEDKEY, 0);
            keybd_event(0x12, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, 0);
            fg = SetForegroundWindow(hwnd) != 0 && GetForegroundWindow() == hwnd;
            Debug($"{source}: foreground needed Alt workaround, now fg={fg}");
        }
        else Debug($"{source}: foreground ok");
        if (!expanded)
        {
            expanded = true;
            Capture.Visibility = Visibility.Visible;
            List.Visibility = Visibility.Visible;
            FadeInList();
            Animate(DockH, ExpH, 220, null);
        }
        Capture.Focus(FocusState.Programmatic);
        pendingMark = true;
        StartRendering();
    }

    void Dock(bool animate)
    {
        expanded = false;
        void Finish()
        {
            Capture.Visibility = Visibility.Collapsed;
            List.Visibility = Visibility.Collapsed;
            pinBottom = true;
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        if (animate) Animate(ExpH, DockH, 180, Finish);
        else Finish();
    }

    void FadeInList()
    {
        var visual = ElementCompositionPreview.GetElementVisual(List);
        var c = visual.Compositor;
        var ease = c.CreateCubicBezierEasingFunction(new(0.2f, 0f), new(0f, 1f));
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f, ease);
        fade.Duration = TimeSpan.FromMilliseconds(260);
        var slide = c.CreateScalarKeyFrameAnimation();
        slide.InsertKeyFrame(0f, -12f);
        slide.InsertKeyFrame(1f, 0f, ease);
        slide.Duration = TimeSpan.FromMilliseconds(260);
        visual.StartAnimation("Opacity", fade);
        visual.StartAnimation("Translation.Y", slide);
    }

    void Animate(double from, double to, double ms, Action done)
    {
        animFrom = from; animTo = to; animMs = ms; animDone = done;
        animStart = Stopwatch.GetTimestamp();
        animFrames = 0;
        StartRendering();
    }

    // CompositionTarget.Rendering keeps the frame loop alive, so it is only subscribed while needed.
    void StartRendering()
    {
        if (rendering) return;
        rendering = true;
        CompositionTarget.Rendering += OnRendering;
    }

    void OnRendering(object sender, object e)
    {
        if (pendingMark && GetForegroundWindow() == hwnd
            && FocusManager.GetFocusedElement(Content.XamlRoot) == (object)Capture)
        {
            File.AppendAllText(LogPath, $"{Stopwatch.GetTimestamp()} {hotkeyQpc}\n");
            pendingMark = false;
        }

        if (animMs > 0)
        {
            double elapsed = Stopwatch.GetElapsedTime(animStart).TotalMilliseconds;
            double p = Math.Min(1, elapsed / animMs);
            double eased = 1 - Math.Pow(1 - p, 3);
            double s = Scale;
            AppWindow.Resize(new SizeInt32((int)(W * s), (int)((animFrom + (animTo - animFrom) * eased) * s)));
            animFrames++;
            if (p >= 1)
            {
                lastAnimMs = elapsed;
                lastFps = animFrames / (elapsed / 1000);
                animMs = 0;
                var done = animDone;
                animDone = null;
                done?.Invoke();
                UpdateStats();
            }
        }

        if (!pendingMark && animMs == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            rendering = false;
        }
    }

    void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && expanded) { Dock(animate: true); e.Handled = true; }
        else if (e.Key == VirtualKey.F2)
        {
            backdrop = (backdrop + 1) % BackdropNames.Length;
            SetBackdrop(backdrop);
            UpdateStats();
            e.Handled = true;
        }
    }

    void OnCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        CommitCapture();
        e.Handled = true;
    }

    void CommitCapture()
    {
        if (string.IsNullOrWhiteSpace(Capture.Text)) return;
        tasks.Insert(0, new TaskItem { Title = Capture.Text.Trim(), Effort = "Quick", Dot = Dots[0] });
        Capture.Text = "";
        UpdateStats();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static unsafe nint SubclassProc(nint h, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (msg == WM_HOTKEY) { instance.OnHotkey("Ctrl+Alt+T"); return 0; }
        if (msg == WM_TAP) { instance.OnHotkey("tap Ctrl+Shift"); return 0; }
        if (msg == WM_WINDOWPOSCHANGING && instance.pinBottom)
        {
            var wp = (WINDOWPOS*)lParam;
            wp->hwndInsertAfter = HWND_BOTTOM;
            wp->flags &= ~SWP_NOZORDER;
        }
        return DefSubclassProc(h, msg, wParam, lParam);
    }

    // Tap detection. Runs on every system keystroke, so it only flips flags and posts a message.
    static nint keyboardHook;
    static bool ctrlDown, shiftDown, armed, dirty;
    static uint armedAt;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static unsafe nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var k = (KBDLLHOOKSTRUCT*)lParam;
            bool down = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
            bool isCtrl = k->vkCode is 0x11 or 0xA2 or 0xA3;
            bool isShift = k->vkCode is 0x10 or 0xA0 or 0xA1;
            if (isCtrl) ctrlDown = down;
            else if (isShift) shiftDown = down;
            else if (down) dirty = true;

            if (ctrlDown && shiftDown && !armed) { armed = true; armedAt = k->time; }
            if (!ctrlDown && !shiftDown)
            {
                if (armed && !dirty && k->time - armedAt < 1000) PostMessageW(instance.hwnd, WM_TAP, 0, 0);
                armed = false;
                dirty = false;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    // ---- Win32 ----
    const uint WM_TAP = 0x8001; // WM_APP + 1
    const int WH_KEYBOARD_LL = 13;
    const nint WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
    const uint KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nuint dwExtraInfo; }

    [DllImport("user32.dll")]
    static extern unsafe nint SetWindowsHookExW(int id, delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc, nint mod, uint thread);
    [DllImport("user32.dll")] static extern int UnhookWindowsHookEx(nint h);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint h, int code, nint w, nint l);
    [DllImport("user32.dll")] static extern int PostMessageW(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
    [DllImport("kernel32.dll")] static extern nint GetModuleHandleW(nint name);

    const int GWL_EXSTYLE = -20;
    const nint WS_EX_TOOLWINDOW = 0x80;
    const uint WM_HOTKEY = 0x0312, WM_WINDOWPOSCHANGING = 0x0046;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000;
    const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    static readonly nint HWND_BOTTOM = 1, HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPOS { public nint hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    [DllImport("user32.dll")] static extern nint GetWindowLongPtrW(nint h, int i);
    [DllImport("user32.dll")] static extern nint SetWindowLongPtrW(nint h, int i, nint v);
    [DllImport("user32.dll", SetLastError = true)] static extern int RegisterHotKey(nint h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern int UnregisterHotKey(nint h, int id);
    [DllImport("user32.dll")] static extern int SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern int SetForegroundWindow(nint h);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint h);
    [DllImport("comctl32.dll")]
    static extern unsafe int SetWindowSubclass(nint h,
        delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint h, uint msg, nint w, nint l);
}
