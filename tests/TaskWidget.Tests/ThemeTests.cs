using System.Text.Json;
using TaskWidget.Core;
using Xunit;

namespace TaskWidget.Tests;

public sealed class ThemeTests
{
    [Fact]
    public void Theme_follows_Windows_until_a_choice_overrides_it_and_the_choice_is_kept()
    {
        var folder = Directory.CreateTempSubdirectory("tw-theme").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            Assert.Equal(ThemeChoice.System, model.Theme);
            Assert.False(model.AppearsDark);

            model.ReportSystemAppearance(dark: true, backdropAvailable: true);
            Assert.True(model.AppearsDark);
            Assert.Equal(ThemeChoice.System, model.Theme);

            model.ReportSystemAppearance(dark: false, backdropAvailable: true);
            Assert.False(model.AppearsDark);
            Assert.False(File.Exists(Path.Combine(folder, "settings.json")));

            model.Theme = ThemeChoice.Dark;
            Assert.True(model.AppearsDark);
            Assert.Equal(2, model.ThemeIndex);

            model.ReportSystemAppearance(dark: false, backdropAvailable: true);
            Assert.True(model.AppearsDark);
            Assert.Equal(ThemeChoice.Dark, model.Theme);

            clock.Advance(TimeSpan.FromMilliseconds(300));
            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
            {
                Assert.Equal(1, saved.RootElement.GetProperty("schemaVersion").GetInt32());
                Assert.Equal("dark", saved.RootElement.GetProperty("theme").GetString());
            }

            model.Dispose();
            var again = new AppModel(folder, new ManualClock());
            again.ReportSystemAppearance(dark: false, backdropAvailable: true);
            Assert.Equal(ThemeChoice.Dark, again.Theme);
            Assert.True(again.AppearsDark);

            again.Theme = ThemeChoice.Light;
            again.ReportSystemAppearance(dark: true, backdropAvailable: true);
            Assert.False(again.AppearsDark);
            again.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Backdrop_falls_back_to_solid_without_changing_the_saved_choice()
    {
        var folder = Directory.CreateTempSubdirectory("tw-backdrop").FullName;
        var clock = new ManualClock();
        var model = new AppModel(folder, clock);
        try
        {
            Assert.Equal(BackdropChoice.Mica, model.Backdrop);
            Assert.Equal(BackdropChoice.Mica, model.EffectiveBackdrop);
            Assert.False(model.ShowBackdropNote);

            model.Backdrop = BackdropChoice.Acrylic;
            Assert.Equal(2, model.BackdropIndex);
            model.ReportSystemAppearance(dark: false, backdropAvailable: false);

            Assert.Equal(BackdropChoice.Acrylic, model.Backdrop);
            Assert.Equal(BackdropChoice.Solid, model.EffectiveBackdrop);
            Assert.True(model.ShowBackdropNote);

            model.Backdrop = BackdropChoice.MicaAlt;
            Assert.Equal(BackdropChoice.MicaAlt, model.Backdrop);
            Assert.Equal(BackdropChoice.Solid, model.EffectiveBackdrop);

            clock.Advance(TimeSpan.FromMilliseconds(300));
            using (var saved = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
                Assert.Equal("micaAlt", saved.RootElement.GetProperty("backdrop").GetString());

            model.ReportSystemAppearance(dark: true, backdropAvailable: true);
            Assert.Equal(BackdropChoice.MicaAlt, model.Backdrop);
            Assert.Equal(BackdropChoice.MicaAlt, model.EffectiveBackdrop);
            Assert.False(model.ShowBackdropNote);
            Assert.True(model.AppearsDark);

            clock.Advance(TimeSpan.FromMilliseconds(300));
            using (var again = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "settings.json"))))
                Assert.Equal("micaAlt", again.RootElement.GetProperty("backdrop").GetString());

            model.Dispose();
            var reloaded = new AppModel(folder, new ManualClock());
            Assert.Equal(BackdropChoice.MicaAlt, reloaded.Backdrop);
            Assert.Equal(BackdropChoice.MicaAlt, reloaded.EffectiveBackdrop);
            reloaded.ReportSystemAppearance(dark: false, backdropAvailable: false);
            Assert.Equal(BackdropChoice.MicaAlt, reloaded.Backdrop);
            Assert.Equal(BackdropChoice.Solid, reloaded.EffectiveBackdrop);
            reloaded.Dispose();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void An_older_settings_file_keeps_the_system_theme_and_Mica()
    {
        var folder = Directory.CreateTempSubdirectory("tw-theme-old").FullName;
        File.WriteAllText(Path.Combine(folder, "settings.json"), """{"schemaVersion":1,"docked":true}""");
        var model = new AppModel(folder, new ManualClock());
        try
        {
            Assert.Equal(ThemeChoice.System, model.Theme);
            Assert.Equal(BackdropChoice.Mica, model.Backdrop);
            Assert.True(model.Docked);
            Assert.Equal(0, model.ThemeIndex);
            Assert.Equal(0, model.BackdropIndex);

            model.ThemeIndex = -1;
            model.BackdropIndex = 9;
            Assert.Equal(ThemeChoice.System, model.Theme);
            Assert.Equal(BackdropChoice.Mica, model.Backdrop);
        }
        finally
        {
            model.Dispose();
            Directory.Delete(folder, recursive: true);
        }
    }
}
