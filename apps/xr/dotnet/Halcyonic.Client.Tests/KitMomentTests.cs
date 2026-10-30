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
        Assert.That(sent.Source, Is.EqualTo("Codex · Observed"));
        Assert.That(Text(sent.Headline), Is.EqualTo("Sent · Awaiting Start"));
        Assert.That(sent.Source + Text(sent.Headline), Does.Not.Contain("Started").And.Not.Contain("Starting"), "accepted is not started");
    }

    [Test]
    public void TheRuntimesEditIsItsReportUntilTheComputerSeesTheFile()
    {
        var edit = KitMoment.ReportedEdit(Codex, KitFileChange.Changed, "src/towers.ts", At);
        Assert.That(edit.Provenance, Is.EqualTo(KitProvenance.Reported));
        Assert.That(edit.Source, Is.EqualTo("Codex · Reported"));
        Assert.That(edit.Headline.ToString(), Is.EqualTo("Edit Reported · [src/towers.ts]"), "its report, the file theirs");
        Assert.That(edit.Fact, Is.EqualTo("Not Yet Observed"));
        Assert.That(edit.Tone, Is.EqualTo(KitMomentTone.Neutral));
    }

    [Test]
    public void TheComputersReadingConfirmsTheFileNeverItsLines()
    {
        var seen = KitMoment.ObservedChange(KitFileChange.Changed, "src/towers.ts", 41, 54, At);
        Assert.That(seen.Provenance, Is.EqualTo(KitProvenance.Observed));
        Assert.That(seen.Source, Is.EqualTo("Your Computer · Observed"));
        Assert.That(seen.Headline.ToString(), Is.EqualTo("Change Observed · [src/towers.ts] · 41 to 54 Lines · 10:42"));
        Assert.That(seen.Diff, Is.Null, "the computer keeps no contents, so no lines are claimed");
        Assert.That(KitMoment.ObservedChange(KitFileChange.Created, "src/wobble.ts", null, 12, At).Headline.ToString(), Is.EqualTo("File Made · [src/wobble.ts] · 12 Lines · 10:42"));
        Assert.That(KitMoment.ObservedChange(KitFileChange.Deleted, "src/old.ts", 9, null, At).Headline.ToString(), Is.EqualTo("File Removed · [src/old.ts] · 10:42"));
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
        Assert.That(KitMomentWords.Counts(diff), Is.EqualTo("+14 −3 Lines"));
    }

    [Test]
    public void TheAgentsWordsAreTheirsAndReported()
    {
        var says = KitMoment.AgentSays(Codex, "I'll import Body from matter-js.");
        Assert.That(says.Provenance, Is.EqualTo(KitProvenance.Reported));
        Assert.That(says.Source, Is.EqualTo("Codex · Reported"));
        Assert.That(says.Headline.Runs.All(run => run.Theirs), Is.True);
    }

    [Test]
    public void AnApprovalCarriesTheSandboxLineInTheFormTheRuntimeDeclares()
    {
        var leaves = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.MayLeave, complete: true, At);
        var stays = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.StaysInside, complete: true, At);
        var silent = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.NotDeclared, complete: true, At);
        Assert.That(leaves.Lines, Is.EqualTo(new[] { "May Run Outside Sandbox · Your Access" }));
        Assert.That(stays.Lines, Is.EqualTo(new[] { "Runs Inside Sandbox" }));
        Assert.That(silent.Lines, Is.Empty, "nothing claimed where nothing was declared; the question and its locked answers say nothing runs yet");
        Assert.That(leaves.Source, Is.EqualTo("Agent · Question"), "no app's name in our words");
        Assert.That(leaves.Tone, Is.EqualTo(KitMomentTone.Attention));
        Assert.That(leaves.Headline.ToString(), Is.EqualTo("Run Command? · [npm install matter-js]"));
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
        Assert.That(host.Source, Is.EqualTo("Your Computer · Observed"));
        Assert.That(host.Tone, Is.EqualTo(KitMomentTone.Failure));
        Assert.That(agent.Provenance, Is.EqualTo(KitProvenance.Reported));
        Assert.That(agent.Source, Is.EqualTo("Codex · Reported"));
        Assert.That(agent.Source + agent.Headline, Does.Not.Contain("your computer").IgnoreCase);
        Assert.That(agent.Headline.ToString(), Is.EqualTo("Check Reported · [npm run build · exit 1]"), "a separator between two of theirs stays theirs");
        Assert.That(agent.Tone, Is.EqualTo(KitMomentTone.Neutral), "colour is for what the computer knows; the agent's outcome is its words");
    }

    [Test]
    public void ACheckIsUnderWayOnlyBeforeItsExitAndUnknownWhenItEndedWithoutOne()
    {
        var running = KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: null, At);
        var unknown = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: null, At);
        var passed = KitMoment.HostCheck(KitCheckScope.BuildsAndOpens, ended: true, passed: true, At);
        Assert.That(Text(running.Headline), Is.EqualTo("Computer Check · Builds · Under Way"), "never the agent's own Checking");
        Assert.That(Text(unknown.Headline), Is.EqualTo("Check Ran · Result Unknown"));
        Assert.That(Text(passed.Headline), Is.EqualTo("Build Passed · Builds · Opens"));
        Assert.That(Text(KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, At).Headline), Is.EqualTo("Build Failed · Builds"));
        Assert.That(Text(running.Headline), Does.Not.StartWith("Checking"), "the computer's own check never shares the agent's badge word (X17A-S1)");
        Assert.That(passed.Tone, Is.EqualTo(KitMomentTone.Passed));
        Assert.That(unknown.Tone, Is.EqualTo(KitMomentTone.Neutral));
        Assert.Throws<ArgumentException>(() => KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: true, At), "an outcome needs an exit");
    }

    [Test]
    public void ReadySaysOnlyWhatTheCheckThatRanShowed()
    {
        var builds = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At);
        var opens = KitMoment.HostCheck(KitCheckScope.BuildsAndOpens, ended: true, passed: true, At);
        Assert.That(KitMoment.Ready("Tower Toss", TimeSpan.FromSeconds(400), builds, tryRoute: true).Lines.Single(), Is.EqualTo("Builds · 6 min 40 s"));
        Assert.That(KitMoment.Ready("Tower Toss", TimeSpan.FromSeconds(400), opens, tryRoute: true).Lines.Single(), Is.EqualTo("Builds · Opens · 6 min 40 s"));
        Assert.That(KitMoment.Ready("Tower Toss", null, builds, tryRoute: true).Lines.Single(), Is.EqualTo("Builds"));
        Assert.That(KitMoment.Ready("Tower Toss", null, builds, tryRoute: true).Headline.ToString(), Is.EqualTo("Build Ready · [Tower Toss]"));
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
        Assert.That(skipped.Source, Is.EqualTo("Your Computer · Observed"));
        Assert.That(Text(skipped.Headline), Is.EqualTo("Not Observed · too many files"));
        Assert.That(Text(skipped.Headline), Does.Not.Contain("No change").IgnoreCase);
    }

    [Test]
    public void ARefusedPackageSaysTheInstallDidNotRun()
    {
        var refused = KitMoment.PackageRefused("matter-js", At);
        Assert.That(refused.Fact, Is.EqualTo("Install Not Run"));
        Assert.That(refused.Headline.ToString(), Is.EqualTo("Not in Starter · [matter-js]"));
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
        Assert.That(five.Earlier, Is.EqualTo("Earlier · 4 More"));
    }

    [Test]
    public void AnUnansweredQuestionStaysTheShownMoment()
    {
        var question = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.MayLeave, complete: true, At);
        var slot = new KitMomentSlot(new[] { KitMoment.Sent(Codex), question, KitMoment.AgentSays(Codex, "Waiting.") }, working: true);
        Assert.That(slot.Shown, Is.EqualTo(1));
        Assert.That(slot.Earlier, Is.EqualTo("Earlier · 1 More"), "only the moments before the shown one are earlier");
        Assert.That(slot.Newer, Is.EqualTo("1 Newer"), "what came after a waiting question is said apart");
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
    public void EveryMomentSplitsIntoTheCardsParts()
    {
        // The card's anatomy (the art direction's handoff, 2026-10-30): a short Title, metadata segments, a command in a well,
        // or the agent's own prose. Today's words split across the parts; what is theirs keeps its run in every part.
        string? Title(KitMoment moment) => moment.Title?.ToString();
        string[] Meta(KitMoment moment) => moment.Meta.Select(segment => segment.ToString()).ToArray();
        var sent = KitMoment.Sent(Codex);
        Assert.That((Title(sent), Meta(sent), sent.Well), Is.EqualTo(("Sent", new[] { "Awaiting Start" }, (string?)null)));
        var edit = KitMoment.ReportedEdit(Codex, KitFileChange.Changed, "src/towers.ts", At);
        Assert.That((Title(edit), Meta(edit)), Is.EqualTo(("Edit Reported", new[] { "[src/towers.ts]" })));
        var seen = KitMoment.ObservedChange(KitFileChange.Changed, "src/towers.ts", 41, 54, At);
        Assert.That((Title(seen), Meta(seen)), Is.EqualTo(("Change Observed", new[] { "[src/towers.ts]", "41 to 54 Lines", "10:42" })));
        var made = KitMoment.ObservedChange(KitFileChange.Created, "src/wobble.ts", null, 12, At);
        Assert.That((Title(made), Meta(made)), Is.EqualTo(("File Made", new[] { "[src/wobble.ts]", "12 Lines", "10:42" })));
        var gone = KitMoment.ObservedChange(KitFileChange.Deleted, "src/old.ts", 9, null, At);
        Assert.That((Title(gone), Meta(gone)), Is.EqualTo(("File Removed", new[] { "[src/old.ts]", "10:42" })));
        var says = KitMoment.AgentSays(Codex, "I'll make the towers wobble.", At);
        Assert.That(Title(says), Is.EqualTo("Agent Note"), "our Title names the moment; their words stay prose beneath it");
        Assert.That(says.Prose!.AllTheirs, Is.True, "their prose stays theirs");
        var asks = KitMoment.Question(Codex, "npm install matter-js", KitSandbox.MayLeave, complete: true, At);
        Assert.That((Title(asks), asks.Well, Meta(asks)), Is.EqualTo(("Run Command?", "npm install matter-js", new string[0])));
        var check = KitMoment.HostCheck(KitCheckScope.BuildsAndOpens, ended: true, passed: true, At);
        Assert.That((Title(check), Meta(check)), Is.EqualTo(("Build Passed", new[] { "Builds · Opens" })), "the outcome in the Title (ART's review), what it covered in the metadata");
        var unknown = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: null, At);
        Assert.That((Title(unknown), Meta(unknown)), Is.EqualTo(("Check Ran", new[] { "Result Unknown" })));
        var running = KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: null, At);
        Assert.That((Title(running), Meta(running)), Is.EqualTo(("Computer Check", new[] { "Builds · Under Way" })));
        var ran = KitMoment.AgentCheck(Codex, "npm run build", "exit 1", At);
        Assert.That((Title(ran), ran.Well, Meta(ran)), Is.EqualTo(("Check Reported", "npm run build", new[] { "[exit 1]" })), "their command in the well, their outcome theirs");
        var refused = KitMoment.PackageRefused("matter-js", At);
        Assert.That((Title(refused), Meta(refused)), Is.EqualTo(("Not in Starter", new[] { "[matter-js]" })));
        var skipped = KitMoment.NotObserved("too many files", At);
        Assert.That((Title(skipped), Meta(skipped)), Is.EqualTo(("Not Observed", new[] { "too many files" })));
        var ready = KitMoment.Ready("Tower Toss", null, KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At), tryRoute: false);
        Assert.That((Title(ready), Meta(ready)), Is.EqualTo(("Build Ready", new[] { "[Tower Toss]" })), "their project's name keeps its run in the metadata");
        // Every moment has something to read: a Title, their prose or a command.
        foreach (var moment in new[] { sent, edit, seen, made, gone, says, asks, check, ran, refused, skipped, ready })
            Assert.That(moment.Title != null || moment.Prose != null || moment.Well != null, Is.True, moment.Kind.ToString());
    }

    [Test]
    public void EveryActionHasItsWords()
    {
        foreach (KitMomentAction action in Enum.GetValues(typeof(KitMomentAction)))
            Assert.That(KitMomentWords.Action(action), Is.Not.Empty, action.ToString());
        Assert.That(KitMomentWords.Action(KitMomentAction.YesRunIt), Is.EqualTo("Run"));
        Assert.That(KitMomentWords.Action(KitMomentAction.TryIt), Is.EqualTo("Try Build"));
    }
}
