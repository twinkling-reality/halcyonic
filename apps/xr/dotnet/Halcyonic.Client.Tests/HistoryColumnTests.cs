using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// History and Restore (gap 2; FIELD's A14 states in A17's card language): the versions newest first and named from the
/// person's tasks, a review's files a page at a time, Restore behind its lock on the first part carrying the digest of
/// the review shown, and sent, done with Undo Restore, incomplete, out of date, refused and not set up, every state a
/// card with no fact joined to another by a dot.
/// </summary>
public class HistoryColumnTests
{
    private const string ProjectId = "01a19000-0000-7000-8000-000000000001";
    private const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 18, 50, 0, TimeSpan.Zero);

    private static string Id(int n) => $"01a19000-0000-7000-8000-{n:x12}";

    private static CheckpointView Checkpoint(int n, CheckpointReason reason, string at, string? execution = null) => new()
    {
        CheckpointId = Id(n),
        ProjectId = ProjectId,
        Reason = reason,
        ExecutionId = execution,
        RecordedAt = at,
        Tree = new string('b', 64),
        Files = 12,
    };

    private static CheckpointsResponse Listed(params CheckpointView[] checkpoints) =>
        new() { ProjectId = ProjectId, Result = new AvailableCheckpoints { Checkpoints = checkpoints.ToList() } };

    private static PathList Paths(int total, params string[] paths) => new() { Paths = paths.ToList(), Total = total };

    private static RestorePreviewResponse Preview(string checkpointId, PathList? changed = null, PathList? removed = null, PathList? returned = null,
        bool? headMoved = false, string review = Digest) =>
        new()
        {
            ProjectId = ProjectId,
            Result = new AvailableRestorePreview
            {
                Preview = new RestorePreview
                {
                    CheckpointId = checkpointId,
                    ChangedBack = changed ?? Paths(2, "src/tail.ts", "src/kite.ts"),
                    Removed = removed ?? Paths(1, "src/new.ts"),
                    Returned = returned ?? Paths(1, "src/old.ts"),
                    ProjectHeadMoved = headMoved,
                },
                Review = review,
            },
        };

    /// <summary>Two versions of one task: before it started (18:12) and after it worked (18:40).</summary>
    private static HistoryColumn Column(out ClientProjection state)
    {
        var task = Samples.Workstream("w-tail");
        task.Title = "Tail";
        var execution = Samples.Execution("e-tail", task.WorkstreamId, ExecutionStatus.Completed);
        var snapshot = Samples.Snapshot(1, new[] { task }, new[] { execution });
        state = new ClientProjection();
        state.ApplySnapshot(snapshot, new StateChanges());
        var column = new HistoryColumn(ProjectId, "Kite Run");
        column.Listed(Listed(
            Checkpoint(1, CheckpointReason.BeforeTask, "2026-11-11T18:12:00.000Z", execution.ExecutionId),
            Checkpoint(2, CheckpointReason.AfterTurn, "2026-11-11T18:40:00.000Z", execution.ExecutionId)), state, Now, TimeZoneInfo.Utc);
        return column;
    }

    private static void NoDots(HistoryColumn column)
    {
        var card = column.Card();
        if (card == null) Assert.That(column.Sentence, Is.Not.Null.And.Not.Contain("·"));
        else Assert.That(card.AnyDot, Is.False, string.Join(" | ", card.Words()));
    }

    [Test]
    public void TheVersionsStandNewestFirstNamedFromThePersonsTasks()
    {
        var column = Column(out _);
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Versions));
        var card = column.Card()!;
        Assert.That(card.Title!.Text, Is.EqualTo("History"));
        Assert.That(card.Description!.Text, Is.EqualTo("Pick a moment to restore."));
        Assert.That(card.List.Select(row => row.Name.Text), Is.EqualTo(new[] { "After Tail Worked", "Before Tail Started" }));
        Assert.That(card.List.Select(row => row.Note), Is.EqualTo(new[] { "18:40", "18:12" }));
        Assert.That(card.List.Select(row => row.Kind), Is.EqualTo(new[] { RoomKind.Worked, RoomKind.Started }));
        Assert.That(card.List[0].Name.Runs.Select(run => run.Theirs), Is.EqualTo(new[] { false, true, false }), "the task's name is the person's, so it leans");
        Assert.That(column.Actions().Where(action => action.Kind == HistoryActionKind.Version).Select(action => action.CheckpointId),
            Is.EqualTo(new[] { Id(2), Id(1) }));
        Assert.That(column.Actions().Last().Kind, Is.EqualTo(HistoryActionKind.Back));
        NoDots(column);
    }

    [Test]
    public void ManyVersionsPageAndAVersionWithNoKnownTaskStillReads()
    {
        var column = new HistoryColumn(ProjectId, "Kite Run");
        var many = Enumerable.Range(1, 7).Select(n => Checkpoint(n, CheckpointReason.AfterTurn, $"2026-11-10T1{n}:00:00.000Z", Id(900))).ToArray();
        column.Listed(Listed(many), null, Now, TimeZoneInfo.Utc);
        Assert.That(column.PageCount, Is.EqualTo(2));
        Assert.That(column.Card()!.List.Count, Is.EqualTo(RoomCard.ListAPage));
        Assert.That(column.Card()!.List[0].Name.Text, Is.EqualTo("After a Task Worked"));
        Assert.That(column.Card()!.List[0].Note, Is.EqualTo("Yesterday 17:00"));
        Assert.That(column.Card()!.List.Last().Note, Is.EqualTo("Yesterday 13:00"));
        Assert.That(column.Actions().Select(action => action.Kind), Does.Contain(HistoryActionKind.Older));
        Assert.That(column.Older(), Is.True);
        Assert.That(column.Card()!.List.Count, Is.EqualTo(2));
        Assert.That(column.Actions().Select(action => action.Kind), Does.Not.Contain(HistoryActionKind.Older));
        NoDots(column);
        Assert.That(column.Back(), Is.True, "Back on a later page shows the newer one");
        Assert.That(column.Page, Is.EqualTo(0));
        Assert.That(column.Back(), Is.False, "Back on the first page leaves History");
        Assert.That(HistoryText.When("2026-11-08T09:00:00.000Z", Now, TimeZoneInfo.Utc), Is.EqualTo("8 Nov 09:00"));
    }

    [Test]
    public void NoHistoryKeptAndNoVersionsYetAreStatusesNotErrors()
    {
        var off = new HistoryColumn(ProjectId, "Kite Run");
        off.Listed(new CheckpointsResponse { ProjectId = ProjectId, Result = new UnavailableCheckpoints { Reason = new ErrorInfo { Code = "undo_unavailable", Message = "Undo is off." } } },
            null, Now, TimeZoneInfo.Utc);
        Assert.That(off.Stage, Is.EqualTo(HistoryStage.NotSetUp));
        Assert.That(off.Card()!.Status!.Value.Title, Is.EqualTo("No History Yet"));
        Assert.That(off.Card()!.Status!.Value.Line, Is.EqualTo("The computer keeps none for this project"));
        Assert.That(off.Card()!.Words(), Does.Not.Contain("Undo is off."), "the computer's own reason is not shown as Halcyonic's words");
        NoDots(off);

        var none = new HistoryColumn(ProjectId, "Kite Run");
        none.Listed(Listed(), null, Now, TimeZoneInfo.Utc);
        Assert.That(none.Card()!.Status!.Value.Title, Is.EqualTo("No History Yet"));
        NoDots(none);

        var unread = new HistoryColumn(ProjectId, "Kite Run");
        Assert.That(unread.Card(), Is.Null);
        Assert.That(unread.Sentence, Is.EqualTo("Reading History"));
        unread.ReadFailed();
        Assert.That(unread.Card()!.Status!.Value.Title, Is.EqualTo("Not Read"));
        Assert.That(unread.Actions().First().Kind, Is.EqualTo(HistoryActionKind.TryAgain));
        NoDots(unread);
    }

    [Test]
    public void AReviewListsWhatRestoringDoesWithRestoreBehindItsLock()
    {
        var column = Column(out _);
        Assert.That(column.Choose(Id(1)), Is.True);
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Previewing));
        Assert.That(column.Sentence, Is.EqualTo("Reading History"));
        var changed = Paths(6, "a.ts", "b.ts", "c.ts", "d.ts", "e.ts", "f.ts");
        column.Previewed(Preview(Id(1), changed: changed));
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Review));
        var card = column.Card()!;
        Assert.That(card.Title!.Text, Is.EqualTo("Before Tail Started"));
        Assert.That(card.Description!.Text, Is.EqualTo("You can undo this."));
        Assert.That(card.List.Count, Is.EqualTo(RoomCard.ListAPage));
        Assert.That(card.List.All(row => row.Mono), Is.True);
        Assert.That(card.List[0].Note, Is.EqualTo("Reverts"));
        Assert.That(card.More!.Text, Is.EqualTo("And 3 More"), "eight files, five shown");
        var restore = column.Actions().First();
        Assert.That((restore.Kind, restore.Words, restore.Main, restore.Locked), Is.EqualTo((HistoryActionKind.Restore, "Restore", true, true)));
        Assert.That(column.Actions().Last().Kind, Is.EqualTo(HistoryActionKind.Cancel));
        Assert.That(column.Restore(Samples.Client, Now), Is.Null, "never sent before its lock is passed");
        NoDots(column);
        Assert.That(column.Arm(), Is.True);
        Assert.That(column.Actions().First().Locked, Is.False);

        column.Cancel();
        column.Choose(Id(1));
        column.Previewed(Preview(Id(1)));
        Assert.That(column.Card()!.List.Select(row => row.Note), Is.EqualTo(new[] { "Reverts", "Reverts", "Goes", "Comes Back" }));
        Assert.That(column.Card()!.More, Is.Null);
    }

    [Test]
    public void MoreFilesThanAPageAndAChangeSinceStandTogetherOnTheFootLine()
    {
        // AUDIT's F1: the change since then never hides that more files follow than the review shows.
        var column = Column(out _);
        column.Choose(Id(1));
        column.Previewed(Preview(Id(1), changed: Paths(6, "a.ts", "b.ts", "c.ts", "d.ts", "e.ts", "f.ts"), headMoved: true));
        Assert.That(column.Card()!.List.Count, Is.EqualTo(RoomCard.ListAPage));
        Assert.That(column.Card()!.More!.Text, Is.EqualTo("The project changed since then too. 3 more files are not shown."));
        NoDots(column);
        column.Cancel();
        column.Choose(Id(1));
        column.Previewed(Preview(Id(1), changed: Paths(4, "a.ts", "b.ts", "c.ts", "d.ts"), headMoved: true));
        Assert.That(column.Card()!.More!.Text, Is.EqualTo("The project changed since then too. 1 more file is not shown."));
    }

    [Test]
    public void TheVersionBeforeAnAddIsUndoneByUndoAdd()
    {
        var column = new HistoryColumn(ProjectId, "Kite Run");
        column.Listed(Listed(Checkpoint(4, CheckpointReason.BeforeAdd, "2026-11-11T18:30:00.000Z")), null, Now, TimeZoneInfo.Utc);
        Assert.That(column.Card()!.List.Single().Name.Text, Is.EqualTo("Before the Add"), "with no task known");
        Assert.That(column.Card()!.List.Single().Kind, Is.EqualTo(RoomKind.Started));
        column.Choose(Id(4));
        column.Previewed(Preview(Id(4)));
        Assert.That(column.Card()!.Title!.Text, Is.EqualTo("Before the Add"));
        Assert.That(column.Actions().First().Words, Is.EqualTo("Undo Add"));
    }

    [Test]
    public void ARestoreCarriesTheDigestOfTheReviewShownAndUndoRestoreReadsItsOwnReview()
    {
        var column = Column(out _);
        column.Choose(Id(1));
        column.Previewed(Preview(Id(1)));
        Assert.That(column.ReviewDigest, Is.EqualTo(Digest));
        column.Arm();
        var command = column.Restore(Samples.Client, Now);
        Assert.That(command, Is.Not.Null);
        Assert.That((command!.Payload.ProjectId, command.Payload.CheckpointId, command.Payload.Review), Is.EqualTo((ProjectId, Id(1), Digest)));
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Sending));
        Assert.That(column.SentCommandId, Is.EqualTo(command.CommandId));
        Assert.That(column.Card(), Is.Null, "sent shows only the sentence");
        Assert.That(column.Sentence, Is.EqualTo("Restoring 4 Files"));
        Assert.That(column.Actions(), Is.Empty, "nothing is pressed while it restores");
        NoDots(column);

        column.Answered(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Accepted });
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Sending), "accepted is not done: the restore's own event says so");
        column.Restored(new ProjectRestoredEvent
        {
            ProjectId = ProjectId,
            Payload = new ProjectRestoredPayload
            {
                CheckpointId = Id(1), BeforeRestoreCheckpointId = Id(3), Tree = new string('c', 64),
                ChangedBack = Paths(2), Removed = Paths(1), Returned = Paths(1),
            },
        });
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Done));
        Assert.That(column.Card()!.Status!.Value.Title, Is.EqualTo("Restored"));
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo("4 files are as they were"));
        var undo = column.Actions().First();
        Assert.That((undo.Kind, undo.Main, undo.CheckpointId), Is.EqualTo((HistoryActionKind.UndoRestore, true, Id(3))));
        Assert.That(column.Actions().Select(action => action.Kind), Is.EqualTo(new[] { HistoryActionKind.UndoRestore, HistoryActionKind.Changes, HistoryActionKind.Close }));
        NoDots(column);

        Assert.That(column.Choose(Id(3)), Is.True, "Undo Restore opens the review of the version kept just before");
        Assert.That(column.Sentence, Is.EqualTo("Reading History"));
        Assert.That(column.Reviewed!.Name.Text, Is.EqualTo("Before a Restore"));
        column.Previewed(Preview(Id(3)));
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Review));
    }

    [Test]
    public void AFolderChangedSinceTheReviewRestoresNothingAndAsksForAnotherReview()
    {
        var column = Column(out _);
        column.Choose(Id(1));
        column.Previewed(Preview(Id(1), headMoved: true));
        Assert.That(column.Card()!.More!.Text, Is.EqualTo("The project changed since then too."));
        column.Arm();
        var command = column.Restore(Samples.Client, Now)!;
        column.Answered(new CommandView
        {
            CommandId = "another", Status = CommandStatus.Rejected, Rejection = new CommandRejection { Code = RejectionCode.ReviewStale, Message = "x" },
        });
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Sending), "another command's answer changes nothing");
        column.Answered(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Rejected, Rejection = new CommandRejection { Code = RejectionCode.ReviewStale, Message = "x" },
        });
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Stale));
        Assert.That(column.Card()!.Status!.Value.Title, Is.EqualTo("Not Restored"));
        Assert.That(column.Card()!.Status!.Value.Line, Is.EqualTo("The project changed while you looked"));
        Assert.That(column.Actions().First().Kind, Is.EqualTo(HistoryActionKind.ReviewAgain));
        NoDots(column);
        Assert.That(column.Choose(Id(1)), Is.True);
        Assert.That(column.ReviewDigest, Is.Null, "a new review must be read before anything is sent");
    }

    [Test]
    public void ARefusalAndAnIncompleteRestoreSaySoInTheirOwnWords()
    {
        var refused = Column(out _);
        refused.Choose(Id(1));
        refused.Previewed(Preview(Id(1)));
        refused.Arm();
        var command = refused.Restore(Samples.Client, Now)!;
        refused.Answered(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Rejected, Rejection = new CommandRejection { Code = RejectionCode.ProjectNotFound, Message = "x" },
        });
        Assert.That(refused.Stage, Is.EqualTo(HistoryStage.Refused));
        Assert.That(refused.Card()!.Status!.Value.Line, Is.EqualTo("Nothing changed"));
        Assert.That(refused.Actions().Single().Kind, Is.EqualTo(HistoryActionKind.Close));
        NoDots(refused);

        var partial = Column(out _);
        partial.Choose(Id(1));
        partial.Previewed(Preview(Id(1)));
        partial.Arm();
        partial.Restore(Samples.Client, Now);
        partial.RestoreIncomplete(new ProjectRestoreIncompleteEvent
        {
            ProjectId = ProjectId,
            Payload = new ProjectRestoreIncompletePayload
            {
                CheckpointId = Id(1), Written = true, NotRestored = Paths(2), Failure = new ErrorInfo { Code = "x", Message = "x" },
            },
        });
        Assert.That(partial.Stage, Is.EqualTo(HistoryStage.Incomplete));
        Assert.That(partial.Card()!.Status!.Value.Line, Is.EqualTo("Some files changed: check Changes"));
        Assert.That(partial.Actions().Select(action => action.Kind), Is.EqualTo(new[] { HistoryActionKind.Changes, HistoryActionKind.Close }));
        NoDots(partial);
    }

    [Test]
    public void AnswersForAnotherProjectOrVersionChangeNothing()
    {
        var column = Column(out _);
        column.Listed(new CheckpointsResponse { ProjectId = Id(77), Result = new AvailableCheckpoints() }, null, Now, TimeZoneInfo.Utc);
        Assert.That(column.Versions.Count, Is.EqualTo(2));
        Assert.That(column.Choose(Id(99)), Is.False, "not a version listed");
        column.Choose(Id(1));
        column.Previewed(Preview(Id(2)));
        Assert.That(column.Stage, Is.EqualTo(HistoryStage.Previewing), "a review of another version is not this one's");
    }

    [Test]
    public async Task TheReaderReadsTheTwoRoutesAndRefusesAnswersForAnotherProjectOrVersion()
    {
        var asked = new List<string>();
        string versions = HalcyonicJson.Serialize(Listed(Checkpoint(1, CheckpointReason.BeforeTask, "2026-11-11T18:12:00.000Z")));
        string preview = HalcyonicJson.Serialize(Preview(Id(1)));
        var reader = new ProjectHistoryReader((path, _) =>
        {
            asked.Add(path);
            return Task.FromResult(path.EndsWith("/restore-preview", StringComparison.Ordinal) ? preview : versions);
        });
        var listed = await reader.VersionsAsync(ProjectId);
        Assert.That(((AvailableCheckpoints)listed.Result).Checkpoints.Single().CheckpointId, Is.EqualTo(Id(1)));
        var read = await reader.PreviewAsync(ProjectId, Id(1));
        Assert.That(((AvailableRestorePreview)read.Result).Review, Is.EqualTo(Digest));
        Assert.That(asked, Is.EqualTo(new[]
        {
            $"api/projects/{ProjectId}/checkpoints",
            $"api/projects/{ProjectId}/checkpoints/{Id(1)}/restore-preview",
        }));
        Assert.ThrowsAsync<FormatException>(() => reader.VersionsAsync(Id(5)));
        Assert.ThrowsAsync<FormatException>(() => reader.PreviewAsync(ProjectId, Id(2)));
        Assert.That(ProjectHistoryReader.VersionsPath("a/b"), Is.EqualTo("api/projects/a%2Fb/checkpoints"));
    }
}
