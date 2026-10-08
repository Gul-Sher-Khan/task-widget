using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskWidget.Core;

namespace TaskWidget;

public sealed partial class Welcome : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(AppModel), typeof(Welcome), new PropertyMetadata(null, OnModelChanged));

    public Welcome() => InitializeComponent();

    public AppModel Model
    {
        get => (AppModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public bool Card { get; set; }

    static void OnModelChanged(DependencyObject source, DependencyPropertyChangedEventArgs args)
    {
        if (source is Welcome welcome && args.NewValue is AppModel model)
            welcome.SignIn.Model = model;
    }
}
