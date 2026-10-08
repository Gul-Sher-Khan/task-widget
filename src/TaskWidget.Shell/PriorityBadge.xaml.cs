using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskWidget.Core;

namespace TaskWidget;

public sealed partial class PriorityBadge : UserControl
{
    public static readonly DependencyProperty PriorityProperty = DependencyProperty.Register(
        nameof(Priority), typeof(Priority), typeof(PriorityBadge), new PropertyMetadata(Priority.Low));

    public PriorityBadge() => InitializeComponent();

    public Priority Priority
    {
        get => (Priority)GetValue(PriorityProperty);
        set => SetValue(PriorityProperty, value);
    }
}
