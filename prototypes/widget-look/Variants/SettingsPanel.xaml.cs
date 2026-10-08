// PROTOTYPE: throwaway. Sign-in (Shell.StartSignIn) and the hotkey recorder are simulated.
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;

namespace Look;

public sealed partial class SettingsPanel : UserControl
{
    public Prefs P => Shell.Prefs;
    public string[] Models => Prefs.Models;
    public string[] Themes => Shell.ThemeNames;
    public string[] Backdrops => Shell.BackdropNames;
    public Visibility TransparencyOff => new UISettings().AdvancedEffectsEnabled ? Visibility.Collapsed : Visibility.Visible;

    readonly HashSet<VirtualKey> held = new();
    string combo;
    bool otherKey;

    public SettingsPanel()
    {
        InitializeComponent();
        RecordButton.PreviewKeyDown += Record_KeyDown;
        RecordButton.PreviewKeyUp += Record_KeyUp;
        RecordButton.LostFocus += (_, _) => P.Recording = false;
        // LOOK_UPDATE=1 opens Settings scrolled to About, for screenshots.
        if (System.Environment.GetEnvironmentVariable("LOOK_UPDATE") == "1") Loaded += (_, _) => Scroller.ChangeView(null, 10000, null, true);
    }

    // Sign out: no confirmation; Tasks and Captures are kept, the signed-out banner appears and new Captures wait.
    void SignOut_Click(object s, RoutedEventArgs e) => P.SignedIn = false;

    // ---- updates: the first check finds 0.2.0; a check while offline (2nd, 4th…) fails ----
    int checks;

    void CheckNow_Click(object s, RoutedEventArgs e)
    {
        P.Update = Prefs.Upd.Checking;
        bool fail = checks++ % 2 == 1;
        Shell.After(1200, () =>
        {
            P.CheckedWhen = fail ? "No connection. Will try again later." : "Checked just now";
            P.Update = fail ? Prefs.Upd.Failed : P.Version == P.NewVersion ? Prefs.Upd.Current : Prefs.Upd.Available;
            if (P.UpdAvailable) Shell.Store.Attention = true;
        });
    }

    void Install_Click(object s, RoutedEventArgs e)
    {
        P.Downloaded = 0;
        P.Update = Prefs.Upd.Downloading;
        for (int i = 1; i <= 10; i++)
        {
            double v = i / 10.0;
            Shell.After(i * 180, () => P.Downloaded = v);
        }
        // The real app runs the installer silently and restarts; here we jump straight to the new version.
        Shell.After(2300, () => { P.Version = P.NewVersion; P.CheckedWhen = "Updated just now"; P.Update = Prefs.Upd.Current; Shell.Store.Attention = false; });
    }

    // ---- hotkey recorder: a non-modifier key gives a chord; releasing a lone modifier pair gives a tap ----
    void Record_Click(object s, RoutedEventArgs e)
    {
        P.Recording = !P.Recording;
        held.Clear(); combo = null; otherKey = false;
    }

    static bool IsMod(VirtualKey k) => k is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows
        or VirtualKey.RightWindows or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift
        or VirtualKey.LeftMenu or VirtualKey.RightMenu;

    static string Name(VirtualKey k) => k switch
    {
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl => "Ctrl",
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift => "Shift",
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu => "Alt",
        VirtualKey.LeftWindows or VirtualKey.RightWindows => "Win",
        VirtualKey.Space => "Space",
        _ => k.ToString(),
    };

    static string Mods()
    {
        var parts = new List<string>();
        foreach (var (k, n) in new[] { (VirtualKey.Control, "Ctrl"), (VirtualKey.Menu, "Alt"), (VirtualKey.Shift, "Shift"), (VirtualKey.LeftWindows, "Win") })
            if (InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down)) parts.Add(n);
        return string.Join(" + ", parts);
    }

    void Record_KeyDown(object s, KeyRoutedEventArgs e)
    {
        if (!P.Recording) return;
        e.Handled = true;
        if (e.Key == VirtualKey.Escape) { P.Recording = false; return; }
        if (IsMod(e.Key)) { held.Add(e.Key); combo = Mods(); return; }
        otherKey = true;
        string mods = Mods();
        if (mods.Length == 0) return; // a bare key is not allowed
        P.Hotkey = mods + " + " + Name(e.Key);
        P.Recording = false;
    }

    void Record_KeyUp(object s, KeyRoutedEventArgs e)
    {
        if (!P.Recording || !IsMod(e.Key)) return;
        e.Handled = true;
        if (Mods().Length > 0) return;
        if (!otherKey && combo != null && combo.Contains('+')) { P.Hotkey = combo; P.Recording = false; }
        held.Clear(); combo = null; otherKey = false;
    }
}
