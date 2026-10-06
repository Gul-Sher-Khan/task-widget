// PROTOTYPE: row behaviour shared by every variant (click to expand, keys, drag, inline edit, context menu, undo keys).
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Look;

public static class RowKit
{
    static Store S => Shell.Store;
    public static TaskVm Item(object sender) => (sender as FrameworkElement)?.DataContext as TaskVm;

    public static void Attach(ListView lv, UIElement root)
    {
        lv.SelectionMode = ListViewSelectionMode.None;
        lv.IsItemClickEnabled = true;
        lv.ItemClick += (_, e) => { var t = (TaskVm)e.ClickedItem; if (!t.IsEditing && !t.IsPending) S.ToggleExpand(t); };

        TaskVm dragged = null;
        int from = -1;
        lv.DragItemsStarting += (_, e) =>
        {
            dragged = e.Items.FirstOrDefault() as TaskVm;
            if (dragged == null || dragged.IsPending || S.ShowDone) { e.Cancel = true; dragged = null; return; }
            from = S.Open.IndexOf(dragged);
        };
        lv.DragItemsCompleted += (_, _) => { if (dragged != null) S.DragMoved(dragged, from); dragged = null; };

        lv.KeyDown += (_, e) =>
        {
            if (FocusManager.GetFocusedElement(lv.XamlRoot) is not ListViewItem c || lv.ItemFromContainer(c) is not TaskVm t) return;
            bool ctrl = Ctrl;
            switch (e.Key)
            {
                case VirtualKey.Space: S.ToggleComplete(t); break;
                case VirtualKey.Enter: S.ToggleExpand(t); break;
                case VirtualKey.Delete: S.Delete(t); break;
                case VirtualKey.E or VirtualKey.F2: t.IsEditing = true; break;
                case VirtualKey.P: S.CyclePriority(t); break;
                case VirtualKey.Up when ctrl: S.Move(t, -1); Refocus(lv, t); break;
                case VirtualKey.Down when ctrl: S.Move(t, 1); Refocus(lv, t); break;
                default: return;
            }
            e.Handled = true;
        };

        lv.RightTapped += (_, e) =>
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is not TaskVm t || t.IsPending) return;
            var menu = new MenuFlyout();
            menu.Items.Add(Item("Edit title", "", () => t.IsEditing = true));
            menu.Items.Add(Item("Re-interpret Capture…", "", () => { }));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("Delete", "", () => S.Delete(t)));
            menu.ShowAt(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
            e.Handled = true;
        };

        root.KeyDown += (_, e) =>
        {
            if (FocusManager.GetFocusedElement(lv.XamlRoot) is TextBox) return;
            if (!Ctrl) return;
            if (e.Key == VirtualKey.Z) { S.Undo(); e.Handled = true; }
            else if (e.Key == VirtualKey.Y) { S.Redo(); e.Handled = true; }
        };
    }

    static bool Ctrl => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

    static void Refocus(ListView lv, TaskVm t) =>
        lv.DispatcherQueue.TryEnqueue(() => (lv.ContainerFromItem(t) as ListViewItem)?.Focus(FocusState.Keyboard));

    static MenuFlyoutItem Item(string text, string glyph, System.Action a)
    {
        var i = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        i.Click += (_, _) => a();
        return i;
    }

    // Capture box: Enter commits, Shift+Enter is a newline (Wispr Flow research).
    public static void AttachCapture(TextBox box)
    {
        box.AcceptsReturn = true;
        box.TextWrapping = TextWrapping.Wrap;
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Enter) return;
            bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if (shift) return;
            Shell.CommitCapture();
            e.Handled = true;
        };
    }

    // ---- handlers referenced from the variants' DataTemplates ----
    public static void Check(object s) { if (Item(s) is { } t) S.ToggleComplete(t); }
    public static void Pri(object s) { if (Item(s) is { } t) S.CyclePriority(t); }
    public static void Eff(object s) { if (Item(s) is { } t) S.CycleEffort(t); }
    public static void BeginEdit(object s) { if (Item(s) is { IsPending: false } t) t.IsEditing = true; }

    public static void EditLoaded(object s)
    {
        var tb = (TextBox)s;
        tb.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (tb.Visibility != Visibility.Visible) return;
            tb.Text = Item(tb)?.Title ?? "";
            tb.DispatcherQueue.TryEnqueue(() => { tb.Focus(FocusState.Programmatic); tb.SelectAll(); });
        });
        tb.KeyDown += (_, e) =>
        {
            var t = Item(tb);
            if (t == null) return;
            if (e.Key == VirtualKey.Enter) { S.Rename(t, tb.Text); t.IsEditing = false; e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { t.IsEditing = false; e.Handled = true; }
        };
        tb.LostFocus += (_, _) => { var t = Item(tb); if (t is { IsEditing: true }) { S.Rename(t, tb.Text); t.IsEditing = false; } };
    }
}
