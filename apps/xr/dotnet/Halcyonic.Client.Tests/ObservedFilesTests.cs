using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The computer's own observation of an execution's folder over a turn (<c>execution.files.observed</c>, ADR 0038) is kept
/// in the client model, each file with its change kind and line counts as the contract gives them, live or read from
/// history alike, for the redesign to draw (lead 2026-11-05).
/// </summary>
public class ObservedFilesTests
{
    private const string Observed =
        "{\"turn_id\":\"t1\"," +
        "\"created\":{\"files\":[{\"path\":\"src/stars.ts\",\"lines\":42,\"bytes\":1300}],\"total\":3}," +
        "\"changed\":{\"files\":[{\"path\":\"src/game.ts\",\"lines_before\":120,\"lines_after\":131,\"bytes\":4100}],\"total\":1}," +
        "\"deleted\":{\"files\":[{\"path\":\"assets/old.png\",\"lines_before\":null}],\"total\":1}," +
        "\"not_read\":2,\"skipped\":null}";

    private const string Skipped =
        "{\"turn_id\":\"t2\",\"created\":{\"files\":[],\"total\":0},\"changed\":{\"files\":[],\"total\":0},\"deleted\":{\"files\":[],\"total\":0}," +
        "\"not_read\":0,\"skipped\":{\"code\":\"no_baseline\",\"message\":\"No earlier reading of the folder.\"}}";

    [Test]
    public void AnObservedTurnKeepsEachFileWithItsChangeAndLineCounts()
    {
        var log = new ActivityLog();
        var changed = log.Record(new[] { ActivityLogBoundTests.Journaled(5, "e1", "execution.files.observed", Observed) });

        Assert.That(changed, Does.Contain("e1"));
        var turn = log.ObservedFor("e1").Single();
        Assert.That((turn.Position, turn.TurnId, turn.Skipped), Is.EqualTo((5L, "t1", (FilesObservedSkippedCode?)null)));
        Assert.That(turn.Files.Select(file => (file.Path, file.Change, file.LinesBefore, file.LinesAfter, file.Bytes)), Is.EqualTo(new (string, ObservedChange, long?, long?, long?)[]
        {
            ("src/stars.ts", ObservedChange.Created, null, 42, 1300),
            ("src/game.ts", ObservedChange.Changed, 120, 131, 4100),
            ("assets/old.png", ObservedChange.Deleted, null, null, null),
        }));
        Assert.That((turn.CreatedTotal, turn.ChangedTotal, turn.DeletedTotal, turn.Unlisted, turn.NotRead), Is.EqualTo((3L, 1L, 1L, 2L, 2L)),
            "the totals count files the lists leave out");
        Assert.That(log.For("e1"), Is.Empty, "an observation is not an Activity line");
    }

    [Test]
    public void TurnsKeepJournalOrderWhetherLiveOrFromHistoryAndNeverTwice()
    {
        var log = new ActivityLog();
        var later = ActivityLogBoundTests.Journaled(9, "e1", "execution.files.observed", Skipped);
        var earlier = ActivityLogBoundTests.Journaled(5, "e1", "execution.files.observed", Observed);
        log.Record(new[] { later });
        log.Record(new[] { earlier, later });

        Assert.That(log.ObservedFor("e1").Select(turn => turn.Position), Is.EqualTo(new[] { 5L, 9L }));
        var skipped = log.ObservedFor("e1")[1];
        Assert.That((skipped.Skipped, skipped.Files.Count), Is.EqualTo(((FilesObservedSkippedCode?)FilesObservedSkippedCode.NoBaseline, 0)),
            "a turn the computer did not read says why, never no changes");
        Assert.That(log.ObservedFor("other"), Is.Empty);

        log.Clear();
        Assert.That(log.ObservedFor("e1"), Is.Empty);
    }
}
