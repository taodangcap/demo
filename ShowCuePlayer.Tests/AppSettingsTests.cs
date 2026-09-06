using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShowCuePlayer.Models;

[assembly: DoNotParallelize]

namespace ShowCuePlayer.Tests;

[TestClass]
public sealed class AppSettingsTests
{
    [TestMethod]
    public void Validate_ClampsInvalidNumericValues()
    {
        var settings = new AppSettings
        {
            MasterVolume = double.NaN,
            AudioBusVolume = -3,
            VideoBusVolume = 8,
            CrossfadeSeconds = double.PositiveInfinity,
            AudioBufferMs = -1
        };

        settings.Validate();

        Assert.AreEqual(1, settings.MasterVolume);
        Assert.AreEqual(0, settings.AudioBusVolume);
        Assert.AreEqual(1, settings.VideoBusVolume);
        Assert.AreEqual(2, settings.CrossfadeSeconds);
        Assert.AreEqual(5, settings.AudioBufferMs);
    }

    [TestMethod]
    public void EnsureHotkeys_KeepsSupportedBindingsAndRemovesLegacyActions()
    {
        var settings = new AppSettings
        {
            Hotkeys = new()
            {
                [HotkeyActions.ToggleKaraokeOnOff] = "F8",
                ["Go"] = "Enter",
                ["ToggleLedBlackout"] = "B"
            }
        };
        settings.EnsureHotkeys();

        Assert.AreEqual("F8", settings.Hotkeys[HotkeyActions.ToggleKaraokeOnOff]);
        Assert.IsTrue(settings.Hotkeys.ContainsKey(HotkeyActions.StopAll));
        Assert.IsFalse(settings.Hotkeys.ContainsKey("Go"));
        Assert.IsFalse(settings.Hotkeys.ContainsKey("ToggleLedBlackout"));
        Assert.AreEqual(HotkeyActions.Catalog.Count, settings.Hotkeys.Count);
    }

    [TestMethod]
    public void Karaoke_DefaultsToManualTake()
    {
        var settings = new AppSettings();

        Assert.IsFalse(settings.KaraokeAutoNextEnabled);
    }

    [TestMethod]
    public void Validate_NormalizesKaraokeSoundEffects()
    {
        var settings = new AppSettings
        {
            KaraokeSoundEffects = new()
            {
                new KaraokeSoundEffect
                {
                    Id = "same", Name = "", FilePath = @"C:\Fx\applause.mp3", Volume = double.NaN
                },
                new KaraokeSoundEffect
                {
                    Id = "same", Name = "Cổ vũ", FilePath = @"C:\Fx\cheer.wav",
                    HotkeyText = "  Shift+F2  ", Volume = 4
                }
            }
        };

        settings.Validate();

        Assert.AreEqual(2, settings.KaraokeSoundEffects.Count);
        Assert.AreEqual("applause", settings.KaraokeSoundEffects[0].Name);
        Assert.AreNotEqual(settings.KaraokeSoundEffects[0].Id, settings.KaraokeSoundEffects[1].Id);
        Assert.AreEqual("Shift+F2", settings.KaraokeSoundEffects[1].HotkeyText);
        Assert.AreEqual(1, settings.KaraokeSoundEffects[0].Volume);
        Assert.AreEqual(1, settings.KaraokeSoundEffects[1].Volume);
    }
}
