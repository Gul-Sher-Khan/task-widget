// PROTOTYPE: throwaway stack spike. Not production code.
using Microsoft.UI.Xaml;

namespace Spike;

public partial class App : Application
{
    MainWindow window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Log($"UNHANDLED {e.Message} :: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            window = new MainWindow();
            window.Activate();
        }
        catch (System.Exception ex)
        {
            Log($"STARTUP {ex}");
            throw;
        }
    }

    static void Log(string line) => System.IO.File.AppendAllText(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "stack-spike-debug.log"), line + "\n");
}
