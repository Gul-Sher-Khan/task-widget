// PROTOTYPE: throwaway.
using Microsoft.UI.Xaml.Controls;

namespace Look;

public sealed partial class Welcome : UserControl
{
    public Prefs P => Shell.Prefs;
    public bool Card { get; set; }
    public bool NoCard => !Card;

    public Welcome() => InitializeComponent();
}
