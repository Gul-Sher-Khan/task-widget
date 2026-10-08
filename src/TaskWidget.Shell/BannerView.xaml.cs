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

    void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Banner is WidgetBanner.Recovered or WidgetBanner.StartedEmpty)
            Model.OpenDataFolder();
        else if (Model.Banner == WidgetBanner.SignedOut)
            _ = Model.SignIn();
        else
            Model.SettingsOpen = true;
    }

    void Dismiss_Click(object sender, RoutedEventArgs e) => Model.DismissBanner();
}
