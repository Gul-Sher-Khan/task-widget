using Microsoft.UI.Xaml;
using TaskWidget.Core;
using Windows.UI.Text;

namespace TaskWidget;

public static class Row
{
    public static Visibility WhenHigh(Priority priority) =>
        priority == Priority.High ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility WhenMedium(Priority priority) =>
        priority == Priority.Medium ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility WhenLow(Priority priority) =>
        priority == Priority.Low ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility WhenDetails(string details) =>
        string.IsNullOrEmpty(details) ? Visibility.Collapsed : Visibility.Visible;

    public static string PriorityTip(Priority priority) => $"{priority} priority";

    public static string EffortLabel(Effort effort) => effort switch
    {
        Effort.Quick => "15m",
        Effort.Short => "1h",
        _ => "1h+",
    };

    public static string EffortTip(Effort effort) => effort switch
    {
        Effort.Quick => "Quick · 15 min or less",
        Effort.Short => "Short · up to an hour",
        _ => "Long · more than an hour",
    };

    public static string CountText(int count) => count.ToString();

public static Visibility When(bool visible) =>
        visible ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Unless(bool hidden) =>
        hidden ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility WhenPositive(int count) =>
        count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static TextDecorations Strike(bool striking) =>
        striking ? TextDecorations.Strikethrough : TextDecorations.None;

    public static double DoneFade(bool done, bool striking) => done || striking ? 0.5 : 1;

    public static Visibility WhenText(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    // Prototype Prefs.ListMaxHeight: a one-line row is ~37 DIP.
    public static double ListMaxHeight(int rows) => rows * 37 + 4;
}
