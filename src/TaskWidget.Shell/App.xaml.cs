using Microsoft.UI.Xaml;
using TaskWidget.Core;

namespace TaskWidget;

public partial class App : Application
{
    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        LookLaunch.Prepare();
        var gateName = LookLaunch.Active ? LookLaunch.GateName : InstanceGate.DefaultName;
        if (!InstanceGate.TryAcquire(gateName, out var gate))
        {
            DesktopLayer.LetAnotherProcessTakeTheForeground();
            InstanceGate.SignalRaise(gateName);
            Environment.Exit(0);
            return;
        }

        var folder = LookLaunch.Active
            ? LookLaunch.Folder
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskWidget");
        var model = new AppModel(
            folder,
            new SystemClock(),
            LookLaunch.CreateHandler(),
            new SystemBrowser(),
            new DpapiProtector(),
            network: LookLaunch.CreateNetwork());
        LookLaunch.AfterModel(model);
        var window = new MainWindow(model);
        gate.RaiseRequested += () => window.DispatcherQueue.TryEnqueue(window.Raise);
        model.BringToFront += () => window.DispatcherQueue.TryEnqueue(window.Raise);
        gate.Listen();
        window.Closed += (_, _) => gate.Dispose();
        window.Activate();
        if (LookLaunch.Active)
            window.DispatcherQueue.TryEnqueue(window.ApplyScreenshotOverrides);
    }
}
