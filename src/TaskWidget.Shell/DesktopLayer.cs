using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using TaskWidget.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace TaskWidget;

// Widget z-order, Dock chrome, full-screen detection, and display-change messages. No Progman or WorkerW reparenting.
sealed partial class DesktopLayer : IDisposable
{
    enum Layer
    {
        Pinned,
        Front,
        Hidden,
        Free,
    }

    const nuint SubclassId = 1;
    const int CoverSlack = 8;

    // Win32 HWND insert-after values from Winuser.h.
    static readonly HWND InsertBottom = (HWND)(nint)1;
    static readonly HWND InsertTop = (HWND)(nint)0;
    static readonly HWND InsertTopMost = (HWND)(nint)(-1);

    static readonly SUBCLASSPROC Subclass = OnSubclass;
    static readonly WINEVENTPROC WinEvent = OnWinEvent;
    static DesktopLayer? current;

    readonly AppWindow appWindow;
    readonly OverlappedPresenter presenter;
    readonly AppModel model;
    readonly DispatcherQueue dispatcher;
    readonly Action reanchor;
    readonly HWND hwnd;
    readonly GCHandle self;
    readonly uint taskbarCreated;
    readonly UnhookWinEventSafeHandle hook;
    HWND covering;
    Layer layer = Layer.Pinned;
    bool overFullScreen;
    bool allowHide;
    bool suspend;
    bool subclassed;
    bool disposed;

    public DesktopLayer(Window window, AppModel model, Action reanchor)
    {
        this.model = model;
        this.reanchor = reanchor;
        appWindow = window.AppWindow;
        presenter = (OverlappedPresenter)appWindow.Presenter;
        dispatcher = window.DispatcherQueue;
        hwnd = (HWND)WindowNative.GetWindowHandle(window);
        // A tool window, as in the prototype: no taskbar button and not in Alt+Tab. Set once; the style is
        // never rewritten afterwards, so the window's own visibility bit is never overwritten.
        var ex = (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)(uint)(ex | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW));
        appWindow.IsShownInSwitchers = false;
        taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");
        self = GCHandle.Alloc(this);
        subclassed = PInvoke.SetWindowSubclass(hwnd, Subclass, SubclassId, (nuint)(nint)GCHandle.ToIntPtr(self));
        if (!subclassed)
        {
            self.Free();
            throw new InvalidOperationException("Could not subclass the Widget window.");
        }
        hook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_MINIMIZEEND,
            default,
            WinEvent,
            0,
            0,
            PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS);
        current = this;
    }

    public event Action? Deactivated;

    public event Action<bool>? FocusChanged;

    public event Action? DisplayChanged;

    public event Action? WorkAreaChanged;

    public event Action<DpiNotice>? DpiChanged;

    public readonly record struct DpiNotice(int OuterWidth, int OuterHeight);

    public static void LetAnotherProcessTakeTheForeground() =>
        PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);

    public void PinToBottom()
    {
        layer = Layer.Pinned;
        overFullScreen = false;
        allowHide = false;
        suspend = true;
        try
        {
            ApplyWidgetChrome();
            PInvoke.SetWindowPos(
                hwnd,
                InsertBottom,
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
            presenter.IsAlwaysOnTop = false;
            if (PInvoke.GetForegroundWindow() == hwnd)
                GiveFocusAway();
        }
        finally
        {
            suspend = false;
        }
    }

    public void BringToFront(bool coverFullScreen)
    {
        layer = Layer.Front;
        overFullScreen = coverFullScreen;
        allowHide = false;
        suspend = true;
        try
        {
            ApplyWidgetChrome();
            PInvoke.SetWindowPos(
                hwnd,
                coverFullScreen ? InsertTopMost : InsertTop,
                0,
                0,
                0,
                0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW);
            appWindow.Show();
            PInvoke.SetForegroundWindow(hwnd);
        }
        finally
        {
            suspend = false;
        }
    }

    // The Widget leaves while docked. Focus goes to the window under it, not to the Dock.
    public void Hide()
    {
        layer = Layer.Hidden;
        overFullScreen = false;
        allowHide = true;
        suspend = true;
        try
        {
            presenter.IsAlwaysOnTop = false;
            if (PInvoke.GetForegroundWindow() == hwnd)
                GiveFocusAway();
            appWindow.Hide();
        }
        finally
        {
            suspend = false;
        }
    }

    public void AllowClose()
    {
        layer = Layer.Free;
        allowHide = true;
    }

    public void ReapplyZOrder()
    {
        switch (layer)
        {
            case Layer.Pinned:
                PinToBottom();
                break;
            case Layer.Front:
                BringToFront(overFullScreen);
                break;
        }
    }

    public void DragByCaption()
    {
        PInvoke.ReleaseCapture();
        PInvoke.SendMessage(hwnd, PInvoke.WM_NCLBUTTONDOWN, (WPARAM)(nuint)PInvoke.HTCAPTION, default);
    }

    public void YieldToFullScreen()
    {
        if (covering.IsNull || covering == hwnd || !PInvoke.IsWindow(covering))
            return;

        suspend = true;
        try
        {
            PInvoke.SetForegroundWindow(covering);
        }
        finally
        {
            suspend = false;
        }
    }

    public void CheckFullScreen()
    {
        // Ignore foreground changes we caused ourselves while pinning or yielding.
        if (disposed || suspend)
            return;

        var foreground = PInvoke.GetForegroundWindow();
        if (foreground == hwnd || foreground.IsNull)
            return;

        if (CoversOurMonitor(foreground))
        {
            covering = foreground;
            model.SetFullScreenApp(true);
        }
        else
        {
            covering = default;
            model.SetFullScreenApp(false);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (current == this)
            current = null;
        hook.Dispose();
        if (subclassed)
            PInvoke.RemoveWindowSubclass(hwnd, Subclass, SubclassId);
        if (self.IsAllocated)
            self.Free();
    }

    static LRESULT OnSubclass(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        var layer = GCHandle.FromIntPtr((nint)data).Target as DesktopLayer;
        if (layer is null || layer.disposed)
            return PInvoke.DefSubclassProc(hwnd, msg, wParam, lParam);
        return layer.Handle(hwnd, msg, wParam, lParam);
    }

    static void OnWinEvent(HWINEVENTHOOK hook, uint evt, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt != PInvoke.EVENT_SYSTEM_FOREGROUND
            && evt != PInvoke.EVENT_SYSTEM_MOVESIZEEND
            && evt != PInvoke.EVENT_SYSTEM_MINIMIZEEND)
            return;

        var layer = current;
        if (layer is null || layer.disposed)
            return;
        layer.dispatcher.TryEnqueue(() => layer.CheckFullScreen());
    }

    unsafe static DpiNotice ReadSuggested(LPARAM lParam)
    {
        var suggested = (RECT*)(nint)lParam;
        if (suggested is null)
            return new DpiNotice(0, 0);
        return new DpiNotice(suggested->right - suggested->left, suggested->bottom - suggested->top);
    }

    LRESULT Handle(HWND window, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == taskbarCreated || msg == PInvoke.WM_DISPLAYCHANGE)
        {
            dispatcher.TryEnqueue(() =>
            {
                if (msg == taskbarCreated)
                    reanchor();
                else
                    DisplayChanged?.Invoke();
            });
            return PInvoke.DefSubclassProc(window, msg, wParam, lParam);
        }

        if (msg == PInvoke.WM_SETTINGCHANGE && (SYSTEM_PARAMETERS_INFO_ACTION)(uint)wParam.Value == SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA)
        {
            dispatcher.TryEnqueue(() => WorkAreaChanged?.Invoke());
            return PInvoke.DefSubclassProc(window, msg, wParam, lParam);
        }

        if (msg == PInvoke.WM_DPICHANGED)
        {
            DpiChanged?.Invoke(ReadSuggested(lParam));
            return default;
        }

        if (msg == PInvoke.WM_ACTIVATE)
            NoteActivation(wParam);

        if (msg == PInvoke.WM_SYSCOMMAND && layer is Layer.Pinned or Layer.Front)
        {
            if ((wParam.Value & 0xFFF0) == PInvoke.SC_MINIMIZE)
                return default;
        }

        if (msg == PInvoke.WM_WINDOWPOSCHANGING)
            AdjustZOrder(lParam);

        if (msg == PInvoke.WM_ENDSESSION && wParam.Value != 0)
            model.FlushPending();

        return PInvoke.DefSubclassProc(window, msg, wParam, lParam);
    }

    unsafe void AdjustZOrder(LPARAM lParam)
    {
        var pos = (WINDOWPOS*)(nint)lParam;
        if (layer == Layer.Pinned)
        {
            // Win+D hides ordinary windows. Refusing that keeps the Widget on the desktop.
            pos->hwndInsertAfter = InsertBottom;
            pos->flags &= ~SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
            if (!allowHide)
                pos->flags &= ~SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW;
        }
        else if (layer == Layer.Front && overFullScreen)
        {
            pos->hwndInsertAfter = InsertTopMost;
            pos->flags &= ~SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
        }
    }

    void NoteActivation(WPARAM wParam)
    {
        var state = (uint)(wParam.Value & 0xFFFF);
        bool active = state != PInvoke.WA_INACTIVE;
        // Focus is reported even while we move the window ourselves, so a tap knows whether the Widget has it.
        dispatcher.TryEnqueue(() =>
        {
            if (!disposed)
                FocusChanged?.Invoke(active);
        });

        if (!active)
        {
            if (suspend || layer is Layer.Hidden or Layer.Free)
                return;
            dispatcher.TryEnqueue(() =>
            {
                if (disposed || layer is Layer.Hidden or Layer.Free)
                    return;
                Deactivated?.Invoke();
            });
            return;
        }

        if (layer == Layer.Pinned)
            layer = Layer.Front;
    }

    void ApplyWidgetChrome()
    {
        presenter.IsAlwaysOnTop = layer == Layer.Front && overFullScreen;
    }

    bool CoversOurMonitor(HWND candidate)
    {
        if (!PInvoke.IsWindowVisible(candidate) || PInvoke.IsIconic(candidate))
            return false;
        if (IsCloaked(candidate) || IsShell(candidate))
            return false;
        if (!PInvoke.GetWindowRect(candidate, out RECT bounds))
            return false;

        var monitor = PInvoke.MonitorFromWindow(candidate, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var ours = PInvoke.MonitorFromWindow(hwnd, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor != ours)
            return false;

        unsafe
        {
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (!PInvoke.GetMonitorInfo(monitor, ref info))
                return false;

            var screen = info.rcMonitor;
            return bounds.left <= screen.left + CoverSlack
                && bounds.top <= screen.top + CoverSlack
                && bounds.right >= screen.right - CoverSlack
                && bounds.bottom >= screen.bottom - CoverSlack;
        }
    }

    static unsafe bool IsCloaked(HWND candidate)
    {
        int cloaked = 0;
        var result = PInvoke.DwmGetWindowAttribute(
            candidate,
            DWMWINDOWATTRIBUTE.DWMWA_CLOAKED,
            &cloaked,
            (uint)sizeof(int));
        return result.Succeeded && cloaked != 0;
    }

    static bool IsShell(HWND candidate)
    {
        Span<char> buffer = stackalloc char[64];
        int length = PInvoke.GetClassName(candidate, buffer);
        if (length <= 0)
            return false;
        var name = buffer[..length];
        return name.SequenceEqual("Progman")
            || name.SequenceEqual("WorkerW")
            || name.SequenceEqual("Shell_TrayWnd")
            || name.SequenceEqual("Shell_SecondaryTrayWnd");
    }

    void GiveFocusAway()
    {
        var cursor = PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_HWNDPREV);
        while (!cursor.IsNull && cursor != hwnd)
        {
            if (PInvoke.IsWindowVisible(cursor) && !PInvoke.IsIconic(cursor) && !IsCloaked(cursor))
            {
                PInvoke.SetForegroundWindow(cursor);
                return;
            }

            cursor = PInvoke.GetWindow(cursor, GET_WINDOW_CMD.GW_HWNDPREV);
        }
    }
}
