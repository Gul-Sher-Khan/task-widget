using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskWidget.Core;

namespace TaskWidget;

public sealed partial class EffortChip : UserControl
{
    public static readonly DependencyProperty EffortProperty = DependencyProperty.Register(
        nameof(Effort), typeof(Effort), typeof(EffortChip), new PropertyMetadata(Effort.Quick));

    public EffortChip() => InitializeComponent();

    public Effort Effort
    {
        get => (Effort)GetValue(EffortProperty);
        set => SetValue(EffortProperty, value);
    }
}
