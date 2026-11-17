using System;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class NewWorkSafetyTests
{
    private static CommandFactory Commands() => new(new ClientInfo
    {
        Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest",
    });

    [Test]
    public void ReviewShowsEveryValueWholeUnderItsLabel()
    {
        var objective = new string('W', 4000);
        var review = new NewWorkReview(new string('P', 200), "Workstream", "OpenCode", "Local model",
            "on this Mac, tools declared", "ollama/local:latest", objective);
        Assert.That(review.Items.Select(item => item.Label), Is.EqualTo(new[]
        {
            "Project", "Task Name", "Agent App", "Model", "Model Location", "Model ID", "First Task",
        }));
        Assert.That(review.Items[0].Value, Is.EqualTo(new string('P', 200)));
        Assert.That(review.Items[^1].Value, Is.EqualTo(objective), "nothing is shortened");
        Assert.That(review.Paginated, Is.False);
        Assert.That(review.CanConfirm, Is.False, "nothing can be confirmed before the pages are laid out");
    }

    [Test]
    public void ReviewShowsUnicodeAsCodePointsAndDistinguishesTypedMarkers()
    {
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref",
            "中かな🙂 \\u{4E2D}\nsecond\tthird\u202E");
        var first = review.Items.Single(item => item.Label == NewWorkReview.FirstTaskLabel);
        var shown = first.Value;
        // AUDIT2's marks (21:48Z), the approval well's one rule, and FIELD's font check (21:5xZ): every kit face holds printable
        // ASCII, the accented Latin letters and nine marks, so a character none holds (CJK, emoji) shows by its code point, never as a
        // missing glyph's box.
        Assert.That(shown, Does.StartWith("‹U+4E2D›‹U+304B›‹U+306A›‹U+1F642› \\u{4E2D}↵\nsecond⇥third‹U+202E›"), "a typed \\u{4E2D} stays their text");
        Assert.That(first.ValueRuns.Runs.Where(run => run.Theirs).Select(run => run.Text), Has.Some.Contain("\\u{4E2D}"), "a typed marker stays their text, never a mark");
        Assert.That(first.ValueRuns.Runs.Where(run => run.Theirs).Any(run => run.Text.Contains("‹U+")), Is.False, "every code point shown is a mark of ours");
        var faced = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Café \u2014 “quoted” \u2026 done").Items.Single(item => item.Label == NewWorkReview.FirstTaskLabel).Value;
        Assert.That(faced, Is.EqualTo("Café \u2014 “quoted” \u2026 done"), "é, a dash, quotes and an ellipsis every face holds stand as written");
        // The lead's line (22:10Z): Latin-1 Supplement and Latin Extended-A stand as written; the next letter after them, ƀ, by code.
        var latin = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Caf\u00E9, se\u00F1or, \u00FCber, \u0141\u00F3d\u017A \u0180").Items.Single(item => item.Label == NewWorkReview.FirstTaskLabel);
        Assert.That(latin.Value, Is.EqualTo("Caf\u00E9, se\u00F1or, \u00FCber, \u0141\u00F3d\u017A ‹U+0180›"));
        Assert.That(latin.ValueRuns.Runs.Where(run => run.Mark).Select(run => run.Text), Is.EqualTo(new[] { "‹U+0180›" }), "the one mark is ƀ's");
        // AUDIT3's condition: a no-break space is in the Latin list but still shows as a mark, never as a plain space.
        var spaced = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Caf\u00E9\u00A0bar").Items.Single(item => item.Label == NewWorkReview.FirstTaskLabel).Value;
        Assert.That(spaced, Does.Contain("‹U+00A0›"));
        Assert.That(spaced.IndexOf('\u00A0'), Is.EqualTo(-1), "no bare no-break space stands in the review");
    }

    [Test]
    public void ATypedEllipsisInATitleShowsAsItsCodePointAndHalcyonicAddsNone()
    {
        var draft = new NewWorkDraft(new CommandFactory(new ClientInfo { Name = "test" })) { Objective = "Make it pop\u2026 then ship it. More after." };
        var review = new NewWorkReview("Project", draft.Title, "Runtime", "Model", "unknown", "ref", draft.Objective);
        var title = review.Items.Single(item => item.Label == "Task Name");
        Assert.That(draft.Title, Is.EqualTo("Make it pop\u2026 then ship it."));
        // AUDIT2's marks (21:48Z): the person's ellipsis as they wrote it, their own text, leaning, never taken for a cut of ours.
        Assert.That(title.Value, Is.EqualTo("Make it pop\u2026 then ship it."), "the person's ellipsis, as written");
        Assert.That(title.ValueRuns.Runs.Single(run => run.Text.Contains('\u2026')).Theirs, Is.True, "theirs, never Halcyonic's");
        Assert.That(new NewWorkReview("Project", "Short", "Runtime", "Model", "unknown", "ref", "Short").Items[1].Value, Is.EqualTo("Short"));
        // The title keeps its own row, and the first task its own label.
        var whole = new NewWorkReview("Project", "Short", "Runtime", "Model", "unknown", "ref", "Short");
        Assert.That((whole.Items.Single(item => item.Kind == ReviewItemKind.TaskName).RowLabel, whole.Items.Single(item => item.Label == NewWorkReview.FirstTaskLabel).RowLabel),
            Is.EqualTo(("Task Name", (string?)"First Task")));
    }

    [Test]
    public void ATaskWithNothingToNameItByIsNamedInHalcyonicsOwnWordAndATypedNewTaskStaysTheirs()
    {
        var unnamed = new NewWorkDraft(new CommandFactory(new ClientInfo { Name = "test" })) { Objective = "\u200B\u200B" };
        Assert.That(unnamed.Title, Is.EqualTo(NewWorkDraft.UnnamedTask));
        var ours = new NewWorkReview("Project", unnamed.Title, "Runtime", "Model", "unknown", "ref", unnamed.Objective)
            .Items.Single(item => item.Label == "Task Name").ValueRuns;
        Assert.That(ours.AnyTheirs, Is.False, "our word, upright");
        var typed = new NewWorkReview("Project", "New Task", "Runtime", "Model", "unknown", "ref", "New Task")
            .Items.Single(item => item.Label == "Task Name").ValueRuns;
        Assert.That(typed.AllTheirs, Is.True, "the person's own words, leaning, though they read the same");
    }

    [Test]
    public void EveryRunOfTheirsHoldsOnlyFaceCharactersAndEveryCodeIsAMarkOfOurs()
    {
        // AUDIT3's TM1: the whole review's successor to "outside words show as printable ASCII". Whatever comes in, every run of theirs
        // holds only characters a kit face draws, and every code point shown (‹U+…›) stands in a mark of ours, never in their text.
        // The kit faces' own set (FIELD's KitFontAtlas.Characters, 2026-11-17): printable ASCII and nine marks, written out here on purpose.
        bool InFace(char unit) => unit >= 0x20 && unit <= 0x7E || "\u2026\u2013\u2014\u2018\u2019\u201C\u201D\u00B7\u2212".IndexOf(unit) >= 0;
        var hard = " lead\ttab\nnext\r\nCRLF 中🙂 Café \u202E\u200B\u0007 ‹U+0041› ↵ ⇥ \\u{41} “q” — … trail ";
        var reviews = new[]
        {
            new NewWorkReview(hard, hard, hard, hard, "unknown", hard, hard, folder: hard),
            new NewWorkReview("P\uD800", "T", "R\uDC00x", "M", "unknown", "ref", "\uFEFFstart\u00A0mid\u2028line"),
        };
        foreach (var review in reviews)
            foreach (var item in review.Items)
            {
                foreach (var run in item.ValueRuns.Runs.Where(run => run.Theirs))
                    Assert.That(run.Text.All(InFace), Is.True, item.Label + ": a run of theirs holds a character no face draws: " + run.Text);
                foreach (var run in item.ValueRuns.Runs.Where(run => run.Text.Contains("‹U+")))
                    Assert.That(run.Mark, Is.True, item.Label + ": a code point shown in a run that is not a mark of ours: " + run.Text);
            }
    }

    [Test]
    public void NamesAreSpelledOnceNotThroughPlainFirst()
    {
        var root = new LocationRoot
        {
            Path = "/Users/me/Projects",
            Name = "Projects",
            Status = LocationRootStatus.Available,
            Folders = new System.Collections.Generic.List<LocationFolder> { new() { Name = "site\u202E", Path = "/Users/me/Projects/site\u202E" } },
            FoldersTruncated = false,
        };
        var folder = ProjectFolder.Existing(root, root.Folders[0]);
        var review = new NewWorkReview("Project", "Title", "Runtime\u200B", "Model", "unknown", "ref", "Objective", folder.Describe(name => name));
        Assert.That(review.Items.Single(item => item.Label == "Folder").Value, Is.EqualTo("site‹U+202E› in Projects"));
        Assert.That(review.Items.Single(item => item.Label == "Agent App").Value, Is.EqualTo("Runtime‹U+200B›"));
        Assert.That(string.Concat(review.Items.Select(item => item.Value)), Does.Not.Contain("‹U+2039›"), "no marker of Plain's marked again");
    }

    [Test]
    public void PagesHoldWholeItemsAndSplitOnlyAnItemTallerThanAPage()
    {
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective");
        review.Paginate(new[] { 1, 2, 3, 1, 1, 1, 20 }, 6);
        Assert.That(review.Pages.Select(page => string.Join(" ", page.Select(part => part.Item + ":" + part.FirstLine + "+" + part.Lines))), Is.EqualTo(new[]
        {
            "0:0+1 1:0+2 2:0+3",
            "3:0+1 4:0+1 5:0+1",
            "6:0+6",
            "6:6+6",
            "6:12+6",
            "6:18+2",
        }), "the third item never splits; the objective starts a page of its own and fills whole pages");
        Assert.That(review.Pages.SelectMany(page => page).Where(part => part.Item == 6).Select(part => part.Part), Is.EqualTo(new[] { 0, 1, 2, 3 }));

        review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 8 }, 6);
        Assert.That(review.Pages[^1].Select(part => part.Item), Is.EqualTo(new[] { 6 }), "a tall item's last part holds what remains");
        var shorter = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective", "cards in Projects");
        shorter.Paginate(new[] { 1, 1, 9, 1, 1, 1, 1, 1 }, 6);
        Assert.That(shorter.Pages[2].Select(part => part.Item), Is.EqualTo(new[] { 2, 3, 4, 5 }), "what follows a tall item continues under its last part");
    }

    [Test]
    public void EveryLineOfEveryItemIsOnExactlyOnePage()
    {
        var random = new Random(20260930);
        for (var round = 0; round < 500; round++)
        {
            var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective", "folder", round % 2 == 0 ? "before" : null);
            var pageLines = random.Next(1, 14);
            var lines = review.Items.Select(_ => random.Next(1, 3 * pageLines + 2)).ToArray();
            review.Paginate(lines, pageLines);
            var seen = review.Items.Select(_ => new bool[0]).ToList();
            for (var item = 0; item < lines.Length; item++) seen[item] = new bool[lines[item]];
            foreach (var page in review.Pages)
            {
                Assert.That(page.Sum(part => part.Lines), Is.LessThanOrEqualTo(pageLines));
                foreach (var part in page)
                {
                    for (var line = part.FirstLine; line < part.FirstLine + part.Lines; line++)
                    {
                        Assert.That(seen[part.Item][line], Is.False, "no line shows twice");
                        seen[part.Item][line] = true;
                    }
                }
            }
            Assert.That(seen.All(item => item.All(shown => shown)), Is.True, "every line shows");
            for (var item = 0; item < lines.Length; item++)
            {
                if (lines[item] <= pageLines) Assert.That(review.Pages.SelectMany(page => page).Count(part => part.Item == item), Is.EqualTo(1), "an item that fits a page is whole");
            }
        }
    }

    /// <summary>
    /// A long first task's review pages to Confirm (AUDIT3's C4, SCREENS' journey j5 and j6): its title is made short, never cut,
    /// and keeps its own row showing the title sent, so no ellipsis of ours stands on any part (the bar, which refuses a drawn
    /// ellipsis as a cut, can report each part drawn), and reading every part opens Confirm on the last.
    /// </summary>
    [Test]
    public void ALongFirstTasksReviewPagesToConfirmWithNoEllipsisOnAnyPart()
    {
        var objective = string.Join(" ", Enumerable.Range(1, 8).Select(part => "Part " + part + ": " + new string('w', 120) + "."));
        var draft = new NewWorkDraft(new CommandFactory(new ClientInfo { Name = "test" })) { Objective = objective };
        Assert.That(draft.Title, Is.EqualTo("Part 1"), "its first words to sixty, the colon left at the cut dropped");
        var review = new NewWorkReview("Project", draft.Title, "Runtime", "Model", "unknown", "ref", objective);
        Assert.That(review.Items.Single(item => item.Kind == ReviewItemKind.TaskName).Value, Is.EqualTo(draft.Title), "the row shows the title sent");
        review.Paginate(review.Items.Select(item => item.Kind == ReviewItemKind.FirstTask ? 30 : 1).ToList(), 8);
        Assert.That(review.PageCount, Is.GreaterThan(2));
        Assert.That(review.Items.Any(item => item.Value.Contains('\u2026') || item.Runs.Text.Contains('\u2026')), Is.False, "no ellipsis of ours on any part");
        Assert.That(review.CanConfirm, Is.False);
        Samples.ReadThrough(review);
        Assert.That((review.Page, review.CanConfirm), Is.EqualTo((review.PageCount - 1, true)));
    }

    [Test]
    public void OnlyTheLastPageCanConfirm()
    {
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective");
        review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 30 }, 6);
        Assert.That(review.PageCount, Is.GreaterThan(2));
        Assert.That(review.CanConfirm, Is.False);
        Samples.ReadThrough(review);
        Assert.That(review.CanConfirm, Is.True);
        Assert.That(review.Page, Is.EqualTo(review.PageCount - 1));
        review.Previous();
        Assert.That(review.CanConfirm, Is.False);
        review.Drawn(100);
        review.Next(101);
        Assert.That(review.CanConfirm, Is.True);
        Assert.Throws<ArgumentException>(() => review.Paginate(new[] { 1, 2 }, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 1 }, 0));
    }

    [Test]
    public void TheLastPageIsNotEnoughEveryLineMustHaveBeenDrawn()
    {
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective");
        review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 12 }, 6);
        Assert.That(review.Next(10), Is.False, "nothing moves on before the part showing is drawn");
        review.Drawn(0);
        Assert.That(review.Next(1), Is.True);
        Assert.That(review.Next(2), Is.False, "nor before the new part is drawn");
        review.Drawn(2);
        review.Next(3);
        Assert.That(review.Page, Is.EqualTo(review.PageCount - 1));
        Assert.That(review.CanConfirm, Is.False, "on the last page, but its lines not drawn yet");
        Assert.That(review.Drawn(3), Is.True, "drawing the last unread part is what offers Yes");
        Assert.That(review.CanConfirm, Is.True);
        Assert.That(review.Drawn(4), Is.False, "already offered");
    }

    [Test]
    public void ARemeasureToMoreLinesMidReviewOffersYesOnlyAfterTheNewLinesAreDrawn()
    {
        // As the security review found it: read to the last part, then the text grows (or a banner
        // leaves), the first task wraps to more lines, and the old page number pointed past text never drawn.
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective");
        review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 12 }, 7);
        Assert.That(review.PageCount, Is.EqualTo(3), "the six short items, then the first task in two parts");
        review.Drawn(0);
        review.Next(1);
        review.Drawn(1);
        Assert.That(review.DrawnWhole(6), Is.False, "the first task's first part drawn, its second not yet");
        review.Paginate(new[] { 1, 1, 2, 1, 1, 2, 24 }, 6);
        Assert.That(review.CanConfirm, Is.False);
        Assert.That(review.Page, Is.Not.EqualTo(review.PageCount - 1), "never the old page number, which pointed past text never drawn");
        Assert.That(review.DrawnWhole(0) && review.DrawnWhole(5), Is.True, "items drawn whole stay read, whatever their lines now");
        Assert.That(review.Parts.Single().Item, Is.EqualTo(6), "it lands on the first part not yet read");
        Assert.That(review.Parts.Single().FirstLine, Is.EqualTo(0), "the first task drawn in part is read again from its start");
        var parts = 1;
        var now = 50.0;
        review.Drawn(now);
        Assert.That(review.CanConfirm, Is.False);
        while (review.Next(now += 1))
        {
            review.Drawn(now);
            parts++;
        }
        Assert.That(parts, Is.EqualTo(4), "every part of the first task again, 24 lines at 6 a part");
        Assert.That(review.CanConfirm, Is.True);
        review.Paginate(new[] { 1, 1, 2, 1, 1, 2, 30 }, 6);
        Assert.That(review.CanConfirm, Is.True, "drawn whole, it stays read when it wraps again");
    }

    [Test]
    public void ARemeasureToFewerLinesKeepsWhatWasDrawnWholeAndLandsOnTheFirstPartUnread()
    {
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective");
        review.Paginate(new[] { 2, 2, 2, 2, 2, 2, 20 }, 4);
        review.Drawn(0);
        review.Next(1);
        review.Drawn(1);
        Assert.That(review.DrawnWhole(3), Is.True);
        Assert.That(review.DrawnWhole(4), Is.False);
        review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 10 }, 8);
        Assert.That(review.Page, Is.EqualTo(0), "the first page holds items 4 and 5, not yet read");
        Assert.That(review.CanConfirm, Is.False);
        Samples.ReadThrough(review);
        Assert.That(review.CanConfirm, Is.True);
        review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 10 }, 20);
        Assert.That((review.PageCount, review.CanConfirm), Is.EqualTo((1, true)), "drawn whole, laid out again on one page: still read");
    }

    [Test]
    public void ADoublePressNeverPassesAPartAlmostUnseen()
    {
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective");
        review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 18 }, 6);
        review.Drawn(10.0);
        Assert.That(review.Next(10.3), Is.False, "the first part showed only 0.3 s");
        Assert.That(review.Next(10.4), Is.True);
        review.Drawn(10.41);
        Assert.That(review.Next(10.5), Is.False, "the second press of a double press is ignored");
        Assert.That(review.Page, Is.EqualTo(1));
        Assert.That(review.Next(10.81), Is.True);
    }

    [Test]
    public void NoOrderOfRemeasuresDrawsAndPressesOffersYesBeforeEveryCharacterWasPlaced()
    {
        // Counted by what was placed, not by the review's own rule: each item is so many characters,
        // a layout wraps it at a width, and drawing a part places that part's characters for good.
        var random = new Random(20261002);
        for (var round = 0; round < 400; round++)
        {
            var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective", "folder");
            var count = review.Items.Count;
            var characters = Enumerable.Range(0, count).Select(_ => random.Next(1, 400)).ToArray();
            var placed = characters.Select(length => new bool[length]).ToArray();
            var width = 1;
            void Layout()
            {
                width = random.Next(8, 60);
                review.Paginate(characters.Select(length => (length + width - 1) / width).ToList(), random.Next(2, 9));
            }
            Layout();
            var now = 0.0;
            for (var step = 0; step < 80; step++)
            {
                now += random.NextDouble();
                switch (random.Next(5))
                {
                    case 0:
                        Layout();
                        break;
                    case 1:
                        review.Drawn(now);
                        foreach (var part in review.Parts)
                        {
                            var from = part.FirstLine * width;
                            var to = Math.Min((part.FirstLine + part.Lines) * width, characters[part.Item]);
                            for (var character = from; character < to; character++) placed[part.Item][character] = true;
                        }
                        break;
                    case 2:
                        review.Previous();
                        break;
                    default:
                        review.Next(now);
                        break;
                }
                if (review.CanConfirm)
                {
                    Assert.That(placed.All(item => item.All(character => character)), Is.True, "Yes only once every character was placed at least once");
                    Assert.That(review.Page, Is.EqualTo(review.PageCount - 1));
                }
            }
        }
    }

    [Test]
    public void ProjectionCompletesACommandEvenWhenItsAcknowledgementIsLost()
    {
        var command = Commands().CreateWorkstream(Guid.NewGuid().ToString("D"), "Title", "Objective");
        var submission = new NewWorkSubmission(command);
        submission.LostAcknowledgement(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.OutcomeUnknown));
        submission.Observe(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Accepted,
        });
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.OutcomeUnknown));
        submission.Observe(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Completed,
            Result = new WorkstreamCreatedResult { WorkstreamId = Guid.NewGuid().ToString("D") },
        });
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.Completed));
        Assert.That(submission.EffectiveRecord!.Result, Is.InstanceOf<WorkstreamCreatedResult>());
    }

    [Test]
    public void CompletionArrivingBeforeTheLostAcknowledgementStillWins()
    {
        var command = Commands().CreateProject("Project");
        var submission = new NewWorkSubmission(command);
        submission.Observe(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Completed,
            Result = new ProjectCreatedResult { ProjectId = Guid.NewGuid().ToString("D") },
        });
        submission.LostAcknowledgement(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.Completed));
        Assert.That(submission.HasExpectedResult, Is.True);
    }

    [Test]
    public void CompletedCommandWithUnexpectedResultCannotReleaseTheGuard()
    {
        var command = Commands().CreateWorkstream(Guid.NewGuid().ToString("D"), "Title", "Objective");
        var submission = new NewWorkSubmission(command);
        submission.Observe(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Completed,
            Result = new ProjectCreatedResult { ProjectId = Guid.NewGuid().ToString("D") },
        });
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.Completed));
        Assert.That(submission.HasExpectedResult, Is.False);
    }

    [Test]
    public void WrongCommandRecordCannotClearAnUnknownOutcome()
    {
        var command = Commands().CreateProject("Project");
        var submission = new NewWorkSubmission(command);
        submission.LostAcknowledgement(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        submission.Observe(new CommandView { CommandId = Guid.NewGuid().ToString("D"), Status = CommandStatus.Completed });
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.OutcomeUnknown));
    }

    [Test]
    public void ACommandKnownNotToHaveBeenSentCanBeRetried()
    {
        var command = Commands().CreateProject("Project");
        var submission = new NewWorkSubmission(command);
        submission.LostAcknowledgement(new SessionUnavailableException("Not connected."));
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.NotSent));
    }
}
