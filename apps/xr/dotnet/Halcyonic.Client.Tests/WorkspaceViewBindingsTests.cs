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
        Surface = file.Surface, Section = (WorkspaceSection)file.Screen.Section, Page = file.Screen.Page,
        Instruction = file.ContinuationDraft(), Request = file.ContinuationRequest(),
    };
    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void AnOldDrawnRequestEntryAfterANewSnapshotCannotRetargetBeforeTick(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var work = Samples.Workstream(TaskId, WorkstreamStatus.Completed, RunA);
        var execution = Samples.Execution(RunA, TaskId, ExecutionStatus.Completed);
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { work }, new[] { execution }), new StateChanges());
        var file = new FileColumn(host, () => WorkspacePresenter.Present(work, state, new ActivityLog(), live: true),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        WorkspaceViewBindings bindings = null!;
        bindings = new WorkspaceViewBindings(Record, () => bindings.Capture());
        bindings.Bind(file, Record(file));
        var opened = 0; WorkspaceRequestColumn? request = null;
        file.OfferWorkspaceRequests(() =>
        {
            opened++;
            var context = bindings.Captured(file)!;
            file.Surface = WorkspaceSurface.Request;
            request = file.RequestFor(context, () => file.Now?.Execution?.ExecutionId == context.ExecutionId);
        });
        file.Drawn(file.Frame!, null); file.Act(FileScreens.OpenWorkspaceRequest, null);
        Assert.That(opened, Is.EqualTo(1));
        request!.Drawn(request.Frame!, null); request.Act(WorkspaceRequestColumn.Edit, null);
        host.Keyboard!.Value.Done("Latest A request must stay on A");
        file.Drawn(file.Frame!, null);
        work.CurrentExecutionId = RunB;
        execution = Samples.Execution(RunB, TaskId, ExecutionStatus.Completed);
        state.ApplySnapshot(Samples.Snapshot(2, new[] { work }, new[] { execution }), new StateChanges());
        // Deliberately invoke the old drawn entry before the normal periodic Tick.
        file.Act(FileScreens.OpenWorkspaceRequest, null);
        Assert.That(opened, Is.EqualTo(1), "the old physical draw grants no opening on the new execution");
        Assert.That(file.RequestView, Is.Null);
        Assert.That(file.ContinuationRequest(), Is.Null);
        var records = bindings.Capture();
        Assert.That(records.Single(record => record.ExecutionId == RunA).Request!.Text, Is.EqualTo("Latest A request must stay on A"));
        Assert.That(records.Single(record => record.ExecutionId == RunB).Request, Is.Null);
        file.Drawn(file.Frame!, null); file.Act(FileScreens.OpenWorkspaceRequest, null);
        Assert.That((opened, request!.ExecutionId, request.Draft.Text, request.RequestId), Is.EqualTo((2, RunB, "", (string?)null)));
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void RequestRetirementPreservesLatestUnsavedAAndCreatesIndependentB(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var work = Samples.Workstream(TaskId, WorkstreamStatus.Completed, RunA);
        var execution = Samples.Execution(RunA, TaskId, ExecutionStatus.Completed);
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { work }, new[] { execution }), new StateChanges());
        var file = new FileColumn(host, () => WorkspacePresenter.Present(work, state, new ActivityLog(), live: true),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        var record = Record(file); record.Request = new WorkspaceRequestContinuation { Text = "Earlier A request" }; record.Surface = WorkspaceSurface.Request;
        file.RestoreContinuation(record);
        var request = file.RequestFor(record, () => true);
        var write = true;
        var recovery = new WorkspaceViewRecovery(() => (true, null), _ => write);
        WorkspaceViewBindings bindings = null!;
        bindings = new WorkspaceViewBindings(Record, () => recovery.Keep(bindings.Capture(), null));
        bindings.Bind(file, record); recovery.Keep(bindings.Capture(), null);
        write = false;
        request.Restore(new WorkspaceRequestContinuation { Text = "Latest A request after private write failed", Spoken = true });
        Assert.That(recovery.Views.Single().Request!.Text, Is.EqualTo("Earlier A request"));
        work.CurrentExecutionId = RunB;
        execution = Samples.Execution(RunB, TaskId, ExecutionStatus.Completed);
        state.ApplySnapshot(Samples.Snapshot(2, new[] { work }, new[] { execution }), new StateChanges());
        host.Wait(2); file.Tick();
        var records = bindings.Capture();
        var old = records.Single(view => view.ExecutionId == RunA);
        Assert.That((old.Request!.Text, old.Request.Spoken, old.Surface), Is.EqualTo(("Latest A request after private write failed", true, WorkspaceSurface.Request)));
        Assert.That(recovery.IsSaved(old), Is.False);
        var fresh = records.Single(view => view.ExecutionId == RunB);
        Assert.That((fresh.Request, fresh.Surface), Is.EqualTo(((WorkspaceRequestContinuation?)null, WorkspaceSurface.Task)));
        Assert.That(fresh.HandoffId, Is.Not.EqualTo(old.HandoffId));
        old.Request.Text = "Mutate the returned copy";
        Assert.That(bindings.Capture().Single(view => view.ExecutionId == RunA).Request!.Text, Is.EqualTo("Latest A request after private write failed"));
        Assert.That(host.Sent, Is.Empty);
    }

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
