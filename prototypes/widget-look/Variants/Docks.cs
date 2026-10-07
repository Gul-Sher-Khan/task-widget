// PROTOTYPE: code-behind for the three Dock designs.
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Look;

public sealed partial class DockStatus : UserControl, IDockView
{
    public Store S => Shell.Store;
    public DockSpot Spot => DockSpot.TopCenter;
    public double TopOffset => 8;
    public bool OpensCapture => false;

    public DockStatus()
    {
        InitializeComponent();
        DockKit.WhileProcessing(this, Bar, (v, on) => DockKit.Sweep(v, on, (float)(ActualWidth - 40)));
    }
}

public sealed partial class DockNext : UserControl, IDockView, INotifyPropertyChanged
{
    public Store S => Shell.Store;
    public DockSpot Spot => DockSpot.TopCenter;
    public double TopOffset => 8;
    public bool OpensCapture => false;
    public event PropertyChangedEventHandler PropertyChanged;

    TaskVm top;
    public string TopTitle => top?.Title ?? "";
    public bool Striking => top?.IsStriking ?? false;

    public DockNext()
    {
        InitializeComponent();
        DockKit.WhileProcessing(this, Bar, (v, on) => DockKit.Sweep(v, on, (float)(ActualWidth - 40)));
        PropertyChangedEventHandler storeChanged = (_, e) => { if (e.PropertyName == nameof(Store.Top)) Track(); };
        Loaded += (_, _) => { S.PropertyChanged += storeChanged; Track(); };
        Unloaded += (_, _) => S.PropertyChanged -= storeChanged;
    }

    void Track()
    {
        if (top != null) top.PropertyChanged -= TopChanged;
        top = S.Top;
        if (top != null) top.PropertyChanged += TopChanged;
        Refresh();
    }

    void TopChanged(object s, PropertyChangedEventArgs e) => Refresh();

    void Refresh()
    {
        if (top != null) { Pri.Priority = top.Priority; Eff.Effort = top.Effort; }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TopTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Striking)));
    }

    void Tick_Click(object s, RoutedEventArgs e) { if (top != null) S.ToggleComplete(top); }
}

public sealed partial class DockCapture : UserControl, IDockView
{
    public Store S => Shell.Store;
    public DockSpot Spot => DockSpot.TopCenter;
    public double TopOffset => 8;
    public bool OpensCapture => true;

    public DockCapture() => InitializeComponent();
}
