using Microsoft.UI.Xaml;
using TaskWidget.Core;

namespace TaskWidget;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskWidget");
        var window = new MainWindow(new AppModel(folder, new SystemClock()));
        window.Activate();
    }
}
