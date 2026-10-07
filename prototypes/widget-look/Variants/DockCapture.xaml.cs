// PROTOTYPE: code-behind for the Dock ("Capture bar", picked in round 2).
using Microsoft.UI.Xaml.Controls;

namespace Look;

public sealed partial class DockCapture : UserControl, IDockView
{
    public Store S => Shell.Store;
    public DockSpot Spot => DockSpot.TopCenter;
    public double TopOffset => 8;
    public bool OpensCapture => true;

    public DockCapture() => InitializeComponent();
}
