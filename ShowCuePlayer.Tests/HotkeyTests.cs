using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShowCuePlayer.Helpers;
using ShowCuePlayer.Services;
using System;
using System.Windows.Input;

namespace ShowCuePlayer.Tests;

[TestClass]
public sealed class HotkeyTests
{
    [TestMethod]
    public void TryParse_AcceptsStandardAndAliasBindings()
    {
        Assert.IsTrue(HotkeyUtil.TryParse("Ctrl+Shift+A", out var key1, out var mod1));
        Assert.AreEqual(Key.A, key1);
        Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, mod1);
        Assert.AreEqual("Ctrl+Shift+A", HotkeyUtil.Format(key1, mod1));

        Assert.IsTrue(HotkeyUtil.TryParse("Esc", out var escKey, out var escMod));
        Assert.AreEqual(Key.Escape, escKey);
        Assert.AreEqual(ModifierKeys.None, escMod);

        Assert.IsTrue(HotkeyUtil.TryParse("Alt+Enter", out var enterKey, out var enterMod));
        Assert.AreEqual(Key.Return, enterKey);
        Assert.AreEqual(ModifierKeys.Alt, enterMod);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Ctrl+Bogus+F1")]
    [DataRow("F1+F2")]
    [DataRow("Ctrl")]
    public void TryParse_RejectsInvalidBindings(string binding)
    {
        Assert.IsFalse(HotkeyUtil.TryParse(binding, out var key, out var mods));
        Assert.AreEqual(Key.None, key);
        Assert.AreEqual(ModifierKeys.None, mods);
    }

    [TestMethod]
    public void HotkeyService_DetectsConflictsAndUnregistersCleanly()
    {
        using var service = new HotkeyService(NullLogger<HotkeyService>.Instance);
        var cue1 = Guid.NewGuid();
        var cue2 = Guid.NewGuid();

        Assert.IsTrue(service.Register(cue1, "Ctrl+F1", Key.F1, ModifierKeys.Control));
        Assert.IsTrue(service.HasConflict(Key.F1, ModifierKeys.Control, excludeCueId: cue2));
        Assert.IsFalse(service.HasConflict(Key.F1, ModifierKeys.Control, excludeCueId: cue1));
        Assert.IsFalse(service.Register(cue2, "Ctrl+F1", Key.F1, ModifierKeys.Control));

        service.Unregister(cue1);
        Assert.IsTrue(service.Register(cue2, "Ctrl+F1", Key.F1, ModifierKeys.Control));
        Assert.AreEqual(1, service.GetAll().Count);
    }
}
