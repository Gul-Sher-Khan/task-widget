// PROTOTYPE: in-memory Settings plus two prototype-only review switches.
namespace Look;

public sealed class Prefs : Bindable
{
    // Review switches (prototype only): approximate a contrast theme; run every animation 5x slower.
    public bool ContrastPreview, SlowMo;

    // ---- Settings ----
    int theme, backdrop, maxRows = 8, connection, model, provider;
    bool signedIn = true, launchAtLogin = true, showSettings, recording, testing;
    string baseUrl = "https://api.groq.com/openai/v1", apiKey = "", keyModel = "openai/gpt-oss-20b", testResult = "";
    string hotkey = "Ctrl + Shift";

    public int Theme { get => theme; set => Set(ref theme, value); }
    public int Backdrop { get => backdrop; set => Set(ref backdrop, value); }
    public int MaxRows { get => maxRows; set { if (Set(ref maxRows, value)) { Raise(nameof(ListMaxHeight)); Raise(nameof(MaxRowsD)); } } }
    public double MaxRowsD { get => maxRows; set => MaxRows = (int)value; }
    public double ListMaxHeight => maxRows * 37 + 4; // a one-line row is ~37 DIP
    public bool LaunchAtLogin { get => launchAtLogin; set => Set(ref launchAtLogin, value); }
    public bool ShowSettings { get => showSettings; set { if (Set(ref showSettings, value)) Raise(nameof(ShowTasks)); } }
    public bool ShowTasks => !showSettings;

    // Connection: exactly one is active (0 ChatGPT, 1 API key); the other's details are kept.
    public int Connection { get => connection; set { if (Set(ref connection, value)) { Raise(nameof(IsChatGpt)); Raise(nameof(IsApiKey)); } } }
    public bool IsChatGpt => connection == 0;
    public bool IsApiKey => connection == 1;
    public bool SignedIn { get => signedIn; set { if (Set(ref signedIn, value)) Raise(nameof(SignedOut)); } }
    public bool SignedOut => !signedIn;
    public string Account => "gul@example.com";
    public string Plan => "ChatGPT Pro";
    public static readonly string[] Models = { "Automatic (gpt-5.6-sol)", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-6-astra" };
    public int Model { get => model; set => Set(ref model, value); }

    public static readonly string[] Providers = { "Groq", "OpenRouter", "Custom" };
    public int Provider
    {
        get => provider;
        set
        {
            if (!Set(ref provider, value)) return;
            Raise(nameof(IsCustom));
            if (value == 0) BaseUrl = "https://api.groq.com/openai/v1";
            if (value == 1) BaseUrl = "https://openrouter.ai/api/v1";
            TestResult = "";
        }
    }
    public bool IsCustom => provider == 2;
    public string BaseUrl { get => baseUrl; set => Set(ref baseUrl, value); }
    public string ApiKey { get => apiKey; set => Set(ref apiKey, value); }
    public string KeyModel { get => keyModel; set => Set(ref keyModel, value); }
    public bool Testing { get => testing; set => Set(ref testing, value); }
    public string TestResult { get => testResult; set { if (Set(ref testResult, value)) Raise(nameof(HasTestResult)); } }
    public bool HasTestResult => testResult.Length > 0;

    public string Hotkey { get => hotkey; set => Set(ref hotkey, value); }
    public bool Recording { get => recording; set { if (Set(ref recording, value)) Raise(nameof(NotRecording)); } }
    public bool NotRecording => !recording;
}
