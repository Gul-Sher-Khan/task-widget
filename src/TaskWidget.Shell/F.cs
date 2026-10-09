using Microsoft.UI.Xaml;

namespace TaskWidget;

public static class F
{
    public static Visibility Visible(bool shown) => shown ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hidden(bool hidden) => hidden ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility VisibleWhenBoth(bool first, bool second) =>
        first && second ? Visibility.Visible : Visibility.Collapsed;

    public static string TapLine(string hotkey) => $"Tap {hotkey.Replace(" + ", "+")} anywhere to capture";

    // Motion.xaml DimOpacity (0.45). Re-interpret fades the Capture's old Tasks to this.
    public static double Dim(bool dimmed) =>
        dimmed ? (double)Application.Current.Resources["DimOpacity"] : 1;

    public static string DockName(bool processing) =>
        processing ? "Interpreting" : "Capture a thought";

    public static string HotkeyButton(string hotkey, bool recording) =>
        recording ? "Press keys" : "Hotkey, " + hotkey;
}
