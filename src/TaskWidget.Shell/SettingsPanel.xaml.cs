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
