using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using TaskWidget.Core;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace TaskWidget;

// The Dock: a tool window pinned to the bottom of the z-order, like the Widget, so it lives on the desktop and never
// covers other windows. It never takes activation and reserves no screen space (not an AppBar).
// Like the prototype, it keeps the DWM border so the window gets Windows 11's rounded corners.
public sealed partial class DockWindow : Window
{
    public const double WidthDip = 380;
    public const double HeightDip = 44;

    const nuint SubclassId = 3;

    // Win32 HWND insert-after value from Winuser.h.
    static readonly HWND InsertBottom = (HWND)(nint)1;
    static readonly SUBCLASSPROC Subclass = OnSubclass;

    readonly HWND hwnd;
    readonly GCHandle self;
    readonly WindowLook look;
    readonly Func<RectInt32> placement;
    int motion;

    public DockWindow(AppModel model, Func<RectInt32> placement)
    {
        Model = model;
        this.placement = placement;
        InitializeComponent();
        Title = "Task Widget";
        AppIcon.Apply(AppWindow);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        hwnd = (HWND)WindowNative.GetWindowHandle(this);
        var ex = (WINDOW_EX_STYLE)(uint)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        ex |= WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE;
        PInvoke.SetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)(uint)ex);

        self = GCHandle.Alloc(this);
        if (!PInvoke.SetWindowSubclass(hwnd, Subclass, SubclassId, (nuint)(nint)GCHandle.ToIntPtr(self)))
        {
            self.Free();
            throw new InvalidOperationException("Could not subclass the Dock window.");
        }

        look = new WindowLook(this, Host);
        // Off screen until the first ShowDock, so starting up never flashes it.
        AppWindow.Move(new PointInt32(-32000, -32000));
        Host.Loaded += (_, _) => Host.XamlRoot.Changed += (_, _) =>
        {
            if (IsShown)
                Place(placement());
        };
        Closed += (_, _) =>
        {
            look.Dispose();
            PInvoke.RemoveWindowSubclass(hwnd, Subclass, SubclassId);
            self.Free();
        };
    }

    public AppModel Model { get; }

    public bool IsShown { get; private set; }

    // Creates the window's content once, then keeps it hidden until it's wanted.
    public void Start()
    {
        Activate();
        AppWindow.Hide();
    }

    public void ApplyLook(ElementTheme theme, bool dark, bool contrast, BackdropChoice backdrop) =>
        look.Apply(theme, dark, contrast, backdrop);

    // Dock appears: content fades in while scaling up on a spring (DockInMs, DockSpring*).
    public void ShowDock(bool animate = true)
    {
        motion++;
        IsShown = true;
        Place(placement());
        PInvoke.ShowWindow(hwnd, SHOW_WINDOW_CMD.SW_SHOWNA);
        PinToBottom();
        if (!animate)
        {
            WindowMotion.AnimateContent(DockRoot, show: true, 1);
            return;
        }

        WindowMotion.SpringIn(DockRoot, WidthDip);
        WindowMotion.AnimateContent(DockRoot, show: true, WindowMotion.Ms("DockInMs"));
    }

    // Dock leaves: a quick fade (DockOutMs), then the window hides.
    public void HideDock()
    {
        if (!IsShown)
            return;
        IsShown = false;
        int token = ++motion;
        WindowMotion.AnimateContent(DockRoot, show: false, WindowMotion.Ms("DockOutMs"), () =>
        {
            if (token == motion)
                AppWindow.Hide();
        });
    }

    public void HideNow()
    {
        motion++;
        IsShown = false;
        AppWindow.Hide();
    }

    // Display, DPI, work-area and Explorer restarts move the anchor; the bottom pin is re-asserted after Explorer returns.
    public void Reanchor()
    {
        if (!IsShown)
            return;
        Place(placement());
        PinToBottom();
    }

    static LRESULT OnSubclass(HWND window, uint msg, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        if (msg == PInvoke.WM_WINDOWPOSCHANGING && GCHandle.FromIntPtr((nint)data).Target is DockWindow dock)
            dock.HoldAtBottom(lParam);
        return PInvoke.DefSubclassProc(window, msg, wParam, lParam);
    }

    // Clicks and other apps can't raise the Dock. While it's shown, Win+D can't hide it either.
    unsafe void HoldAtBottom(LPARAM lParam)
    {
        var pos = (WINDOWPOS*)(nint)lParam;
        pos->hwndInsertAfter = InsertBottom;
        pos->flags &= ~SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
        if (IsShown)
            pos->flags &= ~SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW;
    }

    void PinToBottom() =>
        PInvoke.SetWindowPos(
            hwnd,
            InsertBottom,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);

    // AppWindow.ResizeClient reserves a caption this window does not have, so size the frame ourselves.
    void Place(RectInt32 client)
    {
        int frameW = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        int frameH = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        AppWindow.MoveAndResize(new RectInt32(client.X, client.Y, client.Width + frameW, client.Height + frameH));
    }

    void Host_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!Model.Docked)
            return;
        Model.Raise();
        e.Handled = true;
    }
}
