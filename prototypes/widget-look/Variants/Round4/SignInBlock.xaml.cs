// PROTOTYPE: throwaway.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Look;

public sealed partial class SignInBlock : UserControl
{
    public Prefs P => Shell.Prefs;

    public SignInBlock() => InitializeComponent();

    void SignIn_Click(object s, RoutedEventArgs e) => Shell.StartSignIn();
    void Cancel_Click(object s, RoutedEventArgs e) => Shell.CancelSignIn();
}
