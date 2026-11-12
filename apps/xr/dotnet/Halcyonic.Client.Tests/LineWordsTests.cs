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
        "Your Answers",
        "Nothing is waiting for you.",
        "Your computer refused this headset",
        "New Folder · Projects",
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
        // Halcyonic's own row, made from its words, stands upright; words LabelText would change can't be made into one at all (AUDIT's X1).
        if (LabelText.Plain(spoof) == spoof.Trim(' ')) Assert.That(new PageLine(spoof).Runs.AnyTheirs, Is.False, "Halcyonic's own row, made from its words, stands upright.");
        else Assert.Throws<ArgumentException>(() => new PageLine(spoof), "raw outside text is never Halcyonic's row");
    }

    // As the untrusted-text journeys do: outside text in a string that a public constructor turns into Halcyonic's runs.
    private static readonly string Hostile = "\u202Eclaim\tline\r\nnext \u0003" + (char)0xD800;

    [Test]
    public void AnActivityEntryGivenOutsideTextAsAStringSpellsIt()
    {
        Assert.That(new ActivityEntry(1, Samples.Time, ActivityKind.Tool, Hostile, reported: false).Text, Is.EqualTo(LabelText.Plain(Hostile)));
    }

    [Test]
    public void ASectionLineGivenOutsideTextAsAStringSpellsIt()
    {
        var line = new SectionLine(Hostile, Hostile, SectionTone.Claim);
        Assert.That(line.Runs.Text, Is.EqualTo(LabelText.Plain(Hostile)));
        Assert.DoesNotThrow(() => line.Runs.ForTextMeshPro());
    }

    [Test]
    public void ASectionsProvenanceGivenAsAStringSpellsIt()
    {
        var section = new SectionPresentation(SectionKind.Understanding, Hostile, SectionTone.Secondary, Array.Empty<SectionLine>(), simulated: false);
        Assert.That(section.ProvenanceRuns.Text, Is.EqualTo(LabelText.Plain(Hostile)));
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
        Assert.That((LineWords.Ours("Open · ") + LineWords.Theirs(" \n")).Runs.Single().Theirs, Is.False);
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

    [TestCase(": ")]
    [TestCase(", ")]
    [TestCase("; ")]
    [TestCase(" \u00B7 ")]
    [TestCase(" ")]
    public void ASeparatorBetweenTwoRunsOfTheirsDoesNotMakeTheLineHalcyonics(string separator)
    {
        // "{header}: {labels}" is outside text whole, drawn as an answer row is, with no grounds round a colon (lane V).
        var words = LineWords.Theirs("Lockout") + LineWords.Ours(separator) + LineWords.Theirs("15 minutes");

        Assert.That(words.Runs.Single().Theirs, Is.True);
        Assert.That(words.Text, Is.EqualTo("Lockout" + separator + "15 minutes"));
        Assert.That((LineWords.Theirs("a") + LineWords.Ours(" in ") + LineWords.Theirs("b")).Runs.Count, Is.EqualTo(3), "a word is Halcyonic's, not a separator");
        Assert.That((LineWords.Ours("Used ") + LineWords.Theirs("a") + LineWords.Ours(".")).Runs.Count, Is.EqualTo(3), "punctuation at a line's end stays Halcyonic's");
    }

    [Test]
    public void APartNeverStartsOrEndsOnTheSecondBackslashOfAPair()
    {
        // Every backslash is doubled for the label: cutting a part between a pair's two would leave a lone one,
        // which turns the next characters into another (the independent review of batch B).
        var shown = LabelText.ForTextMeshPro("a\\u0003b\\\\c");
        var second = shown.IndexOf('\\') + 1;
        Assert.That(LabelText.CharacterStart(shown, second), Is.EqualTo(second - 1));
        Assert.That(LabelText.CharacterStart(shown, second - 1), Is.EqualTo(second - 1));
        var pairs = shown.LastIndexOf('c') - 4;
        Assert.That(Enumerable.Range(pairs, 4).Select(index => LabelText.CharacterStart(shown, index)), Is.EqualTo(new[] { pairs, pairs, pairs + 2, pairs + 2 }));
        Assert.That(LabelText.CharacterStart(shown, 0), Is.EqualTo(0));
        Assert.That(LabelText.CharacterStart(shown, shown.Length), Is.EqualTo(shown.Length));
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
    /// Words a line is given whole, Halcyonic's own or outside text its builder has already shown (AUDIT's X1, 2026-11-03):
    /// what LabelText's rule would change, line by line, is refused as Halcyonic's own runs refuse it, so a builder that
    /// forgot the rule fails here instead of putting an override, a zero-width space or an end of text on a label.
    /// </summary>
    private static readonly string[] Unshown =
    {
        "Used \u202Ekcatta",
        "Sent\u200B · Awaiting Start",
        "one\ttwo",
        "Changes Unverified\r\nOutput Shortened",
        "\uE5CC More",
        "Step\u2028Two",
    };

    [TestCaseSource(nameof(Unshown))]
    public void WordsGivenWholeRefuseWhatLabelTextWouldChangeLineByLine(string words)
    {
        Assert.Throws<ArgumentException>(() => new PageLine(words), words);
    }

    [Test]
    public void WordsGivenWholeRefuseAnEndOfTextOnALaterLine()
    {
        // Kept out of the cases above: a test runner drops a case whose name holds U+0003, so it would never run there.
        Assert.Throws<ArgumentException>(() => new PageLine("Output Shortened\nExit Code\u0003 · 1"));
    }

    [Test]
    public void WordsGivenWholeKeepTheirLinesAndTheSpacesAtTheirEnds()
    {
        Assert.That(new PageLine("Command Output · Exited\nToday 10:42\n").Runs.Text, Is.EqualTo("Command Output · Exited\nToday 10:42\n"));
        Assert.That(new PageLine("Latest · ").Runs.Text, Is.EqualTo("Latest · "));
        Assert.That(new PageLine("Exit Code · 1  Output").Words, Is.EqualTo("Exit Code · 1  Output"), "spaces inside a line stay as written");
        Assert.That(RecordedResultText.Body("  indented\n\tthen a tab  \n").Text, Is.EqualTo("  indented\n‹U+0009›then a tab  \n"),
            "recorded output, already shown line by line, keeps its indentation");
    }

    [Test]
    public void OutsideTextNotYetSpelledNeverReachesALabel()
    {
        // The review's names as they came (LineWords.Unspelled): a label refuses them, alone, joined to other runs, or cut.
        var raw = LineWords.Ours("Agent App · ") + LineWords.Unspelled("Cod\tex\u202E");
        Assert.Throws<InvalidOperationException>(() => raw.ForTextMeshPro());
        Assert.Throws<InvalidOperationException>(() => (LineWords.Unspelled("src") + LineWords.Theirs("games")).ForTextMeshPro(), "joined to a shown run");
        Assert.Throws<InvalidOperationException>(() => (LineWords.Theirs("a") + LineWords.Ours(" · ") + LineWords.Unspelled("b")).ForTextMeshPro(),
            "joined across a separator into one run of theirs");
        Assert.Throws<InvalidOperationException>(() => raw.CutToFit(words => words.Text.Length <= 16).ForTextMeshPro(), "cut to fit");
        Assert.That(LineWords.Unspelled("").IsEmpty, Is.True);
        Assert.That(() => (LineWords.Ours("Agent App · ") + LineWords.Theirs("Cod\tex\u202E")).ForTextMeshPro(), Throws.Nothing, "shown by the rule, it reaches the label");
    }

    [Test]
    public void TheReviewSpellsANameGivenWithATabALineBreakAndABidiMark()
    {
        // The review before Start building is given names as they came and spells every character itself (its Safe), so a
        // name a label could never show whole still reaches the person, spelled, and never throws.
        var review = new NewWorkReview("Tower\tToss\nTwo", "Title", "Cod\u202Eex", "qwen\t3", "On Your Computer", "ref\nid", "Make it wobble.",
            folder: "src\tgames\u202E");

        Assert.That(review.Items.Select(item => item.Text), Is.EqualTo(new[]
        {
            "Project · Tower\\u{9}Toss\\u{A}Two",
            "Folder · src\\u{9}games\\u{202E}",
            "Task Name · Title",
            "Agent App · Cod\\u{202E}ex",
            "Model · qwen\\u{9}3",
            "Model Location · On Your Computer",
            "Model ID · ref\\u{A}id",
            "First Task · Make it wobble.",
        }));
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
        var words = LineWords.Ours("Used ") + LineWords.Theirs(edge) + LineWords.Ours(" or ") + LineWords.Theirs("\\" + edge) + LineWords.Ours(" (+1)");
        var (text, theirs) = words.ForTextMeshPro();

        // The label's text is what LabelText gives a TextMeshPro label for the whole line, run by run, with one
        // more space wherever outside text meets Halcyonic's words, for the ground's padding.
        Assert.That(text, Is.EqualTo(string.Join(LineWords.Pad, words.Runs.Select(run => run.Text.Replace("\\", "\\\\")))));
        Assert.That(theirs.Count, Is.EqualTo(2));
        // Each character of the label's text, as the label shows it, belongs to the run it came from; the added spaces to none of theirs.
        var owners = Shown(text);
        var expected = new List<bool>();
        for (var index = 0; index < words.Runs.Count; index++)
        {
            if (index > 0) expected.Add(false);
            var run = words.Runs[index];
            expected.AddRange(Shown(run.Text.Replace("\\", "\\\\")).Select(_ => run.Theirs));
        }
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
        Assert.That(ours.ToString(), Is.EqualTo("Used " + LineWords.Pad + LineWords.Pad + " or " + LineWords.Pad + LineWords.Pad + " (+1)"));
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
    public void HalcyonicsPunctuationFollowsAGroundANarrowNoBreakSpaceAwayAndHalcyonicsSpaceAFullOne()
    {
        // Punctuation follows a ground's edge as it follows a letter (lane V): never a word space away.
        var approval = LineWords.Ours("It wants to use ") + LineWords.TheirName("bash") + LineWords.Ours(":");
        Assert.That(approval.ForTextMeshPro().Text, Is.EqualTo("It wants to use " + LineWords.Pad + "bash" + LineWords.NarrowPad + ":"));
        var source = LineWords.Ours("From Salidium ") + LineWords.TheirName("0.6.0") + LineWords.Ours(", 2 minutes ago");
        Assert.That(source.ForTextMeshPro().Text, Is.EqualTo("From Salidium " + LineWords.Pad + "0.6.0" + LineWords.NarrowPad + ", 2 minutes ago"));
        // An opening mark before outside text holds it as close.
        var opened = LineWords.Ours("(") + LineWords.TheirName("bash") + LineWords.Ours(" ran)");
        Assert.That(opened.ForTextMeshPro().Text, Is.EqualTo("(" + LineWords.NarrowPad + "bash" + LineWords.Pad + " ran)"));
        // The narrow space is a space a line never breaks at, so the mark never starts a row of its own.
        Assert.That(LineWords.NarrowPad, Is.EqualTo("\u202F"));
        Assert.That(LineWords.Pad, Is.EqualTo(" "));
    }

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

    [Test]
    public void Where_no_ground_is_drawn_the_runs_keep_only_their_own_spacing()
    {
        var line = LineWords.Ours("New ") + LineWords.TheirName("kite-run") + LineWords.Ours(" in Projects");
        Assert.That(line.ForTextMeshPro().Text, Is.EqualTo("New  kite-run  in Projects"), "a ground's room where one is drawn");
        var (text, theirs) = line.ForTextMeshPro(padded: false);
        Assert.That(text, Is.EqualTo("New kite-run in Projects"), "the room's faces: one space, never two");
        Assert.That(text.Substring(theirs[0].Start, theirs[0].End - theirs[0].Start), Is.EqualTo("kite-run"), "the run's edges still on its words");
    }
}
