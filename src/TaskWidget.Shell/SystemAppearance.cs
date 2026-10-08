using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using TaskWidget.Core;
using Windows.UI.ViewManagement;

namespace TaskWidget;

// Listens for dark/light, accent and contrast changes. HighContrastChanged throws when the app is unpackaged.
sealed partial class SystemAppearance : IDisposable
{
    readonly AppModel model;
    readonly Action contrastChanged;
    readonly UISettings ui = new();
    readonly AccessibilitySettings accessibility = new();
    readonly DispatcherQueue? queue;
    int disposed;

    public SystemAppearance(AppModel model, Action contrastChanged)
    {
        this.model = model;
        this.contrastChanged = contrastChanged;
        queue = DispatcherQueue.GetForCurrentThread();
        ui.ColorValuesChanged += OnColorsChanged;
        Publish();
    }

    public bool ContrastTheme { get; private set; }

    public void Publish()
    {
        if (Volatile.Read(ref disposed) != 0)
            return;

        bool wasContrast = ContrastTheme;
        ContrastTheme = accessibility.HighContrast;
        bool dark = ui.GetColorValue(UIColorType.Background).R < 128;
        model.ReportSystemAppearance(dark, MaterialsAvailable());
        if (wasContrast != ContrastTheme)
            contrastChanged();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        ui.ColorValuesChanged -= OnColorsChanged;
    }

    void OnColorsChanged(UISettings sender, object args)
    {
        if (queue is null || queue.HasThreadAccess)
            Publish();
        else
            queue.TryEnqueue(Publish);
    }

    bool MaterialsAvailable()
    {
        if (ContrastTheme)
            return false;

        bool mica = MicaController.IsSupported();
        if (model.Backdrop == BackdropChoice.Acrylic)
            return mica && DesktopAcrylicController.IsSupported();
        return mica;
    }
}
