// PROTOTYPE: throwaway.
using Microsoft.UI.Xaml.Controls;

namespace Look;

public sealed partial class DockA : UserControl, IDockView
{
    public Store S => Shell.Store;
    public DockSpot Spot => DockSpot.TopCenter;
    public double TopOffset => 6;

    public DockA()
    {
        InitializeComponent();
        // Processing: the icons breathe.
        DockKit.WhileProcessing(this, Icons, DockKit.Breathe);
    }
}
