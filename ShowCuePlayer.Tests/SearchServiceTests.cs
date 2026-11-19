using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShowCuePlayer.Models;
using ShowCuePlayer.Services;

namespace ShowCuePlayer.Tests;

[TestClass]
public sealed class SearchServiceTests
{
    private readonly SearchService _service = new();

    [TestMethod]
    public void Search_EmptyQuery_ReturnsAllCues()
    {
        var cues = new[]
        {
            new CueModel { Title = "Track A", FilePath = @"C:\a.mp3" },
            new CueModel { Title = "Track B", FilePath = @"C:\b.mp3" }
        };

        var results = _service.Search(cues, "   ");

        Assert.AreEqual(2, results.Count);
    }

    [TestMethod]
    public void Search_MatchesVietnameseWithoutDiacriticsAndHandlesDStroke()
    {
        var cues = new[]
        {
            new CueModel { Title = "Độ Ta Không Độ Nàng", Artist = "Anh Duy", FilePath = @"C:\music\dtkdn.mp3" },
            new CueModel { Title = "Hoa Nở Không Màu", Artist = "Hoài Lâm", FilePath = @"C:\music\hnkm.mp3" },
            new CueModel { Title = "Other Song", Artist = "Singer", Notes = "intro nhẹ", HotkeyText = "Ctrl+F5", FilePath = @"C:\music\x.mp3" }
        };

        var dStrokeMatch = _service.Search(cues, "do ta khong do nang");
        var diacriticMatch = _service.Search(cues, "hoai lam");
        var hotkeyMatch = _service.Search(cues, "ctrl+f5");
        var noteMatch = _service.Search(cues, "intro nhe");

        Assert.AreEqual(1, dStrokeMatch.Count);
        Assert.AreEqual("Độ Ta Không Độ Nàng", dStrokeMatch[0].Title);
        Assert.AreEqual(1, diacriticMatch.Count);
        Assert.AreEqual("Hoa Nở Không Màu", diacriticMatch[0].Title);
        Assert.AreEqual(1, hotkeyMatch.Count);
        Assert.AreEqual("Other Song", hotkeyMatch[0].Title);
        Assert.AreEqual(1, noteMatch.Count);
        Assert.AreEqual("Other Song", noteMatch[0].Title);
    }
}
