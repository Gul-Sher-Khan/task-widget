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

    public CaptureRow()
    {
        InitializeComponent();
        Edit.PreviewKeyDown += Edit_KeyDown;
    }

    void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    void OnItem(TaskVm old, TaskVm now)
    {
        if (old != null) old.PropertyChanged -= Item_Changed;
        if (now != null) now.PropertyChanged += Item_Changed;
        Raise(nameof(EditHint));
        if (now is { IsEditingCapture: true }) BeginEdit();
    }

    void Item_Changed(object s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TaskVm.IsEditingCapture) && Item.IsEditingCapture) BeginEdit();
        if (e.PropertyName is nameof(TaskVm.IsFailed)) Raise(nameof(EditHint));
    }

    // A failed row's Edit runs it again; a fresh Re-interpret replaces the Capture's Tasks.
    bool IsReinterpret => Item?.ReinterpretOf != null && !Item.IsFailed;
    public string EditHint => IsReinterpret ? "Fix the text, then Enter to re-interpret · Esc to cancel" : "Fix the text, then Enter to retry · Esc to cancel";

    void BeginEdit()
    {
        Edit.Text = Item.Title;
        DispatcherQueue.TryEnqueue(() => { Edit.Focus(FocusState.Programmatic); Edit.SelectionStart = Edit.Text.Length; });
    }

    void Edit_KeyDown(object s, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { S.CancelCaptureEdit(Item); e.Handled = true; return; }
        if (e.Key != VirtualKey.Enter) return;
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down)) return;
        S.SubmitCapture(Item, Edit.Text);
        e.Handled = true;
    }

    public FontWeight Weight(bool primary) => primary ? FontWeights.SemiBold : FontWeights.Normal;

    void Retry_Click(object s, RoutedEventArgs e) => S.Retry(Item);
    void Edit_Click(object s, RoutedEventArgs e) => S.EditCapture(Item);
    void Make_Click(object s, RoutedEventArgs e) => S.MakeAsIs(Item);
    void Discard_Click(object s, RoutedEventArgs e) => S.Discard(Item);
}
