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
        var model = new AppModel(folder, new SystemClock(), new HttpClientHandler(), new SystemBrowser(), new DpapiProtector());
        var window = new MainWindow(model);
        model.BringToFront += () => window.DispatcherQueue.TryEnqueue(() => window.Activate());
        window.Activate();
    }
}
