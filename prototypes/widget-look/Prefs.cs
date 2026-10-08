// PROTOTYPE: in-memory Settings, app state for the round-4 screens, and prototype-only review switches.
namespace Look;

public enum SignInState { Idle, Waiting, Failed, NotEligible }
public enum Banner { None, NewerVersion, SignedOut, Recovered, StartedEmpty }

public sealed class Prefs : Bindable
{
    // Review switches (prototype only): approximate a contrast theme; run every animation 5x slower.
    public bool ContrastPreview, SlowMo;

    // ---- Settings ----
    int theme, backdrop, maxRows = 8, model;
    bool signedIn = true, launchAtLogin = true, showSettings, recording;
    string hotkey = "Ctrl + Shift";

    public int Theme { get => theme; set => Set(ref theme, value); }
    public int Backdrop { get => backdrop; set => Set(ref backdrop, value); }
    public int MaxRows { get => maxRows; set { if (Set(ref maxRows, value)) { Raise(nameof(ListMaxHeight)); Raise(nameof(MaxRowsD)); } } }
    public double MaxRowsD { get => maxRows; set => MaxRows = (int)value; }
    public double ListMaxHeight => maxRows * 37 + 4; // a one-line row is ~37 DIP
    public bool LaunchAtLogin { get => launchAtLogin; set => Set(ref launchAtLogin, value); }
    public bool ShowSettings { get => showSettings; set { if (Set(ref showSettings, value)) Raise(nameof(ShowTasks)); } }
    public bool ShowTasks => !showSettings;

    // ---- Connection: ChatGPT only (ADR 0003) ----
    public bool SignedIn
    {
        get => signedIn;
        set { if (Set(ref signedIn, value)) { Raise(nameof(SignedOut)); Raise(nameof(ShowWelcome)); RaiseBanner(); } }
    }
    public bool SignedOut => !signedIn;
    public string Account => "gul@example.com";
    public string Plan => "ChatGPT Pro";
    public static readonly string[] Models = { "Automatic (gpt-5.6-sol)", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-6-astra" };
    public int Model { get => model; set => Set(ref model, value); }

    public string Hotkey { get => hotkey; set { if (Set(ref hotkey, value)) Raise(nameof(HotkeyKeys)); } }
    public string[] HotkeyKeys => hotkey.Split(" + ");
    public bool Recording { get => recording; set { if (Set(ref recording, value)) Raise(nameof(NotRecording)); } }
    public bool NotRecording => !recording;

    // ---- First run: the welcome card shows until the first sign-in, then never again ----
    bool welcomed = true;
    public bool Welcomed { get => welcomed; set { if (Set(ref welcomed, value)) { Raise(nameof(ShowWelcome)); RaiseBanner(); } } }
    public bool ShowWelcome => !welcomed && !signedIn;

    // ---- Sign-in: one flow and one state, shared by the welcome card, the signed-out banner and Settings ----
    SignInState signIn;
    string signInCause = "";
    public SignInState SignIn
    {
        get => signIn;
        set
        {
            if (!Set(ref signIn, value)) return;
            foreach (var n in new[] { nameof(SignInIdle), nameof(SignInWaiting), nameof(SignInFailed), nameof(SignInNotEligible), nameof(SignInButton) })
                Raise(n);
        }
    }
    public bool SignInIdle => signIn == SignInState.Idle;
    public bool SignInWaiting => signIn == SignInState.Waiting;
    public bool SignInFailed => signIn == SignInState.Failed;
    public bool SignInNotEligible => signIn == SignInState.NotEligible;
    public bool SignInButton => signIn is SignInState.Idle or SignInState.NotEligible; // the plain "Sign in with ChatGPT" button shows
    public string SignInCause { get => signInCause; set => Set(ref signInCause, value); }

    // ---- Header banner: one at a time; newer version > signed out > recovery ----
    bool readOnly, recovered, startedEmpty;
    public bool ReadOnly { get => readOnly; set { if (Set(ref readOnly, value)) { Raise(nameof(Editable)); RaiseBanner(); } } }
    public bool Editable => !readOnly;
    public bool Recovered { get => recovered; set { if (Set(ref recovered, value)) RaiseBanner(); } }
    public bool StartedEmpty { get => startedEmpty; set { if (Set(ref startedEmpty, value)) RaiseBanner(); } }
    public Banner Banner => readOnly ? Banner.NewerVersion
                          : signedIn == false && welcomed ? Banner.SignedOut
                          : recovered ? Banner.Recovered
                          : startedEmpty ? Banner.StartedEmpty
                          : Banner.None;
    public bool HasBanner => Banner != Banner.None;
    void RaiseBanner() { Raise(nameof(Banner)); Raise(nameof(HasBanner)); }

    // ---- "Waiting for dictation…" (tap Ctrl+Shift on an empty box) ----
    bool waitingDictation;
    public bool WaitingDictation { get => waitingDictation; set { if (Set(ref waitingDictation, value)) Raise(nameof(NotWaitingDictation)); } }
    public bool NotWaitingDictation => !waitingDictation;

    // ---- Prototype-only simulation switches (control bar) ----
    public static readonly string[] SignInOutcomes = { "succeeds", "didn't finish", "plan not eligible" };
    public int SignInOutcome;
    public static readonly string[] CaptureOutcomes = { "ok", "timeout", "429", "bad reply", "zero Tasks", "plan lapsed" };
    public int CaptureOutcome;
    bool offline;
    public bool Offline { get => offline; set => Set(ref offline, value); }
    public bool DictationArrives = true, HoldPending;

    // ---- Updates (Packaging, updates and signing): check-and-notify from GitHub Releases, never silent ----
    public enum Upd { Current, Checking, Available, Downloading, Failed }
    bool autoUpdate = true;
    Upd update;
    string version = "0.1.0", checkedWhen = "Checked today, 09:12";
    double downloaded;

    public bool AutoUpdate { get => autoUpdate; set => Set(ref autoUpdate, value); }
    public string Version { get => version; set { if (Set(ref version, value)) Raise(nameof(VersionLine)); } }
    public string VersionLine => "Task Widget " + version;
    public string NewVersion => "0.2.0";
    public string CheckedWhen { get => checkedWhen; set => Set(ref checkedWhen, value); }
    public double Downloaded { get => downloaded; set => Set(ref downloaded, value); }
    public Upd Update
    {
        get => update;
        set
        {
            if (!Set(ref update, value)) return;
            foreach (var n in new[] { nameof(UpdCurrent), nameof(UpdChecking), nameof(UpdAvailable), nameof(UpdDownloading), nameof(UpdFailed), nameof(CanCheck) })
                Raise(n);
        }
    }
    public bool UpdCurrent => update == Upd.Current;
    public bool UpdChecking => update == Upd.Checking;
    public bool UpdAvailable => update == Upd.Available;
    public bool UpdDownloading => update == Upd.Downloading;
    public bool UpdFailed => update == Upd.Failed;
    public bool CanCheck => update is Upd.Current or Upd.Failed;
}
