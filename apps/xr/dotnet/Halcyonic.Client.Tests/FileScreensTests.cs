using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A task's file's Activity, Changes and Checks, their side panels and their footers.</summary>
public class FileScreensTests
{
    private readonly CommandFactory factory = new(Samples.Client);

    private static readonly AnswerRoom Room = new(4);

    /// <summary>The work as an agent app offering exactly these actions, with the control plane's confirmations kept.</summary>
    internal static WorkspacePresentation Offering(WorkspacePresentation workspace, params WorkspaceAction[] actions) =>
        new(workspace.Character, workspace.Objective, workspace.Execution, workspace.Runtime, actions,
            actions.Where(action => workspace.RequiresConfirmation(action)).ToList(), workspace.Commands, workspace.Activity);

    internal static string?[] Slots(Footer footer) =>
        new[] { PromptSlot.Close, PromptSlot.Rare, PromptSlot.Free, PromptSlot.Secondary, PromptSlot.FarRight }.Select(slot => footer[slot]?.Id).ToArray();

    private static SectionPresentation Understand(UnderstandPrompt prompt, string json, AnswerDepth depth) =>
        UnderstandingPresenter.Present(prompt, Intelligence.ExecutionId, Intelligence.Live(Intelligence.Understanding(json), "2026-09-20T16:21:30.000Z"),
            false, null, Intelligence.At(Answers.Now), Intelligence.Utc, depth: depth);

    private static FileAnswer Answer(UnderstandPrompt prompt, string json = Intelligence.Verified) =>
        new(Understand(prompt, json, AnswerDepth.Brief), Understand(prompt, json, AnswerDepth.Full));

    private static SectionPresentation Checked(string json, AnswerDepth depth) =>
        CheckedPresenter.Present(Intelligence.ExecutionId, Intelligence.Live(Intelligence.Understanding(json), "2026-09-20T16:21:30.000Z"), false, null,
            Intelligence.Live(Intelligence.Evaluation(ControlPlaneApiTests.Available), "2026-09-26T18:01:00.000Z"), false, null,
            Intelligence.At("2026-09-26T18:02:00.000Z"), Intelligence.Utc, depth: depth);

    /// <summary>A file with every answer read from <paramref name="json"/>, showing <paramref name="section"/>.</summary>
    internal static FileScreen Read(FileSection section, string json = Intelligence.Verified) => new()
    {
        Section = section,
        WhatChanged = Answer(UnderstandPrompt.WhatChanged, json),
        WhyChanged = Answer(UnderstandPrompt.WhyChanged, json),
        HowBuilt = Answer(UnderstandPrompt.HowBuilt, json),
        Checked = new FileAnswer(Checked(json, AnswerDepth.Brief), Checked(json, AnswerDepth.Full)),
    };

    private static WaitingWork Running()
    {
        var work = new WaitingWork();
        work.Change(execution =>
        {
            execution.PendingApprovals.Clear();
            execution.Status = ExecutionStatus.Running;
        }, WorkstreamStatus.Running);
        return work;
    }

    private MenuFrame Screen(FileScreen screen, WorkspacePresentation? workspace = null, WorkspaceSteering? steering = null, AnswerRoom? room = null) =>
        FileScreens.Screen(workspace ?? Running().Present(), steering ?? new WorkspaceSteering(factory), screen, room ?? Room);

    [Test]
    public void TheFileIsItsTaskTitledUnderItsStatePillWithItsFourSections()
    {
        var work = Running();
        var workspace = work.Present();
        var frame = Screen(new FileScreen { Section = FileSection.Changes }, workspace);
        Assert.That((frame.Subject, frame.SubjectIsData), Is.EqualTo((work.Workstream.Title, true)));
        Assert.That(frame.Pill!.Word, Is.EqualTo(StateLanguage.BadgeOf(workspace.Character).Word), "the same badge its character wears");
        Assert.That(frame.Sections.Select(section => (section.Key, section.Words, section.Chosen)), Is.EqualTo(new[]
        {
            ("waiting", "Waiting", false), ("activity", "Activity", false), ("changes", "Changes", true), ("checks", "Checks", false),
        }));
        foreach (FileSection section in System.Enum.GetValues(typeof(FileSection)))
        {
            Assert.That(FileScreens.SectionOf(FileScreens.Key(section)), Is.EqualTo(section));
        }
        Assert.That(FileScreens.SectionOf("doing"), Is.Null);
    }

    [Test]
    public void ChangesShowsTheBriefAnswersEachKeepingItsClassAndTheFirstOfEachOpensItsFullAnswer()
    {
        var frame = Screen(Read(FileSection.Changes), room: AnswerRoom.Unlimited);
        Assert.That(frame.Lines.Select(line => (line.Words, line.Chip)).Take(3), Is.EqualTo(new[]
        {
            ("4 files changed: 4 edited", (string?)null),
            ("1 file not checked after the last change", "Inferred"),
            ("I will add an idempotency key in ChargeService so a retried charge returns the first one.", SectionLine.AgentChip),
        }));
        var openers = frame.Lines.Where(line => line.Opens).ToList();
        Assert.That(openers.Select(line => line.Key), Is.EqualTo(new[] { FileScreens.WhatChangedKey, FileScreens.WhyChangedKey, FileScreens.HowBuiltKey }));
        Assert.That(openers.All(line => line.Action == FileScreens.Open && line.Pressable), Is.True);
        Assert.That((frame.Lines[2].Runs.AllTheirs, frame.Lines[2].Icon), Is.EqualTo((true, (GlazeIcon?)GlazeIcon.Quote)), "the agent's words lean as its own");
        Assert.That(frame.Lines.Where(line => !line.Opens).Any(line => line.Action != null), Is.False, "the other lines only say something");
        Assert.That(frame.Source, Does.StartWith("From Salidium"), "one source line, last");
        // The version Salidium names for itself leans on its ground; "From Salidium" and when are Halcyonic's (settled 2026-10-08).
        Assert.That(frame.SourceRuns!.Runs.Select(run => run.Theirs), Is.EqualTo(new[] { false, true, false }));
        Assert.That(frame.SourceRuns!.Runs[0].Text, Is.EqualTo("From Salidium "));
        Assert.That(frame.Side, Is.Null);
        Assert.That(frame.Lines.Any(line => line.Tone == LineTone.Waiting), Is.False, "amber only for what waits");
    }

    [Test]
    public void AChosenLineOpensItsFullAnswerBesideThePageEachChangedFileWithItsKindsIcon()
    {
        var screen = Read(FileSection.Changes);
        screen.Chosen = FileScreens.WhatChangedKey;
        var frame = Screen(screen, room: new AnswerRoom(10));
        Assert.That(frame.Lines[0].Chosen, Is.True);
        var side = frame.Side!;
        Assert.That(side.Subject, Is.EqualTo(WorkspaceText.PromptLabel(UnderstandPrompt.WhatChanged)));
        Assert.That(side.Source, Does.StartWith("From Salidium"));
        Assert.That(side.Lines.Any(line => line.Action != null), Is.False, "a side panel holds nothing to press");
        var files = side.Lines.Where(line => line.Icon != null).ToList();
        Assert.That(files.Count, Is.GreaterThan(0));
        Assert.That(files.Select(line => line.Icon), Is.All.EqualTo(GlazeIcon.CodeFile), "the fixture changes TypeScript files: code, never its language's mark");
        Assert.That(side.Lines.Where(line => line.Icon == null).Select(line => line.Words), Has.None.Contains(".ts:"),
            "only a changed file's line carries a file's icon");
    }

    [Test]
    public void EachFileKindHasItsOwnGenericGlyph()
    {
        var icons = System.Enum.GetValues(typeof(FileKind)).Cast<FileKind>().Select(FileScreens.Icon).ToList();
        Assert.That(icons, Is.EqualTo(new[]
        {
            GlazeIcon.CodeFile, GlazeIcon.DatabaseFile, GlazeIcon.DataFile, GlazeIcon.TextFile, GlazeIcon.ImageFile, GlazeIcon.ScriptFile,
            GlazeIcon.PackageFile, GlazeIcon.Folder,
        }));
    }

    [Test]
    public void ChangesAndChecksSayPlainlyTheyAreStillReadingNeverAnEmptyPage()
    {
        foreach (var section in new[] { FileSection.Changes, FileSection.Checks })
        {
            var frame = Screen(new FileScreen { Section = section });
            Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { FileScreens.StillReading(section) }), section.ToString());
            Assert.That(frame.Lines[0].Tone, Is.EqualTo(LineTone.Secondary));
            Assert.That(frame.Source, Is.Null, "nothing is read yet, so no source is named");
        }
        Assert.That(FileScreens.StillReading(FileSection.Changes), Is.EqualTo("Reading Changes"));
    }

    [Test]
    public void ASourceThatAnsweredWithoutConclusionsSaysWhyAsThePagesLine()
    {
        var failed = UnderstandingPresenter.Present(UnderstandPrompt.WhatChanged, Intelligence.ExecutionId, null, loading: false, error: "Connection refused",
            Intelligence.At(Answers.Now), Intelligence.Utc, depth: AnswerDepth.Brief);
        var frame = Screen(new FileScreen { Section = FileSection.Changes, WhatChanged = new FileAnswer(failed, failed) });
        Assert.That(frame.Lines.Single().Words, Does.Contain("Connection refused"));
        Assert.That(frame.Source, Is.Null);
    }

    [Test]
    public void EveryPageFitsTheRoomAndNoClassIsUpgraded()
    {
        foreach (var json in new[] { Intelligence.Verified })
        {
            foreach (var section in new[] { FileSection.Changes, FileSection.Checks })
            {
                var screen = Read(section, json);
                var seen = 0;
                do
                {
                    var frame = Screen(screen);
                    Assert.That(frame.Lines.Sum(line => line.Rows) + (frame.Source == null ? 0 : 1) <= Room.Rows || frame.Lines.Count == 1, Is.True,
                        "a page holds the room's rows, its source line one of them, or one line taller than it");
                    if (frame.Side is { } side)
                    {
                        Assert.That(side.Lines.Sum(line => line.Rows) + 1 <= Room.Rows || side.Lines.Count == 1, Is.True, "and so does its side panel");
                    }
                    foreach (var line in frame.Lines)
                    {
                        Assert.That(line.Chip, Is.Not.EqualTo("Observed"));
                        Assert.That(line.Tone, Is.Not.EqualTo(LineTone.Waiting));
                    }
                    if (screen.Pages > 1) Assert.That(frame.Footer[PromptSlot.FarRight]!.Kind, Is.EqualTo(PromptKind.NextPage));
                    screen.NextPage();
                    seen++;
                }
                while (screen.Page != 0 && seen < 20);
                Assert.That(seen, Is.EqualTo(screen.Pages), "Next page goes through every page and back to the first");
            }
        }
        var brief = Read(FileSection.Changes);
        var lines = new[] { brief.WhatChanged!, brief.WhyChanged!, brief.HowBuilt! }.SelectMany(answer => answer.Brief.Lines).Where(line => !line.Source).ToList();
        var shown = Screen(brief, room: AnswerRoom.Unlimited).Lines;
        Assert.That(shown.Select(line => line.Chip), Is.EqualTo(lines.Select(line => line.Chip)), "every chip as the answer gave it");
    }

    [Test]
    public void ChecksOpensItsFullAnswerAndOffersRefreshBesideClose()
    {
        var screen = Read(FileSection.Checks);
        var frame = Screen(screen, room: AnswerRoom.Unlimited);
        Assert.That(frame.Lines[0].Words, Is.EqualTo("Tests passed at 15:40: 118/118 tests passed (vitest)"));
        Assert.That(frame.Lines[0].Key, Is.EqualTo(FileScreens.ChecksKey));
        Assert.That(Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, FileScreens.Refresh, null, null, null }));
        screen.Chosen = FileScreens.ChecksKey;
        var opened = Screen(screen, room: AnswerRoom.Unlimited);
        Assert.That(opened.Side!.Subject, Is.EqualTo(WorkspaceText.WhatWasChecked));
        Assert.That(opened.Side.Lines.Count, Is.GreaterThan(frame.Lines.Count), "the side panel holds the full answer");
    }

    /// <summary>
    /// Where the understanding source couldn't answer, its why still opens the full answer, so the evaluation
    /// source's measurement is reached, under its own source (the outside-text audit's gap 8).
    /// </summary>
    [Test]
    public void TheMeasurementIsReachedWhenTheUnderstandingSourceHadNoAnswer()
    {
        var unobserved = Intelligence.Failure("unavailable", "runtime_not_observed", "Salidium does not observe sessions of the opencode runtime.");
        SectionPresentation Checked(AnswerDepth depth) =>
            CheckedPresenter.Present(Intelligence.ExecutionId, Intelligence.Live(Intelligence.Understanding(unobserved), "2026-09-20T16:21:30.000Z"),
                false, null, Intelligence.Live(Intelligence.Evaluation(ControlPlaneApiTests.Available), "2026-09-26T18:01:00.000Z"), false,
                null, Intelligence.At("2026-09-26T18:02:00.000Z"), Intelligence.Utc, depth: depth);
        var screen = new FileScreen { Section = FileSection.Checks, Checked = new FileAnswer(Checked(AnswerDepth.Brief), Checked(AnswerDepth.Full)) };
        var why = Screen(screen, room: AnswerRoom.Unlimited).Lines.Single();
        Assert.That((why.Words, why.Opens, why.Key), Is.EqualTo(("From Salidium · Understanding unavailable: it doesn't follow tasks this agent app runs.", true, FileScreens.ChecksKey)));
        screen.Chosen = FileScreens.ChecksKey;
        var sources = new List<string?>();
        var side = Screen(screen, room: AnswerRoom.Unlimited).Side!;
        for (var part = 0; part < (side.Parts?.Parts ?? 1); part++)
        {
            sources.Add(Screen(screen, room: AnswerRoom.Unlimited).Side!.Source);
            screen.NextPage();
        }
        Assert.That(sources, Has.Some.EqualTo("From Seorak, read 1 minute ago"));
    }

    [Test]
    public void ASourcesOwnWordsInsideAnAnswerAreShownAsData()
    {
        SectionPresentation Failing(AnswerDepth depth) =>
            CheckedPresenter.Present(Intelligence.ExecutionId, Intelligence.Live(Intelligence.Understanding(Intelligence.Verified), "2026-09-20T16:21:30.000Z"),
                false, null, Intelligence.Live(Intelligence.Evaluation(ControlPlaneApiTests.Available), "2026-09-26T18:01:00.000Z"), false,
                "connection <b>refused</b>", Intelligence.At("2026-09-26T18:02:00.000Z"), Intelligence.Utc, depth: depth);
        var screen = new FileScreen { Section = FileSection.Checks, Checked = new FileAnswer(Failing(AnswerDepth.Brief), Failing(AnswerDepth.Full)), Chosen = FileScreens.ChecksKey };
        var lines = new List<PageLine>();
        var sources = new List<(string? Source, bool IsData)>();
        var parts = Screen(screen, room: AnswerRoom.Unlimited).Side!.Parts?.Parts ?? 1;
        for (var part = 0; part < parts; part++)
        {
            var side = Screen(screen, room: AnswerRoom.Unlimited).Side!;
            lines.AddRange(side.Lines);
            sources.Add((side.Source, side.SourceIsData));
            screen.NextPage();
        }
        Assert.That(lines.Where(line => line.Words.Contains("refused")).All(line => line.WordsAreData), Is.True, "never as Halcyonic's words");
        // The measurement's own page carries its source and why it couldn't be read again, as its source line.
        var refused = sources.Where(source => source.Source != null && source.Source.Contains("refused")).ToList();
        Assert.That(refused, Is.Not.Empty, "the measurement's failure shows in the side panel");
        Assert.That(refused.All(source => source.IsData), Is.True, "and as data, never as Halcyonic's words");
    }

    [Test]
    public void AnAnswersSourceLineIsDataAndTheAgentsIsHalcyonicsOwn()
    {
        var screen = Read(FileSection.Checks);
        screen.Chosen = FileScreens.ChecksKey;
        var checks = Screen(screen, room: AnswerRoom.Unlimited);
        Assert.That((checks.Source, checks.SourceIsData), Is.EqualTo((checks.Source, true)), "an answer's provenance holds a source's version and errors");
        Assert.That(checks.Side!.SourceIsData, Is.True);
        var changes = Screen(Read(FileSection.Changes));
        Assert.That(changes.SourceIsData, Is.True);
        var activity = Screen(new FileScreen { Section = FileSection.Activity });
        Assert.That(activity.Source, Is.Null, "activity identifies reported claims on their own rows");
    }

    [Test]
    public void ASidePanelInPartsTakesTheFootersNextPage()
    {
        var screen = Read(FileSection.Checks);
        screen.Chosen = FileScreens.ChecksKey;
        var frame = Screen(screen, room: new AnswerRoom(3));
        Assert.That(frame.Side!.Parts, Is.Not.Null);
        var (part, parts) = frame.Side.Parts!.Value;
        Assert.That((part, parts > 1), Is.EqualTo((0, true)));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Next page"));
        screen.NextPage();
        Assert.That(Screen(screen, room: new AnswerRoom(3)).Side!.Parts!.Value.Part, Is.EqualTo(1));
        screen.Section = FileSection.Changes;
        Assert.That((screen.Chosen, screen.Page, screen.SidePart), Is.EqualTo(((string?)null, 0, 0)), "another section starts afresh");
    }

    [Test]
    public void ActivityKeepsObservedEventsAndAgentClaimsDistinctWithoutRepeatingTaskState()
    {
        var present = Running().Present();
        var activity = new[]
        {
            new ActivityEntry(1, "2026-09-26T09:00:01.000Z", ActivityKind.Tool, "Ran the tests", false),
            new ActivityEntry(2, "2026-09-26T09:00:02.000Z", ActivityKind.Message, "Done with the migration", true),
        };
        var actions = new[] { WorkspaceAction.Instruct, WorkspaceAction.Interrupt };
        var workspace = new WorkspacePresentation(present.Character, present.Objective, present.Execution, present.Runtime, actions,
            actions.Where(present.RequiresConfirmation).ToList(), present.Commands, activity);
        var frame = Screen(new FileScreen { Section = FileSection.Activity, Speak = true }, workspace);
        Assert.That(Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, FileScreens.Stop, null, FileScreens.HoldToTalk, FileScreens.TellIt }));
        Assert.That(frame.Footer[PromptSlot.FarRight]!.DrawnAsMain, Is.True);
        Assert.That(frame.Footer[PromptSlot.FarRight]!.Words, Is.EqualTo("Write Instruction"));
        var quote = frame.Lines.Last();
        // The agent's words alone, no quotes, leaning beside Halcyonic's quote mark, "Agent says" their class (settled 2026-10-08).
        Assert.That((quote.Words, quote.Chip, quote.Runs.AllTheirs, quote.Icon, quote.Fact), Is.EqualTo(("Done with the migration", SectionLine.AgentChip, true, (GlazeIcon?)GlazeIcon.Quote, "09:00")));
        Assert.That(quote.FactRuns!.AnyTheirs, Is.False, "the time is Halcyonic's");
        Assert.That(frame.Lines.Sum(line => line.Rows), Is.LessThanOrEqualTo(Room.Rows));
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(activity.Select(entry => entry.Text)));
        Assert.That(frame.Lines[0].Runs.AnyTheirs, Is.False, "an observed event is not restyled as agent text");
        Assert.That(frame.Source, Is.Null, "a mixed event log cannot be attributed wholesale to the agent");
    }

    [TestCase(3)]
    [TestCase(4)]
    public void EmptyActivityNamesMissingRecordsBeforeExistingUtilityRoutes(int rows)
    {
        var screen = new FileScreen { Section = FileSection.Activity, WorkspaceRequestOffered = true, ResultsOffered = true };
        var frame = Screen(screen, room: new AnswerRoom(rows));
        Assert.That(frame.Lines.Sum(line => line.Rows), Is.LessThanOrEqualTo(rows));
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "No Activity", "Ask workspace", "Read Results" }));
        Assert.That(frame.Lines[0].Action, Is.Null);
        Assert.That(frame.Lines[0].Tone, Is.EqualTo(LineTone.Secondary));
        Assert.That(frame.Lines.Skip(1).Select(line => line.Action), Is.EqualTo(new[] { FileScreens.OpenWorkspaceRequest, FileScreens.OpenResults }));
    }

    [TestCase(false, false, 1, false)]
    [TestCase(false, true, 1, false)]
    [TestCase(true, false, 1, false)]
    [TestCase(true, true, 1, false)]
    [TestCase(true, true, 2, false)]
    [TestCase(false, true, 2, true)]
    [TestCase(true, true, 2, true)]
    public void EveryActivityRouteRemainsReachableWithinRowAndPhysicalBudgets(bool physical, bool speak, int routeRows, bool notice)
    {
        PageBudget budget = physical ? HeightBudget.Of(TextSize.Larger, subjectRows: 2) : new RowBudget(4);
        var screen = new FileScreen { Section = FileSection.Activity, Speak = speak,
            ShowTaskHereOffered = true, WorkspaceRequestOffered = true, ResultsOffered = true, CharacterColourOffered = true,
            ShowTaskHereRows = routeRows, WorkspaceRequestRows = routeRows, ResultsRows = routeRows, CharacterColourRows = routeRows };
        var workspace = Offering(Running().Present(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory);
        if (notice) screen.Notice = "Nothing was sent. Check the connection before trying again.";
        var notices = 0;
        var seen = new List<string>();
        var first = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4), budget);
        Assert.That(first.Lines.First().Words, Is.EqualTo(notice ? screen.Notice : "No Activity"));
        Assert.That(screen.Pages, Is.GreaterThan(1));
        var count = screen.Pages;
        for (var page = 0; page < count; page++)
        {
            var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4), budget);
            notices += frame.Lines.Count(line => line.Words == screen.Notice);
            Assert.That(screen.Page, Is.EqualTo(page));
            Assert.That(frame.Lines.Sum(line => line.Rows), Is.LessThanOrEqualTo(4));
            var used = 0f;
            for (var index = 0; index < frame.Lines.Count; index++)
            {
                var line = frame.Lines[index];
                used += line.Action == null ? budget.Words(line.Rows) : budget.Target(line.Rows);
                if (index > 0) used += line.Action != null && frame.Lines[index - 1].Action != null ? budget.TargetGap : budget.LineGap;
                if (line.Action != null && line.Action != Footer.NextPage) seen.Add(line.Action);
            }
            Assert.That(used, Is.LessThanOrEqualTo(budget.Room + 1e-6f), "complete final order including body pager fits");
            Assert.That(frame.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(FileScreens.TellIt));
            Assert.That(frame.Footer[PromptSlot.Rare]!.Id, Is.EqualTo(FileScreens.Stop));
            if (speak)
            {
                Assert.That(frame.Footer[PromptSlot.Secondary]!.Id, Is.EqualTo(FileScreens.HoldToTalk));
                Assert.That(frame.Lines.Last().Action, Is.EqualTo(Footer.NextPage));
            }
            else Assert.That(frame.Footer[PromptSlot.Secondary]!.Id, Is.EqualTo(Footer.NextPage));
            Assert.That(steering.Armed, Is.Null);
            screen.NextPage();
        }
        Assert.That(notices, Is.EqualTo(notice ? 1 : 0), "the complete notice belongs to one measured page");
        Assert.That(screen.Page, Is.Zero, "the existing pager cycles back to the truthful first page");
        Assert.That(seen, Is.EqualTo(new[] { FileScreens.ShowTaskHere, FileScreens.OpenWorkspaceRequest, FileScreens.OpenResults, FileScreens.OpenCharacterColour }));
    }

    [Test]
    public void NewActivityNoticeReturnsToVisibleFeedbackWithoutResettingPassivePaging()
    {
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true, ResultsOffered = true, WorkspaceRequestOffered = true, CharacterColourOffered = true, ShowTaskHereOffered = true };
        var workspace = Offering(Running().Present(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory);
        MenuFrame Draw() => FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4), new RowBudget(4));
        Draw(); screen.NextPage();
        Assert.That(screen.Page, Is.GreaterThan(0));
        screen.Notice = "Nothing was sent. Check the connection before trying again.";
        var feedback = Draw();
        Assert.That(screen.Page, Is.Zero);
        Assert.That(feedback.Lines.First().Words, Is.EqualTo(screen.Notice));
        screen.NextPage(); var reached = screen.Page;
        screen.Notice = screen.Notice;
        Draw();
        Assert.That(screen.Page, Is.EqualTo(reached), "same notice does not trap the person on its page");
        screen.Notice = null; Draw();
        Assert.That(screen.Page, Is.EqualTo(reached), "existing notice expiry does not restart navigation");
    }

    [Test]
    public void ImpossibleActivityAllocationRefusesOverflowAndRecoversRoutesAfterReflow()
    {
        var screen = new FileScreen { Section = FileSection.Activity, Speak = true, ResultsOffered = true, ResultsRows = 3, WorkspaceRequestOffered = true };
        var workspace = Offering(Running().Present(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory);
        var refused = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(3), new RowBudget(3));
        Assert.That(refused.Lines.Single().Words, Is.EqualTo("No Room · Move Workspace"));
        Assert.That(refused.Footer.All.Select(entry => entry.Prompt.Id), Is.EqualTo(new[] { Footer.Close, FileScreens.Stop, FileScreens.HoldToTalk, FileScreens.TellIt }));
        Assert.That(screen.ResultsOffered && screen.WorkspaceRequestOffered, Is.True);
        var actions = new List<string>();
        FileScreens.Screen(workspace, steering, screen, new AnswerRoom(5), new RowBudget(5));
        var count = screen.Pages;
        for (var page = 0; page < count; page++)
        {
            var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(5), new RowBudget(5));
            actions.AddRange(frame.Lines.Where(line => line.Action != null).Select(line => line.Action!));
            screen.NextPage();
        }
        Assert.That(actions, Does.Contain(FileScreens.OpenResults).And.Contain(FileScreens.OpenWorkspaceRequest));
    }

    [Test]
    public void ActivityPagesRetainRecentRecordsAndClampAfterRoutesDisappear()
    {
        var present = Running().Present();
        var entries = Enumerable.Range(1, 6).Select(index => new ActivityEntry(index, "2026-09-26T09:00:01.000Z", ActivityKind.Message, "Record " + index, true)).ToArray();
        var workspace = new WorkspacePresentation(present.Character, present.Objective, present.Execution, present.Runtime,
            present.Actions, present.Actions.Where(present.RequiresConfirmation).ToList(), present.Commands, entries);
        var screen = new FileScreen { Section = FileSection.Activity, WorkspaceRequestOffered = true, ResultsOffered = true, CharacterColourOffered = true, ShowTaskHereOffered = true };
        var steering = new WorkspaceSteering(factory);
        var seen = new List<string>();
        FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4), new RowBudget(4));
        var count = screen.Pages;
        for (var page = 0; page < count; page++)
        {
            var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4), new RowBudget(4));
            seen.AddRange(frame.Lines.Where(line => line.Action == null).Select(line => line.Words));
            Assert.That(frame.Lines.Where(line => line.Action == null).All(line => line.Runs.AllTheirs && line.Chip == SectionLine.AgentChip), Is.True);
            if (page + 1 < count) screen.NextPage();
        }
        Assert.That(seen, Is.EqualTo(new[] { "Record 3", "Record 4", "Record 5", "Record 6" }));
        screen.WorkspaceRequestOffered = screen.ResultsOffered = screen.CharacterColourOffered = screen.ShowTaskHereOffered = false;
        var reduced = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4), new RowBudget(4));
        Assert.That((screen.Page, screen.Pages), Is.EqualTo((0, 1)));
        Assert.That(reduced.Lines.Count, Is.EqualTo(4));
        Assert.That(reduced.Footer.All.Any(entry => entry.Prompt.Id == Footer.NextPage), Is.False);
    }

    [Test]
    public void RecordedActivityPrecedesUtilityRoutesWithoutChangingProvenance()
    {
        var present = Running().Present();
        var activity = new[] { new ActivityEntry(1, "2026-09-26T09:00:01.000Z", ActivityKind.Message, "Checking the tests", true) };
        var workspace = new WorkspacePresentation(present.Character, present.Objective, present.Execution, present.Runtime,
            present.Actions, present.Actions.Where(present.RequiresConfirmation).ToList(), present.Commands, activity);
        var frame = Screen(new FileScreen { Section = FileSection.Activity, WorkspaceRequestOffered = true, ResultsOffered = true }, workspace);
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Checking the tests", "Ask workspace", "Read Results" }));
        Assert.That((frame.Lines[0].Chip, frame.Lines[0].Runs.AllTheirs), Is.EqualTo((SectionLine.AgentChip, true)));
    }

    [Test]
    public void EmptyActivityDoesNotInventAnEventFromTheCurrentState()
    {
        var frame = Screen(new FileScreen { Section = FileSection.Activity });
        Assert.That(frame.Lines.Select(line => line.Words), Is.EqualTo(new[] { "No Activity" }));
        Assert.That(frame.Source, Is.Null);
        Assert.That(frame.Pill, Is.Not.Null, "the actual task state remains visible above activity");
        var reading = Screen(new FileScreen { Section = FileSection.Activity, ActivityNote = "Reading activity" });
        Assert.That(reading.Lines.Select(line => line.Words), Is.EqualTo(new[] { "Reading activity" }));
    }

    [Test]
    public void ActivityWhileSomethingWaitsNamesTheWaitingSectionInTheWaitingColour()
    {
        var work = new WaitingWork();
        var frame = Screen(new FileScreen { Section = FileSection.Activity }, work.Present());
        Assert.That((frame.Lines[0].Words, frame.Lines[0].Tone), Is.EqualTo(("Approval Requested · Open Waiting", LineTone.Waiting)));
    }

    [Test]
    public void TellItOffersTheRecordedInstructionsAsRowsWhereThereIsNoKeyboard()
    {
        var presets = new[] { new PresetInstruction("Add a test", "Add a test for refunds."), new PresetInstruction("Wrap up", "Wrap up and summarise.") };
        var workspace = Offering(Running().Present(), WorkspaceAction.Instruct);
        var screen = new FileScreen { Section = FileSection.Activity, Presets = presets };
        var frame = Screen(screen, workspace);
        Assert.That(frame.Lines.Select(line => (line.Words, line.WordsAreData, line.Action, line.Key, line.Choice)), Is.EqualTo(new[]
        {
            ("Add a test for refunds.", true, FileScreens.Preset, "0", true), ("Wrap up and summarise.", true, FileScreens.Preset, "1", true),
        }), "each row shows the very words Tell it sends");
        Assert.That(Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, FileScreens.Cancel, null, null, FileScreens.TellIt }));
        Assert.That((frame.Footer[PromptSlot.FarRight]!.Available, frame.Reason), Is.EqualTo((false, (string?)FileScreens.ChooseAnInstruction)));
        Assert.That(screen.PresetToSend, Is.Null, "nothing to send until one is chosen");

        screen.ChoosePreset(1);
        var chosen = Screen(screen, workspace);
        Assert.That(chosen.Lines.Where(line => line.Chosen).Select(line => line.Key), Is.EqualTo(new[] { "1" }), "choosing only lights the row");
        Assert.That(chosen.Footer[PromptSlot.FarRight]!.Available, Is.True);
        Assert.That(screen.PresetToSend, Is.EqualTo("Wrap up and summarise."), "Tell it sends exactly what the row shows");
        screen.Presets = presets.ToArray();
        Assert.That(screen.ChosenPreset, Is.EqualTo(1), "the same instructions offered again keep what was chosen");
        screen.Presets = new[] { presets[0], new PresetInstruction("Wrap up", "Wrap up and push.") };
        Assert.That(screen.ChosenPreset, Is.Null, "other words clear it, so Tell it never sends what was not chosen");
        screen.Presets = null;
        Assert.That(screen.PresetToSend, Is.Null);
    }

    [Test]
    public void StoppingAsksWithCancelInStopsPlaceAndYesInTheFreeMiddle()
    {
        var workspace = Offering(Running().Present(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory);
        Assert.That(steering.Press(WorkspaceAction.Interrupt, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        var frame = Screen(new FileScreen { Section = FileSection.Activity }, workspace, steering);
        Assert.That(Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, FileScreens.Cancel, FileScreens.Yes, null, null }));
        Assert.That(frame.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Confirm Stop"));
        Assert.That(frame.Lines.Single().Words, Does.StartWith("Stop Task?"));
    }

    [Test]
    public void ANoticeIsThePagesLastLine()
    {
        var frame = Screen(new FileScreen { Section = FileSection.Checks, Notice = "Not Sent · Confirm Timed Out · Press Again" });
        Assert.That(frame.Lines.Last().Words, Does.StartWith("Not Sent"));
    }

    /// <summary>
    /// A long agent report reads whole on the Activity page: in parts a page holds, each exactly its rows of
    /// the words wrapped whole beside the same chip and time, so the parts wrap alike; an observed outcome
    /// keeps its own line.
    /// </summary>
    [Test]
    public void ActivityCarriesALongReportWholeInParts()
    {
        var present = Running().Present();
        var report = string.Join(" ", Enumerable.Repeat("The rate limiter shares one counter per address.", 6));
        var entries = new[]
        {
            new ActivityEntry(1, "2026-09-26T09:00:01.000Z", ActivityKind.Message, report, true),
            new ActivityEntry(2, "2026-09-26T09:00:02.000Z", ActivityKind.Tool, "Command failed: node --test (exit 1)", false),
        };
        var workspace = new WorkspacePresentation(present.Character, present.Objective, present.Execution, present.Runtime,
            present.Actions, present.Actions.Where(present.RequiresConfirmation).ToList(), present.Commands, entries);
        var screen = new FileScreen { Section = FileSection.Activity, ActivityRows = new Dictionary<long, int> { [1] = 7 } };
        var steering = new WorkspaceSteering(factory);
        var parts = new List<PageLine>();
        var outcomes = new List<PageLine>();
        FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4), new RowBudget(4));
        var pages = screen.Pages;
        for (var page = 0; page < pages; page++)
        {
            var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4), new RowBudget(4));
            parts.AddRange(frame.Lines.Where(line => line.Chip == SectionLine.AgentChip));
            outcomes.AddRange(frame.Lines.Where(line => line.Words.StartsWith("Command failed")));
            if (page + 1 < pages) screen.NextPage();
        }
        Assert.That(parts.Select(line => (line.FromRow, line.Rows)), Is.EqualTo(new (int?, int)[] { (0, 3), (3, 3), (6, 1) }),
            "every measured row of the report, in parts a 4-row page holds beside its pager");
        Assert.That(parts.All(line => line.Words == report && line.Runs.AllTheirs && line.Fact == "09:00"), Is.True,
            "each part keeps the whole words, its chip and its time, so the parts wrap alike");
        Assert.That(outcomes.Select(line => (line.Rows, line.Chip)), Is.EqualTo(new[] { (1, (string?)null) }), "the observed outcome keeps its own line");
    }

    /// <summary>
    /// Beside the composer, an agent's report wraps to its measured rows where the room allows, up to four,
    /// and never pushes out the observed outcome beside it; with a row apiece, both stay single lines.
    /// </summary>
    [Test]
    public void PassiveActivityWrapsAReportBesideItsObservedOutcome()
    {
        var report = string.Join(" ", Enumerable.Repeat("The rate limiter shares one counter per address.", 3));
        FileColumn Composing(int activityRows, params ActivityEntry[] entries)
        {
            var present = Offering(Running().Present(), WorkspaceAction.Instruct);
            var current = new WorkspacePresentation(present.Character, present.Objective, present.Execution, present.Runtime, present.Actions,
                present.Actions.Where(present.RequiresConfirmation).ToList(), present.Commands, entries);
            var column = new FileColumn(new FileMenuHost(), () => current, factory, () => null, _ => null, () => "");
            column.ConfigureActivityComposer(() => new RowBudget(activityRows), () => new RowBudget(8));
            column.HoldStarted(FileScreens.HoldToTalk);
            column.Heard("Carry on carefully");
            return column;
        }
        var reported = new ActivityEntry(1, "2026-09-26T09:00:01.000Z", ActivityKind.Message, report, true);
        var failed = new ActivityEntry(2, "2026-09-26T09:00:02.000Z", ActivityKind.Tool, "Command failed: node --test (exit 1)", false);
        var lines = Composing(8, reported, failed).ActivityContextFrame!.Lines;
        Assert.That(lines.Select(line => (line.Words, line.Rows, line.Chip)), Is.EqualTo(new[]
            { (report, 3, (string?)SectionLine.AgentChip), ("Command failed: node --test (exit 1)", 1, (string?)null) }),
            "the report takes the rows the room leaves after the newer outcome");
        var newest = Composing(8, failed, new ActivityEntry(3, "2026-09-26T09:00:03.000Z", ActivityKind.Message, report, true)).ActivityContextFrame!.Lines;
        Assert.That(newest.Select(line => line.Rows), Is.EqualTo(new[] { 1, 3 }), "a newest report leaves a row for the outcome before it");
        var tight = Composing(2, reported, failed).ActivityContextFrame!.Lines;
        Assert.That(tight.Select(line => line.Rows), Is.EqualTo(new[] { 1, 1 }), "with a row apiece both stay, the report cut to its first row");
    }
}
