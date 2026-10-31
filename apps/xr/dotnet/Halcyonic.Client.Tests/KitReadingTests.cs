using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A command read whole before it can run (SECURITY.md; the art direction's reading surface): Run only once the last line has
/// been shown, never locked again by scrolling back, progress that only grows, comments told apart, every line reachable.
/// </summary>
public class KitReadingTests
{
    private static string Command(int lines) => string.Join("\n", Enumerable.Range(1, lines).Select(i => i % 4 == 0 ? "# step " + i : "npm run step-" + i));

    [Test]
    public void RunUnlocksOnlyOnceTheLastLineHasBeenShown()
    {
        var reading = new KitReading(Command(30));
        Assert.That(reading.ReadWhole, Is.False, "nothing shown yet");
        reading.Show(0, 8);
        Assert.That((reading.ReadWhole, reading.Furthest), Is.EqualTo((false, 7)));
        reading.Show(21, 8);
        Assert.That(reading.ReadWhole, Is.False, "line 29 of 30 is still unshown");
        reading.Show(22, 8);
        Assert.That(reading.ReadWhole, Is.True, "the last line shown");
        reading.Show(0, 8);
        Assert.That(reading.ReadWhole, Is.True, "scrolling back never locks Run again");
    }

    [Test]
    public void ProgressOnlyGrows()
    {
        var reading = new KitReading(Command(20));
        var seen = new[] { 0, 5, 2, 12, 0, 10, 12 }.Select(first => { reading.Show(first, 8); return reading.Progress; }).ToList();
        Assert.That(seen.Zip(seen.Skip(1), (a, b) => b >= a).All(up => up), Is.True, string.Join(", ", seen));
        Assert.That(seen.Last(), Is.EqualTo(1.0));
        Assert.That(new KitReading(Command(20)).Progress, Is.EqualTo(0.0), "nothing read before anything is shown");
    }

    [Test]
    public void CommentsAreToldApartAndStillRead()
    {
        var reading = new KitReading("# set up\nnpm install\n   # indented note\nnpm run build\n");
        Assert.That(Enumerable.Range(0, reading.Lines.Count).Select(reading.IsComment), Is.EqualTo(new[] { true, false, true, false }));
        Assert.That(reading.Lines.Count, Is.EqualTo(4), "a trailing newline adds no line");
    }

    [Test]
    public void EveryLineOfAnyLengthIsReachable()
    {
        foreach (var lines in new[] { 1, 2, 8, 9, 57 })
        {
            var reading = new KitReading(Command(lines));
            var first = reading.Show(int.MaxValue, 8);
            Assert.That(reading.ReadWhole, Is.True, $"{lines} lines: the end reachable");
            Assert.That(first, Is.EqualTo(System.Math.Max(0, lines - 8)), $"{lines} lines: the last page is full");
            Assert.That((reading.HiddenAbove, reading.HiddenBelow(8)), Is.EqualTo((lines > 8, false)), $"{lines} lines: fades only where lines are hidden");
        }
    }

    /// <summary>A well that holds <paramref name="columns"/> characters a row, two fewer on a row that hangs indented.</summary>
    private static System.Func<LineWords, bool, bool> Columns(int columns) => (words, continued) => words.Text.Length <= (continued ? columns - 2 : columns);

    private static string[] RowsOf(KitReading reading, int line) => reading.Rows.Where(row => row.Line == line).Select(row => row.Text).ToArray();

    [Test]
    public void ALineLongerThanTheWellIsReadOnlyOnceItsLastRowIsShown()
    {
        var thousand = string.Concat(Enumerable.Range(0, 100).Select(i => "--tower" + (i % 10) + "  ")).Substring(0, 1000);
        var alone = new KitReading(thousand, Columns(60));
        Assert.That(alone.Rows.Count, Is.GreaterThan(16), "1,000 characters at 60 a row");
        alone.Show(0, 8);
        Assert.That((alone.ReadWhole, alone.Progress < 1), Is.EqualTo((false, true)), "the line's first rows only");
        alone.Show(8, 8);
        Assert.That(alone.ReadWhole, Is.False, "its last row still unshown");
        alone.Show(int.MaxValue, 8);
        Assert.That(alone.ReadWhole, Is.True, "its last row shown");

        var between = new KitReading("echo start\n" + thousand + "\necho end", Columns(60));
        var lastOfLong = between.Rows.Count - 2;
        between.Show(lastOfLong - 7, 8);
        Assert.That((between.ReadWhole, between.Furthest), Is.EqualTo((false, lastOfLong)), "the long line read whole, the last line not yet");
        between.Show(lastOfLong - 6, 8);
        Assert.That(between.ReadWhole, Is.True);
    }

    [Test]
    public void ALineWrapsOnlyAtSpacesWhileItsTokensFit()
    {
        var line = string.Join(" ", Enumerable.Range(0, 40).Select(i => i % 3 == 0 ? "--workspace" : i % 3 == 1 ? "towers-" + i : "apps/tower-toss"));
        var rows = RowsOf(new KitReading(line, Columns(30)), 0);
        Assert.That(rows.Length, Is.GreaterThan(10));
        Assert.That(string.Concat(rows), Is.EqualTo(line), "every character kept, in order");
        Assert.That(rows.Take(rows.Length - 1).All(row => row.EndsWith(" ", System.StringComparison.Ordinal)), Is.True, string.Join(" | ", rows));
        Assert.That(rows.Skip(1).All(row => row[0] != ' '), Is.True, "a row after the first starts with a word");
        Assert.That(new KitReading(line, Columns(30)).Rows.Select(row => row.Continued), Is.EqualTo(rows.Select((_, index) => index > 0)), "every row after the first hangs");
    }

    [Test]
    public void ATokenLongerThanARowBreaksAfterItsLastSlashThatFits()
    {
        var url = "https://example.com/" + string.Concat(Enumerable.Repeat("segment/", 12)) + "end";
        var rows = RowsOf(new KitReading("curl " + url, Columns(30)), 0);
        Assert.That(string.Concat(rows), Is.EqualTo("curl " + url));
        Assert.That(rows[0], Is.EqualTo("curl "), "the token goes to its own row before it breaks");
        Assert.That(rows.Skip(1).Take(rows.Length - 2).All(row => row.EndsWith("/", System.StringComparison.Ordinal) && row.Length <= 28), Is.True, string.Join(" | ", rows));
    }

    [Test]
    public void AnUnbrokenTokenLongerThanARowBreaksWhereTheRowEnds()
    {
        var token = string.Concat(Enumerable.Repeat("c2lnbmVk", 50));
        var reading = new KitReading(token, Columns(60));
        Assert.That(RowsOf(reading, 0).Select(row => row.Length), Is.EqualTo(new[] { 60, 58, 58, 58, 58, 58, 50 }));
        reading.Show(0, 6);
        Assert.That(reading.ReadWhole, Is.False, "the token's last row unshown");
        reading.Show(1, 6);
        Assert.That(reading.ReadWhole, Is.True);
        var flag = "--" + new string('x', 27) + "-" + new string('y', 40);
        Assert.That(RowsOf(new KitReading(flag, Columns(30)), 0).All(row => !row.EndsWith("-", System.StringComparison.Ordinal)), Is.True, "never after a hyphen, so never between -- and its name");
    }

    [Test]
    public void RowsLoseNothingAndSplitNothing()
    {
        var reading = new KitReading("    npm run build\t--watch\n" + new string('a', 59) + "\U0001F600" + new string('b', 70) + "\n#  note  ", Columns(60));
        for (var line = 0; line < reading.Lines.Count; line++)
            Assert.That(string.Concat(RowsOf(reading, line)), Is.EqualTo(reading.ShownLine(line)), $"line {line + 1} whole");
        Assert.That(reading.ShownLine(0), Is.EqualTo("    npm run build‹U+0009›--watch"), "indentation kept, a tab shown as its code point");
        Assert.That(reading.ShownLine(2), Is.EqualTo("#  note  "), "trailing spaces kept");
        Assert.That(RowsOf(reading, 1).Select(row => row.Length), Is.EqualTo(new[] { 59, 58, 14 }), "a break inside the pair moved before it");
        Assert.That(new KitReading("ab\U0001F600", (_, _) => false).Rows.Select(row => row.Text), Is.EqualTo(new[] { "a", "b", "\U0001F600" }), "a character at least to a row, a pair kept whole");
    }

    [Test]
    public void TheReadersWordsAreTheVoiceStandards()
    {
        Assert.That(new[] { KitReaderWords.ReadToEnd, KitReaderWords.EndOfCommand, KitReaderWords.Fold }, Is.EqualTo(new[] { "Read to End", "End of Command", "Fold" }));
    }
}
