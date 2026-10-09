using System.ComponentModel;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TaskWidget.Core;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using WinRT;

namespace TaskWidget;

public sealed partial class MainWindow : Window
{
    const double MarginDip = 28;
    const double DockTopDip = 8;
    // The prototype rolls the Widget up to, and unrolls it from, this height.
    const double RolledDip = 36;

    SystemAppearance? appearance;
    readonly WindowLook look;
    readonly DockWindow dock;
    bool widgetHidden;
    double current;
    double from;
    double target;
    double widgetHeight;
    double landHeight;
    double animMs;
    long animStart;
    bool rendering;
    bool placed;
    bool finishing;
    int dragFrom = -1;
    bool driving;
    bool slide;
    bool headerDrag;
    bool arriving;
    bool arranging;
    bool capping;
    bool sizingDpi;
    double windowCap = double.PositiveInfinity;
    double snapX1;
    double snapY1;
    double snapX2;
    double snapY2;
    readonly List<MonitorSnap> screens = [];
    RectInt32 fromRect;
    RectInt32 toRect;
    Action? animDone;
    readonly DesktopLayer desktop;
    readonly CaptureHotkey hotkey;

    public MainWindow(AppModel model)
    {
        Model = model;
        Row.AllowEdits = model.Editable;
        InitializeComponent();
        Host.DataContext = model;
        var settings = new SettingsPanel(model);
        SettingsHost.Children.Add(settings);
        BannerHost.Children.Add(new BannerView(model));
        Title = "Task Widget";

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        look = new WindowLook(this, Host);
        dock = new DockWindow(model, DockClient);

        desktop = new DesktopLayer(this, Model, Reanchor);
        desktop.DisplayChanged += Reanchor;
        desktop.WorkAreaChanged += Reanchor;
        desktop.DpiChanged += OnDpi;
        hotkey = new CaptureHotkey(this, Model, () => Capture.Text);
        settings.RecordingChanged = value => hotkey.Recording = value;
        AppWindow.Closing += (_, _) => desktop.AllowClose();

        // The Widget lays out off screen until it is placed on its anchor, so start-up never flashes a
        // default-sized window. Starting docked, it then hides and only the Dock appears.
        AppWindow.Move(new PointInt32(-32000, -32000));

        Capture.PreviewKeyDown += Capture_PreviewKeyDown;
        Host.PreviewKeyDown += Host_PreviewKeyDown;
        List.PreviewKeyDown += List_PreviewKeyDown;
        List.RightTapped += List_RightTapped;
        List.MaxHeight = Model.ListMaxHeight;
        List.DragItemsStarting += List_DragItemsStarting;
        List.DragItemsCompleted += List_DragItemsCompleted;
        Root.KeyDown += Root_KeyDown;
        ApplyUndoMotion();
        Host.SizeChanged += (_, _) => OnHostSize();
        Model.PropertyChanged += OnModelPropertyChanged;
        Model.RaiseRequested += OnRaiseAgain;
        desktop.Deactivated += OnShellDeactivated;
        desktop.FocusChanged += Model.NoteFocus;
        appearance = new SystemAppearance(Model, () =>
        {
            if (placed)
                ApplyLook();
        });
        Closed += (_, _) =>
        {
            Model.PropertyChanged -= OnModelPropertyChanged;
            Model.RaiseRequested -= OnRaiseAgain;
            desktop.Deactivated -= OnShellDeactivated;
            desktop.FocusChanged -= Model.NoteFocus;
            CompositionTarget.Rendering -= Tick;
            desktop.Dispose();
            hotkey.Dispose();
            appearance?.Dispose();
            look.Dispose();
            dock.Close();
            Model.Dispose();
        };
        dock.Start();
    }

    public AppModel Model { get; }

    void Done_Click(object sender, RoutedEventArgs e) => Model.ToggleDoneView();

    void Check_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskRow task)
            Model.Tick(task);
    }

    void Undo_Click(object sender, RoutedEventArgs e) => Model.Undo();

    void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TaskRow { IsEditing: false } task)
            Model.ToggleDetails(task);
    }

    void List_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.ItemContainer is null)
            return;

        if (args.InRecycleQueue)
        {
            UnhookRow(args.ItemContainer);
            return;
        }

        if (args.Item is not TaskRow task)
            return;

        if (!ReferenceEquals(args.ItemContainer.Tag, task))
        {
            UnhookRow(args.ItemContainer);
            args.ItemContainer.Tag = task;
            task.PropertyChanged += RowChanged;
        }

        AutomationProperties.SetName(args.ItemContainer, Row.Announce(task));
    }

    void UnhookRow(FrameworkElement container)
    {
        if (container.Tag is TaskRow previous)
            previous.PropertyChanged -= RowChanged;
        container.Tag = null;
    }

    void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TaskRow task)
            return;
        if (e.PropertyName is not (
            nameof(TaskRow.Title) or nameof(TaskRow.Priority) or nameof(TaskRow.Effort)
            or nameof(TaskRow.Reason) or nameof(TaskRow.PendingText)
            or nameof(TaskRow.IsPending) or nameof(TaskRow.IsWaiting)
            or nameof(TaskRow.IsFailed)))
            return;

        var index = List.Items.IndexOf(task);
        if (index < 0 || List.ContainerFromIndex(index) is not FrameworkElement container)
            return;
        AutomationProperties.SetName(container, Row.Announce(task));
    }

    public void ApplyScreenshotOverrides()
    {
        if (!LookLaunch.Active)
            return;

        if (LookLaunch.OpenSettings)
        {
            Model.SettingsOpen = true;
            if (LookLaunch.ShowUpdate && SettingsHost.Children.OfType<SettingsPanel>().FirstOrDefault() is SettingsPanel panel)
                panel.ScrollToAbout();
        }

        if (!LookLaunch.Dictation)
            return;

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(800);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            // As the prototype: a hotkey session is up, then a tap on the empty box waits for dictation.
            Model.Raise();
            Model.Tap("");
        };
        timer.Start();
    }

    void Title_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskRow task)
            Model.BeginTitleEdit(task);
        e.Handled = true;
    }

    void Effort_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskRow task)
            Model.CycleEffort(task);
    }

    void Priority_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TaskRow task)
            Model.CyclePriority(task);
    }

    void Edit_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        box.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (box.Visibility != Visibility.Visible || box.DataContext is not TaskRow task)
                return;
            box.Text = task.Title;
            box.DispatcherQueue.TryEnqueue(() =>
            {
                box.Focus(FocusState.Programmatic);
                box.SelectAll();
            });
        });
        box.KeyDown += (_, args) =>
        {
            if (box.DataContext is not TaskRow task)
                return;
            if (args.Key == VirtualKey.Enter)
            {
                Model.EditTitle(task, box.Text);
                args.Handled = true;
            }
            else if (args.Key == VirtualKey.Escape)
            {
                Model.CancelTitleEdit(task);
                args.Handled = true;
            }
        };
        box.LostFocus += (_, _) =>
        {
            if (box.DataContext is TaskRow { IsEditing: true } task)
                Model.EditTitle(task, box.Text);
        };
    }

    void Details_Loaded(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        if (box.DataContext is TaskRow task)
            box.Text = task.Details;
        box.LostFocus += (_, _) => CommitDetails(box);
        box.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Enter)
                return;
            var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
            if (shift)
                return;
            CommitDetails(box);
            args.Handled = true;
        };
    }

    void CommitDetails(TextBox box)
    {
        if (box.DataContext is TaskRow { IsExpanded: true } task)
            Model.EditDetails(task, box.Text);
    }

    public void Raise() => Model.Raise();

    void List_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (List.XamlRoot is not null && FocusManager.GetFocusedElement(List.XamlRoot) is TextBox)
            return;

        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && Model.SelectedIndex >= 0 && Model.SelectedIndex < Model.Tasks.Count)
        {
            var from = Model.SelectedIndex;
            var moving = Model.Tasks[from];
            if (e.Key == VirtualKey.Up)
                Model.MoveTo(from, from - 1);
            else if (e.Key == VirtualKey.Down)
                Model.MoveTo(from, from + 1);
            else
                ctrl = false;

            if (ctrl)
            {
                var now = Model.Tasks.IndexOf(moving);
                if (now >= 0)
                    Model.SelectedIndex = now;
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case VirtualKey.Up:
                Model.SelectUp();
                break;
            case VirtualKey.Down:
                Model.SelectDown();
                break;
            case VirtualKey.Space:
                Model.TickSelected();
                break;
            case VirtualKey.Enter:
                Model.ExpandSelected();
                break;
            case VirtualKey.E:
                if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
                    return;
                Model.EditSelectedTitle();
                break;
            case VirtualKey.Delete:
                Model.DeleteSelected();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    void List_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not TaskRow task || task.NotTask || Model.ReadOnly)
            return;

        var edit = new MenuFlyoutItem
        {
            Text = "Edit title",
            Icon = new FontIcon { Glyph = "\uE70F" },
        };
        edit.Click += (_, _) => Model.BeginTitleEdit(task);
        var again = new MenuFlyoutItem
        {
            Text = "Re-interpret Capture…",
            Icon = new FontIcon { Glyph = "\uE72C" },
        };
        again.Click += (_, _) => Model.Reinterpret(task);
        var item = new MenuFlyoutItem
        {
            Text = "Delete",
            Icon = new FontIcon { Glyph = "\uE74D" },
        };
        item.Click += (_, _) => Model.Delete(task);
        var menu = new MenuFlyout();
        menu.Items.Add(edit);
        menu.Items.Add(again);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(item);
        menu.ShowAt(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
        e.Handled = true;
    }

    void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (Root.XamlRoot is null || FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox)
            return;

        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (!ctrl)
            return;

        if (e.Key == VirtualKey.Z)
            Model.Undo();
        else if (e.Key == VirtualKey.Y)
            Model.Redo();
        else
            return;

        e.Handled = true;
    }

    void Resort_Click(object sender, RoutedEventArgs e) => Model.ReSort();

    void ApplyUndoMotion()
    {
        var visual = ElementCompositionPreview.GetElementVisual(UndoPill);
        var compositor = visual.Compositor;
        var show = compositor.CreateAnimationGroup();
        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.Target = "Opacity";
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(Ms("UndoFadeInMs"));
        var rise = compositor.CreateScalarKeyFrameAnimation();
        rise.Target = "Translation.Y";
        rise.InsertKeyFrame(0, (float)Ms("UndoRisePx"));
        rise.InsertKeyFrame(1, 0, WindowMotion.Ease(compositor, true));
        rise.Duration = TimeSpan.FromMilliseconds(Ms("UndoInMs"));
        show.Add(fade);
        show.Add(rise);
        var hide = compositor.CreateScalarKeyFrameAnimation();
        hide.Target = "Opacity";
        hide.InsertKeyFrame(1, 0);
        hide.Duration = TimeSpan.FromMilliseconds(Ms("UndoOutMs"));
        ElementCompositionPreview.SetIsTranslationEnabled(UndoPill, true);
        ElementCompositionPreview.SetImplicitShowAnimation(UndoPill, show);
        ElementCompositionPreview.SetImplicitHideAnimation(UndoPill, hide);
    }

    void List_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        dragFrom = e.Items.Count == 1 && e.Items[0] is TaskRow row && !row.NotTask ? Model.Tasks.IndexOf(row) : -1;
        if (dragFrom < 0)
            e.Cancel = true;
    }

    void List_DragItemsCompleted(object sender, DragItemsCompletedEventArgs e)
    {
        if (dragFrom < 0 || e.Items.Count != 1 || e.Items[0] is not TaskRow row)
        {
            dragFrom = -1;
            return;
        }

        var to = Model.Tasks.IndexOf(row);
        var from = dragFrom;
        dragFrom = -1;
        Model.AcceptReorder(from, to);
    }

    void Header_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (driving || headerDrag || arranging)
            return;
        if (!e.GetCurrentPoint(HeaderStrip).Properties.IsLeftButtonPressed)
            return;
        if (e.OriginalSource is DependencyObject source && IsOnButton(source))
            return;

        headerDrag = true;
        try
        {
            desktop.DragByCaption();
        }
        finally
        {
            headerDrag = false;
        }

        FinishHeaderDrag();
        e.Handled = true;
    }

    static bool IsOnButton(DependencyObject source)
    {
        while (true)
        {
            if (source is Button)
                return true;
            var parent = VisualTreeHelper.GetParent(source);
            if (parent is null)
                return false;
            source = parent;
        }
    }

    void Collapse_Click(object sender, RoutedEventArgs e)
    {
        if (driving)
            return;
        Model.Dock();
    }

    void RevealSelected()
    {
        var rows = Model.VisibleTasks;
        if (Model.SelectedIndex < 0 || Model.SelectedIndex >= rows.Count)
            return;
        var item = rows[Model.SelectedIndex];
        List.ScrollIntoView(item);
        if (List.ContainerFromItem(item) is ListViewItem container)
            container.Focus(FocusState.Keyboard);
        else
            List.DispatcherQueue.TryEnqueue(() =>
            {
                if (List.ContainerFromItem(item) is ListViewItem later)
                    later.Focus(FocusState.Keyboard);
            });
    }

    void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppModel.SelectedIndex))
        {
            RevealSelected();
            return;
        }

        if (e.PropertyName == nameof(AppModel.ListMaxHeight))
        {
            ApplyListHeight();
            return;
        }

        if (e.PropertyName == nameof(AppModel.RestartRequested) && Model.RestartRequested)
        {
            if (DispatcherQueue.HasThreadAccess)
                FinishUpdate();
            else
                DispatcherQueue.TryEnqueue(FinishUpdate);
            return;
        }

        if (e.PropertyName == nameof(AppModel.Docked))
        {
            if (!placed)
                return;
            if (Model.Docked)
                ToDock();
            else
                ToWidget();
            return;
        }

        if (e.PropertyName == nameof(AppModel.Raised))
        {
            if (!placed)
                return;
            if (Model.Raised)
                OnRaised();
            else
                OnDismissed();
            return;
        }

        if (e.PropertyName == nameof(AppModel.FullScreenApp))
        {
            if (!placed)
                return;
            OnFullScreenChanged();
            return;
        }

        if (e.PropertyName is nameof(AppModel.Theme) or nameof(AppModel.Backdrop))
            appearance?.Publish();

        if (!placed)
            return;

        if (e.PropertyName is nameof(AppModel.Theme) or nameof(AppModel.Backdrop)
            or nameof(AppModel.AppearsDark) or nameof(AppModel.EffectiveBackdrop))
            ApplyLook();
    }

    void Host_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape)
            return;

        if (!Model.Escape())
            Model.Leave();
        e.Handled = true;
    }

    void OnRaised()
    {
        if (!driving)
            Reanchor();
        desktop.BringToFront(Model.FullScreenApp);
        // Over a full-screen app the model stays docked, so the hidden Widget unrolls here.
        if (widgetHidden && !driving)
            ToWidget();
        Capture.Focus(FocusState.Programmatic);
    }

    void OnRaiseAgain()
    {
        if (!placed)
            return;
        if (!driving)
            Reanchor();
        desktop.BringToFront(Model.FullScreenApp);
        Capture.Focus(FocusState.Programmatic);
    }

    void OnDismissed()
    {
        if (Model.Docked)
        {
            if (Model.FullScreenApp)
            {
                // Leave the full-screen app at once; the Dock stays hidden while it runs.
                HideWidgetNow();
                desktop.YieldToFullScreen();
                return;
            }

            // A roll under way checks the state again when it ends.
            if (driving)
                return;
            ToDock();
            return;
        }

        desktop.PinToBottom();
        if (Model.FullScreenApp)
            desktop.YieldToFullScreen();
    }

    void OnFullScreenChanged()
    {
        if (driving)
            return;

        if (Model.Raised)
        {
            desktop.BringToFront(Model.FullScreenApp);
            return;
        }

        if (Model.Docked)
            SyncDock();
    }

    void OnShellDeactivated() => Model.Deactivate(ForegroundProcess.FileName());

    void Reanchor()
    {
        if (!placed || Host.XamlRoot is null || headerDrag || arranging)
            return;

        arranging = true;
        try
        {
            RefreshMonitors();
            if (!widgetHidden)
                Place(WidgetClient(current > 0 ? current : widgetHeight));
            dock.Reanchor();

            desktop.ReapplyZOrder();
            desktop.CheckFullScreen();
        }
        finally
        {
            arranging = false;
        }
    }

    void OnDpi(DesktopLayer.DpiNotice notice)
    {
        if (!placed || Host.XamlRoot is null || sizingDpi)
            return;

        sizingDpi = true;
        try
        {
            if (headerDrag)
            {
                int frameW = AppWindow.Size.Width - AppWindow.ClientSize.Width;
                int frameH = AppWindow.Size.Height - AppWindow.ClientSize.Height;
                Place(new RectInt32(
                    AppWindow.Position.X,
                    AppWindow.Position.Y,
                    Math.Max(1, notice.OuterWidth - frameW),
                    Math.Max(1, notice.OuterHeight - frameH)));
                return;
            }

            Reanchor();
        }
        finally
        {
            sizingDpi = false;
        }
    }

    void Settings_Click(object sender, RoutedEventArgs e) => Model.ToggleSettings();

    void ClearDraft_Click(object sender, RoutedEventArgs e)
    {
        Model.ClearDraft();
        Capture.Focus(FocusState.Programmatic);
    }

    // The installer replaces this process. A separate command waits, installs silently, then starts Task Widget again.
    void FinishUpdate()
    {
        if (finishing || Model.InstallerPath is not string installer || Environment.ProcessPath is not string app)
            return;

        finishing = true;
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 3 >nul & \"" + installer + "\" /VERYSILENT /NORESTART & start \"\" \"" + app + "\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        Close();
    }

    void Capture_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (ctrl && e.Key == VirtualKey.Z)
        {
            Model.Undo();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == VirtualKey.Y)
        {
            Model.Redo();
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Enter)
            return;

        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
        if (shift.HasFlag(CoreVirtualKeyStates.Down))
            return;

        Model.UpdateDraft(Capture.Text);
        Model.CommitCapture();
        e.Handled = true;
    }

    void OnHostSize()
    {
        if (Host.XamlRoot is null || arranging || headerDrag || arriving || capping)
            return;
        if (Root.ActualHeight <= 0)
            return;

        if (!placed)
        {
            placed = true;
            ApplyLook();
            arranging = true;
            try
            {
                RefreshMonitors();
                current = Capped(Root.ActualHeight);
                widgetHeight = current;
                Place(WidgetClient(current));
                if (Model.Docked)
                {
                    HideWidgetNow();
                    desktop.CheckFullScreen();
                    SyncDock();
                }
                else
                {
                    desktop.PinToBottom();
                    desktop.CheckFullScreen();
                }
            }
            finally
            {
                arranging = false;
            }

            return;
        }

        if (driving || widgetHidden)
            return;

        RefreshPlacementLimits();
        var next = Capped(Root.ActualHeight);
        if (Math.Abs(next - target) < 0.5 && rendering && !slide)
            return;
        if (Math.Abs(next - current) < 0.5)
            return;

        AnimateHeight(next, next > current ? Ms("WidgetGrowMs") : Ms("WidgetShrinkMs"), null);
    }

    // Collapse, as the prototype's Shell.Collapse: the content fades and the Widget rolls up toward the Dock,
    // hides, and then the Dock appears. The backdrop cannot fade, so the content and the window height animate.
    void ToDock()
    {
        if (Host.XamlRoot is null)
            return;

        if (widgetHidden)
        {
            SyncDock();
            return;
        }

        driving = true;
        RefreshMonitors();
        widgetHeight = Root.ActualHeight > 0 ? Capped(Root.ActualHeight) : current;
        WindowMotion.AnimateContent(Root, show: false, Ms("WidgetContentOutMs"));
        AnimateHeight(RolledDip, Ms("WidgetRollUpMs"), () =>
        {
            HideWidgetNow();
            // Expanded again during the roll-up: unroll from here.
            if (!Model.Docked || Model.Raised)
            {
                ToWidget();
                return;
            }

            SyncDock();
        });
    }

    // Expand, as the prototype's Shell.Expand and hotkey session: the Dock fades out while the Widget
    // unrolls from 36 px on its anchor with its content fading in.
    void ToWidget()
    {
        if (Host.XamlRoot is null)
            return;

        dock.HideDock();

        driving = true;
        RefreshMonitors();
        Root.Visibility = Visibility.Visible;
        Root.Measure(new Size(Root.Width, double.PositiveInfinity));
        Root.UpdateLayout();
        var height = Capped(Math.Max(Root.ActualHeight, Root.DesiredSize.Height));
        if (height <= 0)
            height = widgetHeight;
        if (widgetHidden)
        {
            current = RolledDip;
            Place(WidgetClient(current));
            widgetHidden = false;
        }

        desktop.BringToFront(Model.FullScreenApp);
        Activate();
        WindowMotion.AnimateContent(Root, show: true, Ms("WidgetContentInMs"));
        AnimateHeight(height, Ms("WidgetUnrollMs"), () =>
        {
            driving = false;
            widgetHeight = current;
            // Commit or Esc during the unroll still has to send a temporary Widget away.
            if (!Model.Raised && Model.Docked)
                OnDismissed();
            else
                OnHostSize();
        });
        Capture.Focus(FocusState.Programmatic);
    }

    void HideWidgetNow()
    {
        if (rendering)
        {
            CompositionTarget.Rendering -= Tick;
            rendering = false;
        }

        animDone = null;
        driving = false;
        widgetHidden = true;
        // Docked, the list is not laid out or rendered at all, as when the Widget was one collapsed panel.
        Root.Visibility = Visibility.Collapsed;
        desktop.Hide();
    }

    // The Dock shows while docked, unless the Widget is up or a full-screen app is on the home monitor.
    void SyncDock()
    {
        if (Model.ShowDock && widgetHidden)
        {
            if (!dock.IsShown)
                dock.ShowDock();
        }
        else if (dock.IsShown)
        {
            dock.HideNow();
        }
    }

    void AnimateHeight(double to, double ms, Action? done)
    {
        from = current;
        target = to;
        slide = false;
        arriving = false;
        animMs = ms;
        animDone = done;
        animStart = System.Diagnostics.Stopwatch.GetTimestamp();
        EnsureRendering();
    }

    void Tick(object? sender, object e)
    {
        var p = Math.Min(1, System.Diagnostics.Stopwatch.GetElapsedTime(animStart).TotalMilliseconds / animMs);
        var eased = arriving ? Arrive(p) : 1 - Math.Pow(1 - p, 3);
        if (slide)
            Place(p >= 1 ? toRect : Lerp(fromRect, toRect, eased));
        else
        {
            current = from + (target - from) * eased;
            Place(WidgetClient(current));
        }

        if (p < 1)
            return;

        CompositionTarget.Rendering -= Tick;
        rendering = false;
        if (slide)
        {
            current = landHeight;
            target = landHeight;
        }

        arriving = false;
        var done = animDone;
        animDone = null;
        done?.Invoke();
    }

    void AnimateRect(RectInt32 fromClient, RectInt32 toClient, double ms, double endHeightDip, Action done, bool arrive = false)
    {
        slide = true;
        arriving = arrive;
        if (arrive)
        {
            var resources = Application.Current.Resources;
            snapX1 = (double)resources["EaseOutX1"];
            snapY1 = (double)resources["EaseOutY1"];
            snapX2 = (double)resources["EaseOutX2"];
            snapY2 = (double)resources["EaseOutY2"];
        }

        fromRect = fromClient;
        toRect = toClient;
        landHeight = endHeightDip;
        animMs = ms;
        animDone = done;
        animStart = System.Diagnostics.Stopwatch.GetTimestamp();
        EnsureRendering();
    }

    void EnsureRendering()
    {
        if (rendering)
            return;
        rendering = true;
        CompositionTarget.Rendering += Tick;
    }

    static double Ms(string key) => WindowMotion.Ms(key);

    double Scale => Host.XamlRoot.RasterizationScale;

    void FinishHeaderDrag()
    {
        if (!placed || Host.XamlRoot is null)
            return;

        RefreshMonitors();
        int x = AppWindow.Position.X + AppWindow.Size.Width / 2;
        int y = AppWindow.Position.Y + AppWindow.Size.Height / 2;
        var id = MonitorUnder(x, y);
        if (id.Length > 0)
            Model.DropOnMonitor(id);
        SnapToAnchor();
    }

    void SnapToAnchor()
    {
        var height = current > 0 ? current : widgetHeight;
        if (height <= 0)
            height = Capped(Root.ActualHeight);
        var target = WidgetClient(height);
        var fromClient = new RectInt32(
            AppWindow.Position.X,
            AppWindow.Position.Y,
            AppWindow.ClientSize.Width,
            AppWindow.ClientSize.Height);
        if (Near(fromClient, target))
        {
            Place(target);
            desktop.ReapplyZOrder();
            desktop.CheckFullScreen();
            return;
        }

        // The Widget arrives on its anchor with Motion.xaml's EaseOut, over the window's grow time.
        AnimateRect(fromClient, target, Ms("WidgetGrowMs"), height, () =>
        {
            desktop.ReapplyZOrder();
            desktop.CheckFullScreen();
        }, arrive: true);
    }

    void RefreshMonitors()
    {
        screens.Clear();
        screens.AddRange(MonitorCatalog.Read());
        Model.NoteMonitors(screens.Select(screen => new TaskWidget.Core.ConnectedMonitor(screen.DeviceId, screen.Primary)).ToArray());
        RefreshPlacementLimits();
    }

    void RefreshPlacementLimits()
    {
        if (capping)
            return;

        capping = true;
        try
        {
            var monitor = Placement();
            if (monitor is null || Host.XamlRoot is null)
                windowCap = double.PositiveInfinity;
            else
            {
                double scale = monitor.Dpi > 0 ? monitor.Dpi / 96.0 : Scale;
                int margin = (int)Math.Ceiling(MarginDip * scale);
                int capPx = monitor.Work.Height - margin;
                double capDip = capPx / scale;
                windowCap = capDip < 120 ? 120 : capDip;
            }

            ApplyListHeight();
        }
        finally
        {
            capping = false;
        }
    }

    // The header and Capture box stay put. Only the task list scrolls, and only after the row cap.
    void ApplyListHeight()
    {
        var rows = Model.ListMaxHeight;
        var limit = rows;
        if (!double.IsPositiveInfinity(windowCap) && List.ActualHeight > 0)
        {
            var chrome = HeaderStrip.ActualHeight
                + BannerHost.ActualHeight
                + CaptureHost.ActualHeight
                + WelcomeCard.ActualHeight
                + EmptyHotkey.ActualHeight
                + SettingsHost.ActualHeight
                + Root.Padding.Top
                + Root.Padding.Bottom;
            var room = windowCap - chrome;
            if (room >= 37)
                limit = Math.Min(rows, room);
        }

        if (double.IsNaN(List.MaxHeight) || Math.Abs(List.MaxHeight - limit) > 0.5)
            List.MaxHeight = limit;
    }

    double Capped(double heightDip)
    {
        var max = windowCap;
        if (double.IsNaN(max) || double.IsInfinity(max) || max <= 0)
            return heightDip;
        return Math.Min(heightDip, max);
    }

    MonitorSnap? Placement()
    {
        if (screens.Count == 0)
            return null;

        var id = Model.PlacementMonitorId;
        foreach (var screen in screens)
        {
            if (screen.DeviceId == id)
                return screen;
        }

        foreach (var screen in screens)
        {
            if (screen.Primary)
                return screen;
        }

        return screens[0];
    }

    string MonitorUnder(int x, int y)
    {
        MonitorSnap? nearest = null;
        long best = long.MaxValue;
        foreach (var screen in screens)
        {
            if (x >= screen.Bounds.X && x < screen.Bounds.X + screen.Bounds.Width
                && y >= screen.Bounds.Y && y < screen.Bounds.Y + screen.Bounds.Height)
                return screen.DeviceId;

            long dx = screen.Bounds.X + screen.Bounds.Width / 2 - x;
            long dy = screen.Bounds.Y + screen.Bounds.Height / 2 - y;
            long dist = dx * dx + dy * dy;
            if (dist < best)
            {
                best = dist;
                nearest = screen;
            }
        }

        return nearest?.DeviceId ?? "";
    }

    (RectInt32 Area, double Scale) Anchor()
    {
        var monitor = Placement();
        if (monitor is { Dpi: > 0 })
            return (monitor.Work, monitor.Dpi / 96.0);

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        return (area, Scale);
    }

    RectInt32 WidgetClient(double heightDip)
    {
        var (area, scale) = Anchor();
        int width = (int)Math.Ceiling(Root.Width * scale);
        int height = (int)Math.Ceiling(Capped(heightDip) * scale);
        int x = area.X + area.Width - width - (int)Math.Ceiling(MarginDip * scale);
        int y = area.Y + (int)Math.Ceiling(MarginDip * scale);
        return new RectInt32(x, y, width, height);
    }

    RectInt32 DockClient()
    {
        var (area, scale) = Anchor();
        int width = (int)Math.Ceiling(DockWindow.WidthDip * scale);
        int height = (int)Math.Ceiling(DockWindow.HeightDip * scale);
        int x = area.X + (area.Width - width) / 2;
        int y = area.Y + (int)Math.Ceiling(DockTopDip * scale);
        return new RectInt32(x, y, width, height);
    }

    static bool Near(RectInt32 a, RectInt32 b) =>
        Math.Abs(a.X - b.X) <= 1 && Math.Abs(a.Y - b.Y) <= 1
        && Math.Abs(a.Width - b.Width) <= 1 && Math.Abs(a.Height - b.Height) <= 1;

    double Arrive(double x) => EaseOutY(x, snapX1, snapY1, snapX2, snapY2);

    static double EaseOutY(double x, double x1, double y1, double x2, double y2)
    {
        if (x <= 0)
            return 0;
        if (x >= 1)
            return 1;

        double t = x;
        for (var i = 0; i < 8; i++)
        {
            var slope = BezierSlope(t, x1, x2);
            if (Math.Abs(slope) < 1e-6)
                break;
            t -= (Bezier(t, x1, x2) - x) / slope;
            if (t < 0)
                t = 0;
            else if (t > 1)
                t = 1;
        }

        return Bezier(t, y1, y2);
    }

    static double Bezier(double t, double c1, double c2)
    {
        double u = 1 - t;
        return 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t;
    }

    static double BezierSlope(double t, double c1, double c2)
    {
        double u = 1 - t;
        return 3 * u * u * c1 + 6 * u * t * (c2 - c1) + 3 * t * t * (1 - c2);
    }

    void Place(RectInt32 client)
    {
        int frameW = AppWindow.Size.Width - AppWindow.ClientSize.Width;
        int frameH = AppWindow.Size.Height - AppWindow.ClientSize.Height;
        AppWindow.MoveAndResize(new RectInt32(client.X, client.Y, client.Width + frameW, client.Height + frameH));
    }

    static RectInt32 Lerp(RectInt32 a, RectInt32 b, double t) => new(
        (int)Math.Round(a.X + (b.X - a.X) * t),
        (int)Math.Round(a.Y + (b.Y - a.Y) * t),
        (int)Math.Round(a.Width + (b.Width - a.Width) * t),
        (int)Math.Round(a.Height + (b.Height - a.Height) * t));

    // Theme and backdrop for both windows. Contrast themes get no backdrop and the HighContrast tokens.
    void ApplyLook()
    {
        bool contrast = appearance?.ContrastTheme == true;
        bool preview = LookLaunch.ContrastPreview && !contrast;
        var theme = preview
            ? ElementTheme.Dark
            : contrast
                ? ElementTheme.Default
                : Model.Theme switch
                {
                    ThemeChoice.Light => ElementTheme.Light,
                    ThemeChoice.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
        bool dark = preview || Model.AppearsDark;
        var effective = contrast || preview ? BackdropChoice.Solid : Model.EffectiveBackdrop;
        look.Apply(theme, dark, contrast, effective);
        dock.ApplyLook(theme, dark, contrast, effective);
    }
}
