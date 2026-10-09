using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskWidget.Core;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Text;

namespace TaskWidget;

public sealed partial class CaptureRow : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(TaskRow), typeof(CaptureRow), new PropertyMetadata(null, (d, e) => ((CaptureRow)d).OnItem((TaskRow?)e.OldValue, (TaskRow?)e.NewValue)));

    public CaptureRow()
    {
        InitializeComponent();
        Edit.PreviewKeyDown += Edit_KeyDown;
    }

    public TaskRow? Item
    {
        get => (TaskRow?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public string EditHint => Item is { IsReinterpret: true, IsFailed: false }
        ? "Fix the text, then Enter to re-interpret · Esc to cancel"
        : "Fix the text, then Enter to retry · Esc to cancel";

    public FontWeight Weight(bool primary) => primary ? FontWeights.SemiBold : FontWeights.Normal;

    void OnItem(TaskRow? old, TaskRow? now)
    {
        if (old is not null)
            old.PropertyChanged -= Item_Changed;
        if (now is not null)
            now.PropertyChanged += Item_Changed;
        Hint.Text = EditHint;
        if (now is { IsEditingCapture: true })
            BeginEdit();
    }

    void Item_Changed(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskRow.IsEditingCapture) or nameof(TaskRow.IsFailed) or nameof(TaskRow.IsReinterpret))
            Hint.Text = EditHint;
        if (e.PropertyName == nameof(TaskRow.IsEditingCapture) && Item is { IsEditingCapture: true })
            BeginEdit();
    }

    void BeginEdit()
    {
        if (Item is null)
            return;

        Edit.Text = Item.Title;
        DispatcherQueue.TryEnqueue(() =>
        {
            Edit.Focus(FocusState.Programmatic);
            Edit.SelectionStart = Edit.Text.Length;
        });
    }

    void Edit_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Item is null)
            return;

        if (e.Key == VirtualKey.Escape)
        {
            FindModel()?.CancelCaptureEdit(Item);
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Enter)
            return;
        if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
            return;

        _ = FindModel()?.SubmitCapture(Item, Edit.Text);
        e.Handled = true;
    }

    void Retry_Click(object sender, RoutedEventArgs e)
    {
        if (Item is not null)
            _ = FindModel()?.Retry(Item);
    }

    void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (Item is not null)
            FindModel()?.EditCapture(Item);
    }

    void Make_Click(object sender, RoutedEventArgs e)
    {
        if (Item is not null)
            FindModel()?.MakeTaskAsIs(Item);
    }

    void Discard_Click(object sender, RoutedEventArgs e)
    {
        if (Item is not null)
            FindModel()?.Discard(Item);
    }

    AppModel? FindModel()
    {
        DependencyObject? node = this;
        while (node is not null)
        {
            if (node is FrameworkElement element && element.DataContext is AppModel model)
                return model;
            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }
}
