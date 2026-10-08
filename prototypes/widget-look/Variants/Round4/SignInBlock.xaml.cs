// PROTOTYPE: throwaway.
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Look;

public sealed partial class SignInBlock : UserControl
{
    public Prefs P => Shell.Prefs;
    // Variant C centres itself on the welcome card and stretches in Settings.
    public bool Centered { get; set; }
    public HorizontalAlignment Align => Centered ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
    public TextAlignment TextAlign => Centered ? TextAlignment.Center : TextAlignment.Left;

    public SignInBlock() => InitializeComponent();

    void SignIn_Click(object s, RoutedEventArgs e) => Shell.StartSignIn();
    void Cancel_Click(object s, RoutedEventArgs e) => Shell.CancelSignIn();
}
