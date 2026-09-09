using Microsoft.VisualStudio.TestTools.UnitTesting;
using ShowCuePlayer.Services;
using System;

namespace ShowCuePlayer.Tests;

[TestClass]
public sealed class GitHubUpdateServiceTests
{
    [TestMethod]
    [DataRow("v1.2.3", 1, 2, 3)]
    [DataRow("1.4.0", 1, 4, 0)]
    [DataRow("v2.0.1-beta", 2, 0, 1)]
    public void TryParseReleaseVersion_AcceptsGitHubSemverTags(
        string tag, int major, int minor, int build)
    {
        var parsed = GitHubUpdateService.TryParseReleaseVersion(tag, out var version);

        Assert.IsTrue(parsed);
        Assert.AreEqual(new Version(major, minor, build), version);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("latest")]
    [DataRow("v1.two.3")]
    public void TryParseReleaseVersion_RejectsInvalidTags(string tag)
        => Assert.IsFalse(GitHubUpdateService.TryParseReleaseVersion(tag, out _));
}
