using Microsoft.UI.Windowing;

namespace TaskWidget;

// The window icon, from the .ico that also gives the .exe and the installer theirs (assets/icon).
static class AppIcon
{
    static readonly string Path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TaskWidget.ico");

    public static void Apply(AppWindow window)
    {
        if (File.Exists(Path))
            window.SetIcon(Path);
    }
}
