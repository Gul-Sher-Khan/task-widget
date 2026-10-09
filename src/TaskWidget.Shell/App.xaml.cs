using Microsoft.UI.Xaml;
using TaskWidget.Core;

namespace TaskWidget;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!InstanceGate.TryAcquire(InstanceGate.DefaultName, out var gate))
        {
            DesktopLayer.LetAnotherProcessTakeTheForeground();
            InstanceGate.SignalRaise(InstanceGate.DefaultName);
            Environment.Exit(0);
            return;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskWidget");
        var model = new AppModel(folder, new SystemClock(), new HttpClientHandler(), new SystemBrowser(), new DpapiProtector(), network: new SystemNetwork());
        var window = new MainWindow(model);
        gate.RaiseRequested += () => window.DispatcherQueue.TryEnqueue(window.Raise);
        model.BringToFront += () => window.DispatcherQueue.TryEnqueue(window.Raise);
        gate.Listen();
        window.Closed += (_, _) => gate.Dispose();
        window.Activate();
    }
}
