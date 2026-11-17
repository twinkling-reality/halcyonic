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

    [Test]
    public void APieceNoLineHoldsBreaksAfterAHyphenOrBeforeACapitalNeverInsideLetters()
    {
        Assert.That(PathWrap.FinerPieces("welcomeScreen.uikitml"), Is.EqualTo(new[] { "welcome", "Screen.uikitml" }));
        Assert.That(PathWrap.FinerPieces("golden-balloon_test"), Is.EqualTo(new[] { "golden-", "balloon_", "test" }));
        Assert.That(PathWrap.FinerPieces("README"), Is.EqualTo(new[] { "README" }), "capitals in a row are one word");
        Assert.That(PathWrap.FinerPieces("averyveryverylongname"), Is.EqualTo(new[] { "averyveryverylongname" }), "no break point: whole");
        Assert.That(PathWrap.FinerPieces("-x"), Is.EqualTo(new[] { "-x" }), "a leading hyphen is part of the name");
        foreach (var piece in new[] { "welcomeScreen.uikitml", "golden-balloon_test", "a-B-c" })
            Assert.That(string.Concat(PathWrap.FinerPieces(piece)), Is.EqualTo(piece), "nothing dropped or added");
    }

    private const string LongFile = "src/scenes/kite-meadow/components/tail-streamer/StreamingTailRendererWithWindGusts.ts";

    [Test]
    public void AStepsPathBreaksOnlyAfterASlashAndItsNameStaysWhole()
    {
        var (lines, shown) = PathWrap.Fold("Editing ", LongFile, Width(40));
        Assert.That(lines.Count, Is.LessThanOrEqualTo(3));
        Assert.That(string.Concat(lines), Is.EqualTo("Editing " + shown), "nothing dropped or added past the elision");
        Assert.That(shown, Does.EndWith("/StreamingTailRendererWithWindGusts.ts"), "the file's name is never shortened");
        Assert.That(lines[lines.Count - 1], Does.EndWith("StreamingTailRendererWithWindGusts.ts"), "the name stands whole on the last line");
        // Every line but the last ends at a slash, or is the act alone: never inside a folder's name at its hyphen.
        foreach (var line in lines.Take(lines.Count - 1)) Assert.That(line.EndsWith("/", StringComparison.Ordinal) || line == "Editing ", Is.True, "\"" + line + "\" breaks inside a name");
    }

    [Test]
    public void ALongPathLeavesOutItsLeadingFoldersBeforeItTakesAFourthLine()
    {
        const string deeper = "src/scenes/kite-meadow/components/tail-streamer/effects/wind/gusts/StreamingTailRendererWithWindGusts.ts";
        Assert.That(PathWrap.Lines("Editing " + deeper, Width(40)).Count, Is.GreaterThan(3), "the whole path would take a fourth line");
        var (lines, shown) = PathWrap.Fold("Editing ", deeper, Width(40));
        Assert.That(lines.Count, Is.LessThanOrEqualTo(3));
        Assert.That(shown, Does.StartWith(PathWrap.Elision), "the leading folders give way to the elision");
        Assert.That(deeper.EndsWith(shown.Substring(PathWrap.Elision.Length), StringComparison.Ordinal), Is.True, "only leading folders are left out, whole");
        Assert.That(shown, Does.EndWith("/StreamingTailRendererWithWindGusts.ts"));
        Assert.That(string.Concat(lines), Is.EqualTo("Editing " + shown));
        // Where it fits, nothing is left out.
        Assert.That(PathWrap.Fold("Editing ", LongFile, Width(200)), Is.EqualTo(((System.Collections.Generic.IReadOnlyList<string>)new[] { "Editing " + LongFile }, LongFile)).Using(new FoldComparer()));
        Assert.That(PathWrap.Fold("Editing ", "index.html", Width(40)).Lines, Is.EqualTo(new[] { "Editing index.html" }));
    }

    [Test]
    public void ANameTooLongForALineBreaksOnlyAtItsOwnWordBoundaries()
    {
        var (lines, shown) = PathWrap.Fold("Editing ", "src/StreamingTailRendererWithWindGusts.ts", Width(20));
        Assert.That(shown, Does.EndWith("StreamingTailRendererWithWindGusts.ts"), "the name is never shortened");
        var name = string.Concat(lines).Substring(string.Concat(lines).LastIndexOf('/') + 1);
        Assert.That(name, Is.EqualTo("StreamingTailRendererWithWindGusts.ts"));
        // Each break inside the name falls before a capital that follows a small letter, or after its dot.
        var at = 0;
        foreach (var line in lines)
        {
            at += line.Length;
            var whole = string.Concat(lines);
            if (at >= whole.Length || whole[at - 1] == '/' || whole[at - 1] == ' ') continue;
            Assert.That(whole[at - 1] == '.' || char.IsLower(whole[at - 1]) && char.IsUpper(whole[at]), Is.True, "a break inside a word at " + at + " in \"" + whole + "\"");
        }
    }

    private sealed class FoldComparer : System.Collections.Generic.IEqualityComparer<(System.Collections.Generic.IReadOnlyList<string> Lines, string Shown)>
    {
        public bool Equals((System.Collections.Generic.IReadOnlyList<string> Lines, string Shown) a, (System.Collections.Generic.IReadOnlyList<string> Lines, string Shown) b) =>
            a.Shown == b.Shown && a.Lines.SequenceEqual(b.Lines);

        public int GetHashCode((System.Collections.Generic.IReadOnlyList<string> Lines, string Shown) value) => value.Shown.GetHashCode();
    }
}
