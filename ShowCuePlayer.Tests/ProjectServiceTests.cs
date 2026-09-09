using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShowCuePlayer.Models;
using ShowCuePlayer.Services;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ShowCuePlayer.Tests;

[TestClass]
public sealed class ProjectServiceTests
{
    [TestMethod]
    public async Task SevenZyx_RoundTrip_PreservesKaraokeSoundboard()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"showcue-{Guid.NewGuid():N}.7zyx");
        try
        {
            var writer = new ProjectService();
            var project = await writer.NewProjectAsync();
            project.KaraokeSoundEffects = new()
            {
                new KaraokeSoundEffect
                {
                    Id = "applause",
                    Name = "Vỗ tay",
                    FilePath = @"D:\SoundFX\applause.mp3",
                    Loop = true,
                    Volume = 0.65,
                    HotkeyText = "Ctrl+F1"
                }
            };
            await writer.SaveAsAsync(filePath);

            var reader = new ProjectService();
            var loaded = await reader.OpenAsync(filePath);

            Assert.IsNotNull(loaded);
            Assert.IsNotNull(loaded.KaraokeSoundEffects);
            Assert.AreEqual(1, loaded.KaraokeSoundEffects.Count);
            Assert.AreEqual("Vỗ tay", loaded.KaraokeSoundEffects[0].Name);
            Assert.AreEqual(@"D:\SoundFX\applause.mp3", loaded.KaraokeSoundEffects[0].FilePath);
            Assert.IsTrue(loaded.KaraokeSoundEffects[0].Loop);
            Assert.AreEqual(0.65, loaded.KaraokeSoundEffects[0].Volume, 0.001);
            Assert.AreEqual("Ctrl+F1", loaded.KaraokeSoundEffects[0].HotkeyText);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }
}
