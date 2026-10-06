// PROTOTYPE: Win32 interop, mostly lifted from the stack spike (bottom-pinning, tap Ctrl+Shift hook).
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Look;

static unsafe class Native
{
    public const uint WM_TAP = 0x8001;
    static nint tapTarget;
    public static Func<bool> PinBottom = () => false;
    public static Action OnTap;

    public static void ToolWindow(nint h, bool noActivate)
    {
        nint ex = GetWindowLongPtrW(h, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        if (noActivate) ex |= WS_EX_NOACTIVATE;
        SetWindowLongPtrW(h, GWL_EXSTYLE, ex);
    }

    public static void Topmost(nint h, bool on, bool show = false) =>
        SetWindowPos(h, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | (show ? SWP_SHOWWINDOW : 0));

    public static void Bottom(nint h) =>
        SetWindowPos(h, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static void Foreground(nint h)
    {
        SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
        if (SetForegroundWindow(h) != 0 && GetForegroundWindow() == h) return;
        // A hook grants no foreground rights; a synthetic Alt tap counts as fresh input from us.
        keybd_event(0x12, 0, 1, 0);
        keybd_event(0x12, 0, 3, 0);
        SetForegroundWindow(h);
    }

    public static bool IsForeground(nint h) => GetForegroundWindow() == h;
    public static double Scale(nint h) => GetDpiForWindow(h) / 96.0;

    public static void RoundCorners(nint h, bool round)
    {
        int pref = round ? 2 : 1; // DWMWCP_ROUND : DWMWCP_DONOTROUND
        DwmSetWindowAttribute(h, 33, &pref, 4);
    }

    public static void InstallWidget(nint h)
    {
        tapTarget = h;
        SetWindowSubclass(h, &SubclassProc, 1, 0);
        hook = SetWindowsHookExW(13, &KeyboardProc, GetModuleHandleW(0), 0);
    }

    public static void Uninstall() { if (hook != 0) UnhookWindowsHookEx(hook); }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint SubclassProc(nint h, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
    {
        if (msg == WM_TAP) { OnTap?.Invoke(); return 0; }
        if (msg == WM_WINDOWPOSCHANGING && PinBottom())
        {
            var wp = (WINDOWPOS*)lParam;
            wp->hwndInsertAfter = HWND_BOTTOM;
            wp->flags &= ~SWP_NOZORDER;
        }
        return DefSubclassProc(h, msg, wParam, lParam);
    }

    // Tap Ctrl+Shift: press both, release both, no other key in between, under 1 s. Fires on release.
    static nint hook;
    static bool ctrlDown, shiftDown, armed, dirty;
    static uint armedAt;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    static nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var k = (KBDLLHOOKSTRUCT*)lParam;
            bool down = wParam == 0x100 || wParam == 0x104;
            bool isCtrl = k->vkCode is 0x11 or 0xA2 or 0xA3;
            bool isShift = k->vkCode is 0x10 or 0xA0 or 0xA1;
            if (isCtrl) ctrlDown = down;
            else if (isShift) shiftDown = down;
            else if (down) dirty = true;
            if (ctrlDown && shiftDown && !armed) { armed = true; armedAt = k->time; }
            if (!ctrlDown && !shiftDown)
            {
                if (armed && !dirty && k->time - armedAt < 1000) PostMessageW(tapTarget, WM_TAP, 0, 0);
                armed = false;
                dirty = false;
            }
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)] struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct WINDOWPOS { public nint hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }

    const int GWL_EXSTYLE = -20;
    const nint WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    const uint WM_WINDOWPOSCHANGING = 0x0046;
    const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    static readonly nint HWND_BOTTOM = 1, HWND_TOPMOST = -1, HWND_NOTOPMOST = -2;

    [DllImport("user32.dll")] static extern nint SetWindowsHookExW(int id, delegate* unmanaged[Stdcall]<int, nint, nint, nint> proc, nint mod, uint thread);
    [DllImport("user32.dll")] static extern int UnhookWindowsHookEx(nint h);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint h, int code, nint w, nint l);
    [DllImport("user32.dll")] static extern int PostMessageW(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, nuint extra);
    [DllImport("kernel32.dll")] static extern nint GetModuleHandleW(nint name);
    [DllImport("user32.dll")] static extern nint GetWindowLongPtrW(nint h, int i);
    [DllImport("user32.dll")] static extern nint SetWindowLongPtrW(nint h, int i, nint v);
    [DllImport("user32.dll")] static extern int SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint f);
    [DllImport("user32.dll")] static extern int SetForegroundWindow(nint h);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint h);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint h, int attr, void* value, int size);
    [DllImport("comctl32.dll")] static extern int SetWindowSubclass(nint h, delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nuint, nuint, nint> proc, nuint id, nuint data);
    [DllImport("comctl32.dll")] static extern nint DefSubclassProc(nint h, uint msg, nint w, nint l);
}
