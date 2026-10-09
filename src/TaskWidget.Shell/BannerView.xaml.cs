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
        if (Model.Banner == WidgetBanner.SignedOut)
        {
            if (Model.SignInWaiting)
                Model.CancelSignIn();
            else
                _ = Model.SignIn();
            return;
        }

        Model.SettingsOpen = true;
    }
}
