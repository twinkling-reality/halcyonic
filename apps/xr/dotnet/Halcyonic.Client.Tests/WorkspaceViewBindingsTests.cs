using System;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspaceViewBindingsTests
{
    private const string TaskId = "0192f1a0-0000-7000-8000-000000000101";
    private const string RunA = "0192f1a0-0000-7000-8000-000000000102";
    private const string RunB = "0192f1a0-0000-7000-8000-000000000103";
    private static WorkspaceContinuation Record(FileColumn file) => new()
    {
        HandoffId = Guid.NewGuid().ToString("D"), ConnectionKey = new string('a', 64), JournalId = Samples.JournalId,
        ProjectId = Samples.ProjectId, WorkstreamId = TaskId, ExecutionId = file.Now?.Execution?.ExecutionId,
        Section = (WorkspaceSection)file.Screen.Section, Page = file.Screen.Page, Instruction = file.ContinuationDraft(),
    };
    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void FailedPersistencePreservesLatestAAndCapturesANewBWithoutRetargeting(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var work = Samples.Workstream(TaskId, WorkstreamStatus.Completed, RunA);
        var execution = Samples.Execution(RunA, TaskId, ExecutionStatus.Completed);
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { work }, new[] { execution }), new StateChanges());
        state.ApplyWelcome(Samples.Welcome(resumed: false, head: 1));
        var file = new FileColumn(host, () => WorkspacePresenter.Present(work, state, new ActivityLog(), live: true),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        var write = true;
        var recovery = new WorkspaceViewRecovery(() => (true, null), _ => write);
        WorkspaceViewBindings bindings = null!;
        bindings = new WorkspaceViewBindings(Record, () => recovery.Keep(bindings.Capture(), null));
        bindings.Bind(file, Record(file));
        void Write(string words)
        {
            for (var draw = 0; draw < 3; draw++) { file.Drawn(file.Frame!, null); host.Wait(0.5); }
            file.Act(file.Screen.Instruction.Open ? FileScreens.EditInstruction : FileScreens.TellIt, null);
            host.Keyboard!.Value.Done(words);
            Assert.That(file.Screen.Instruction.Words, Is.EqualTo(words), "the actual offered editor must take this new local draft");
        }
        Write("Earlier saved A words.");
        Assert.That(recovery.Views.Single().Instruction!.Text, Is.EqualTo("Earlier saved A words."));
        write = false;
        Write("Latest A words must survive the failed private write.");
        var oldKeyboard = host.Keyboard!.Value.Done;
        work.CurrentExecutionId = RunB;
        execution = Samples.Execution(RunB, TaskId, ExecutionStatus.Completed);
        state.ApplySnapshot(Samples.Snapshot(2, new[] { work }, new[] { execution }), new StateChanges());
        host.Wait(2); file.Tick();
        Assert.That(file.Screen.Instruction.Open, Is.False, "the old editor cannot target the new run");
        var kept = bindings.Capture();
        var earlier = kept.Single(record => record.ExecutionId == RunA);
        Assert.That(earlier.Instruction!.Text, Is.EqualTo("Latest A words must survive the failed private write."));
        Assert.That(recovery.IsSaved(earlier), Is.False, "in-memory latest words must never claim the earlier disk version is current");
        Assert.That(WorkspaceContinuity.MatchesCurrent(earlier, earlier.ConnectionKey, state), Is.False);
        Write("B is a separate ordinary draft.");
        oldKeyboard("Late overwrite from A");
        kept = bindings.Capture();
        var current = kept.Single(record => record.ExecutionId == RunB);
        Assert.That(current.HandoffId, Is.Not.EqualTo(earlier.HandoffId));
        Assert.That(current.Instruction!.Text, Is.EqualTo("B is a separate ordinary draft."));
        Assert.That(kept.Single(record => record.ExecutionId == RunA).Instruction!.Text, Is.EqualTo(earlier.Instruction!.Text));
        Assert.That(WorkspaceContinuity.MatchesCurrent(current, current.ConnectionKey, state), Is.True);
        Assert.That(recovery.Views.Single().Instruction!.Text, Is.EqualTo("Earlier saved A words."), "failed write leaves durable data untouched");
        write = true;
        Assert.That(recovery.Keep(kept, current.HandoffId), Is.True);
        Assert.That(recovery.Views.Count, Is.EqualTo(2));
        Assert.That(recovery.IsSaved(earlier), Is.True);
        Assert.That(recovery.IsSaved(current), Is.True);
        Assert.That(file.Screen.Instruction.ExecutionId, Is.EqualTo(RunB));
        Assert.That(file.Screen.Instruction.Words, Is.EqualTo(current.Instruction!.Text));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void BackgroundSelectionNeverReopensTheLatestMinimizeAndExplicitRestoreCan()
    {
        var host = new FileMenuHost();
        var places = MenuBar.Places.ToDictionary(place => place, place => (Func<IMenuColumn>)(() => new WorkspaceNoticeColumn(host, place.ToString())));
        var navigator = new MenuNavigator(places);
        var presentation = new WorkspacePresentationState();
        presentation.Suspending += () => navigator.Compact();
        presentation.Changed += () => { if (!presentation.Minimized) navigator.Expand(); };
        navigator.OpenMenu(MenuPlace.Projects);
        presentation.Minimize();
        var recovered = new WorkspaceNoticeColumn(host, "Recovered ordinary draft.");
        navigator.RecoverBeside(recovered, TaskId, presentation);
        Assert.That(presentation.Minimized, Is.True);
        Assert.That(navigator.IsCompact, Is.True);
        Assert.That(navigator.Beside, Is.SameAs(recovered));
        Assert.That(navigator.Frames(new MenuBar(MenuPlace.Projects, TasksText.Waiting(0))).Beside, Is.Null);
        navigator.RecoverMenu(MenuPlace.Projects, presentation);
        Assert.That((presentation.Minimized, navigator.IsCompact), Is.EqualTo((true, true)));
        navigator.RecoverBeside(recovered, TaskId, presentation, reopen: true);
        Assert.That((presentation.Minimized, navigator.IsCompact), Is.EqualTo((false, false)));
        Assert.That(host.Sent, Is.Empty);
    }
}
