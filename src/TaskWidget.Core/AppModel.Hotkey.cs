using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskWidget.Core;

public sealed partial class AppModel
{
    public static readonly TimeSpan DictationWait = TimeSpan.FromMilliseconds(4000);

    HotkeyBinding hotkeyBinding = HotkeyBinding.CtrlShift;
    IDisposable? dictationTimer;
    bool finishingDictation;

    [ObservableProperty]
    public partial bool WaitingDictation { get; private set; }

    public HotkeyBinding HotkeyBinding => hotkeyBinding;

    public string LanguageToggleWarning => HotkeyBinding.LanguageToggleWarning;

    public bool HasDraftText => CaptureText.Length > 0;

    public string CapturePlaceholder => WaitingDictation ? "Waiting for dictation…" : "Capture a thought…";

    public void Tap(string fieldText)
    {
        if (!ShowWidget)
        {
            Raise();
            return;
        }

        var waiting = WaitingDictation;
        if (!string.Equals(fieldText, CaptureText, StringComparison.Ordinal))
            UpdateDraft(fieldText);

        if (waiting && !WaitingDictation)
            return;

        if (CanCommit(CaptureText))
        {
            CommitCapture();
            Dock();
            return;
        }

        BeginWaiting();
    }

    public void Leave()
    {
        StopWaiting();
        if (!Docked)
            Dock();
    }

    public void Deactivate(string? processName)
    {
        if (IsWisprFlow(processName))
            return;

        StopWaiting();
        if (Raised && (FullScreenApp || overFullScreen))
        {
            NoteDeactivated();
            return;
        }

        if (!Docked)
            Dock();
        else if (Raised)
            NoteDeactivated();
    }

    public void ClearDraft()
    {
        if (CaptureText.Length == 0)
            return;

        var previous = CaptureText;
        StopWaiting();
        CaptureText = "";
        Push(
            "Draft cleared",
            () => CaptureText = previous,
            () => CaptureText = "",
            pill: false);
    }

    public void SetHotkey(HotkeyBinding binding)
    {
        if (binding.Equals(hotkeyBinding))
            return;

        hotkeyBinding = binding;
        Hotkey = binding.Text;
        MarkDirty();
    }

    public static bool IsWisprFlow(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var name = Path.GetFileNameWithoutExtension(processName.Trim());
        return name.Equals("Wispr Flow", StringComparison.OrdinalIgnoreCase)
            || name.Equals("WisprFlow", StringComparison.OrdinalIgnoreCase);
    }

    void NoteDraftForDictation(string value)
    {
        if (finishingDictation || !WaitingDictation || !CanCommit(value))
            return;

        FinishDictation();
    }

    void FinishDictation()
    {
        finishingDictation = true;
        try
        {
            StopWaiting();
            CommitCapture();
            Dock();
        }
        finally
        {
            finishingDictation = false;
        }
    }

    void BeginWaiting()
    {
        dictationTimer?.Dispose();
        WaitingDictation = true;
        dictationTimer = clock.Schedule(DictationWait, DictationTimedOut);
    }

    void DictationTimedOut()
    {
        if (!WaitingDictation)
            return;

        StopWaiting();
        Dock();
    }

    void StopWaiting()
    {
        dictationTimer?.Dispose();
        dictationTimer = null;
        if (WaitingDictation)
            WaitingDictation = false;
    }

    void ApplyHotkey(string? text)
    {
        if (!HotkeyBinding.TryParse(text, out var binding))
            return;

        hotkeyBinding = binding;
        Hotkey = binding.Text;
    }

    static bool CanCommit(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Trim();
        if (normalized.Length == 0)
            return false;

        return normalized.Split('\n')[0].Trim().Length > 0;
    }

    partial void OnWaitingDictationChanged(bool value) => OnPropertyChanged(nameof(CapturePlaceholder));
}
