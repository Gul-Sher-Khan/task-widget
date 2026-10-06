// PROTOTYPE: throwaway.
using Microsoft.UI.Xaml.Controls;

namespace Look;

public sealed partial class DockC : UserControl, IDockView
{
    public Store S => Shell.Store;
    public DockSpot Spot => DockSpot.TopRight;
    public double TopOffset => 6;

    public DockC()
    {
        InitializeComponent();
        DockKit.WhileProcessing(this, G0, (v, on) => DockKit.Wave(v, on, 0));
        DockKit.WhileProcessing(this, G1, (v, on) => DockKit.Wave(v, on, 1));
        DockKit.WhileProcessing(this, G2, (v, on) => DockKit.Wave(v, on, 2));
    }
}
