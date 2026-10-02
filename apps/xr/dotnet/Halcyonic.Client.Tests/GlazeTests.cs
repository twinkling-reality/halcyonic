using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The tokens hold to their contrast and to Meta's minimums (ADR 0023).</summary>
public class GlazeTokenTests
{
    private static readonly GlazeColor DarkestShown = GlazeColor.Hex(0x1A1A1A);

    private static IEnumerable<GlazeTone> Tones => (GlazeTone[])Enum.GetValues(typeof(GlazeTone));

    [Test]
    public void ContrastFollowsTheWcagFormula()
    {
        Assert.That(GlazeColor.Contrast(GlazeColor.Hex(0x000000), GlazeColor.Hex(0xFFFFFF)), Is.EqualTo(21).Within(1e-9));
        Assert.That(GlazeColor.Contrast(Glaze.Panel, Glaze.Panel), Is.EqualTo(1).Within(1e-9));
        Assert.That(GlazeColor.Contrast(Glaze.Text, Glaze.Panel), Is.EqualTo(GlazeColor.Contrast(Glaze.Panel, Glaze.Text)), "order does not matter");
        Assert.That(Glaze.Panel.ToString(), Is.EqualTo("#1B222D"));
    }

    [Test]
    public void TextReadsOnEverySurfaceItSitsOn()
    {
        foreach (var surface in new[] { Glaze.Panel, Glaze.Raised, Glaze.Control, Glaze.ControlHover, Glaze.ControlPressed, Glaze.Well })
        {
            Assert.That(GlazeColor.Contrast(Glaze.Text, surface), Is.GreaterThanOrEqualTo(4.5), $"text on {surface}");
        }
        Assert.That(GlazeColor.Contrast(Glaze.Text, Glaze.Panel), Is.GreaterThanOrEqualTo(7), "body text on a panel");
        foreach (var surface in new[] { Glaze.Panel, Glaze.Raised, Glaze.Control })
        {
            Assert.That(GlazeColor.Contrast(Glaze.TextSecondary, surface), Is.GreaterThanOrEqualTo(4.5), $"secondary text on {surface}");
        }
        Assert.That(GlazeColor.Contrast(Glaze.TextDisabled, Glaze.Panel), Is.GreaterThanOrEqualTo(4.5), "disabled text still reads");
        Assert.That(GlazeColor.Contrast(Glaze.Outline, Glaze.Panel), Is.GreaterThanOrEqualTo(3), "an outline on a panel");
        Assert.That(GlazeColor.Contrast(Glaze.Outline, Glaze.Raised), Is.GreaterThanOrEqualTo(3), "an outline on a row");
    }

    [Test]
    public void EveryToneReadsOnAPanelInItsContainerAndOnItsFill()
    {
        foreach (var tone in Tones)
        {
            var colors = Glaze.Tone(tone);
            Assert.That(GlazeColor.Contrast(colors.Foreground, Glaze.Panel), Is.GreaterThanOrEqualTo(7), $"{tone} text on a panel");
            Assert.That(GlazeColor.Contrast(colors.Foreground, colors.Container), Is.GreaterThanOrEqualTo(4.5), $"{tone} text in its container");
            Assert.That(GlazeColor.Contrast(colors.OnStrong, colors.Strong), Is.GreaterThanOrEqualTo(4.5), $"text on {tone}'s fill");
            // The neutral fill is a secondary button's surface, which its words identify; every other
            // tone's fill is a state or the primary action, which must stand out on its own.
            if (tone == GlazeTone.Neutral) continue;
            Assert.That(GlazeColor.Contrast(colors.Strong, Glaze.Panel), Is.GreaterThanOrEqualTo(3), $"{tone}'s fill against a panel");
        }
    }

    [Test]
    public void NoSurfaceIsDarkerThanTheDisplayShows()
    {
        foreach (var surface in new[] { Glaze.Panel, Glaze.Raised, Glaze.Control, Glaze.Well })
        {
            Assert.That(surface.Luminance, Is.GreaterThanOrEqualTo(DarkestShown.Luminance), $"{surface} is darker than #1A1A1A");
        }
    }

    [Test]
    public void TheMenusTypeStepsDownFromThreeSizesAndKeepsTo14Dp()
    {
        Assert.That(Glaze.Menu.TitleDegrees, Is.EqualTo(24 * Glaze.DegreesPerDp));
        Assert.That(Glaze.Menu.BodyDegrees, Is.EqualTo(18 * Glaze.DegreesPerDp));
        Assert.That(Glaze.Menu.LabelDegrees, Is.EqualTo(15 * Glaze.DegreesPerDp));
        Assert.That(Glaze.Menu.TitleDegrees, Is.GreaterThan(Glaze.Menu.BodyDegrees));
        Assert.That(Glaze.Menu.BodyDegrees, Is.GreaterThan(Glaze.Menu.LabelDegrees));
        Assert.That(Glaze.Menu.LabelDegrees, Is.GreaterThan(Glaze.MinimumTextDegrees), "the smallest is over 14 dp");
        Assert.That(Glaze.Menu.PillDegrees, Is.EqualTo(Glaze.Menu.BodyDegrees), "the pill's word at the content's size");
        Assert.That(Glaze.Menu.PlaneMeters, Is.EqualTo(0.46f), "touch distance");
    }

    [Test]
    public void EveryGapOnTheMenuIsAWholeNumberOfGridSteps()
    {
        Assert.That(Glaze.Menu.GridDegrees, Is.EqualTo(8 * Glaze.DegreesPerDp), "8 dp");
        foreach (var (name, gap) in new[]
        {
            ("padding", Glaze.Menu.PaddingDegrees),
            ("between groups", Glaze.Menu.GroupGapDegrees),
            ("from a name to its value", Glaze.Menu.LabelToValueDegrees),
            ("the icon column", Glaze.Menu.IconColumnDegrees),
            ("between parts", Glaze.Menu.PartGapDegrees),
        })
        {
            var steps = gap / Glaze.Menu.GridDegrees;
            Assert.That(steps, Is.EqualTo(MathF.Round(steps)).Within(1e-5f), $"{name} is {steps} grid steps");
            Assert.That(steps, Is.GreaterThanOrEqualTo(1f), name);
        }
        Assert.That(Glaze.Menu.PaddingDegrees, Is.EqualTo(24 * Glaze.DegreesPerDp));
        Assert.That(Glaze.Menu.GroupGapDegrees, Is.EqualTo(16 * Glaze.DegreesPerDp));
    }

    [Test]
    public void EveryWordOnTheGlassReadsOverAWhiteWall()
    {
        // Passthrough's brightest: the glass at 96 percent over white, and a chosen shape's lit fill over that.
        foreach (var (where, under) in new[] { ("the glass", Glaze.Menu.GlassOverWhite), ("a chosen shape", Glaze.Menu.LitOverWhite) })
        {
            foreach (var (name, colour) in new[]
            {
                ("text", Glaze.Text),
                ("secondary text", Glaze.TextSecondary),
                ("a quiet prompt's or line's words", Glaze.Menu.QuietText),
                ("the main action's words", Glaze.Tone(GlazeTone.Accent).Foreground),
                ("waiting for you", Glaze.Tone(GlazeTone.Attention).Foreground),
                ("a good line", Glaze.Tone(GlazeTone.Success).Foreground),
                ("a problem", Glaze.Tone(GlazeTone.Failure).Foreground),
                ("moving work", Glaze.Tone(GlazeTone.Active).Foreground),
                ("practice and demo", Glaze.Tone(GlazeTone.Simulated).Foreground),
            })
            {
                Assert.That(GlazeColor.Contrast(colour, under), Is.GreaterThanOrEqualTo(4.5), $"{name} on {where} over white");
            }
        }
        Assert.That(Glaze.Menu.GlassOverWhite.Luminance, Is.GreaterThan(Glaze.Panel.Luminance), "a white wall lightens the glass");
        Assert.That(GlazeColor.Contrast(Glaze.TextDisabled, Glaze.Menu.LitOverWhite), Is.LessThan(4.5), "why quiet words take the secondary colour on the menu");
    }

    [Test]
    public void OneSelectionTreatmentIsALitFillAndFrameAndAFainterFrameAlone()
    {
        Assert.That(Glaze.Menu.LitFillOpacity, Is.EqualTo(0.10f));
        Assert.That(Glaze.Menu.LitFrameOpacity, Is.EqualTo(0.78f));
        Assert.That(Glaze.Menu.PointedFrameOpacity, Is.EqualTo(0.42f));
        Assert.That(Glaze.Menu.PointedFrameOpacity, Is.LessThan(Glaze.Menu.LitFrameOpacity), "pointed at, the frame is fainter");
        Assert.That(Glaze.Menu.GlassOpacity, Is.EqualTo(Glaze.PlateOpacity));
    }

    [Test]
    public void ColoursBlendAsADisplayBlendsThem()
    {
        var white = GlazeColor.Hex(0xFFFFFF);
        var black = GlazeColor.Hex(0x000000);
        Assert.That(black.Over(white, 1), Is.EqualTo(black));
        Assert.That(black.Over(white, 0), Is.EqualTo(white));
        Assert.That(black.Over(white, 0.5), Is.EqualTo(GlazeColor.Hex(0x808080)));
    }

    [Test]
    public void TypeAndTargetsKeepToMetasMinimums()
    {
        Assert.That(Glaze.MinimumTextDegrees, Is.EqualTo(14 * Glaze.DegreesPerDp));
        Assert.That(Glaze.CaptionDegrees, Is.GreaterThan(Glaze.MinimumTextDegrees), "the smallest text is over 14 dp");
        Assert.That(Glaze.BodyDegrees, Is.GreaterThanOrEqualTo(18 * Glaze.DegreesPerDp), "body text is comfortable, 18 dp");
        Assert.That(Glaze.BadgeDegrees, Is.GreaterThan(Glaze.CaptionDegrees));
        Assert.That(Glaze.MinimumTargetDegrees, Is.EqualTo(48 * Glaze.DegreesPerDp), "Meta's 48 dp hit target");
        Assert.That(Glaze.TargetDegrees, Is.EqualTo(60 * Glaze.DegreesPerDp), "Meta's 60 dp for primary hand targets");
        Assert.That(Glaze.TargetGapMeters, Is.EqualTo(0.012f), "12 mm between targets");
        Assert.That(Glaze.PlateOpacity, Is.GreaterThanOrEqualTo(0.94f), "a plate a bright room cannot wash out");
    }

    [Test]
    public void AnglesAndSizesConvertBothWays()
    {
        var meters = Glaze.MetersAt(Glaze.TitleDegrees, 2.4f);
        Assert.That(meters, Is.EqualTo(0.0524f).Within(0.0005f), "a title's em 2.4 m away");
        Assert.That(Glaze.DegreesOf(meters, 2.4f), Is.EqualTo(Glaze.TitleDegrees).Within(1e-4f));
    }
}

/// <summary>Every state has one word, one icon, a tone, a fill and an edge, and never only a colour.</summary>
public class StateLanguageTests
{
    private static CharacterActivity[] Activities => (CharacterActivity[])Enum.GetValues(typeof(CharacterActivity));

    private static WorkState[] States => (WorkState[])Enum.GetValues(typeof(WorkState));

    internal static CharacterPresentation Character(
        CharacterActivity activity,
        AttentionLevel attention = AttentionLevel.None,
        IReadOnlyList<string>? notes = null,
        int approvals = 0,
        bool synthetic = false,
        bool recorded = false,
        bool stale = false,
        IReadOnlyList<string>? details = null,
        string title = "Fix the authentication regression") =>
        new(
            "w1",
            title,
            activity,
            CharacterPresenter.LabelOf(activity),
            attention,
            notes ?? new List<string>(),
            approvals,
            synthetic,
            recorded,
            stale,
            details);

    [Test]
    public void EachActivityHasTheStateItReadsAs()
    {
        var expected = new Dictionary<CharacterActivity, WorkState>
        {
            [CharacterActivity.Idle] = WorkState.NotStarted,
            [CharacterActivity.Starting] = WorkState.Starting,
            [CharacterActivity.Working] = WorkState.Working,
            [CharacterActivity.Verifying] = WorkState.CheckingItsWork,
            [CharacterActivity.WaitingForHuman] = WorkState.WaitingForYou,
            [CharacterActivity.TurnFinished] = WorkState.FinishedThisRound,
            [CharacterActivity.Failed] = WorkState.CouldNotFinish,
            [CharacterActivity.Interrupted] = WorkState.Stopped,
            [CharacterActivity.Unknown] = WorkState.CantTellYet,
        };
        foreach (var activity in Activities)
        {
            Assert.That(StateLanguage.StateOf(activity, AttentionLevel.None), Is.EqualTo(expected[activity]), activity.ToString());
        }
    }

    [Test]
    public void TheWordsAreTheOnesTheOwnerApproved()
    {
        Assert.That(States.Select(StateLanguage.WordOf), Is.EqualTo(new[]
        {
            "Not started", "Starting", "Working", "Checking its work", "Waiting for you", "Finished this round",
            "Checks failed", "Couldn't finish", "Stopped", "Can't tell yet",
        }));
    }

    [Test]
    public void AFinishedRoundWhoseChecksFailedSaysSo()
    {
        Assert.That(StateLanguage.StateOf(CharacterActivity.TurnFinished, AttentionLevel.Notice), Is.EqualTo(WorkState.ChecksFailed));
        Assert.That(StateLanguage.StateOf(CharacterActivity.TurnFinished, AttentionLevel.None), Is.EqualTo(WorkState.FinishedThisRound));
    }

    [Test]
    public void SomethingWaitingForThePersonWinsOverWhatTheWorkIsDoing()
    {
        foreach (var activity in Activities)
        {
            Assert.That(StateLanguage.StateOf(activity, AttentionLevel.ActionRequired), Is.EqualTo(WorkState.WaitingForYou), activity.ToString());
        }
        Assert.That(StateLanguage.StateOf(CharacterActivity.WaitingForHuman, AttentionLevel.None), Is.EqualTo(WorkState.WaitingForYou));
        Assert.That(StateLanguage.StateOf(CharacterActivity.Failed, AttentionLevel.Notice), Is.EqualTo(WorkState.CouldNotFinish), "a failure is a notice, not a request");
    }

    [Test]
    public void NoStateIsToldByColourAlone()
    {
        var icons = new HashSet<GlazeIcon>();
        foreach (var state in States)
        {
            var (tone, icon, fill, edge, _) = StateLanguage.Look(state);
            Assert.That(StateLanguage.WordOf(state), Is.Not.Empty, $"{state} has a word");
            Assert.That(icons.Add(icon), Is.True, $"{state} has an icon of its own");
            foreach (var other in States.Where(other => other != state))
            {
                var look = StateLanguage.Look(other);
                Assert.That(look.Icon != icon || look.Fill != fill || look.Edge != edge || look.Tone != tone, Is.True, $"{state} and {other} look alike");
            }
        }
    }

    [Test]
    public void OnlyWaitingForYouBreathesAndOnlyStartingAndWorkingTurn()
    {
        foreach (var activity in Activities)
        {
            var badge = StateLanguage.BadgeOf(Character(activity));
            Assert.That(badge.Breathes, Is.EqualTo(badge.State == WorkState.WaitingForYou), $"{badge.State} breathes");
            Assert.That(badge.Turns, Is.EqualTo(badge.State is WorkState.Starting or WorkState.Working), $"{badge.State} turns");
        }
    }

    [Test]
    public void ToneFamiliesAreFewAndMeanOneThingEach()
    {
        Assert.That(States.Where(state => StateLanguage.Look(state).Tone == GlazeTone.Attention), Is.EqualTo(new[] { WorkState.WaitingForYou }), "amber means Waiting for you");
        Assert.That(States.Where(state => StateLanguage.Look(state).Tone == GlazeTone.Failure),
            Is.EqualTo(new[] { WorkState.ChecksFailed, WorkState.CouldNotFinish }), "red means something went wrong");
        Assert.That(States.Where(state => StateLanguage.Look(state).Tone == GlazeTone.Active),
            Is.EqualTo(new[] { WorkState.Starting, WorkState.Working, WorkState.CheckingItsWork }), "ice means the work moves");
        Assert.That(States.Any(state => StateLanguage.Look(state).Tone == GlazeTone.Accent), Is.False, "the accent is for acting, never a state");
    }

    [Test]
    public void ALastKnownBadgeKeepsItsWordGhostsAndStandsStill()
    {
        var badge = StateLanguage.BadgeOf(Character(CharacterActivity.Working, stale: true));
        Assert.That(badge.Word, Is.EqualTo("Working"));
        Assert.That(badge.LastKnown, Is.True);
        Assert.That(badge.Icon, Is.EqualTo(GlazeIcon.LastKnown));
        Assert.That(badge.Turns, Is.False);
        var waiting = StateLanguage.BadgeOf(Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, stale: true));
        Assert.That(waiting.Breathes, Is.False, "nothing breathes on stale data");
    }

    [Test]
    public void WaitingForYouCountsWhatWaitsWhenMoreThanOne()
    {
        Assert.That(StateLanguage.BadgeOf(Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, new[] { "a" }, approvals: 1)).Text, Is.EqualTo("Waiting for you"));
        Assert.That(StateLanguage.BadgeOf(Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, new[] { "a", "b" }, approvals: 2)).Text, Is.EqualTo("Waiting for you · 2"));
        Assert.That(StateLanguage.BadgeOf(Character(CharacterActivity.Working, notes: new[] { "a", "b" })).Count, Is.EqualTo(0), "only Waiting for you counts");
    }

    [Test]
    public void MarksSayWhereTheWorkComesFromBesideTheStateNeverInIt()
    {
        Assert.That(StateLanguage.MarksOf(Character(CharacterActivity.Working)), Is.Empty);
        Assert.That(StateLanguage.MarksOf(Character(CharacterActivity.Working, synthetic: true)).Single().Word, Is.EqualTo("Practice"));
        Assert.That(StateLanguage.MarksOf(Character(CharacterActivity.Working, synthetic: true, recorded: true)).Single().Word, Is.EqualTo("Demo"));
        Assert.That(StateLanguage.MarksOf(Character(CharacterActivity.Working, recorded: true)).Single().Word, Is.EqualTo("Recorded"));
        Assert.That(StateLanguage.BadgeOf(Character(CharacterActivity.Working, synthetic: true, recorded: true)).Word, Is.EqualTo("Working"));
    }

    [Test]
    public void EveryPresentationsStatusUsesTheSameWords()
    {
        Assert.That(CharacterPresenter.LabelOf(CharacterActivity.Verifying), Is.EqualTo("Checking its work"));
        Assert.That(CharacterPresenter.LabelOf(CharacterActivity.Unknown), Is.EqualTo("Can't tell yet"));
        Assert.That(CharacterPresenter.LabelOf(CharacterActivity.Failed), Is.EqualTo("Couldn't finish"));
    }
}

/// <summary>The label keeps the title, the badge and the marks apart; the reason lives in the peek.</summary>
public class CharacterLabelTests
{
    private static WorkspacePresentation Workspace(CharacterPresentation character, params ActivityEntry[] activity) =>
        new(character, null, null, null, new WorkspaceAction[0], new WorkspaceAction[0], new CommandFeedback[0], activity);

    private static ActivityEntry Entry(long position, ActivityKind kind, string text, bool reported = false) =>
        new(position, "2026-10-01T09:00:00.000Z", kind, text, reported);

    [Test]
    public void TheLabelHasATitleABadgeAndMarksAndNoReason()
    {
        var character = StateLanguageTests.Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired,
            new[] { "It wants to run: Run the migration" }, approvals: 1, synthetic: true);
        var label = CharacterLabel.Of(character);
        Assert.That(label.Title, Is.EqualTo("Fix the authentication regression"));
        Assert.That(label.Badge.Text, Is.EqualTo("Waiting for you"));
        Assert.That(label.Marks.Single().Word, Is.EqualTo("Practice"));
    }

    [Test]
    public void ThePeekGivesTheReasonWithoutRepeatingTheState()
    {
        var character = StateLanguageTests.Character(CharacterActivity.Failed, AttentionLevel.Notice,
            new[] { "Couldn't finish: The model provider rejected the request." }, details: new[] { "The model provider rejected the request." });
        var peek = PeekCard.Of(Workspace(character));
        Assert.That(peek.Badge.Word, Is.EqualTo("Couldn't finish"));
        Assert.That(peek.Reason, Is.EqualTo("The model provider rejected the request."));
        Assert.That(peek.Next, Is.EqualTo("Open it to see why."));
    }

    [Test]
    public void AReasonWithNothingMoreToSayLeavesTheBadgeToSayIt()
    {
        var character = StateLanguageTests.Character(CharacterActivity.Failed, AttentionLevel.Notice,
            new[] { "It couldn't finish." }, details: new[] { "" });
        var peek = PeekCard.Of(Workspace(character, Entry(1, ActivityKind.Tool, "bash failed")));
        Assert.That(peek.Reason, Is.Empty, "not the latest activity, which would read as the reason");
    }

    [Test]
    public void WithNothingWaitingThePeekSaysWhatItDidLast()
    {
        var character = StateLanguageTests.Character(CharacterActivity.Working);
        var peek = PeekCard.Of(Workspace(character,
            Entry(1, ActivityKind.Tool, "bash succeeded"),
            Entry(2, ActivityKind.Message, "The migration ran\nand the tests pass.", reported: true),
            Entry(3, ActivityKind.Turn, "Turn started")));
        Assert.That(peek.Reason, Is.EqualTo("It says: “The migration ran and the tests pass.”"), "round boundaries skipped; agent text a claim");
        Assert.That(peek.Next, Is.Null);
        Assert.That(PeekCard.Of(Workspace(character)).Reason, Is.Empty, "nothing known yet: the badge says it all");
    }

    [Test]
    public void ThePeekCountsTheReasonsBehindTheFirst()
    {
        var character = StateLanguageTests.Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired,
            new[] { "It wants to run: Run the migration", "Asks you: Which colour?" }, approvals: 1);
        var peek = PeekCard.Of(Workspace(character));
        Assert.That(peek.ReasonLine, Is.EqualTo("It wants to run: Run the migration (+1 more)"));
        Assert.That(peek.Next, Is.EqualTo("Open it to answer."));
    }

    [Test]
    public void ALastKnownPeekOffersNothingToDo()
    {
        var character = StateLanguageTests.Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired,
            new[] { "It wants your approval." }, approvals: 1, stale: true);
        var peek = PeekCard.Of(Workspace(character));
        Assert.That(peek.Badge.LastKnown, Is.True);
        Assert.That(peek.Next, Is.Null);
        Assert.That(peek.ReasonLine, Is.EqualTo("Last known: It wants your approval."), "the words say what the ghosted badge shows");
        Assert.That(PeekCard.Of(Workspace(StateLanguageTests.Character(CharacterActivity.Working, stale: true))).ReasonLine, Is.EqualTo("Last known."));
    }

    [Test]
    public void APeekSaysWhatOpeningIsForAndWhatItsMarkMeans()
    {
        var finished = PeekCard.Of(Workspace(StateLanguageTests.Character(CharacterActivity.TurnFinished, synthetic: true)));
        Assert.That(finished.Next, Is.EqualTo("Open it to see what changed."));
        Assert.That(finished.MarkLine, Is.EqualTo("Practice run: nothing is built."));
        var checks = PeekCard.Of(Workspace(StateLanguageTests.Character(CharacterActivity.TurnFinished, AttentionLevel.Notice,
            new[] { "Checks: 1 failed, 23 passed" }, details: new[] { "1 failed, 23 passed" })));
        Assert.That(checks.Badge.Word, Is.EqualTo("Checks failed"));
        Assert.That(checks.Reason, Is.EqualTo("1 failed, 23 passed"));
        Assert.That(checks.Next, Is.EqualTo("Open it to see what failed."));
        var demo = PeekCard.Of(Workspace(StateLanguageTests.Character(CharacterActivity.Working, synthetic: true, recorded: true)));
        Assert.That(demo.MarkLine, Is.EqualTo("Demo: recorded, nothing reaches an agent."));
        Assert.That(demo.Next, Is.Null, "nothing to open for while it works");
        Assert.That(PeekCard.Of(Workspace(StateLanguageTests.Character(CharacterActivity.Working, recorded: true))).MarkLine, Is.EqualTo("Recorded: a replay, not live work."));
        Assert.That(PeekCard.Of(Workspace(StateLanguageTests.Character(CharacterActivity.Working))).MarkLine, Is.Null);
    }

    [Test]
    public void PeeksAreEqualWhenTheyShowTheSame()
    {
        var character = StateLanguageTests.Character(CharacterActivity.Working);
        Assert.That(PeekCard.Of(Workspace(character)), Is.EqualTo(PeekCard.Of(Workspace(character))));
        Assert.That(PeekCard.Of(Workspace(character)), Is.Not.EqualTo(PeekCard.Of(Workspace(StateLanguageTests.Character(CharacterActivity.Interrupted)))));
    }

    [Test]
    public void ThePresenterGivesReasonsWithoutTheirStateWords()
    {
        var failed = Samples.Execution("e1", "w1", ExecutionStatus.Failed);
        failed.StatusReason = new ErrorInfo { Code = "runtime_error", Message = "The model provider returned an error." };
        var workstream = Samples.Workstream("w1", WorkstreamStatus.Failed, "e1", AttentionLevel.Notice, new ExecutionFailedReason { ExecutionId = "e1" });
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { workstream }, new[] { failed }, Samples.Journal()), new StateChanges());
        var character = CharacterPresenter.Present(workstream, state, live: true);
        Assert.That(character.StatusLabel, Is.EqualTo("Couldn't finish"));
        Assert.That(character.AttentionNotes, Is.EqualTo(new[] { "Couldn't finish: The model provider returned an error." }), "the workspace's line keeps the state's words");
        Assert.That(character.AttentionDetails, Is.EqualTo(new[] { "The model provider returned an error." }));
    }
}
