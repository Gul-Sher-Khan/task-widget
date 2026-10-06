// PROTOTYPE: throwaway.
using Microsoft.UI.Xaml.Controls;

namespace Look;

public sealed partial class DockB : UserControl, IDockView
{
    public Store S => Shell.Store;
    public DockSpot Spot => DockSpot.TopCenter;
    public double TopOffset => -9; // hides the window's top rounded corners above the screen edge

    public DockB()
    {
        InitializeComponent();
        DockKit.WhileProcessing(this, Bar, (v, on) => DockKit.Sweep(v, on, (float)(ActualWidth - 24)));
    }
}
