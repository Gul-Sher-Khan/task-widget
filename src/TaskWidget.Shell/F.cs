using Microsoft.UI.Xaml;

namespace TaskWidget;

public static class F
{
    public static Visibility V(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility NV(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static string TapLine(string hotkey) => $"Tap {hotkey.Replace(" + ", "+")} anywhere to capture";
}
