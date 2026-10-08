// PROTOTYPE: throwaway. Banner content follows Prefs.Banner and, for Signed out, the shared sign-in state.
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Look;

public sealed partial class BannerView : UserControl, INotifyPropertyChanged
{
    public Prefs P => Shell.Prefs;
    public event PropertyChangedEventHandler PropertyChanged;

    public BannerView()
    {
        InitializeComponent();
        P.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Prefs.Banner) or nameof(Prefs.SignIn) or nameof(Prefs.SignInCause))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        };
    }

    Banner B => P.Banner;
    bool SignedOutBanner => B == Banner.SignedOut;

    public string Message => B switch
    {
        Banner.NewerVersion => "These tasks were saved by a newer Task Widget. Update to make changes.",
        Banner.SignedOut => P.SignIn switch
        {
            SignInState.Waiting => "Waiting for your browser…",
            SignInState.Failed => "Sign-in didn't finish",
            SignInState.NotEligible => "This ChatGPT plan can't be used in Task Widget. Go, Plus or Pro works.",
            // Draft copy: the banner's wording wasn't fixed by the earlier tickets.
            _ => "You're signed out of ChatGPT. New Captures wait until you sign in.",
        },
        Banner.Recovered => "Your task list was damaged and has been restored from a backup (saved 14:02).",
        Banner.StartedEmpty => "Your task list couldn't be read, so Task Widget started fresh. The old file was kept.",
        _ => "",
    };

    public string Tip => SignedOutBanner && P.SignInFailed ? P.SignInCause : null;
    public bool Busy => SignedOutBanner && P.SignInWaiting;

    public string PrimaryText => B switch
    {
        Banner.NewerVersion => "Get update",
        Banner.SignedOut => P.SignIn switch { SignInState.Waiting => "Cancel", SignInState.Failed => "Try again", _ => "Sign in" },
        Banner.Recovered or Banner.StartedEmpty => "Open folder",
        _ => null,
    };

    public bool Dismissible => B is Banner.Recovered or Banner.StartedEmpty;

    public InfoBarSeverity Severity => B switch
    {
        Banner.SignedOut => P.SignInFailed || P.SignInNotEligible ? InfoBarSeverity.Error : InfoBarSeverity.Warning,
        Banner.StartedEmpty => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational,
    };

    public string Glyph => Severity switch { InfoBarSeverity.Error => "", InfoBarSeverity.Warning => "", _ => "" };

    public Brush IconBrush => Theme(Severity switch
    {
        InfoBarSeverity.Error => "SystemFillColorCriticalBrush",
        InfoBarSeverity.Warning => "SystemFillColorCautionBrush",
        _ => "AccentFillColorDefaultBrush",
    });

    public Brush BackBrush => Theme(Severity switch
    {
        InfoBarSeverity.Error => "SystemFillColorCriticalBackgroundBrush",
        InfoBarSeverity.Warning => "SystemFillColorCautionBackgroundBrush",
        _ => "SystemFillColorAttentionBackgroundBrush",
    });

    // WinUI's own status brushes, resolved for the Widget's current theme (they cover contrast themes too).
    Brush Theme(string key)
    {
        var d = Application.Current.Resources;
        string theme = Shell.SystemContrast ? "HighContrast" : Shell.Theme == ElementTheme.Dark ? "Dark" : "Light";
        foreach (var md in d.MergedDictionaries)
            if (md.ThemeDictionaries.TryGetValue(theme, out var td) && ((ResourceDictionary)td).TryGetValue(key, out var b)) return (Brush)b;
        return d.TryGetValue(key, out var any) ? (Brush)any : null;
    }

    void Primary_Click(object s, RoutedEventArgs e)
    {
        switch (B)
        {
            case Banner.NewerVersion:
                P.ShowSettings = true;
                P.Update = Prefs.Upd.Available;
                break;
            case Banner.SignedOut:
                if (P.SignInWaiting) Shell.CancelSignIn(); else Shell.StartSignIn();
                break;
            default:
                Shell.Note("Open folder: the real app opens %LOCALAPPDATA%\\TaskWidget in Explorer.");
                break;
        }
    }

    void Dismiss_Click(object s, RoutedEventArgs e) { P.Recovered = false; P.StartedEmpty = false; }
    void Close_Click(InfoBar s, object e) { P.Recovered = false; P.StartedEmpty = false; }
}
