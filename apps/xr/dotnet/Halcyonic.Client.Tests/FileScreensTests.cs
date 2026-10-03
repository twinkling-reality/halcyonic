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
    private static FileScreen Read(FileSection section, string json = Intelligence.Verified) => new()
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
            ("“I will add an idempotency key in ChargeService so a retried charge returns the first one.”", "Agent says"),
        }));
        var openers = frame.Lines.Where(line => line.Opens).ToList();
        Assert.That(openers.Select(line => line.Key), Is.EqualTo(new[] { FileScreens.WhatChangedKey, FileScreens.WhyChangedKey, FileScreens.HowBuiltKey }));
        Assert.That(openers.All(line => line.Action == FileScreens.Open && line.Pressable), Is.True);
        Assert.That(frame.Lines[2].Claim, Is.True, "the agent's words lean as its own");
        Assert.That(frame.Lines.Where(line => !line.Opens).Any(line => line.Action != null), Is.False, "the other lines only say something");
        Assert.That(frame.Source, Does.StartWith("From Salidium"), "one source line, last");
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
        Assert.That(FileScreens.StillReading(FileSection.Changes), Does.StartWith("Still reading what changed"));
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
    public void ActivityOffersTellItAsTheMainActionWithStopBesideCloseAndHoldToTalkBesideTellIt()
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
        var quote = frame.Lines.Last();
        Assert.That((quote.Words, quote.Chip, quote.Claim, quote.Fact), Is.EqualTo(("“Done with the migration”", "Agent says", true, "09:00")));
        Assert.That(frame.Lines.Sum(line => line.Rows) + 1, Is.LessThanOrEqualTo(Room.Rows), "the source line takes one of the page's rows");
        Assert.That(frame.Source, Is.EqualTo(FileScreens.AgentSource));
    }

    [Test]
    public void ActivityWhileSomethingWaitsNamesTheWaitingSectionInTheWaitingColour()
    {
        var work = new WaitingWork();
        var frame = Screen(new FileScreen { Section = FileSection.Activity }, work.Present());
        Assert.That((frame.Lines[0].Words, frame.Lines[0].Tone), Is.EqualTo(("It wants your approval. See it under Waiting.", LineTone.Waiting)));
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
        screen.Presets = presets;
        Assert.That(screen.ChosenPreset, Is.Null, "offered again, nothing is chosen");
    }

    [Test]
    public void StoppingAsksWithCancelInStopsPlaceAndYesInTheFreeMiddle()
    {
        var workspace = Offering(Running().Present(), WorkspaceAction.Instruct, WorkspaceAction.Interrupt);
        var steering = new WorkspaceSteering(factory);
        Assert.That(steering.Press(WorkspaceAction.Interrupt, workspace).Step, Is.EqualTo(SteeringStep.Confirm));
        var frame = Screen(new FileScreen { Section = FileSection.Activity }, workspace, steering);
        Assert.That(Slots(frame.Footer), Is.EqualTo(new[] { Footer.Close, FileScreens.Cancel, FileScreens.Yes, null, null }));
        Assert.That(frame.Footer[PromptSlot.Free]!.Words, Is.EqualTo("Yes, stop"));
        Assert.That(frame.Lines.Single().Words, Does.StartWith("Stop what it's doing now?"));
    }

    [Test]
    public void ANoticeIsThePagesLastLine()
    {
        var frame = Screen(new FileScreen { Section = FileSection.Checks, Notice = "Nothing was sent: you didn't confirm in time. Press it again." });
        Assert.That(frame.Lines.Last().Words, Does.StartWith("Nothing was sent"));
    }
}
