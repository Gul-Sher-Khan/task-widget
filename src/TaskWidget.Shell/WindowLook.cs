using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TaskWidget.Core;
using WinRT;

namespace TaskWidget;

// One window's theme and backdrop, as the prototype's HostWindow.ApplyLook does it.
// Solid paints Tokens.xaml's SurfaceSolidBrush for the current theme; a live backdrop leaves the host transparent.
sealed partial class WindowLook : IDisposable
{
    readonly Window window;
    readonly Panel host;
    ISystemBackdropControllerWithTargets? controller;

    public WindowLook(Window window, Panel host)
    {
        this.window = window;
        this.host = host;
    }

    public void Apply(ElementTheme theme, bool dark, bool contrast, BackdropChoice backdrop)
    {
        host.RequestedTheme = theme;
        Release();

        if (backdrop != BackdropChoice.Solid)
        {
            try
            {
                controller = backdrop switch
                {
                    BackdropChoice.MicaAlt => new MicaController { Kind = MicaKind.BaseAlt },
                    BackdropChoice.Acrylic => new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base },
                    _ => new MicaController { Kind = MicaKind.Base },
                };
                controller.AddSystemBackdropTarget(window.As<ICompositionSupportsSystemBackdrop>());
                controller.SetSystemBackdropConfiguration(new SystemBackdropConfiguration
                {
                    // Kept true so Mica and Acrylic stay live when the window is inactive.
                    IsInputActive = true,
                    Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light,
                });
                host.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                return;
            }
            catch (COMException)
            {
                Release();
            }
        }

        host.Background = Surface(contrast ? "HighContrast" : dark ? "Dark" : "Light");
    }

    public void Dispose() => Release();

    void Release()
    {
        if (controller is null)
            return;
        controller.RemoveAllSystemBackdropTargets();
        controller.Dispose();
        controller = null;
    }

    static Brush Surface(string theme)
    {
        foreach (var dictionary in Application.Current.Resources.MergedDictionaries)
        {
            if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed)
                && themed is ResourceDictionary tokens
                && tokens.TryGetValue("SurfaceSolidBrush", out var brush))
                return (Brush)brush;
        }

        return (Brush)Application.Current.Resources["SurfaceSolidBrush"];
    }
}
