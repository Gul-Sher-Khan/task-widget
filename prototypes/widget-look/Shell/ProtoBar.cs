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

public sealed class ProtoBar : Window
{
    readonly TextBlock variant = Label(15, true), state = Label(11, false);
    readonly Button theme, backdrop;
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

        theme = Btn("", () => { Shell.ThemeMode = (Shell.ThemeMode + 1) % 3; Shell.Apply(); });
        backdrop = Btn("", () => { Shell.Backdrop = (Shell.Backdrop + 1) % 4; Shell.Apply(); });

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        row1.Children.Add(Btn("◀", () => Cycle(-1)));
        variant.Width = 150; variant.TextAlignment = TextAlignment.Center; variant.VerticalAlignment = VerticalAlignment.Center;
        row1.Children.Add(variant);
        row1.Children.Add(Btn("▶", () => Cycle(1)));
        row1.Children.Add(new Border { Width = 1, Background = new SolidColorBrush(Colors.DimGray), Margin = new Thickness(4, 6, 4, 6) });
        row1.Children.Add(theme);
        row1.Children.Add(backdrop);
        row1.Children.Add(Btn("Fake Capture", () => { Shell.Store.Capture(Samples[sample++ % Samples.Length]); Refresh(); }));
        row1.Children.Add(Btn("Attention dot", () => { Shell.Store.Attention = !Shell.Store.Attention; Refresh(); }));
        row1.Children.Add(Btn("Dock / Widget", () => { if (Shell.Docked) Shell.Expand(); else Shell.Collapse(); }));
        row1.Children.Add(Btn("3 / 9 / 15 Tasks", () => { int n = Shell.Store.CountOpen; Shell.Store.Seed(n < 6 ? 9 : n < 12 ? 15 : 3); Refresh(); }));

        var col = new StackPanel { Spacing = 4, Padding = new Thickness(12, 8, 12, 8) };
        col.Children.Add(row1);
        col.Children.Add(state);
        Content = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 18, 18, 18)), Children = { col }, RequestedTheme = ElementTheme.Dark };

        double s = Native.Scale(h);
        var wa = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = (int)(1180 * s), hh = (int)(78 * s);
        AppWindow.MoveAndResize(new RectInt32(wa.X + (wa.Width - w) / 2, wa.Y + wa.Height - hh - (int)(16 * s), w, hh));
        Shell.Store.PropertyChanged += (_, _) => Refresh();
    }

    void Cycle(int d)
    {
        Shell.Variant = (Shell.Variant + d + Shell.VariantNames.Length) % Shell.VariantNames.Length;
        Shell.Apply();
    }

    public void Refresh()
    {
        var st = Shell.Store;
        variant.Text = Shell.VariantNames[Shell.Variant];
        ((TextBlock)theme.Content).Text = "Theme: " + Shell.ThemeNames[Shell.ThemeMode];
        ((TextBlock)backdrop.Content).Text = "Backdrop: " + Shell.BackdropNames[Shell.Backdrop];
        state.Text = $"PROTOTYPE · {(Shell.Docked ? "Docked" : Shell.Raised ? "Widget raised" : "Widget on desktop")} · " +
                     $"{st.CountOpen} open (H{st.CountHigh} M{st.CountMedium} L{st.CountLow}) · {st.Done.Count} done · " +
                     $"order {(st.Manual ? "manual" : "ranked")} · processing {st.Processing} · dot {(st.Attention ? "on" : "off")} · " +
                     "tap Ctrl+Shift to raise / commit, Esc to drop";
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
            Content = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = 12 },
            Padding = new Thickness(10, 4, 10, 4), MinHeight = 28,
        };
        b.Click += (_, _) => a();
        return b;
    }
}
