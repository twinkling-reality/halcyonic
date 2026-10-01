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
            "Project: ", "Workstream title: ", "Runtime: ", "Model: ", "Model location: ", "Model reference: ", "Objective: ",
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
        var shown = review.Items[^1].Value;
        Assert.That(shown, Does.Contain("\\u{4E2D}\\u{304B}\\u{306A}\\u{1F642}"));
        Assert.That(shown, Does.Contain("\\\\u{4E2D}"));
        Assert.That(shown, Does.Contain("\\u{A}second\\u{9}third\\u{202E}"));
        Assert.That(review.Items.All(item => item.Text.All(value => value >= 0x20 && value <= 0x7E)), Is.True);
    }

    [Test]
    public void HalcyonicsOwnEllipsisShowsAsWrittenAndATypedOneAsItsCodePoint()
    {
        var draft = new NewWorkDraft(new CommandFactory(new ClientInfo { Name = "test" })) { Objective = new string('W', 150) + "\u2026" + new string('X', 300) };
        var (typed, cut) = draft.TitleSource;
        var review = new NewWorkReview("Project", typed, "Runtime", "Model", "unknown", "ref", draft.Objective, titleCut: cut);
        var title = review.Items.Single(item => item.Label == "Workstream title: ").Value;
        Assert.That(cut, Is.True);
        Assert.That(title, Does.EndWith("X\u2026"), "Halcyonic's ellipsis, as written");
        Assert.That(title, Does.Contain(new string('W', 150) + "\\u{2026}X"), "the person's ellipsis, by its code point");
        Assert.That(title, Does.Not.Contain("\\u{2026}\u2026"));
        Assert.That(review.Items.Where(item => item.Label != "Workstream title: ").All(item => item.Text.All(value => value >= 0x20 && value <= 0x7E)), Is.True);
        Assert.That(new NewWorkReview("Project", "Short", "Runtime", "Model", "unknown", "ref", "Short").Items[1].Value, Is.EqualTo("Short"));
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
        Assert.That(review.Items.Single(item => item.Label == "Where its files live: ").Value, Is.EqualTo("site\\u{202E} in Projects"));
        Assert.That(review.Items.Single(item => item.Label == "Runtime: ").Value, Is.EqualTo("Runtime\\u{200B}"));
        Assert.That(string.Concat(review.Items.Select(item => item.Value)), Does.Not.Contain("\\u{2039}"), "no marker of Plain's spelled again");
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

    [Test]
    public void OnlyTheLastPageCanConfirm()
    {
        var review = new NewWorkReview("Project", "Title", "Runtime", "Model", "unknown", "ref", "Objective");
        review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 30 }, 6);
        Assert.That(review.PageCount, Is.GreaterThan(2));
        Assert.That(review.CanConfirm, Is.False);
        while (!review.CanConfirm) review.Next();
        Assert.That(review.Page, Is.EqualTo(review.PageCount - 1));
        review.Previous();
        Assert.That(review.CanConfirm, Is.False);
        review.Next();
        Assert.That(review.CanConfirm, Is.True);
        Assert.Throws<ArgumentException>(() => review.Paginate(new[] { 1, 2 }, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => review.Paginate(new[] { 1, 1, 1, 1, 1, 1, 1 }, 0));
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
