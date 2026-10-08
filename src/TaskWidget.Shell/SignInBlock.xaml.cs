using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaskWidget.Core;

namespace TaskWidget;

public sealed partial class SignInBlock : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(AppModel), typeof(SignInBlock), new PropertyMetadata(null));

    public SignInBlock() => InitializeComponent();

    public AppModel Model
    {
        get => (AppModel)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    void SignIn_Click(object sender, RoutedEventArgs e) => _ = Model.SignIn();

    void Cancel_Click(object sender, RoutedEventArgs e) => Model.CancelSignIn();
}
