using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A host for one column: a clock to move, words wrapped forty to a row, and every send kept.</summary>
internal sealed class FileMenuHost : IMenuHost
{
    public List<CommandEnvelope> Sent { get; } = new();

    public bool Live { get; set; } = true;

    public bool KeyboardOffered { get; set; } = true;

    public (string Text, string Prompt, Action<string> Done)? Keyboard { get; private set; }

    public ClientProjection? State => null;

    public bool Connected => Live;

    public bool Demonstration => false;

    public double Now { get; set; } = 100;

    public DateTimeOffset Clock { get; set; } = DateTimeOffset.Parse("2026-10-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

    public TimeZoneInfo Zone => TimeZoneInfo.Utc;

    public TextSize TextSize => TextSize.Standard;

    public bool VoiceOffered => true;

    public ControlPlaneApi? Api => null;

    public Task<CommandAckMessage>? Submit(CommandEnvelope command)
    {
        if (!Live) return null;
        Sent.Add(command);
        return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted });
    }

    public void OpenKeyboard(string text, string prompt, Action<string> done) => Keyboard = (text, prompt, done);

    public int RowsOf(string words, float columnDegrees) => Math.Max(1, (words.Length + 39) / 40);

    public int RowsOf(PageLine line, float columnDegrees) => RowsOf(line.Words, columnDegrees);

    public bool FitsHalf(PageLine answer, float columnDegrees) => false;

    public int TitleRows(string subject, float columnDegrees) => 1;

    public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize.Standard, sourceLine);

    public float PageHeight(int subjectRows, bool besideMenu) => MenuPage.Height(TextSize.Standard, subjectRows);

    public void OpenFile(string workstreamId)
    {
    }

    public void OpenNewProject(string? projectId, string? projectName)
    {
    }

    /// <summary>Moves both clocks on.</summary>
    public void Wait(double seconds)
    {
        Now += seconds;
        Clock += TimeSpan.FromSeconds(seconds);
    }
}

public class FileColumnTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private FileColumn Column(FileMenuHost host, Func<WorkspacePresentation?> present, IReadOnlyList<PresetInstruction>? recorded = null) =>
        new(host, present, factory, () => null, _ => recorded, () => "");

    /// <summary>The director draws the column's frame, and a second later it is read.</summary>
    private static void Draw(FileMenuHost host, FileColumn column)
    {
        column.Drawn(column.Frame!, sidePanel: false);
        host.Wait(1);
    }

    private static WorkspacePresentation Approving(WaitingWork work) =>
        FileScreensTests.Offering(work.Present(), WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Interrupt);

    [Test]
    public void ItOpensOnWhatWaitsAndRaisesChangedForEachNewFrame()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        Assert.That(column.Screen.Section, Is.EqualTo(FileSection.Waiting));
        Assert.That(column.Frame!.Lines[0].Words, Is.EqualTo("It wants to run a command:"));
        var changes = 0;
        column.Changed += () => changes++;
        column.Act(MenuFrame.ChooseSection, "activity");
        Assert.That((changes, column.Screen.Section), Is.EqualTo((1, FileSection.Activity)));
        var closed = false;
        column.Closed += () => closed = true;
        column.Act(Footer.Close, null);
        Assert.That(closed, Is.True);
        Assert.That(host.Sent, Is.Empty, "nothing sent by looking");
    }

    [Test]
    public void ApprovingSendsOnlyThroughTheHostOnceTheWholeRequestWasDrawnAndYesOnlyOnce()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Approve));
        Assert.That(column.Frame!.Footer.Confirming, Is.True);

        // The request's one part drawn: Yes shows, and sends once.
        Draw(host, column);
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.EqualTo(FileScreens.Yes));
        column.Act(FileScreens.Yes, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
        Assert.That(host.Sent[0], Is.InstanceOf<ExecutionRespondToApprovalCommand>());
    }

    [Test]
    public void DrawingAnArmedRequestBuildsAgainOnlyWhenYesMayShowSoAPressStandsOnTheFrameDrawn()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        var changes = 0;
        column.Changed += () => changes++;
        Draw(host, column);
        Assert.That((changes, column.Frame!.Footer[PromptSlot.Free]?.Id), Is.EqualTo((1, FileScreens.Yes)), "Yes may show: built once");
        var shown = column.Frame;
        for (var draw = 0; draw < 3; draw++) Draw(host, column);
        Assert.That((changes, column.Frame), Is.EqualTo((1, shown)), "drawn again, it stands as it is");
        column.Act(FileScreens.Yes, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1), "Yes is taken, once");
    }

    [Test]
    public void ARequestInPartsDrawnOverAndOverTurnsByNextPartAndCancelsOrConfirmsAsPressed()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var command = string.Join(" && ", Enumerable.Range(1, 30).Select(step => "psql -c 'ALTER TABLE t" + step + " DROP COLUMN legacy'"));
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(WaitingWork.Approval("approval-long", command, Samples.Time));
        });
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Assert.That(column.Screen.RequestParts, Is.GreaterThan(2), "a request of several parts");
        var changes = 0;
        column.Changed += () => changes++;
        for (var part = 0; part < column.Screen.RequestParts; part++)
        {
            var shown = column.Frame;
            for (var draw = 0; draw < 3; draw++) Draw(host, column);
            var last = part == column.Screen.RequestParts - 1;
            Assert.That(column.Frame, last ? Is.Not.SameAs(shown) : Is.SameAs(shown), "part " + part + ": built again only once Yes may show");
            if (last) break;
            Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.Not.EqualTo(FileScreens.Yes), "no Yes before the last part");
            var before = changes;
            column.Act(FileScreens.NextPart, FileScreens.RequestKey);
            Assert.That((changes - before, column.Screen.RequestPart), Is.EqualTo((1, part + 1)), "Next part is taken");
        }
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.EqualTo(FileScreens.Yes));
        var settled = changes;
        for (var draw = 0; draw < 3; draw++) Draw(host, column);
        Assert.That(changes, Is.EqualTo(settled), "a confirmation drawn again and again raises nothing more");
        column.Act(FileScreens.Cancel, null);
        Assert.That((column.Steering.Armed, host.Sent.Count), Is.EqualTo(((WorkspaceAction?)null, 0)), "Cancel is taken and sends nothing");

        column.Act(FileScreens.Approve, null);
        for (var part = 0; part < column.Screen.RequestParts; part++)
        {
            Draw(host, column);
            Draw(host, column);
            if (part < column.Screen.RequestParts - 1) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
        }
        column.Act(FileScreens.Yes, null);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1), "Yes is taken and sends once");
    }

    [Test]
    public void YesIsJudgedAgainstTheWorkAsItIsWhenPressedNotAsTheLastRebuildReadIt()
    {
        var host = new FileMenuHost();
        var current = Approving(new WaitingWork());
        var column = Column(host, () => current);
        column.Act(FileScreens.Approve, null);
        Draw(host, column);
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.EqualTo(FileScreens.Yes));
        // The state moves on to another request between rebuilds; the column has not ticked since,
        // so what it last read is a snapshot of the request before.
        var later = new WaitingWork();
        later.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(WaitingWork.Approval("approval-2", "Drop the table", Samples.Time));
        });
        current = Approving(later);
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty, "a Yes to the request read before is not a Yes to this one");
        Assert.That(column.Steering.Armed, Is.Null);
    }

    [Test]
    public void ARequestsPartsStayPutWhenItsLastIsReadAndTheQuestionBelowChangesSoNoReadingIsLost()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var command = string.Join(" && ", Enumerable.Range(1, 30).Select(step => "psql -c 'ALTER TABLE t" + step + " DROP COLUMN legacy'"));
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.PendingApprovals.Add(WaitingWork.Approval("approval-long", command, Samples.Time));
        });
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        // The question below the request is two rows before it is read and one after, as at larger text.
        Assert.That(host.RowsOf(WorkspaceText.ReadRequestFirst, 0f), Is.GreaterThan(host.RowsOf(WorkspaceText.ConfirmationPrompt(WorkspaceAction.Approve, null), 0f)));
        var (parts, rows) = (column.Screen.RequestParts, column.Screen.RequestPartRows);
        Assert.That(parts, Is.GreaterThan(1));
        for (var part = 0; part < parts; part++)
        {
            Draw(host, column);
            if (part < parts - 1) column.Act(FileScreens.NextPart, FileScreens.RequestKey);
        }
        Assert.That(column.Steering.Prompt(column.Now!), Is.EqualTo("Approve the request above?"), "the question below has changed");
        Assert.That((column.Screen.RequestParts, column.Screen.RequestPartRows, column.Screen.RequestPart), Is.EqualTo((parts, rows, parts - 1)),
            "the parts stay as read, on the last one, not back at the first");
        Assert.That((column.Steering.CanConfirm, column.Frame!.Footer[PromptSlot.Free]?.Id), Is.EqualTo((true, (string?)FileScreens.Yes)));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1));
    }

    [Test]
    public void ADrawOfAFrameTheColumnNoLongerStandsByCountsForNothing()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        var stale = column.Frame!;
        column.Act(MenuFrame.ChooseSection, "activity");
        column.Drawn(stale, sidePanel: false);
        Assert.That(column.Steering.WholeRequestShown, Is.False, "a frame drawn after the column built another is not what shows");
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void FocusLeavingLapsesAnArmedConfirmationAndSaysSo()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Draw(host, column);
        column.FocusLeft();
        Assert.That(column.Steering.Armed, Is.Null);
        Assert.That(column.Frame!.Lines.Last().Words, Is.EqualTo(WorkspaceText.ConfirmAfresh));
        column.Act(FileScreens.Yes, null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void FocusLeavingResetsWhatWasReadWithinAnArmingButNotAQuestionsReads()
    {
        var host = new FileMenuHost();
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Draw(host, column);
        Assert.That(column.Steering.CanConfirm, Is.True);
        column.FocusLeft();
        column.Act(FileScreens.Approve, null);
        Assert.That(column.Steering.CanConfirm, Is.False, "the request is read again from its first part");
        Assert.That(column.Frame!.Footer[PromptSlot.Free]?.Id, Is.Not.EqualTo(FileScreens.Yes));

        var asking = new AskingWork();
        var answering = Column(host, () => FileScreensTests.Offering(asking.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && answering.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, answering);
            answering.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, answering);
        Assert.That(answering.Screen.Question.Draft!.WasShownWhole(0), Is.True);
        answering.FocusLeft();
        Assert.That((answering.Screen.Question.Draft!.WasShownWhole(0), answering.Screen.Question.QuestionPart), Is.EqualTo((true, (int?)null)),
            "a question read outside an arming stays read, and stays on its answers");
    }

    [Test]
    public void WithoutASessionNothingIsSentAndThePageSaysWhy()
    {
        var host = new FileMenuHost { Live = false };
        var work = new WaitingWork();
        var column = Column(host, () => Approving(work));
        column.Act(FileScreens.Approve, null);
        Draw(host, column);
        column.Act(FileScreens.Yes, null);
        Assert.That(column.Frame!.Lines.Last().Words, Does.StartWith("Couldn't send"));
    }

    /// <summary>A question taking a typed answer, its answers' page in view, with a typed answer long enough for its side panel to take several parts.</summary>
    private (FileMenuHost Host, FileColumn Column) TypedLongAnswer()
    {
        var host = new FileMenuHost();
        var work = new AskingWork(new QuestionView
        {
            QuestionId = "question-1",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new()
                {
                    Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Multiple = false, FreeText = true,
                    Options = new List<QuestionOption> { new() { Label = "15 minutes" }, new() { Label = "1 hour" } },
                },
            },
        });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        column.Act(FileScreens.TypeAnswer, null);
        var words = string.Join(" ", Enumerable.Range(1, 120).Select(step => "Lock it for a minute after the " + step + "th failed try"));
        host.Keyboard!.Value.Done(words);
        Assert.That(column.Screen.Question.SideOption, Is.Not.Null, "the typed answer is cut, so its whole words open beside the page");
        Assert.That(column.Screen.Question.SideParts, Is.GreaterThan(2), "in several parts");
        return (host, column);
    }

    [Test]
    public void ALongTypedAnswerIsReadPartByPartToItsEndAndThenSends()
    {
        var (host, column) = TypedLongAnswer();
        var parts = column.Screen.Question.SideParts;
        for (var part = 0; part < parts; part++)
        {
            Assert.That(column.Screen.Question.SidePart, Is.EqualTo(part), "a rebuild never sends it back to the first part");
            column.Drawn(column.Frame!, sidePanel: true);
            host.Wait(1);
            column.Tick();
            if (part < parts - 1) column.Act(Footer.NextPage, null);
        }
        Assert.That(column.Screen.Question.AnswersRead(0), Is.True, "read to its last part");
        Draw(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1), "and sent");
    }

    [Test]
    public void CloseDetailsOnALongTypedAnswerStaysClosedUntilItsWordsChange()
    {
        var (host, column) = TypedLongAnswer();
        column.Act(SidePanel.Close, null);
        Assert.That(column.Frame!.Side, Is.Null, "the page shows again");
        Draw(host, column);
        column.Tick();
        host.Wait(1);
        column.Tick();
        Assert.That((column.Screen.Question.SideOption, column.Frame!.Side), Is.EqualTo(((int?)null, (SidePanel?)null)), "rebuilds leave it closed");
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done("Ten minutes, then a day after the fifth time, then a reset by email only, with every lockout written to the audit log and the person told why");
        Assert.That(column.Screen.Question.SideOption, Is.Not.Null, "new words open it again, from their first part");
        Assert.That(column.Screen.Question.SidePart, Is.EqualTo(0));
    }

    private static string LongWords(string what) =>
        string.Join(" ", Enumerable.Range(1, 120).Select(step => what + " " + step));

    /// <summary>A file asking <paramref name="prompts"/>, its first answers' page in view.</summary>
    private (FileMenuHost Host, FileColumn Column) Asking(params QuestionPrompt[] prompts)
    {
        var host = new FileMenuHost();
        var work = new AskingWork(new QuestionView { QuestionId = "question-1", Answerable = true, AskedAt = Samples.Time, Prompts = prompts.ToList() });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        return (host, column);
    }

    [Test]
    public void ALongTypedAnswerClosedBeforeItsEndOpensAgainFromItsRowAtThePartToReadNextAndSends()
    {
        var (host, column) = TypedLongAnswer();
        column.Drawn(column.Frame!, sidePanel: true);
        host.Wait(1);
        column.Act(Footer.NextPage, null);
        column.Act(SidePanel.Close, null);
        Assert.That(column.Frame!.Side, Is.Null, "closed before its last part");
        var keyboard = host.Keyboard;
        column.Act(FileScreens.TypeAnswer, null);
        Assert.That((column.Screen.Question.SideOption, column.Screen.Question.SidePart), Is.EqualTo(((int?)2, 1)),
            "its row opens it again at the first part not yet drawn");
        Assert.That(host.Keyboard, Is.EqualTo(keyboard), "and asks for no new words");
        var parts = column.Screen.Question.SideParts;
        for (var part = 1; part < parts; part++)
        {
            column.Drawn(column.Frame!, sidePanel: true);
            host.Wait(1);
            if (part < parts - 1) column.Act(Footer.NextPage, null);
        }
        Draw(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.OfType<ExecutionAnswerQuestionCommand>().Count(), Is.EqualTo(1), "the same words, read to their end, are sent");

        column.Act(FileScreens.TypeAnswer, null);
        Assert.That(host.Keyboard, Is.Not.EqualTo(keyboard), "read whole, the row edits them again");
    }

    [Test]
    public void WithSeveralAnswersAllowedCloseDetailsOnACutAnswerBringsThePageBackNotTheTypedOne()
    {
        var (host, column) = Asking(new QuestionPrompt
        {
            Key = "q0", Header = "Checks", Text = "Which checks should run?", Multiple = true, FreeText = true,
            Options = new List<QuestionOption>
            {
                new() { Label = "Unit tests, then the end to end suite against a fresh database, then a load test of the sign-in endpoint for ten minutes" },
                new() { Label = "Lint" },
            },
        });
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(LongWords("Also the accessibility audit, step"));
        Assert.That(column.Screen.Question.SideOption, Is.EqualTo(2), "the typed answer's panel");
        column.Act(FileScreens.Choose, "0");
        Assert.That(column.Screen.Question.SideOption, Is.EqualTo(0), "the cut answer's panel in its place");
        column.Act(SidePanel.Close, null);
        Assert.That(column.Frame!.Side, Is.Null, "Close details always brings the page back");
    }

    [Test]
    public void BackOnAPromptWhoseLongTypedAnswerIsUnreadItsPanelOpensAtThePartToReadNext()
    {
        var (host, column) = Asking(
            new QuestionPrompt
            {
                Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Multiple = false, FreeText = true,
                Options = new List<QuestionOption> { new() { Label = "15 minutes" } },
            },
            new QuestionPrompt
            {
                Key = "q1", Header = "Notice", Text = "Should the person be told?", Multiple = false, FreeText = false,
                Options = new List<QuestionOption> { new() { Label = "Yes" }, new() { Label = "No" } },
            });
        column.Act(FileScreens.TypeAnswer, null);
        host.Keyboard!.Value.Done(LongWords("Lock it for a minute after try"));
        Assert.That(column.Screen.Question.SideParts, Is.GreaterThan(2));
        column.Drawn(column.Frame!, sidePanel: true);
        host.Wait(1);
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        Assert.That(column.Screen.Question.Prompt, Is.EqualTo(1));
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        column.Act(FileScreens.NextQuestion, null);
        Assert.That(column.Screen.Question.Reviewing, Is.True, "on the person's answers");
        column.Act(FileScreens.GoToQuestion, "0");
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Assert.That((column.Screen.Question.Prompt, column.Screen.Question.SideOption, column.Screen.Question.SidePart), Is.EqualTo((0, (int?)1, 1)),
            "back on the prompt, the unread typed answer's panel opens at the first part not yet drawn");
    }

    [Test]
    public void NextPageOnALongTypedAnswerAdvancesAndARebuildKeepsThePart()
    {
        var (host, column) = TypedLongAnswer();
        column.Drawn(column.Frame!, sidePanel: true);
        host.Wait(1);
        column.Act(Footer.NextPage, null);
        Assert.That(column.Screen.Question.SidePart, Is.EqualTo(1));
        column.Tick();
        host.Wait(1);
        column.Tick();
        Assert.That(column.Screen.Question.SidePart, Is.EqualTo(1), "the next rebuild keeps the part turned to");
    }

    [Test]
    public void CloseDetailsOnAChosenAnswersSidePanelBringsThePageBack()
    {
        var host = new FileMenuHost();
        var work = new AskingWork(new QuestionView
        {
            QuestionId = "question-1",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new()
                {
                    Key = "q0", Header = "Database", Text = "Which database should it use?", Multiple = false, FreeText = false,
                    Options = new List<QuestionOption>
                    {
                        new() { Label = "Postgres, with read replicas in two regions, a nightly snapshot kept for thirty days and point in time recovery" },
                        new() { Label = "SQLite" },
                    },
                },
            },
        });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Draw(host, column);
        column.Act(FileScreens.Choose, "0");
        Draw(host, column);
        Assert.That(column.Frame!.Side, Is.Not.Null, "the long answer's whole words beside the page");
        column.Act(SidePanel.Close, null);
        Assert.That(column.Frame!.Side, Is.Null, "Close details brings the page back, where Send answer sends with everything in view");
        Assert.That(column.Frame!.Lines.Any(line => line.Chosen), Is.True, "the answer stays chosen");
    }

    [Test]
    public void AnAnswerGoesOnlyByChoosingOnThePageThenSendAnswerOnceTheQuestionWasDrawn()
    {
        var host = new FileMenuHost();
        var work = new AskingWork(new QuestionView
        {
            QuestionId = "question-1",
            Answerable = true,
            AskedAt = Samples.Time,
            Prompts = new List<QuestionPrompt>
            {
                new() { Key = "q0", Header = "Lockout", Text = "How long should a lockout last?", Options = new List<QuestionOption> { new() { Label = "15 minutes" }, new() { Label = "1 hour" } }, Multiple = false, FreeText = true },
            },
        });
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer));
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent, Is.Empty, "nothing chosen, nothing read: nothing sent");
        // Where the page packs the question onto its own page first, it is read there, then its answers.
        for (var step = 0; step < 5 && column.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, column);
            column.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        column.Act(FileScreens.Choose, "1");
        Draw(host, column);
        column.Act(FileScreens.SendAnswer, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1), column.Frame!.Reason);
        Assert.That(host.Sent.Single(), Is.InstanceOf<ExecutionAnswerQuestionCommand>());
        Assert.That(((ExecutionAnswerQuestionCommand)host.Sent[0]).Payload.Answers.Single().Selected, Is.EqualTo(new[] { "1 hour" }));
        Assert.That(column.Screen.Section, Is.EqualTo(FileSection.Activity), "an answer sent shows how it goes with the activity");
    }

    [Test]
    public void HoldToTalkUnderTheQuestionDraftsAnAnswerAndForAnInstructionAsksBeforeSending()
    {
        var host = new FileMenuHost();
        var work = new AskingWork();
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Answer, WorkspaceAction.Instruct));
        column.HoldStarted(FileScreens.SpeakAnswer);
        column.HoldEnded(FileScreens.SpeakAnswer, letGo: true);
        column.Heard("Teal");
        Assert.That(column.Screen.Question.Draft!.Typed(0), Is.EqualTo("Teal"), "what was heard becomes the typed answer");
        Assert.That(host.Sent, Is.Empty, "and nothing is sent");

        column.HoldStarted(FileScreens.HoldToTalk);
        column.Heard("Carry on");
        Assert.That(column.Steering.Armed, Is.EqualTo(WorkspaceAction.Instruct), "a heard instruction waits for Yes");
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void TellItOffersTheRecordedInstructionsToChooseAndSendsExactlyTheChosenWords()
    {
        var host = new FileMenuHost();
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var recorded = new[] { new PresetInstruction("Add a test", "Add a test for refunds."), new PresetInstruction("Wrap up", "Wrap up and summarise.") };
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct), recorded);
        column.Act(FileScreens.TellIt, null);
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Add a test for refunds.", "Wrap up and summarise." }));
        Assert.That(host.Keyboard, Is.Null, "the demonstration's instructions stand in for the keyboard");
        column.Act(FileScreens.TellIt, null);
        Assert.That(host.Sent, Is.Empty, "nothing chosen, nothing sent");
        column.Act(FileScreens.Preset, "1");
        Assert.That(host.Sent, Is.Empty, "choosing sends nothing");
        column.Act(FileScreens.TellIt, null);
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Wrap up and summarise."));
    }

    [Test]
    public void WhereNoKeyboardOpensTellItOffersTheInstructionsAndNoRowOnlyOpensIt()
    {
        var host = new FileMenuHost { KeyboardOffered = false };
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct));
        column.Act(FileScreens.TellIt, null);
        Assert.That(host.Keyboard, Is.Null, "no keyboard is asked for");
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Is.EqualTo(WorkspaceText.PresetInstructions.Select(preset => preset.Text)));

        var asking = new AskingWork();
        var answering = Column(host, () => FileScreensTests.Offering(asking.Present(), WorkspaceAction.Answer));
        for (var step = 0; step < 5 && answering.Screen.Question.QuestionPart != null; step++)
        {
            Draw(host, answering);
            answering.Act(FileScreens.NextPart, FileScreens.QuestionKey);
        }
        Assert.That(answering.Frame!.Lines.Where(line => line.Action == FileScreens.Choose), Is.Not.Empty, "the answers show");
        Assert.That(answering.Frame!.Lines.Select(line => line.Action), Has.None.EqualTo(FileScreens.TypeAnswer),
            "a row that would only open the keyboard is left off");
        answering.HoldStarted(FileScreens.SpeakAnswer);
        answering.Heard("Teal");
        var typed = answering.Frame!.Lines.Single(line => line.Action == FileScreens.TypeAnswer);
        Assert.That((typed.Words, typed.Chosen), Is.EqualTo(("Your answer: “Teal”", true)), "words heard stay as their choice");
        answering.Act(FileScreens.TypeAnswer, null);
        Assert.That(host.Keyboard, Is.Null, "pressing it opens no keyboard");
    }

    [Test]
    public void DrawingAnApprovalOnWaitingCountsNothingTowardTheQuestionDraftedMeanwhile()
    {
        var host = new FileMenuHost();
        var work = new AskingWork();
        work.Change(execution => execution.PendingApprovals.Add(WaitingWork.Approval("approval-1", "Run the migration", Samples.Time)));
        var column = Column(host, () => FileScreensTests.Offering(work.Present(), WorkspaceAction.Approve, WorkspaceAction.Deny, WorkspaceAction.Answer));
        Assert.That(column.Screen.Question.Draft, Is.Not.Null, "the question is drafted while the approval shows");
        Assert.That(column.Frame!.Lines.Select(line => line.Action), Has.None.EqualTo(FileScreens.Choose), "Waiting shows the approval");
        for (var draw = 0; draw < 3; draw++) Draw(host, column);
        Assert.That(column.Screen.Question.Draft!.WasShownWhole(0), Is.False, "the approval's page counts nothing toward the question");
    }

    [Test]
    public void TellItWithoutRecordedInstructionsTakesTheKeyboardsWordsOnlyWhenFinished()
    {
        var host = new FileMenuHost();
        var running = new WaitingWork();
        running.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        var column = Column(host, () => FileScreensTests.Offering(running.Present(), WorkspaceAction.Instruct));
        column.Act(FileScreens.TellIt, null);
        Assert.That(host.Keyboard, Is.Not.Null);
        Assert.That(column.Frame!.Lines.Single().Words, Is.EqualTo(WorkspaceText.TypingPrompt));
        host.Keyboard!.Value.Done("Add a test");
        Assert.That(((ExecutionSendInstructionCommand)host.Sent.Single()).Payload.Text, Is.EqualTo("Add a test"));
    }
}
