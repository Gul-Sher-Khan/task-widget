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
    readonly Button contrast, slow, theme, backdrop;
    // Round 4
    readonly Button variant, scene, signIn, outcome, offline, hold, dictation;
    readonly TextBlock note = Label(11, true);
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

        contrast = Btn("", () => { P.ContrastPreview = !P.ContrastPreview; Shell.Apply(); });
        slow = Btn("", () => { P.SlowMo = !P.SlowMo; Shell.Apply(); });
        theme = Btn("", () => P.Theme = (P.Theme + 1) % 3);
        backdrop = Btn("", () => P.Backdrop = (P.Backdrop + 1) % 4);

        var row1 = Row(theme, backdrop, contrast, slow);
        var row2 = Row(
            Btn("Fake Capture", () => { Shell.Store.Capture(Samples[sample++ % Samples.Length]); Refresh(); }),
            Btn("Attention dot", () => { Shell.Store.Attention = !Shell.Store.Attention; Refresh(); }),
            Btn("Dock / Widget", () => { if (Shell.Docked) Shell.Expand(); else Shell.Collapse(); }),
            Btn("Settings", () => { if (Shell.Docked) Shell.Expand(); P.ShowSettings = !P.ShowSettings; }),
            Btn("3 / 9 / 15 Tasks", () => { int n = Shell.Store.CountOpen; Shell.Store.Seed(n < 6 ? 9 : n < 12 ? 15 : 3); Refresh(); }),
            Btn("✕ Quit", () => Application.Current.Exit()));

        // Round 4: variant, scene, and what the simulated sign-in / Capture / dictation do next.
        variant = Btn("", () => { P.Variant = (P.Variant + 1) % 3; Shell.Apply(); });
        scene = Btn("", () => Shell.Scene((Shell.SceneIndex + 1) % Shell.Scenes.Length));
        signIn = Btn("", () => { P.SignInOutcome = (P.SignInOutcome + 1) % Prefs.SignInOutcomes.Length; Refresh(); });
        outcome = Btn("", () => { P.CaptureOutcome = (P.CaptureOutcome + 1) % Prefs.CaptureOutcomes.Length; Refresh(); });
        offline = Btn("", () => { P.Offline = !P.Offline; if (!P.Offline) Shell.Store.RunWaiting(); });
        hold = Btn("", () => { P.HoldPending = !P.HoldPending; if (!P.HoldPending) Shell.Store.ReleaseHeld(); Refresh(); });
        dictation = Btn("", () => { P.DictationArrives = !P.DictationArrives; Refresh(); });
        var row3 = Row(variant, scene, signIn, outcome);
        var row4 = Row(offline, hold, dictation, Btn("Tap with empty box", Shell.SimulateDictation));

        var col = new StackPanel { Spacing = 5, Padding = new Thickness(12, 8, 12, 8) };
        col.Children.Add(row1);
        col.Children.Add(row2);
        col.Children.Add(row3);
        col.Children.Add(row4);
        col.Children.Add(state);
        col.Children.Add(note);
        Content = new Grid { Background = new SolidColorBrush(ColorHelper.FromArgb(255, 18, 18, 18)), Children = { col }, RequestedTheme = ElementTheme.Dark };

        double s = Native.Scale(h);
        var wa = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int w = (int)(900 * s), hh = (int)(196 * s);
        AppWindow.MoveAndResize(new RectInt32(wa.X + (wa.Width - w) / 2, wa.Y + wa.Height - hh - (int)(16 * s), w, hh));
        Shell.Store.PropertyChanged += (_, _) => Refresh();
        P.PropertyChanged += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var st = Shell.Store;
        var P = Shell.Prefs;
        Text(contrast, "Contrast preview: " + (P.ContrastPreview ? "on" : "off"));
        Text(slow, "Slow-mo (5x): " + (P.SlowMo ? "on" : "off"));
        Text(theme, "Theme: " + Shell.ThemeNames[P.Theme]);
        Text(backdrop, "Backdrop: " + Shell.BackdropNames[P.Backdrop]);
        Text(variant, "Round 4 variant: " + Prefs.VariantNames[P.Variant]);
        Text(scene, "Scene: " + Shell.Scenes[Shell.SceneIndex]);
        Text(signIn, "Sign-in: " + Prefs.SignInOutcomes[P.SignInOutcome]);
        Text(outcome, "Next Capture: " + Prefs.CaptureOutcomes[P.CaptureOutcome]);
        Text(offline, "Offline: " + (P.Offline ? "on" : "off"));
        Text(hold, "Hold pending: " + (P.HoldPending ? "on" : "off"));
        Text(dictation, "Dictation: " + (P.DictationArrives ? "arrives" : "never comes"));
        state.Text = $"PROTOTYPE · {(Shell.Docked ? "Docked" : Shell.Raised ? "Widget raised" : "Widget on desktop")} · " +
                     $"{(Shell.SystemContrast ? "Windows contrast theme · " : "")}{st.CountOpen} open (H{st.CountHigh} M{st.CountMedium} L{st.CountLow}) · {st.Done.Count} done · " +
                     $"order {(st.Manual ? "manual" : "ranked")} · processing {st.Processing} · dot {(st.Attention ? "on" : "off")} · " +
                     "tap Ctrl+Shift to raise / commit, Esc to drop";
    }

    public void Note(string text) => note.Text = text;

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
