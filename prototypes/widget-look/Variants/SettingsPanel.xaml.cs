// PROTOTYPE: throwaway. Sign-in, key test and the hotkey recorder are simulated.
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
    public string[] Providers => Prefs.Providers;
    public string[] Themes => Shell.ThemeNames;
    public string[] Backdrops => Shell.BackdropNames;
    public Visibility TransparencyOff => new UISettings().AdvancedEffectsEnabled ? Visibility.Collapsed : Visibility.Visible;
    public Brush TestBrush => P.TestResult.StartsWith("Connected") ? Shell.Res("OkBrush") : Shell.Res("PriHighBrush");

    readonly HashSet<VirtualKey> held = new();
    string combo;
    bool otherKey;

    public SettingsPanel()
    {
        InitializeComponent();
        ConnBar.SelectedItem = ConnBar.Items[P.Connection];
        P.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Prefs.TestResult)) Bindings.Update(); };
        RecordButton.PreviewKeyDown += Record_KeyDown;
        RecordButton.PreviewKeyUp += Record_KeyUp;
        RecordButton.LostFocus += (_, _) => P.Recording = false;
    }

    void ConnBar_SelectionChanged(SelectorBar s, SelectorBarSelectionChangedEventArgs e) =>
        P.Connection = s.Items.IndexOf(s.SelectedItem);

    void SignOut_Click(object s, RoutedEventArgs e) { P.SignedIn = false; Shell.Store.Attention = true; }

    void SignIn_Click(object s, RoutedEventArgs e)
    {
        P.Testing = true;
        Shell.After(1400, () => { P.Testing = false; P.SignedIn = true; Shell.Store.Attention = false; });
    }

    void Test_Click(object s, RoutedEventArgs e)
    {
        P.Testing = true;
        P.TestResult = "";
        Shell.After(900, () =>
        {
            P.Testing = false;
            P.TestResult = P.ApiKey.Length < 8 ? "Key rejected (401)" : "Connected · 0.8 s";
        });
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
