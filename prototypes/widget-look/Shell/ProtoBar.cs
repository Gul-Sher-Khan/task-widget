// PROTOTYPE: the floating control bar. Deliberately plain black/yellow so it never reads as part of the design.
using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace Look;

public sealed partial class ProtoBar : Window
{
    readonly TextBlock state = Label(11, false);
    readonly Button dock, pri, eff, theme, backdrop;
    static readonly string[] Samples =
    {
        "Email Bilal the signed contract, he's waiting on it today and also plan the offsite agenda",
        "Maybe look into a standing desk someday",
        "Fix the broken export button, the CSV has no header row",
        "Call mum back",
    };
    int sample;

    public ProtoBar()
    {
        nint h = WindowNative.GetWindowHandle(this);
        var p = OverlappedPresenter.Create();
        p.SetBorderAndTitleBar(true, false);
        p.IsResizable = false; p.IsMaximizable = false; p.IsMinimizable = false; p.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(p);
        AppWindow.IsShownInSwitchers = false;
        Native.ToolWindow(h, false);
        var P = Shell.Prefs;

        dock = Btn("", () => { P.DockStyle = (P.DockStyle + 1) % Prefs.DockStyles.Length; Shell.Apply(); });
        pri = Btn("", () => { P.PriStyle = (P.PriStyle + 1) % Prefs.PriStyles.Length; Shell.Apply(); });
        eff = Btn("", () => { P.EffStyle = (P.EffStyle + 1) % Prefs.EffStyles.Length; Shell.Apply(); });
        theme = Btn("", () => P.Theme = (P.Theme + 1) % 3);
        backdrop = Btn("", () => P.Backdrop = (P.Backdrop + 1) % 4);

        var row1 = Row(dock, pri, eff, theme, backdrop);
        var row2 = Row(
            Btn("Fake Capture", () => { Shell.Store.Capture(Samples[sample++ % Samples.Length]); Refresh(); }),
            Btn("Attention dot", () => { Shell.Store.Attention = !Shell.Store.Attention; Refresh(); }),
            Btn("Dock / Widget", () => { if (Shell.Docked) Shell.Expand(); else Shell.Collapse(); }),
            Btn("Settings", () => { if (Shell.Docked) Shell.Expand(); P.ShowSettings = !P.ShowSettings; }),
            Btn("3 / 9 / 15 Tasks", () => { int n = Shell.Store.CountOpen; Shell.Store.Seed(n < 6 ? 9 : n < 12 ? 15 : 3); Refresh(); }),
            Btn("✕ Quit", () => Application.Current.Exit()));

        var col = new StackPanel { Spacing = 5, Padding = new Thickness(12, 8, 12, 8) };
        col.Children.Add(row1);
        col.Children.Add(row2);
        col.Children.Add(state);
        Content = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 18, 18, 18)), Children = { col }, RequestedTheme = ElementTheme.Dark };

        double s = Native.Scale(h);
        var wa = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = (int)(900 * s), hh = (int)(112 * s);
        AppWindow.MoveAndResize(new RectInt32(wa.X + (wa.Width - w) / 2, wa.Y + wa.Height - hh - (int)(16 * s), w, hh));
        Shell.Store.PropertyChanged += (_, _) => Refresh();
        P.PropertyChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var st = Shell.Store;
        var P = Shell.Prefs;
        Text(dock, "Dock: " + Prefs.DockStyles[P.DockStyle]);
        Text(pri, "Priority icons: " + Prefs.PriStyles[P.PriStyle]);
        Text(eff, "Effort: " + Prefs.EffStyles[P.EffStyle]);
        Text(theme, "Theme: " + Shell.ThemeNames[P.Theme]);
        Text(backdrop, "Backdrop: " + Shell.BackdropNames[P.Backdrop]);
        state.Text = $"PROTOTYPE · {(Shell.Docked ? "Docked" : Shell.Raised ? "Widget raised" : "Widget on desktop")} · " +
                     $"{st.CountOpen} open (H{st.CountHigh} M{st.CountMedium} L{st.CountLow}) · {st.Done.Count} done · " +
                     $"order {(st.Manual ? "manual" : "ranked")} · processing {st.Processing} · dot {(st.Attention ? "on" : "off")} · " +
                     "tap Ctrl+Shift to raise / commit, Esc to drop";
    }

    static void Text(Button b, string t) => ((TextBlock)b.Content).Text = t;

    static StackPanel Row(params UIElement[] items)
    {
        var r = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var i in items) r.Children.Add(i);
        return r;
    }

    static TextBlock Label(double size, bool bold) => new()
    {
        FontSize = size, Foreground = new SolidColorBrush(bold ? Colors.Gold : Colors.Silver),
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, FontFamily = new FontFamily("Consolas"),
    };

    static Button Btn(string text, Action a)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 12, Foreground = new SolidColorBrush(Colors.Gold) },
            Padding = new Thickness(10, 4, 10, 4), MinHeight = 28,
        };
        b.Click += (_, _) => a();
        return b;
    }
}
