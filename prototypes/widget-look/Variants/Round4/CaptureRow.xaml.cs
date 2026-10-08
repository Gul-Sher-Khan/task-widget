// PROTOTYPE: throwaway.
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Text;

namespace Look;

public sealed partial class CaptureRow : UserControl, INotifyPropertyChanged
{
    public Prefs P => Shell.Prefs;
    static Store S => Shell.Store;
    public event PropertyChangedEventHandler PropertyChanged;

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(TaskVm), typeof(CaptureRow), new PropertyMetadata(null, (d, e) => ((CaptureRow)d).OnItem((TaskVm)e.OldValue, (TaskVm)e.NewValue)));
    public TaskVm Item { get => (TaskVm)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }

    bool open;
    public bool Open { get => open; set { open = value; Raise(nameof(Open)); } }

    public CaptureRow()
    {
        InitializeComponent();
        foreach (var box in new[] { EditA, EditB, EditC }) box.PreviewKeyDown += Edit_KeyDown;
    }

    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    void OnItem(TaskVm old, TaskVm now)
    {
        if (old != null) old.PropertyChanged -= Item_Changed;
        if (now != null) now.PropertyChanged += Item_Changed;
        Open = false;
        Raise(nameof(EditHint)); Raise(nameof(SubmitText));
        if (now is { IsEditingCapture: true }) BeginEdit();
    }

    void Item_Changed(object s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskVm.IsEditingCapture) && Item.IsEditingCapture) BeginEdit();
        if (e.PropertyName is nameof(TaskVm.IsFailed)) { Raise(nameof(EditHint)); Raise(nameof(SubmitText)); }
    }

    // A failed row's Edit runs it again; a fresh Re-interpret replaces the Capture's Tasks.
    bool IsReinterpret => Item?.ReinterpretOf != null && !Item.IsFailed;
    public string SubmitText => IsReinterpret ? "Re-interpret" : "Retry";
    public string EditHint => IsReinterpret ? "Fix the text, then Enter to re-interpret · Esc to cancel" : "Fix the text, then Enter to retry · Esc to cancel";

    TextBox Box => P.Variant switch { 0 => EditA, 1 => EditB, _ => EditC };

    void BeginEdit()
    {
        var box = Box;
        box.Text = Item.Title;
        DispatcherQueue.TryEnqueue(() => { box.Focus(FocusState.Programmatic); box.SelectionStart = box.Text.Length; });
    }

    void Edit_KeyDown(object s, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { S.CancelCaptureEdit(Item); e.Handled = true; return; }
        if (e.Key != VirtualKey.Enter) return;
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)) return;
        S.SubmitCapture(Item, ((TextBox)s).Text);
        e.Handled = true;
    }

    public FontWeight Weight(bool primary) => primary ? FontWeights.SemiBold : FontWeights.Normal;
    public string Chevron(bool isOpen) => isOpen ? "" : "";

    void Toggle_Tapped(object s, TappedRoutedEventArgs e) { Open = !Open; e.Handled = true; }
    void Retry_Click(object s, RoutedEventArgs e) => S.Retry(Item);
    void Edit_Click(object s, RoutedEventArgs e) => S.EditCapture(Item);
    void Make_Click(object s, RoutedEventArgs e) => S.MakeAsIs(Item);
    void Discard_Click(object s, RoutedEventArgs e) => S.Discard(Item);
    void Submit_Click(object s, RoutedEventArgs e) => S.SubmitCapture(Item, Box.Text);
    void Cancel_Click(object s, RoutedEventArgs e) => S.CancelCaptureEdit(Item);
}
