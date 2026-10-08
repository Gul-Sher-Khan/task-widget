using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TaskWidget.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using WinRT.Interop;

namespace TaskWidget;

// Tap uses WH_KEYBOARD_LL. A chord uses RegisterHotKey. The tap decision lives in ModifierTap.
sealed partial class CaptureHotkey : IDisposable
{
    const int HotkeyId = 1;
    const uint TapMessage = 0x8001;
    const nuint SubclassId = 2;
    const byte VkMenu = 0x12;

    static readonly SUBCLASSPROC Subclass = OnSubclass;
    static readonly HOOKPROC Hook = OnHook;
    static readonly FreeLibrarySafeHandle Module = PInvoke.GetModuleHandle((string?)null);
    static CaptureHotkey? current;

    readonly AppModel model;
    readonly DispatcherQueue dispatcher;
    readonly Func<string> fieldText;
    readonly HWND hwnd;
    readonly GCHandle self;
    readonly UnhookWindowsHookExSafeHandle hook;
    bool subclassed;
    bool disposed;

    public CaptureHotkey(Window window, AppModel model, Func<string> fieldText)
    {
        this.model = model;
        this.fieldText = fieldText;
        dispatcher = window.DispatcherQueue;
        hwnd = (HWND)WindowNative.GetWindowHandle(window);
        self = GCHandle.Alloc(this);
        subclassed = PInvoke.SetWindowSubclass(hwnd, Subclass, SubclassId, (nuint)(nint)GCHandle.ToIntPtr(self));
        if (!subclassed)
        {
            self.Free();
            throw new InvalidOperationException("Could not listen for the Capture hotkey.");
        }

        hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, Hook, Module, 0);
        current = this;
        Apply(model.HotkeyBinding);
        model.PropertyChanged += OnModelChanged;
    }

    public bool Recording { get; set; }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        model.PropertyChanged -= OnModelChanged;
        if (current == this)
            current = null;
        PInvoke.UnregisterHotKey(hwnd, HotkeyId);
        hook.Dispose();
        if (subclassed)
            PInvoke.RemoveWindowSubclass(hwnd, Subclass, SubclassId);
        if (self.IsAllocated)
            self.Free();
    }

    void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.Hotkey))
            Apply(model.HotkeyBinding);
    }

    void Apply(HotkeyBinding binding)
    {
        tapState = default;
        bound = binding;
        PInvoke.UnregisterHotKey(hwnd, HotkeyId);
        if (!binding.IsChord || binding.ChordKey is not int key)
            return;

        var modifiers = HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (binding.Control)
            modifiers |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        if (binding.Alt)
            modifiers |= HOT_KEY_MODIFIERS.MOD_ALT;
        if (binding.Shift)
            modifiers |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        if (binding.Windows)
            modifiers |= HOT_KEY_MODIFIERS.MOD_WIN;
        PInvoke.RegisterHotKey(hwnd, HotkeyId, modifiers, (uint)key);
    }

    void Fired(bool fromHook)
    {
        if (Recording || disposed)
            return;

        if (fromHook)
            NudgeForeground();
        model.Tap(fieldText());
    }

    static void NudgeForeground()
    {
        // A low-level hook does not grant foreground rights. A short Alt tap does.
        PInvoke.keybd_event(VkMenu, 0, KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY, 0);
        PInvoke.keybd_event(VkMenu, 0, KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY | KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP, 0);
    }

    static TapState tapState;
    static HotkeyBinding bound = HotkeyBinding.CtrlShift;

    static LRESULT OnSubclass(HWND window, uint msg, WPARAM wParam, LPARAM lParam, nuint id, nuint data)
    {
        var hotkey = GCHandle.FromIntPtr((nint)data).Target as CaptureHotkey;
        if (hotkey is null || hotkey.disposed)
            return PInvoke.DefSubclassProc(window, msg, wParam, lParam);

        if (msg == PInvoke.WM_HOTKEY || msg == TapMessage)
        {
            var fromHook = msg == TapMessage;
            hotkey.dispatcher.TryEnqueue(() => hotkey.Fired(fromHook));
            return default;
        }

        return PInvoke.DefSubclassProc(window, msg, wParam, lParam);
    }

    static LRESULT OnHook(int code, WPARAM wParam, LPARAM lParam)
    {
        var hotkey = current;
        if (code >= 0 && hotkey is not null && !hotkey.Recording && bound.IsTap)
        {
            unsafe
            {
                var info = (KBDLLHOOKSTRUCT*)(nint)lParam;
                var down = wParam.Value == PInvoke.WM_KEYDOWN || wParam.Value == PInvoke.WM_SYSKEYDOWN;
                var (next, fired) = ModifierTap.Apply(tapState, down, (int)info->vkCode, info->time, bound);
                tapState = next;
                if (fired)
                    PInvoke.PostMessage(hotkey.hwnd, TapMessage, default, default);
            }
        }

        return PInvoke.CallNextHookEx(null, code, wParam, lParam);
    }
}
