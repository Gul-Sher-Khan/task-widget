using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TaskWidget.Core;
using Windows.System;
using Windows.UI.Core;

namespace TaskWidget;

public sealed partial class SettingsPanel : UserControl
{
    bool ready;
    readonly HashSet<VirtualKey> held = [];
    string? combo;
    bool otherKey;

    public static readonly DependencyProperty RecordingProperty = DependencyProperty.Register(
        nameof(Recording), typeof(bool), typeof(SettingsPanel), new PropertyMetadata(false));

    public SettingsPanel(AppModel model)
    {
        Model = model;
        InitializeComponent();
        RecordButton.PreviewKeyDown += Record_KeyDown;
        RecordButton.PreviewKeyUp += Record_KeyUp;
        RecordButton.LostFocus += (_, _) => Recording = false;
        Loaded += (_, _) => ready = true;
    }

    public AppModel Model { get; }

    public Action<bool>? RecordingChanged { get; set; }

    public bool Recording
    {
        get => (bool)GetValue(RecordingProperty);
        set
        {
            if (Recording == value)
                return;
            SetValue(RecordingProperty, value);
            RecordingChanged?.Invoke(value);
        }
    }

    public string[] Themes { get; } = ["System", "Light", "Dark"];

    public string[] Backdrops { get; } = ["Mica", "Mica Alt", "Acrylic", "Solid"];

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || ThemeBox.SelectedIndex < 0)
            return;

        Model.ThemeIndex = ThemeBox.SelectedIndex;
    }

    void Backdrop_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || BackdropBox.SelectedIndex < 0)
            return;

        Model.BackdropIndex = BackdropBox.SelectedIndex;
    }

    void Rows_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!ready)
            return;

        Model.RowsBeforeScrolling = (int)e.NewValue;
    }

    void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (!ready)
            return;

        Model.StartWithWindows = Startup.IsOn;
    }

    void Record_Click(object sender, RoutedEventArgs e)
    {
        Recording = !Recording;
        held.Clear();
        combo = null;
        otherKey = false;
        if (Recording)
            RecordButton.Focus(FocusState.Programmatic);
    }

    static bool IsMod(VirtualKey key) => key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows
        or VirtualKey.RightWindows or VirtualKey.LeftControl or VirtualKey.RightControl or VirtualKey.LeftShift or VirtualKey.RightShift
        or VirtualKey.LeftMenu or VirtualKey.RightMenu;

    static string KeyName(VirtualKey key) => key switch
    {
        VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl => "Ctrl",
        VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift => "Shift",
        VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu => "Alt",
        VirtualKey.LeftWindows or VirtualKey.RightWindows => "Win",
        VirtualKey.Space => "Space",
        _ => key.ToString(),
    };

    static string Mods()
    {
        var parts = new List<string>();
        foreach (var (key, name) in new[] { (VirtualKey.Control, "Ctrl"), (VirtualKey.Menu, "Alt"), (VirtualKey.Shift, "Shift"), (VirtualKey.LeftWindows, "Win") })
        {
            if (InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down))
                parts.Add(name);
        }

        return string.Join(" + ", parts);
    }

    void Record_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!Recording)
            return;

        e.Handled = true;
        if (e.Key == VirtualKey.Escape)
        {
            Recording = false;
            return;
        }

        if (IsMod(e.Key))
        {
            held.Add(e.Key);
            combo = Mods();
            return;
        }

        otherKey = true;
        var mods = Mods();
        if (mods.Length == 0 || !HotkeyBinding.TryParse(mods + " + " + KeyName(e.Key), out var binding))
            return;

        Model.SetHotkey(binding);
        Recording = false;
    }

    void Record_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (!Recording || !IsMod(e.Key))
            return;

        e.Handled = true;
        if (Mods().Length > 0)
            return;

        if (!otherKey && combo is not null && HotkeyBinding.TryParse(combo, out var binding))
        {
            Model.SetHotkey(binding);
            Recording = false;
        }

        held.Clear();
        combo = null;
        otherKey = false;
    }
}
