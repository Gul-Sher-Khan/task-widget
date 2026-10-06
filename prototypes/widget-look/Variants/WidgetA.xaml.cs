// PROTOTYPE: throwaway.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Look;

public sealed partial class WidgetA : UserControl, IWidgetView
{
    public Store S => Shell.Store;
    public TextBox CaptureBox => Capture;

    public WidgetA()
    {
        InitializeComponent();
        RowKit.Attach(List, this);
        RowKit.AttachCapture(Capture);
    }

    void Resort_Click(object s, RoutedEventArgs e) => S.Resort();
    void Done_Click(object s, RoutedEventArgs e) => S.ShowDone = !S.ShowDone;
    void Collapse_Click(object s, RoutedEventArgs e) => Shell.Collapse();
    void Check_Click(object s, RoutedEventArgs e) => RowKit.Check(s);
    void Pri_Click(object s, RoutedEventArgs e) => RowKit.Pri(s);
    void Eff_Click(object s, RoutedEventArgs e) => RowKit.Eff(s);
    void Title_DoubleTapped(object s, DoubleTappedRoutedEventArgs e) { RowKit.BeginEdit(s); e.Handled = true; }
    void Edit_Loaded(object s, RoutedEventArgs e) => RowKit.EditLoaded(s);
}
