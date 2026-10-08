using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using TaskWidget.Core;

namespace TaskWidget;

public sealed partial class SettingsPanel : UserControl
{
    bool ready;

    public SettingsPanel(AppModel model)
    {
        Model = model;
        InitializeComponent();
        Loaded += (_, _) => ready = true;
    }

    public AppModel Model { get; }

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

    void CheckNow_Click(object sender, RoutedEventArgs e) => _ = Model.CheckForUpdates();

    void Update_Click(object sender, RoutedEventArgs e) => _ = Model.StartUpdate();

    void Auto_Toggled(object sender, RoutedEventArgs e)
    {
        if (!ready)
            return;

        Model.AutoUpdate = Auto.IsOn;
    }
}
