using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The room's moments (ADR 0040) say only what is true: what the runtime reports stays its report, what the computer
/// saw says so, who ran a check is named, and nothing is offered before what it needs exists.
/// </summary>
public class KitMomentTests
{
    private const string Codex = "Codex";
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 10, 29, 10, 42, 0, TimeSpan.Zero);

    private static string Text(LineWords words) => words.Text;

    [Test]
    public void ASentRequestWaitsAndNeverSaysItStarted()
    {
        var sent = KitMoment.Sent(Codex);
        Assert.That(sent.Provenance, Is.EqualTo(KitProvenance.Observed));
        Assert.That(sent.Source, Is.EqualTo("Sent to Codex"));
        Assert.That(Text(sent.Headline), Is.EqualTo("Waiting for it to start."));
        Assert.That(sent.Source + Text(sent.Headline), Does.Not.Contain("Started").And.Not.Contain("Starting"), "accepted is not started");
    }

    [Test]
    public void TheRuntimesEditIsItsReportUntilTheComputerSeesTheFile()
    {
        var edit = KitMoment.ReportedEdit(Codex, KitFileChange.Changed, "src/towers.ts", At);
        Assert.That(edit.Provenance, Is.EqualTo(KitProvenance.Reported));
        Assert.That(edit.Source, Is.EqualTo("Codex says"));
        Assert.That(edit.Headline.ToString(), Is.EqualTo("Edited [src/towers.ts]"), "past tense, the file theirs");
        Assert.That(edit.Fact, Is.EqualTo("Not seen on your computer yet."));
        Assert.That(edit.Tone, Is.EqualTo(KitMomentTone.Neutral));
    }

    [Test]
    public void TheComputersReadingConfirmsTheFileNeverItsLines()
    {
        var seen = KitMoment.ObservedChange(KitFileChange.Changed, "src/towers.ts", 41, 54, At);
        Assert.That(seen.Provenance, Is.EqualTo(KitProvenance.Observed));
        Assert.That(seen.Source, Is.EqualTo("Seen on your computer"));
        Assert.That(seen.Headline.ToString(), Is.EqualTo("Changed [src/towers.ts], 41 to 54 lines, seen 10:42"));
        Assert.That(seen.Diff, Is.Null, "the computer keeps no contents, so no lines are claimed");
        Assert.That(KitMoment.ObservedChange(KitFileChange.Created, "src/wobble.ts", null, 12, At).Headline.ToString(), Is.EqualTo("Made [src/wobble.ts], 12 lines, seen 10:42"));
        Assert.That(KitMoment.ObservedChange(KitFileChange.Deleted, "src/old.ts", 9, null, At).Headline.ToString(), Is.EqualTo("Removed [src/old.ts], seen 10:42"));
    }

    [Test]
    public void AddedAndRemovedLinesOnlyFromACheckpointDiffWithUndoOn()
    {
        var diff = new KitCheckpointDiff(14, 3);
        var off = KitMoment.ObservedChange(KitFileChange.Changed, "src/towers.ts", 41, 54, At, diff, undoOn: false);
        var on = KitMoment.ObservedChange(KitFileChange.Changed, "src/towers.ts", 41, 54, At, diff, undoOn: true);
        Assert.That(off.Diff, Is.Null, "with undo off there is no checkpoint diff to show");
        Assert.That(off.Undoable, Is.False);
        Assert.That(on.Diff, Is.SameAs(diff));
        Assert.That(on.Undoable, Is.True);
        Assert.That(KitMomentWords.Counts(diff), Is.EqualTo("+14 −3 lines"));
    }

    [Test]
    public void TheAgentsWordsAreTheirsAndReported()
    {
        var says = KitMoment.AgentSays(Codex, "I'll import Body from matter-js.");
        Assert.That(says.Provenance, Is.EqualTo(KitProvenance.Reported));
        Assert.That(says.Source, Is.EqualTo("Codex says"));
        Assert.That(says.Headline.Runs.All(run => run.Theirs), Is.True);
    }

    [Test]
    public void AnApprovalCarriesTheSandboxLineInTheFormTheRuntimeDeclares()
    {
        var leaves = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.MayLeave, complete: true, At);
        var stays = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.StaysInside, complete: true, At);
        var silent = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.NotDeclared, complete: true, At);
        Assert.That(leaves.Lines, Does.Contain("If you approve, it may run outside its sandbox, with your access to your computer."));
        Assert.That(stays.Lines, Does.Contain("If you approve, it still runs inside a sandbox."));
        Assert.That(silent.Lines.Any(line => line.StartsWith("If you approve", StringComparison.Ordinal)), Is.False, "nothing claimed where nothing was declared");
        Assert.That(leaves.Lines.Last(), Is.EqualTo("Nothing runs until you answer."));
        Assert.That(leaves.Source, Is.EqualTo("Codex asks"));
        Assert.That(leaves.Tone, Is.EqualTo(KitMomentTone.Attention));
        Assert.That(leaves.Headline.ToString(), Is.EqualTo("Run this command? [npm install matter-js]"));
    }

    [Test]
    public void AnIncompleteRequestCanOnlyBeRefused()
    {
        var whole = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.MayLeave, complete: true, At);
        var cut = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.MayLeave, complete: false, At);
        Assert.That(whole.Actions, Is.EqualTo(new[] { KitMomentAction.YesRunIt, KitMomentAction.No, KitMomentAction.SaySomethingElse }));
        Assert.That(cut.Actions, Does.Not.Contain(KitMomentAction.YesRunIt));
        Assert.That(cut.Actions, Does.Contain(KitMomentAction.No));
    }

    [Test]
    public void OnlyTheHostsOwnCheckIsCheckedByYourComputer()
    {
        var host = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, At);
        var agent = KitMoment.AgentCheck(Codex, "npm run build", "exit 1", At);
        Assert.That(host.Provenance, Is.EqualTo(KitProvenance.Observed));
        Assert.That(host.Source, Is.EqualTo("Checked by your computer"));
        Assert.That(host.Tone, Is.EqualTo(KitMomentTone.Failure));
        Assert.That(agent.Provenance, Is.EqualTo(KitProvenance.Reported));
        Assert.That(agent.Source, Is.EqualTo("Codex ran"));
        Assert.That(agent.Source + agent.Headline, Does.Not.Contain("your computer"));
        Assert.That(agent.Headline.ToString(), Is.EqualTo("[npm run build: exit 1]"), "a separator between two of theirs stays theirs");
        Assert.That(agent.Tone, Is.EqualTo(KitMomentTone.Neutral), "colour is for what the computer knows; the agent's outcome is its words");
    }

    [Test]
    public void ACheckIsUnderWayOnlyBeforeItsExitAndUnknownWhenItEndedWithoutOne()
    {
        var running = KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: null, At);
        var unknown = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: null, At);
        var passed = KitMoment.HostCheck(KitCheckScope.BuildsAndOpens, ended: true, passed: true, At);
        Assert.That(Text(running.Headline), Is.EqualTo("Checking it builds"));
        Assert.That(Text(unknown.Headline), Is.EqualTo("Ran a check: result not known"));
        Assert.That(Text(passed.Headline), Is.EqualTo("Checked it builds and opens: passed"));
        Assert.That(passed.Tone, Is.EqualTo(KitMomentTone.Passed));
        Assert.That(unknown.Tone, Is.EqualTo(KitMomentTone.Neutral));
        Assert.Throws<ArgumentException>(() => KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: true, At), "an outcome needs an exit");
    }

    [Test]
    public void ReadySaysOnlyWhatTheCheckThatRanShowed()
    {
        var builds = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At);
        var opens = KitMoment.HostCheck(KitCheckScope.BuildsAndOpens, ended: true, passed: true, At);
        Assert.That(KitMoment.Ready("Tower Toss", TimeSpan.FromSeconds(400), builds, tryRoute: true).Lines.Single(), Is.EqualTo("Built in 6 min 40 s, checked it builds."));
        Assert.That(KitMoment.Ready("Tower Toss", TimeSpan.FromSeconds(400), opens, tryRoute: true).Lines.Single(), Is.EqualTo("Built in 6 min 40 s, checked it builds and opens."));
        Assert.That(KitMoment.Ready("Tower Toss", null, builds, tryRoute: true).Lines.Single(), Is.EqualTo("Built, checked it builds."));
        Assert.That(KitMoment.Ready("Tower Toss", null, builds, tryRoute: true).Headline.ToString(), Is.EqualTo("[Tower Toss] is ready"));
    }

    [Test]
    public void TryItOnlyAfterAPassedHostCheckAndWithARoute()
    {
        var passed = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At);
        var failed = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, At);
        var agent = KitMoment.AgentCheck(Codex, "npm run build", "exit 0", At);
        Assert.That(KitMoment.Ready("Tower Toss", null, passed, tryRoute: true).Actions, Does.Contain(KitMomentAction.TryIt));
        Assert.That(KitMoment.Ready("Tower Toss", null, passed, tryRoute: false).Actions, Does.Not.Contain(KitMomentAction.TryIt));
        Assert.Throws<ArgumentException>(() => KitMoment.Ready("Tower Toss", null, failed, tryRoute: true));
        Assert.Throws<ArgumentException>(() => KitMoment.Ready("Tower Toss", null, agent, tryRoute: true), "the agent's own check is its report");
    }

    [Test]
    public void ASkippedReadingIsSaidNeverReadAsNoChanges()
    {
        var skipped = KitMoment.NotObserved("too many files", At);
        Assert.That(skipped.Source, Is.EqualTo("Skipped by your computer"));
        Assert.That(Text(skipped.Headline), Is.EqualTo("Not observed: too many files"));
        Assert.That(Text(skipped.Headline), Does.Not.Contain("No change").IgnoreCase);
    }

    [Test]
    public void ARefusedPackageSaysTheInstallDidNotRun()
    {
        var refused = KitMoment.PackageRefused("matter-js", At);
        Assert.That(refused.Fact, Is.EqualTo("The install didn't run"));
        Assert.That(refused.Headline.ToString(), Is.EqualTo("Wanted [matter-js], which isn't in this starter"));
    }

    [Test]
    public void EarlierCountsOnlyTheMomentsStackedBehindTheShownOne()
    {
        var one = new KitMomentSlot(new[] { KitMoment.Sent(Codex) }, working: true);
        Assert.That(one.Earlier, Is.Null);
        var five = new KitMomentSlot(new[]
        {
            KitMoment.Sent(Codex), KitMoment.AgentSays(Codex, "Looking at the towers."),
            KitMoment.ReportedEdit(Codex, KitFileChange.Created, "src/wobble.ts", At),
            KitMoment.ReportedEdit(Codex, KitFileChange.Changed, "src/towers.ts", At),
            KitMoment.ObservedChange(KitFileChange.Changed, "src/towers.ts", 41, 54, At),
        }, working: true);
        Assert.That(five.Shown, Is.EqualTo(4));
        Assert.That(five.Earlier, Is.EqualTo("Earlier: 4 more"));
    }

    [Test]
    public void AnUnansweredQuestionStaysTheShownMoment()
    {
        var question = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.MayLeave, complete: true, At);
        var slot = new KitMomentSlot(new[] { KitMoment.Sent(Codex), question, KitMoment.AgentSays(Codex, "Waiting.") }, working: true);
        Assert.That(slot.Shown, Is.EqualTo(1));
        Assert.That(slot.Earlier, Is.EqualTo("Earlier: 1 more"), "only the moments before the shown one are earlier");
        Assert.That(slot.Newer, Is.EqualTo("1 newer"), "what came after a waiting question is said apart");
        var plain = new KitMomentSlot(new[] { KitMoment.Sent(Codex), KitMoment.AgentSays(Codex, "Looking.") }, working: true);
        Assert.That(plain.Newer, Is.Null, "nothing is newer than the newest");
    }

    [Test]
    public void StopStandsOnlyWhileTheWorkGoesOn()
    {
        var edit = KitMoment.ReportedEdit(Codex, KitFileChange.Changed, "src/towers.ts", At);
        Assert.That(new KitMomentSlot(new[] { edit }, working: true).Actions, Does.Contain(KitMomentAction.Stop));
        Assert.That(new KitMomentSlot(new[] { edit }, working: false).Actions, Does.Not.Contain(KitMomentAction.Stop));
        Assert.That(new KitMomentSlot(new[] { edit }, working: true).Actions.First(), Is.EqualTo(KitMomentAction.ReadIt));
    }

    [Test]
    public void EveryActionHasItsWords()
    {
        foreach (KitMomentAction action in Enum.GetValues(typeof(KitMomentAction)))
            Assert.That(KitMomentWords.Action(action), Is.Not.Empty, action.ToString());
        Assert.That(KitMomentWords.Action(KitMomentAction.YesRunIt), Is.EqualTo("Yes, run it"));
        Assert.That(KitMomentWords.Action(KitMomentAction.TryIt), Is.EqualTo("Try it"));
    }
}
