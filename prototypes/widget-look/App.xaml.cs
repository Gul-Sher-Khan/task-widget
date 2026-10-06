// PROTOTYPE: throwaway. Not production code.
using Microsoft.UI.Xaml;

namespace Look;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => System.IO.File.AppendAllText(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "widget-look-debug.log"), $"UNHANDLED {e.Exception}\n");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args) => Shell.Start();
}
