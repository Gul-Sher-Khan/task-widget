using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskWidget.Core;

namespace TaskWidget;

public sealed partial class BannerView : UserControl
{
    public BannerView(AppModel model)
    {
        Model = model;
        InitializeComponent();
    }

    public AppModel Model { get; }

    void Primary_Click(object sender, RoutedEventArgs e) => Model.SettingsOpen = true;
}
