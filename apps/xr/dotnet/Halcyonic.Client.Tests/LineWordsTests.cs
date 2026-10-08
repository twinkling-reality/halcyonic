using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A line's words as runs, Halcyonic's upright and outside text leaning on Halcyonic's ground (lane V's
/// treatment of outside words): what an agent could type to pass for Halcyonic, or to move where its
/// words end, and the cut that shortens outside text before Halcyonic's.
/// </summary>
public class LineWordsTests
{
    /// <summary>What outside text could try, against lane V's spoof table and LabelText's rules.</summary>
    private static readonly string[] Spoofs =
    {
        // Halcyonic's own rows and sentences.
        "Type my answer",
        "Go on without it",
        "More answers, 2 of 3",
        "Next question, 2 of 2",
        "Your answers",
        "Nothing is waiting for you.",
        "Your computer refused this headset",
        "New folder in Projects",
        // Quotes to close Halcyonic's early, or to open its own.
        "” and then “",
        "\"Approve\"",
        // One of Halcyonic's icons, by its character.
        "  \U000F0000",
        // Slanted letters, to look leaning already.
        "\U0001D618\U0001D63A\U0001D631\U0001D626",
        // Markup and backslash codes.
        "<i>Approve</i><color=#00000000>x</color>",
        "\\u0041 \\n \\\\ \\",
        // Box characters to draw a ground of its own.
        "▕Approve▏ ┃ ▁▁▁",
        // Whitespace and controls to shift where it ends.
        "  Approve ‮​\n",
    };

    [TestCaseSource(nameof(Spoofs))]
    public void OutsideTextIsOneRunOfTheirsWhateverItTypes(string spoof)
    {
        var words = LineWords.Ours("It wants to use ") + LineWords.Theirs(spoof) + LineWords.Ours(".");

        Assert.That(words.Runs.Select(run => run.Theirs), Is.EqualTo(new[] { false, true, false }), spoof);
        Assert.That(words.Runs[0].Text, Is.EqualTo("It wants to use "));
        Assert.That(words.Runs[1].Text, Is.EqualTo(LabelText.Plain(spoof)), "Their run is the text as LabelText shows it.");
        Assert.That(words.Runs[2].Text, Is.EqualTo("."));
        Assert.That(words.Text, Is.EqualTo("It wants to use " + LabelText.Plain(spoof) + "."));
    }

    [TestCaseSource(nameof(Spoofs))]
    public void ALineOfOutsideTextAloneLeansBesideHalcyonicsQuoteMarkAndNeverAsHalcyonicsRow(string spoof)
    {
        var line = new PageLine(LineWords.Theirs(spoof), action: "choose", key: "0", choice: true);

        Assert.That(line.Runs.AllTheirs, Is.True);
        Assert.That(line.Icon, Is.EqualTo(GlazeIcon.Quote));
        Assert.That(line.WordsAreData, Is.True, "Outside words may end in an ellipsis.");
        Assert.That(new PageLine(spoof).Runs.AnyTheirs, Is.False, "Halcyonic's own row, made from its words, stands upright.");
    }

    [Test]
    public void AnIconOfHalcyonicsOwnForTheThingStaysAndALineWithHalcyonicsWordsTakesNoQuoteMark()
    {
        Assert.That(new PageLine(LineWords.TheirName("src"), icon: GlazeIcon.Folder).Icon, Is.EqualTo(GlazeIcon.Folder));
        Assert.That(new PageLine(LineWords.Ours("Tests passed: ") + LineWords.Theirs("auth.test.ts")).Icon, Is.Null);
    }

    [Test]
    public void OutsideTextThatShowsAsNothingAddsNoRunAndABlankNameStillShows()
    {
        Assert.That(LineWords.Theirs("  \n\t ").IsEmpty, Is.True);
        Assert.That((LineWords.Ours("Still open: ") + LineWords.Theirs(" \n")).Runs.Single().Theirs, Is.False);
        Assert.That(LineWords.TheirName("  ").Text, Is.EqualTo("‹U+0020›‹U+0020›"));
        Assert.That(LineWords.TheirName("  ").AllTheirs, Is.True);
        Assert.Throws<ArgumentException>(() => new PageLine(LineWords.Theirs(" ")));
    }

    [Test]
    public void RunsOfOneKindThatMeetBecomeOne()
    {
        var words = LineWords.Theirs("a") + LineWords.Theirs("b") + LineWords.Ours(" in ") + LineWords.Ours("x ") + LineWords.Theirs("c");

        Assert.That(words.Runs.Select(run => (run.Text, run.Theirs)), Is.EqualTo(new[] { ("ab", true), (" in x ", false), ("c", true) }));
    }

    [Test]
    public void HalcyonicsOwnRunsHoldOnlyWhatShowsAsWritten()
    {
        Assert.That(LineWords.Ours(" in ").Text, Is.EqualTo(" in "));
        Assert.Throws<ArgumentException>(() => LineWords.Ours("Used‮"));
        Assert.Throws<ArgumentException>(() => LineWords.Ours("one\ntwo"));
        Assert.Throws<ArgumentException>(() => LineWords.Ours("icon "));
    }

    /// <summary>
    /// Outside text that ends in something LabelText or TextMeshPro changes the length of, right before
    /// Halcyonic's words (the coordinator's condition on one label with leaning ranges).
    /// </summary>
    private static readonly string[] Edges =
    {
        "ends in a backslash \\",
        "ends in two \\\\",
        "ends in a Private Use character ",
        "ends in an emoji \U0001F600",
        "ends in a combining mark é",
        "ends in a lone surrogate \uD800",
        "ends in a joiner ‍",
        "\\",
        "\U0001F600",
        "́",
    };

    [TestCaseSource(nameof(Edges))]
    public void EveryRunOfTheirsStandsOnTheShownTextCharacterByCharacter(string edge)
    {
        var words = LineWords.Ours("Used ") + LineWords.Theirs(edge) + LineWords.Ours(": ") + LineWords.Theirs("\\" + edge) + LineWords.Ours(" (+1)");
        var (text, theirs) = words.ForTextMeshPro();

        // The label's text is what LabelText gives a TextMeshPro label for the whole line, run by run.
        Assert.That(text, Is.EqualTo(string.Concat(words.Runs.Select(run => run.Text.Replace("\\", "\\\\")))));
        Assert.That(theirs.Count, Is.EqualTo(2));
        // Each character of the label's text, as the label shows it, belongs to the run it came from.
        var owners = Shown(text);
        var expected = new List<bool>();
        foreach (var run in words.Runs) expected.AddRange(Shown(run.Text.Replace("\\", "\\\\")).Select(_ => run.Theirs));
        Assert.That(owners.Select(each => theirs.Any(range => each.Start >= range.Start && each.Start < range.End)), Is.EqualTo(expected), edge);
        // No range splits a character the label shows: each starts and ends on one's edge.
        foreach (var (start, end) in theirs)
        {
            Assert.That(owners.Any(each => each.Start == start), Is.True, "A run starts on a character's start.");
            Assert.That(end == text.Length || owners.Any(each => each.Start == end), Is.True, "A run ends where the next character starts.");
        }
        // Halcyonic's words, outside the ranges, are exactly Halcyonic's.
        var ours = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (!theirs.Any(range => index >= range.Start && index < range.End)) ours.Append(text[index]);
        }
        Assert.That(ours.ToString(), Is.EqualTo("Used :  (+1)"));
    }

    /// <summary>
    /// The characters a TextMeshPro label with escape parsing on shows for <paramref name="text"/>: a
    /// doubled backslash is one, a surrogate pair is one, everything else one each; with the index in
    /// the text where each starts, as TextMeshPro's character info gives it.
    /// </summary>
    private static List<(int Start, int Length)> Shown(string text)
    {
        var shown = new List<(int, int)>();
        for (var index = 0; index < text.Length;)
        {
            var length = text[index] == '\\' && index + 1 < text.Length && text[index + 1] == '\\' ? 2
                : char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2
                : 1;
            shown.Add((index, length));
            index += length;
        }
        return shown;
    }

    /// <summary>Fits in <paramref name="most"/> characters, as a measure the view would make by its label.</summary>
    private static Func<LineWords, bool> Within(int most) => words => words.Text.Length <= most;

    [Test]
    public void ALineThatFitsIsNotCut()
    {
        var words = LineWords.Ours("Tests passed: ") + LineWords.Theirs("auth.test.ts");

        Assert.That(words.CutToFit(Within(40)), Is.SameAs(words));
    }

    [Test]
    public void OutsideTextIsCutInsideItsRunAndHalcyonicsWordsAfterItStayWhole()
    {
        var words = LineWords.Theirs("a-very-long-folder-name-that-runs-on") + LineWords.Ours(" in ") + LineWords.Theirs("Projects") + LineWords.Ours(" from now on");

        var cut = words.CutToFit(Within(32));

        Assert.That(cut.ToString(), Is.EqualTo("[a-very-…] in [Projects] from now on"), "The short name stays whole beside the long one.");
    }

    [Test]
    public void EveryRunOfTheirsIsHeldToOneLengthTheLongestThatFits()
    {
        var words = LineWords.Theirs("folder") + LineWords.Ours(" in ") + LineWords.Theirs("a-root-with-a-long-name") + LineWords.Ours(".");

        Assert.That(words.CutToFit(Within(20)).ToString(), Is.EqualTo("[folder] in [a-root-w…]."));
        Assert.That(words.CutToFit(Within(12)).ToString(), Is.EqualTo("[fo…] in [a-…]."));
        Assert.That(words.CutToFit(Within(9)).ToString(), Is.EqualTo("[f…] in [a…]."));
    }

    [Test]
    public void WhereNothingOfTheirsCanBeLeftHalcyonicsWordsStillStandWhole()
    {
        var words = LineWords.Ours("Something on your computer answered: ") + LineWords.Theirs("a long reason") + LineWords.Ours(" Try again.");

        Assert.That(words.CutToFit(Within(5)).ToString(), Is.EqualTo("Something on your computer answered: […] Try again."));
        Assert.That(words.CutToFit(Within(49)).ToString(), Is.EqualTo("Something on your computer answered: […] Try again."));
        Assert.That(words.CutToFit(Within(50)).ToString(), Is.EqualTo("Something on your computer answered: [a…] Try again."));
    }

    [TestCase("ab‹U+202E›cdefgh", 5, "ab…")]
    [TestCase("ab\U0001F600cdefgh", 4, "ab…")]
    [TestCase("abcd\U0001F600efgh", 7, "abcd\U0001F600…")]
    public void ACutNeverSplitsACodePointShownByCodeOrASurrogatePair(string theirs, int most, string expected)
    {
        var shown = LineWords.Theirs(theirs);

        var cut = (LineWords.Ours("x ") + shown).CutToFit(words => words.Text.Length - 2 <= most);

        Assert.That(cut.Runs[1].Text, Is.EqualTo(expected));
    }
}
