using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Watch It Work's model (<see cref="WatchBuild"/>): what it reads and when, what a panel shows, and that it shows only what the
/// computer read (the lead's 03:03Z unit; AUDIT's MUSTs of 03:10Z; AUDIT3's W4 and H3; AUDIT2's never-shown 429).
/// </summary>
public class WatchBuildTests
{
    private static WatchBuild Watch() => new WatchBuild(line => new[] { line });

    private static ExecutionWritingView Writing(string at, params string[] newestFirst) =>
        new ExecutionWritingView { Paths = newestFirst.ToList(), PathsTotal = newestFirst.Length, TotalComplete = true, At = at };

    private static ProjectFileResponse Text(string path, string text) =>
        new ProjectFileResponse { ProjectId = "p", Path = path, Kind = ProjectFileKind.Text, SizeBytes = text.Length, Text = text, ModifiedAt = "2026-11-16T05:00:00Z" };

    private static ProjectFileResponse Kind(string path, ProjectFileKind kind, long size = 10) =>
        new ProjectFileResponse { ProjectId = "p", Path = path, Kind = kind, SizeBytes = size, Text = null, ModifiedAt = "2026-11-16T05:00:00Z" };

    [Test]
    public void OfferedOnlyOnLiveWorkWhoseTurnRunsAndWhoseFolderIsObserved()
    {
        var running = new ExecutionView { Status = ExecutionStatus.Running, Writing = Writing("t0") };
        Assert.That(WatchBuild.Offered(running, live: true), Is.True);
        Assert.That(WatchBuild.Offered(running, live: false), Is.False, "a recorded run or the demonstration holds no file text");
        Assert.That(WatchBuild.Offered(new ExecutionView { Status = ExecutionStatus.Running }, live: true), Is.False, "no observed list, nothing to show");
        Assert.That(WatchBuild.Offered(new ExecutionView { Status = ExecutionStatus.Completed, Writing = Writing("t0") }, live: true), Is.False);
        Assert.That(WatchBuild.Offered(null, live: true), Is.False);
    }

    [Test]
    public void NoPanelStandsBeforeItsFirstReadAndTheFileIsAFinishedRowUntilThen()
    {
        var watch = Watch();
        Assert.That(watch.Due(Writing("t1", "src/game.js"), now: 0), Is.EqualTo(new[] { "src/game.js" }));
        var before = watch.Arc;
        Assert.That(before.Panels, Is.Empty, "AUDIT MUST 1: never an empty panel");
        Assert.That(before.SoFar.Select(row => row.Path), Is.EqualTo(new[] { "src/game.js" }));
        Assert.That(before.SoFar[0].Lines, Is.Empty);
        watch.Read("src/game.js", Text("src/game.js", "let score = 0;\nfunction start() {}"), now: 0.2);
        var after = watch.Arc;
        Assert.That(after.Panels.Single().Lines, Is.EqualTo(new[] { "let score = 0;", "function start() {}" }));
        Assert.That(after.Panels.Single().Name.Text, Is.EqualTo("game.js"));
        Assert.That(after.Panels.Single().Folder?.Text, Is.EqualTo("src"));
    }

    [Test]
    public void AFirstReadIsUnlitAndALaterReadLightsOnlyWhatChanged()
    {
        var watch = Watch();
        watch.Due(Writing("t1", "a.js"), 0);
        watch.Read("a.js", Text("a.js", "one\ntwo\nthree"), 0.1);
        Assert.That(watch.Arc.Panels.Single().Lit, Is.Empty, "AUDIT MUST 2: a first read says nothing by its lit rows");
        Assert.That(watch.Arc.Panels.Single().New, Is.False, "only the computer's observation says a file is new");
        Assert.That(watch.Due(Writing("t2", "a.js"), 2.5), Is.EqualTo(new[] { "a.js" }));
        watch.Read("a.js", Text("a.js", "one\ntwo and more\nthree\nfour"), 2.6);
        var panel = watch.Arc.Panels.Single();
        Assert.That(panel.Lit.Select(row => panel.Lines[row]), Is.EqualTo(new[] { "two and more", "four" }));
    }

    [Test]
    public void AFileIsReadAgainOnlyWhenTheListMovedAndNeverSoonerThanItsPace()
    {
        var watch = Watch();
        watch.Due(Writing("t1", "a.js"), 0);
        watch.Read("a.js", Text("a.js", "x"), 0.1);
        Assert.That(watch.Due(Writing("t1", "a.js"), 3), Is.Empty, "the list did not move");
        Assert.That(watch.Due(Writing("t2", "a.js"), 1), Is.Empty, "sooner than every two seconds");
        Assert.That(watch.Due(Writing("t2", "a.js"), 2.2), Is.EqualTo(new[] { "a.js" }));
        Assert.That(watch.Due(Writing("t3", "a.js"), 5), Is.Empty, "a read still asked is not asked twice");
    }

    [Test]
    public void OnlyTheNewestThreeAreReadAndNeverMoreThanFourReadsASecond()
    {
        var watch = Watch();
        var due = watch.Due(Writing("t1", "e.js", "d.js", "c.js", "b.js", "a.js"), 0);
        Assert.That(due, Is.EqualTo(new[] { "e.js", "d.js", "c.js" }));
        foreach (var path in due) watch.Read(path, Text(path, path), 0.1);
        Assert.That(watch.Arc.Panels.Select(panel => panel.Path), Is.EqualTo(new[] { "c.js", "d.js", "e.js" }), "newest last");
        Assert.That(watch.Arc.SoFar.Select(row => row.Path), Is.EqualTo(new[] { "a.js", "b.js", "c.js", "d.js", "e.js" }));
        Assert.That(watch.Arc.Count, Is.EqualTo(5));

        var paced = Watch();
        paced.Due(Writing("t1", "c.js", "b.js", "a.js"), 0);
        Assert.That(paced.Due(Writing("t2", "f.js", "e.js", "d.js"), 0.5), Has.Count.EqualTo(1), "four reads in one second at most");
    }

    [Test]
    public void APaceRefusalIsNeverShownAndTheFileIsReadAgain()
    {
        var watch = Watch();
        watch.Due(Writing("t1", "a.js"), 0);
        watch.Read("a.js", Text("a.js", "kept"), 0.1);
        watch.Due(Writing("t2", "a.js"), 2.2);
        watch.Refused("a.js", new ControlPlaneRequestException("Too many reads.", "too_many_requests", 429), 2.3);
        Assert.That(watch.Arc.Panels.Single().State, Is.EqualTo(WritingState.Changing));
        Assert.That(watch.Arc.Panels.Single().Lines, Is.EqualTo(new[] { "kept" }), "it keeps what it showed");
        Assert.That(watch.Due(Writing("t2", "a.js"), 4.5), Is.EqualTo(new[] { "a.js" }), "asked again later");
        watch.Refused("a.js", new ControlPlaneRequestException("Gone.", "file_not_found", 404), 4.6);
        Assert.That(watch.Arc.Panels.Single().State, Is.EqualTo(WritingState.Unreadable));
        Assert.That(watch.Arc.Panels.Single().Lines, Is.Empty);
    }

    [Test]
    public void AFileWithoutShowableTextShowsItsNameAndWhyNeverItsContents()
    {
        var watch = Watch();
        watch.Due(Writing("t1", "logo.png", ".env", "big.json"), 0);
        watch.Read(".env", Kind(".env", ProjectFileKind.Withheld), 0.1);
        watch.Read("logo.png", Kind("logo.png", ProjectFileKind.Binary), 0.1);
        watch.Read("big.json", Kind("big.json", ProjectFileKind.TooLarge, 412_000), 0.1);
        var panels = watch.Arc.Panels.ToDictionary(panel => panel.Path);
        Assert.That(panels[".env"].State, Is.EqualTo(WritingState.KeptPrivate));
        Assert.That(panels["logo.png"].State, Is.EqualTo(WritingState.NotText));
        Assert.That(panels["big.json"].State, Is.EqualTo(WritingState.TooLarge));
        Assert.That(panels["big.json"].Size, Is.EqualTo(412_000));
        Assert.That(panels.Values.SelectMany(panel => panel.Lines), Is.Empty);
    }

    [Test]
    public void TheWindowFollowsTheNewestChangeAndHoldsTwelveRows()
    {
        var watch = Watch();
        var first = string.Join("\n", Enumerable.Range(1, 30).Select(line => "line " + line));
        watch.Due(Writing("t1", "long.js"), 0);
        watch.Read("long.js", Text("long.js", first), 0.1);
        Assert.That(watch.Arc.Panels.Single().Lines, Has.Count.EqualTo(WatchBuild.WindowRows));
        Assert.That(watch.Arc.Panels.Single().Lines[0], Is.EqualTo("line 1"), "a first read shows the file from its top");
        watch.Due(Writing("t2", "long.js"), 2.5);
        watch.Read("long.js", Text("long.js", first.Replace("line 25", "line 25 changed")), 2.6);
        var panel = watch.Arc.Panels.Single();
        Assert.That(panel.Lines.Last(), Is.EqualTo("line 25 changed"));
        Assert.That(panel.Lit.Select(row => panel.Lines[row]), Is.EqualTo(new[] { "line 25 changed" }));
    }

    [Test]
    public void AWindowCappedByTheViewCountsTheFilesOwnLinesLeftOutAboveAndBelow()
    {
        var watch = Watch();
        watch.MostRows = () => 5;
        var first = string.Join("\n", Enumerable.Range(1, 30).Select(line => "line " + line));
        watch.Due(Writing("t1", "long.js"), 0);
        watch.Read("long.js", Text("long.js", first), 0.1);
        var top = watch.Arc.Panels.Single();
        Assert.That(top.Lines, Is.EqualTo(new[] { "line 1", "line 2", "line 3", "line 4", "line 5" }));
        Assert.That((top.LinesAbove, top.LinesBelow), Is.EqualTo((0, 25)));
        watch.Due(Writing("t2", "long.js"), 2.5);
        watch.Read("long.js", Text("long.js", first.Replace("line 25", "line 25 changed")), 2.6);
        var followed = watch.Arc.Panels.Single();
        Assert.That(followed.Lines.Last(), Is.EqualTo("line 25 changed"), "the line being written is always shown");
        Assert.That(followed.Lines, Has.Count.EqualTo(5));
        Assert.That((followed.LinesAbove, followed.LinesBelow), Is.EqualTo((20, 5)));
    }

    [Test]
    public void ACappedWindowNeverStartsOrEndsInsideAWrappedLine()
    {
        var watch = new WatchBuild(line => line.Length > 4 ? new[] { line.Substring(0, 4), line.Substring(4) } : new[] { line }) { MostRows = () => 3 };
        watch.Due(Writing("t1", "a.js"), 0);
        watch.Read("a.js", Text("a.js", "abcdefgh\nij\nklmnop\nqr"), 0.1);
        var panel = watch.Arc.Panels.Single();
        // Rows: abcd efgh | ij | klmn op | qr; three rows from the top would cut "klmnop" at its foot, so the window ends above it.
        Assert.That(panel.Lines, Is.EqualTo(new[] { "abcd", "efgh", "ij" }));
        Assert.That((panel.LinesAbove, panel.LinesBelow), Is.EqualTo((0, 2)));
        watch.Due(Writing("t2", "a.js"), 2.5);
        watch.Read("a.js", Text("a.js", "abcdefgh\nij\nklmnop\nqrs!"), 2.6);
        var after = watch.Arc.Panels.Single();
        // Ending at the changed line, three rows back would start inside "klmnop": the window starts on its first row instead.
        Assert.That(after.Lines, Is.EqualTo(new[] { "klmn", "op", "qrs!" }));
        Assert.That((after.LinesAbove, after.LinesBelow), Is.EqualTo((2, 0)));
    }

    [Test]
    public void WrappedLinesLightEveryRowTheyLay()
    {
        var watch = new WatchBuild(line => line.Length > 4 ? new[] { line.Substring(0, 4), line.Substring(4) } : new[] { line });
        watch.Due(Writing("t1", "a.js"), 0);
        watch.Read("a.js", Text("a.js", "ab\ncd"), 0.1);
        watch.Due(Writing("t2", "a.js"), 2.5);
        watch.Read("a.js", Text("a.js", "ab\ncdefgh"), 2.6);
        var panel = watch.Arc.Panels.Single();
        Assert.That(panel.Lines, Is.EqualTo(new[] { "ab", "cdef", "gh" }));
        Assert.That(panel.Lit, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void TheSettleTakesTheChangesPagesOrderAndMarksOnlyObservedNewFilesNew()
    {
        var watch = Watch();
        watch.Due(Writing("t1", "src/b.js", "src/a.js"), 0);
        watch.Read("src/a.js", Text("src/a.js", "a"), 0.1);
        watch.Read("src/b.js", Text("src/b.js", "b"), 0.1);
        watch.Settle(new List<ObservedFile>
        {
            new ObservedFile("src/b.js", ObservedChange.Created, null, 1, 1),
            new ObservedFile("src/a.js", ObservedChange.Changed, 1, 1, 1),
        });
        var arc = watch.Arc;
        Assert.That(arc.SoFar.Select(row => row.Path), Is.EqualTo(new[] { "src/b.js", "src/a.js" }), "AUDIT MUST 3: the Changes page's order");
        Assert.That(arc.Panels.All(panel => panel.State == WritingState.Settled), Is.True);
        Assert.That(arc.SoFar.Single(row => row.Path == "src/b.js").New, Is.True);
        Assert.That(arc.SoFar.Single(row => row.Path == "src/a.js").New, Is.False);
        Assert.That(watch.Due(Writing("t9", "src/c.js"), 10), Is.Empty, "nothing is read after the settle");
    }

    [Test]
    public void TheLineDiffFindsInsertionsAndChangesAndGivesUpPastItsBound()
    {
        Assert.That(WatchBuild.Changed(new[] { "a", "b", "c" }, new[] { "a", "x", "b", "c", "d" }), Is.EqualTo(new[] { 1, 4 }));
        Assert.That(WatchBuild.Changed(new[] { "a", "b" }, new[] { "a" }), Is.Empty, "a deletion lights nothing that remains");
        var many = Enumerable.Range(0, WatchBuild.MostEdits + 10).Select(index => "n" + index).ToArray();
        Assert.That(WatchBuild.Changed(new[] { "old" }, many), Is.Empty, "past the bound nothing is lit rather than lit wrongly");
    }
}
