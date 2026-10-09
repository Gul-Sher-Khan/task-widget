using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class ModifierTapTests
{
    static readonly HotkeyBinding CtrlShift = HotkeyBinding.Parse("Ctrl + Shift");

    [Fact]
    public void A_clean_tap_fires_on_release()
    {
        var state = default(TapState);
        bool fired;
        (state, fired) = ModifierTap.Apply(state, down: true, virtualKey: 0x11, timeMs: 0, CtrlShift);
        Assert.False(fired);
        (state, fired) = ModifierTap.Apply(state, down: true, virtualKey: 0x10, timeMs: 30, CtrlShift);
        Assert.False(fired);
        (state, fired) = ModifierTap.Apply(state, down: false, virtualKey: 0x11, timeMs: 80, CtrlShift);
        Assert.False(fired);
        (state, fired) = ModifierTap.Apply(state, down: false, virtualKey: 0x10, timeMs: 140, CtrlShift);
        Assert.True(fired);
    }

    [Fact]
    public void A_key_in_between_does_not_fire()
    {
        var state = default(TapState);
        bool fired;
        (state, fired) = ModifierTap.Apply(state, true, 0x11, 0, CtrlShift);
        (state, fired) = ModifierTap.Apply(state, true, 0x41, 20, CtrlShift);
        (state, fired) = ModifierTap.Apply(state, false, 0x41, 40, CtrlShift);
        (state, fired) = ModifierTap.Apply(state, true, 0x10, 60, CtrlShift);
        (state, fired) = ModifierTap.Apply(state, false, 0x10, 80, CtrlShift);
        (state, fired) = ModifierTap.Apply(state, false, 0x11, 100, CtrlShift);
        Assert.False(fired);
    }

    [Fact]
    public void A_tap_held_over_one_second_does_not_fire()
    {
        var state = default(TapState);
        bool fired = false;
        (state, var step) = ModifierTap.Apply(state, true, 0x11, 0, CtrlShift);
        fired |= step;
        (state, step) = ModifierTap.Apply(state, true, 0x10, 10, CtrlShift);
        fired |= step;
        (state, step) = ModifierTap.Apply(state, false, 0x11, 1011, CtrlShift);
        fired |= step;
        (state, step) = ModifierTap.Apply(state, false, 0x10, 1020, CtrlShift);
        fired |= step;
        Assert.False(fired);
    }

    [Fact]
    public void Ctrl_shift_x_does_not_fire()
    {
        var state = default(TapState);
        bool fired = false;
        foreach (var (down, key, time) in new (bool, int, long)[]
        {
            (true, 0x11, 0),
            (true, 0x10, 15),
            (true, 0x58, 30),
            (false, 0x58, 45),
            (false, 0x10, 60),
            (false, 0x11, 75),
        })
        {
            (state, var step) = ModifierTap.Apply(state, down, key, time, CtrlShift);
            fired |= step;
        }

        Assert.False(fired);
    }

    [Fact]
    public void A_chord_binding_does_not_fire_from_the_tap_gesture()
    {
        var chord = HotkeyBinding.Parse("Ctrl + Alt + K");
        var state = default(TapState);
        bool fired = false;
        foreach (var (down, key, time) in new (bool, int, long)[]
        {
            (true, 0x11, 0),
            (true, 0x12, 10),
            (true, 0x4B, 20),
            (false, 0x4B, 40),
            (false, 0x12, 50),
            (false, 0x11, 60),
        })
        {
            (state, var step) = ModifierTap.Apply(state, down, key, time, chord);
            fired |= step;
        }

        Assert.False(fired);
        Assert.True(chord.IsChord);
    }
}

public sealed class CaptureHotkeyTests
{
    [Fact]
    public void A_docked_tap_opens_the_widget()
    {
        var folder = Directory.CreateTempSubdirectory("tw-tap-open").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("half a thought");
            model.Dock();
            model.Tap("half a thought");

            Assert.False(model.Docked);
            Assert.True(model.Raised);
            Assert.Equal("half a thought", model.CaptureText);
            Assert.Empty(model.Tasks);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_tap_on_an_open_widget_behind_other_windows_brings_it_up_first()
    {
        var folder = Directory.CreateTempSubdirectory("tw-tap-unfocused").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("half a thought");
            model.NoteFocus(false);
            model.Tap("half a thought");

            Assert.True(model.Raised);
            Assert.False(model.Docked);
            Assert.False(model.WaitingDictation);
            Assert.Empty(model.Tasks);
            Assert.Equal("half a thought", model.CaptureText);

            model.Tap("half a thought");

            Assert.Equal("half a thought", Assert.Single(model.Tasks).Title);
            Assert.True(model.Docked);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Raising_shows_the_list_with_the_capture_box()
    {
        var folder = Directory.CreateTempSubdirectory("tw-tap-list").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.ToggleSettings();
            model.Dock();
            model.Tap("");

            Assert.True(model.Raised);
            Assert.False(model.SettingsOpen);
            Assert.True(model.ShowingCapture);

            model.Leave();
            model.ToggleDoneView();
            model.Raise();

            Assert.False(model.ShowingDone);
            Assert.True(model.ShowingCapture);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_tap_with_text_commits_and_docks()
    {
        var folder = Directory.CreateTempSubdirectory("tw-tap-commit").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.NoteFocus(true);
            model.Tap("buy milk");

            Assert.Equal("buy milk", Assert.Single(model.Tasks).Title);
            Assert.True(model.Docked);
            Assert.Equal("", model.CaptureText);
            Assert.False(model.WaitingDictation);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_empty_tap_commits_when_text_lands()
    {
        var folder = Directory.CreateTempSubdirectory("tw-dictate").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            model.NoteFocus(true);
            model.Tap("");

            Assert.True(model.WaitingDictation);
            Assert.Equal("Waiting for dictation…", model.CapturePlaceholder);
            Assert.False(model.Docked);
            Assert.Empty(model.Tasks);

            clock.Advance(TimeSpan.FromMilliseconds(3999));
            Assert.True(model.WaitingDictation);
            Assert.Empty(model.Tasks);

            model.UpdateDraft("email Sarah");

            Assert.False(model.WaitingDictation);
            Assert.True(model.Docked);
            Assert.Equal("email Sarah", Assert.Single(model.Tasks).Title);
            Assert.Equal("", model.CaptureText);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_empty_tap_docks_with_no_capture_after_four_seconds()
    {
        var folder = Directory.CreateTempSubdirectory("tw-dictate-timeout").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            model.NoteFocus(true);
            model.Tap("");
            clock.Advance(TimeSpan.FromMilliseconds(4000));

            Assert.False(model.WaitingDictation);
            Assert.True(model.Docked);
            Assert.Empty(model.Tasks);
            Assert.Equal("", model.CaptureText);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Enter_commits_and_leaves_the_widget_open()
    {
        var folder = Directory.CreateTempSubdirectory("tw-enter-stays").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("buy milk");
            model.CommitCapture();

            Assert.False(model.Docked);
            Assert.Equal("buy milk", Assert.Single(model.Tasks).Title);
            Assert.Equal("", model.CaptureText);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Leaving_docks_without_committing()
    {
        var folder = Directory.CreateTempSubdirectory("tw-leave").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("half a thought");
            model.Leave();

            Assert.True(model.Docked);
            Assert.Equal("half a thought", model.CaptureText);
            Assert.Empty(model.Tasks);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_deactivation_to_Wispr_Flow_leaves_the_widget_open()
    {
        var folder = Directory.CreateTempSubdirectory("tw-wispr").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("half a thought");
            model.Deactivate(@"D:\Apps\Wispr Flow.exe");

            Assert.False(model.Docked);
            Assert.Equal("half a thought", model.CaptureText);
            Assert.Empty(model.Tasks);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_deactivation_to_another_window_docks_without_committing()
    {
        var folder = Directory.CreateTempSubdirectory("tw-deactivate").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.UpdateDraft("half a thought");
            model.Deactivate("notepad");

            Assert.True(model.Docked);
            Assert.Equal("half a thought", model.CaptureText);
            Assert.Empty(model.Tasks);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_deactivation_over_full_screen_sends_the_widget_away()
    {
        var folder = Directory.CreateTempSubdirectory("tw-deactivate-cover").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.Dock();
            model.SetFullScreenApp(true);
            model.UpdateDraft("half a thought");
            model.Raise();
            model.Deactivate("notepad");

            Assert.False(model.Raised);
            Assert.True(model.Docked);
            Assert.False(model.ShowDock);
            Assert.Equal("half a thought", model.CaptureText);
            Assert.Empty(model.Tasks);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Wispr_Flow_does_not_dismiss_a_widget_raised_over_full_screen()
    {
        var folder = Directory.CreateTempSubdirectory("tw-wispr-cover").FullName;
        var model = new AppModel(folder, new ManualClock());
        try
        {
            model.Dock();
            model.SetFullScreenApp(true);
            model.UpdateDraft("half a thought");
            model.Raise();
            model.Deactivate("WisprFlow");

            Assert.True(model.Raised);
            Assert.True(model.Docked);
            Assert.Equal("half a thought", model.CaptureText);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_draft_survives_a_new_model_on_the_same_folder()
    {
        var folder = Directory.CreateTempSubdirectory("tw-draft-restart").FullName;
        var clock = new ManualClock();
        try
        {
            var model = new AppModel(folder, clock);
            model.UpdateDraft("email Sarah\r\nbefore Friday");
            model.Dock();
            clock.Advance(TimeSpan.FromMilliseconds(300));
            model.Dispose();

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal("email Sarah\r\nbefore Friday", again.CaptureText);
            Assert.Empty(again.Tasks);
            Assert.True(again.Docked);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Clearing_the_draft_is_restored_by_undo()
    {
        var folder = Directory.CreateTempSubdirectory("tw-clear-draft").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            model.UpdateDraft("buy milk");
            Assert.True(model.HasDraftText);

            model.ClearDraft();

            Assert.Equal("", model.CaptureText);
            Assert.False(model.HasDraftText);

            clock.Advance(TimeSpan.FromMilliseconds(300));
            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "tasks.json"))))
                Assert.Equal("", saved.RootElement.GetProperty("draft").GetString());

            model.Undo();

            Assert.Equal("buy milk", model.CaptureText);
            Assert.True(model.HasDraftText);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Rebinding_to_a_chord_persists()
    {
        var folder = Directory.CreateTempSubdirectory("tw-rebind-chord").FullName;
        var clock = new ManualClock();
        try
        {
            var model = new AppModel(folder, clock);
            Assert.Equal("Ctrl + Shift", model.Hotkey);
            Assert.True(model.HotkeyBinding.IsTap);
            Assert.Equal(
                "A modifier pair (like Ctrl + Shift) fires on a tap: press both, release, no other key. Some keyboards use Ctrl + Shift to switch language.",
                model.LanguageToggleWarning);
            Assert.False(model.HotkeyBinding.IsReserved);

            model.SetHotkey(HotkeyBinding.Parse("Ctrl + Alt + K"));
            Assert.Equal("Ctrl + Alt + K", model.Hotkey);
            Assert.True(model.HotkeyBinding.IsChord);
            clock.Advance(TimeSpan.FromMilliseconds(300));
            model.Dispose();

            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
                Assert.Equal("Ctrl + Alt + K", saved.RootElement.GetProperty("hotkey").GetString());

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal("Ctrl + Alt + K", again.Hotkey);
            Assert.True(again.HotkeyBinding.IsChord);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Rebinding_to_another_modifier_tap_persists()
    {
        var folder = Directory.CreateTempSubdirectory("tw-rebind-tap").FullName;
        var clock = new ManualClock();
        try
        {
            var model = new AppModel(folder, clock);
            model.SetHotkey(HotkeyBinding.Parse("Alt + Shift"));
            clock.Advance(TimeSpan.FromMilliseconds(300));
            model.Dispose();

            var again = new AppModel(folder, new ManualClock());
            Assert.Equal("Alt + Shift", again.Hotkey);
            Assert.True(again.HotkeyBinding.IsTap);
            Assert.False(again.HotkeyBinding.IsChord);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
