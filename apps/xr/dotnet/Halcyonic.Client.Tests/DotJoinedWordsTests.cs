using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The owner's ban on facts joined by a dot (2026-11-11), on every panel and page: a reason nothing was sent is one plain
/// sentence ("Nothing was sent: the task changed. Check it, then try again."), never "Not Sent · Task Changed · Check and Retry"
/// (AUDIT's MUST, 2026-11-16 09:41Z). The client core's older dot-joined words are counted, so they only ever shrink.
/// </summary>
public class DotJoinedWordsTests
{
    /// <summary>
    /// Lines of the client core that hold a string with a "·" in it, after the Not Sent family became sentences. Lower it as more
    /// become sentences; never raise it.
    /// </summary>
    private const int MostDotJoinedLines = 267;

    private static readonly Regex DottedString = new("\"[^\"\\n]*·[^\"\\n]*\"", RegexOptions.CultureInvariant);

    private static (string File, int Line, string Text)[] Lines() =>
        Directory.GetFiles(Repository.PathTo("apps/xr/Packages/com.halcyonic.client/Runtime"), "*.cs")
            .OrderBy(path => path, System.StringComparer.Ordinal)
            .SelectMany(path => File.ReadAllLines(path).Select((text, index) => (File: Path.GetFileName(path), Line: index + 1, Text: text)))
            .Where(line => !line.Text.TrimStart().StartsWith("//", System.StringComparison.Ordinal))
            .ToArray();

    [Test]
    public void NothingSentIsSaidAsASentenceNeverAsDotJoinedFacts()
    {
        var dotted = Lines().Where(line => line.Text.Contains("Not Sent ·", System.StringComparison.Ordinal)
            || line.Text.Contains("Not Sendable ·", System.StringComparison.Ordinal)
            || line.Text.Contains("Code Not Sent ·", System.StringComparison.Ordinal)).ToArray();
        Assert.That(dotted.Select(line => line.File + ":" + line.Line), Is.Empty, "say why nothing was sent as one sentence");
    }

    [Test]
    public void DotJoinedWordsOnlyEverShrink()
    {
        var count = Lines().Count(line => DottedString.IsMatch(line.Text));
        Assert.That(count, Is.LessThanOrEqualTo(MostDotJoinedLines), "a new dot-joined string: lay its parts apart or say it as a sentence");
    }
}
