using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The glance (apps/xr/Android/glance), a Java client for Android, ports LabelText's rule. These are
/// the cases tooling/glance/glance.test.ts runs through the Java; here they run through LabelText, so
/// the two are held to one table. Change both together.
/// </summary>
public class GlanceParityTests
{
    [TestCase("  a\tb  c\n", "a b  c")]
    [TestCase("x\u202Ey", "x‹U+202E›y")]
    [TestCase("a\u200Bb", "a‹U+200B›b")]
    [TestCase("\uE000 icon", "‹U+E000› icon")]
    [TestCase("smile \U0001F600", "smile \U0001F600")]
    [TestCase("tag \U000E0041", "tag ‹U+E0041›")]
    [TestCase("line\u2028break", "line break")]
    [TestCase("no\u00A0break", "no break")]
    [TestCase("", "")]
    public void TheGlanceShowsTextAsLabelTextDoes(string input, string expected) =>
        Assert.That(LabelText.Plain(input), Is.EqualTo(expected));

    [Test]
    public void AnUnpairedSurrogateShowsAsItsCode() =>
        // Built in code: an attribute's string cannot carry an unpaired surrogate intact.
        Assert.That(LabelText.Plain("lone " + (char)0xD800 + " half"), Is.EqualTo("lone ‹U+D800› half"));
}
