using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A file's path breaks only after a slash or a dot, never inside a name (the lead's finish item, 2026-11-16 06:09Z).</summary>
public class PathWrapTests
{
    private static Func<string, bool> Width(int most) => text => text.Length <= most;

    [Test]
    public void APathThatFitsIsOneLine()
    {
        Assert.That(PathWrap.Lines("src/game.js", Width(40)), Is.EqualTo(new[] { "src/game.js" }));
    }

    [Test]
    public void ALongNameBreaksAfterItsDotNeverInsideIt()
    {
        var lines = PathWrap.Lines("assets/welcome.uikitml", Width(14));
        Assert.That(lines, Is.EqualTo(new[] { "assets/", "welcome.", "uikitml" }));
        Assert.That(lines, Has.None.EqualTo("uikitm"), "the screen once read welcome.uikitm / l");
    }

    [Test]
    public void EveryLineEndsAfterASlashOrADotExceptTheLast()
    {
        var path = "apps/control-plane/src/network/usage-page.test.ts";
        var lines = PathWrap.Lines(path, Width(18));
        Assert.That(string.Concat(lines), Is.EqualTo(path), "nothing dropped, nothing added");
        Assert.That(lines.Take(lines.Count - 1).Select(line => line[^1]), Is.All.Matches<char>(end => end is '/' or '.'));
        Assert.That(lines.Any(line => line.Contains("usage-page")), "a hyphenated name stands whole");
    }

    [Test]
    public void APieceNoLineHoldsStandsAloneAndWhole()
    {
        var lines = PathWrap.Lines("a/averyveryverylongname", Width(6));
        Assert.That(lines, Is.EqualTo(new[] { "a/", "averyveryverylongname" }), "never cut: the view shrinks it");
    }

    [Test]
    public void ADotThatStartsANameIsPartOfIt()
    {
        Assert.That(PathWrap.Pieces("config/.env"), Is.EqualTo(new[] { "config/", ".env" }));
        Assert.That(PathWrap.Pieces("a.b"), Is.EqualTo(new[] { "a.", "b" }));
        Assert.That(PathWrap.Pieces("name."), Is.EqualTo(new[] { "name." }));
    }
}
