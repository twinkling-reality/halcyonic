using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A file of Changes read on the headset (the parity map's gaps 5 and 10): the versions around the observed turn found in
/// the history, the file's change parsed into marked and numbered rows, wrapped and paged without a cut, the whole file
/// as it is now, every state a card with no dot between facts and every line theirs, and a way back at each step.
/// </summary>
public class FileChangeColumnTests
{
    private const string ProjectId = "01a19000-0000-7000-8000-000000000001";
    private const string JournalId = "01a19000-0000-7000-8000-0000000000aa";
    private const string Run = "01a19000-0000-7000-8000-0000000000e1";
    private const string Path = "src/game/towers.ts";

    private static string Id(int n) => $"01a19000-0000-7000-8000-{n:x12}";

    private static RoomChange Changed(string path = Path) => new(RoomChangeKind.Changed, path, 3, 4);

    private static VersionEntry Version(int n, string? turn, string? previous, string execution = Run) => new()
    {
        EntryId = Id(100 + n),
        At = "2026-11-11T14:00:00.000Z",
        Checkpoint = new CheckpointView
        {
            CheckpointId = Id(n),
            ProjectId = ProjectId,
            Reason = CheckpointReason.AfterTurn,
            ExecutionId = execution,
            RecordedAt = "2026-11-11T14:00:00.000Z",
            Tree = new string('b', 64),
            Files = 12,
            TurnId = turn,
        },
        Folder = new HistoryProjectFolder(),
        Previous = previous,
        ChangedFiles = 1,
    };

    private static NotKeptEntry NotKept(int n) => new()
    {
        EntryId = Id(100 + n),
        At = "2026-11-11T14:00:00.000Z",
        Reason = CheckpointReason.AfterTurn,
        ExecutionId = Run,
        Failure = new CheckpointFailure { Code = CheckpointFailureCode.TooLarge, Message = "Too many files." },
    };

    private static string History(bool more, params HistoryEntry[] entries) => HalcyonicJson.Serialize(new ProjectHistoryResponse
    {
        ProjectId = ProjectId,
        Result = new AvailableHistory { Entries = entries.ToList(), More = more, Total = entries.Length },
    });

    private static ProjectFileChangeResponse Change(string? diff, bool complete = true, bool binary = false, bool withheld = false, string path = Path) => new()
    {
        ProjectId = ProjectId,
        From = Id(1),
        To = Id(2),
        Path = path,
        Change = FileChangeKind.Changed,
        Binary = binary,
        Withheld = withheld,
        Diff = diff,
        Complete = complete,
    };

    private static ProjectFileResponse File(string? text, ProjectFileKind kind = ProjectFileKind.Text, string path = Path) => new()
    {
        ProjectId = ProjectId,
        Path = path,
        Kind = kind,
        SizeBytes = text?.Length ?? 300_000,
        Text = kind == ProjectFileKind.Text ? text : null,
        Lines = kind == ProjectFileKind.Text ? text?.Split('\n').Length : null,
        ModifiedAt = "2026-11-11T14:00:00.000Z",
    };

    private const string Diff =
        "diff --git a/src/game/towers.ts b/src/game/towers.ts\n" +
        "index 1111111..2222222 100644\n" +
        "--- a/src/game/towers.ts\n" +
        "+++ b/src/game/towers.ts\n" +
        "@@ -1,3 +1,4 @@\n" +
        " import { Tower } from './tower';\n" +
        "-const height = 3;\n" +
        "+const height = 4;\n" +
        "+const width = 2;\n" +
        " export { height };\n" +
        "@@ -40,2 +41,2 @@ function stack() {\n" +
        "-  return height;\n" +
        "+  return height * width;\n" +
        " }\n" +
        "\\ No newline at end of file\n";

    private static FileChangeColumn Opened(RoomChange? row = null)
    {
        var column = new FileChangeColumn(ProjectId, row ?? Changed());
        Assert.That(column.Located(new TurnVersions(TurnVersionsFound.Found, Id(1), Id(2))), Is.True);
        return column;
    }

    private static void NoDots(RoomCard? card)
    {
        Assert.That(card, Is.Not.Null);
        Assert.That(card!.Status is RoomCardStatus status ? new[] { status.Title, status.Line ?? "" } : Array.Empty<string>(),
            Has.None.Contains("·"), "a status joins no facts on a dot");
        Assert.That(card.Slots.Select(slot => slot.Value.Text), Has.None.Contains("·"));
    }

    [Test]
    public void TheDiffParsesIntoMarkedNumberedRowsWithAGapBetweenPartsAndNoHeaders()
    {
        var rows = FileChangeColumn.ParseDiff(Diff);
        Assert.That(rows.Select(row => (row.Kind, row.Number, row.Text)), Is.EqualTo(new (RoomLineKind, int?, string)[]
        {
            (RoomLineKind.Same, 1, "import { Tower } from './tower';"),
            (RoomLineKind.Removed, 2, "const height = 3;"),
            (RoomLineKind.Added, 2, "const height = 4;"),
            (RoomLineKind.Added, 3, "const width = 2;"),
            (RoomLineKind.Same, 4, "export { height };"),
            (RoomLineKind.Gap, 36, ""),
            (RoomLineKind.Removed, 40, "  return height;"),
            (RoomLineKind.Added, 41, "  return height * width;"),
            (RoomLineKind.Same, 42, "}"),
        }));
    }

    [Test]
    public void TheChangeIsACardTitledByTheFileItsPathUnderItItsLinesAsASlotAndEveryRowTheirs()
    {
        var column = Opened();
        Assert.That(column.Stage, Is.EqualTo(FileChangeStage.Reading));
        Assert.That(column.Card(), Is.Null, "while it reads, the sentence sweeps");
        Assert.That(column.Sentence, Is.EqualTo("Reading File"));
        column.Changed(Change(Diff));
        var card = column.Card()!;
        Assert.That(card.Title!.Text, Is.EqualTo("towers.ts"));
        Assert.That(card.Title.Runs.All(run => run.Theirs), Is.True, "the file's name is theirs");
        Assert.That(card.Description!.Text, Is.EqualTo(Path));
        Assert.That(card.Description.Runs.All(run => run.Theirs), Is.True);
        Assert.That(card.Slots.Select(slot => (slot.Kind, slot.Value.Text)), Is.EqualTo(new[] { (RoomKind.Changed, "Edited"), (RoomKind.Added, "3 Lines"), (RoomKind.Removed, "2 Lines") }),
            "the change's kind, then its lines added and removed apart, each under its own mark");
        NoDots(card);
        Assert.That(card.AnyDot, Is.False);
        Assert.That(card.Reading.Count, Is.EqualTo(9), "the gap between the two parts is a row");
        Assert.That(card.Reading.Where(row => row.Kind != RoomLineKind.Gap).All(row => row.Text!.Runs.All(run => run.Theirs)), Is.True, "every line is theirs");
        Assert.That(card.Reading.Select(row => row.Indent).Max(), Is.EqualTo(2), "a line's leading spaces stand as its indent");
        Assert.That(card.Reading.Where(row => row.Kind == RoomLineKind.Removed).All(row => row.Number == null), Is.True, "one gutter: a removed line carries no number");
        Assert.That(card.Reading.Where(row => row.Kind == RoomLineKind.Added && !row.Continued).Select(row => row.Number), Is.EqualTo(new int?[] { 2, 3, 41 }));
        Assert.That(card.More, Is.Null, "one page has no page place");
        Assert.That(column.Actions().Select(action => (action.Kind, action.Words, action.Main)), Is.EqualTo(new[]
        {
            (FileChangeActionKind.WholeFile, "Whole File", false),
            (FileChangeActionKind.Back, "Back", false),
        }));
    }

    [Test]
    public void ALongLineWrapsAtItsSpacesIntoContinuedRowsWithEveryCharacterKeptAndATabAsSpaces()
    {
        foreach (var scale in new[] { 1f, Comfort.LargerTextScale })
        {
            var column = new FileChangeColumn(ProjectId, Changed(), scale);
            column.Located(new TurnVersions(TurnVersionsFound.Found, Id(1), Id(2)));
            var line = "\tconst tower = buildTower(height, width, colour, sky.clouds.first());";
            column.Changed(Change("@@ -1 +1 @@\n+" + line + "\n"));
            var rows = column.Rows;
            Assert.That(rows.Count, Is.GreaterThan(1));
            Assert.That((rows[0].Number, rows[0].Indent), Is.EqualTo(((int?)1, 4)), "the tab stands as an indent of four");
            Assert.That(rows.Skip(1).All(row => row.Continued && row.Number == null && row.Kind == RoomLineKind.Added && row.Indent == 4), Is.True,
                "a wrapped row is a continuation of an added line at its indent, never a new numbered line");
            Assert.That(string.Concat(rows.Select(row => row.Text!.Text)), Is.EqualTo(line.Substring(1)), "every character kept");
            foreach (var row in rows)
                Assert.That(row.Indent + row.Text!.Text.TrimEnd(' ').Length, Is.LessThanOrEqualTo(RoomReading.Columns(scale, row.Continued)), "no row is longer than FIELD's columns hold");
            for (var index = 0; index + 1 < rows.Count; index++)
                Assert.That(rows[index].Text!.Text.EndsWith(" ", StringComparison.Ordinal) || rows[index].Text!.Text.EndsWith(",", StringComparison.Ordinal)
                    || rows[index + 1].Text!.Text.StartsWith("(", StringComparison.Ordinal) || rows[index + 1].Text!.Text.StartsWith(".", StringComparison.Ordinal),
                    Is.True, "it breaks at spaces, after punctuation or before a call, never mid-word: " + rows[index].Text!.Text);
        }
    }

    [Test]
    public void TheReadingPagesByRowsKeepingALineWholeAndNextPageLeadsThenReturnsToTheFirst()
    {
        var column = Opened();
        var diff = "@@ -1,30 +1,30 @@\n" + string.Concat(Enumerable.Range(1, 30).Select(n => " line " + n + "\n"));
        column.Changed(Change(diff));
        Assert.That(column.PageCount, Is.EqualTo(3));
        Assert.That(column.Rows.Count, Is.EqualTo(RoomReading.RowsAPage));
        Assert.That(column.Card()!.More!.Text, Is.EqualTo("Page 1 of 3"));
        Assert.That(column.Actions().Select(action => (action.Kind, action.Words, action.Main)), Is.EqualTo(new[]
        {
            (FileChangeActionKind.NextPage, "Next Page", true), (FileChangeActionKind.WholeFile, "Whole File", false), (FileChangeActionKind.Back, "Back", false),
        }));
        Assert.That(column.PreviousPage(), Is.False, "nothing before the first page");
        Assert.That(column.NextPage(), Is.True);
        Assert.That(column.Rows[0].Number, Is.EqualTo(RoomReading.RowsAPage + 1));
        Assert.That(column.Actions().Select(action => action.Kind).Take(2), Is.EqualTo(new[] { FileChangeActionKind.NextPage, FileChangeActionKind.PreviousPage }));
        column.NextPage();
        Assert.That((column.Page, column.Card()!.More!.Text), Is.EqualTo((2, "Page 3 of 3")));
        Assert.That(column.Actions().Select(action => (action.Kind, action.Main)).First(), Is.EqualTo((FileChangeActionKind.PreviousPage, false)), "the last page has no Next Page");
        Assert.That(column.NextPage(), Is.False);
        Assert.That(column.PreviousPage(), Is.True);
        Assert.That(column.Page, Is.EqualTo(1));
    }

    [Test]
    public void AtLargerTextTheRowsWrapAgainAndThePageShownKeepsItsFirstLine()
    {
        var column = Opened();
        var diff = "@@ -1,30 +1,30 @@\n" + string.Concat(Enumerable.Range(1, 30).Select(n => " let value" + n + " = compute(" + n + ", other);\n"));
        column.Changed(Change(diff));
        var standard = column.PageCount;
        column.NextPage();
        var first = column.Rows[0].Number;
        column.AtScale(Comfort.LargerTextScale);
        Assert.That(column.Rows.Select(row => row.Number), Does.Contain(first), "the line read first stays on the page shown");
        Assert.That(column.PageCount, Is.GreaterThan(standard), "at Larger the rows hold fewer characters, so there are more pages");
    }

    [Test]
    public void WholeFileReadsOnceShowsTheFileAsItIsNowAndBackReturnsToTheChangeWhereItWas()
    {
        var column = Opened();
        column.Changed(Change("@@ -1,30 +1,30 @@\n" + string.Concat(Enumerable.Range(1, 30).Select(n => " line " + n + "\n"))));
        column.NextPage();
        Assert.That(column.OpenWhole(), Is.True, "the whole file is read");
        Assert.That(column.Stage, Is.EqualTo(FileChangeStage.Reading));
        Assert.That(column.Actions().Select(action => action.Kind), Is.EqualTo(new[] { FileChangeActionKind.Back }));
        column.FileRead(File("first line\nsecond line\n"));
        var card = column.Card()!;
        Assert.That(card.Title!.Text, Is.EqualTo("towers.ts"));
        Assert.That(card.Description!.Text, Is.EqualTo("As it is now:" + Path), "the path's run carries its own gap");
        Assert.That(card.Description.Runs.First().Theirs, Is.False);
        Assert.That(card.Description.Runs.Last().Theirs, Is.True, "ours first, the path theirs and last");
        Assert.That(card.Slots, Is.Empty);
        Assert.That(card.Reading.Select(row => (row.Kind, row.Number, row.Text!.Text)), Is.EqualTo(new (RoomLineKind, int?, string)[]
        {
            (RoomLineKind.Same, 1, "first line"),
            (RoomLineKind.Same, 2, "second line"),
        }));
        Assert.That(column.Actions().Select(action => action.Kind), Is.EqualTo(new[] { FileChangeActionKind.Back }), "no Whole File on the whole file");
        Assert.That(column.Back(), Is.True);
        Assert.That((column.Stage, column.Page), Is.EqualTo((FileChangeStage.Change, 1)), "back at the change, on its page");
        Assert.That(column.OpenWhole(), Is.False, "read once: shown again without a read");
        Assert.That(column.Stage, Is.EqualTo(FileChangeStage.File));
        column.Back();
        Assert.That(column.Back(), Is.False, "Back at the change leaves for Changes");
    }

    [Test]
    public void EachFailureSaysItsOwnTrueLineAndTryAgainStandsOnlyWhereTheComputerDidNotAnswer()
    {
        // AUDIT's F2 (2026-11-12): "did not answer" only where no answer came; an answer that could not be read says so.
        Assert.That(FileChangeColumn.Answered(new ControlPlaneRequestException("could not be reached", new System.Net.Http.HttpRequestException())), Is.False);
        Assert.That(FileChangeColumn.Answered(new TaskCanceledException()), Is.False);
        Assert.That(FileChangeColumn.Answered(new ControlPlaneRequestException("refused", "not_found", 404)), Is.True, "a refusal is an answer");
        Assert.That(FileChangeColumn.Answered(new FormatException("for another project")), Is.True);
        Assert.That(FileChangeColumn.Answered(new InvalidOperationException("something else")), Is.Null, "whether an answer came is not known");
        var unknown = Opened();
        unknown.ReadFailed(answered: null);
        Assert.That((unknown.Card()!.Status!.Value.Line, unknown.Actions().Select(action => action.Kind)), Is.EqualTo(("Reason unknown", new[] { FileChangeActionKind.Back })));
        var silent = Opened();
        silent.ReadFailed(answered: false);
        Assert.That((silent.Card()!.Status!.Value.Title, silent.Card()!.Status!.Value.Line), Is.EqualTo(("Not Read", "The computer did not answer")));
        Assert.That(silent.Actions().Select(action => action.Kind), Does.Contain(FileChangeActionKind.TryAgain));
        var answered = Opened();
        answered.ReadFailed(answered: true);
        Assert.That((answered.Card()!.Status!.Value.Title, answered.Card()!.Status!.Value.Line), Is.EqualTo(("Not Read", "The computer's reply can't be shown")));
        Assert.That(answered.Actions().Select(action => action.Kind), Is.EqualTo(new[] { FileChangeActionKind.Back }), "trying again cannot help an answer it could not read");
    }

    [Test]
    public void OnlyTheFoldersFirstVersionSaysItKeptNoEarlierOneAndAVersionNotFoundSaysSo()
    {
        // AUDIT's F3 (2026-11-12): "kept no earlier version" is true only where the turn's version is the folder's first.
        var first = new FileChangeColumn(ProjectId, Changed());
        first.Located(new TurnVersions(TurnVersionsFound.First));
        Assert.That(first.Card()!.Status!.Value.Line, Is.EqualTo("The computer kept no earlier version of this file"));
        var none = new FileChangeColumn(ProjectId, Changed());
        none.Located(new TurnVersions(TurnVersionsFound.None));
        Assert.That((none.Card()!.Status!.Value.Title, none.Card()!.Status!.Value.Line), Is.EqualTo(("Nothing to Compare", "The earlier version was not found")));
    }

    [Test]
    public void AGapThatCountsTheLinesItSkipsOpensThePageOfThePartItLeadsTo()
    {
        // A first part that fills a page exactly: its gap would close page 1, so it opens page 2 with the part after it.
        var diff = "@@ -1,12 +1,12 @@\n" + string.Concat(Enumerable.Range(1, FileChangeColumn.RowsAPage).Select(n => " const a" + n + " = " + n + ";\n"))
            + "@@ -40,1 +40,1 @@\n-const b = 1;\n+const b = 2;\n";
        var column = Opened();
        column.Changed(Change(diff));
        Assert.That(column.PageCount, Is.EqualTo(2));
        Assert.That(column.Rows.Any(row => row.Kind == RoomLineKind.Gap), Is.False, "page 1 ends on its last line");
        column.NextPage();
        Assert.That((column.Rows[0].Kind, column.Rows[0].Number), Is.EqualTo((RoomLineKind.Gap, (int?)27)), "lines 13 to 39 skipped, said where page 2 begins");
        Assert.That(column.Rows.Skip(1).Select(row => row.Kind), Is.EqualTo(new[] { RoomLineKind.Removed, RoomLineKind.Added }));
    }

    [Test]
    public void AFileThatCannotBeShownSaysWhyAsAStatusUnderItsName()
    {
        (FileChangeColumn Column, string Title, string? Line, RoomKind Mark) Case(Action<FileChangeColumn> act, bool whole = false)
        {
            var column = Opened();
            act(column);
            var card = column.Card()!;
            var status = card.Status!.Value;
            NoDots(card);
            // A card that shows no lines has nothing to say "as it is now" of, even on the whole file: the path alone.
            Assert.That((card.Title!.Text, card.TitleMono, card.Description!.Text), Is.EqualTo(("towers.ts", true, Path)),
                "the file's name first, in mono, its path under it, then the status (whole: " + whole + ")");
            return (column, status.Title, status.Line, status.Mark);
        }

        var binary = Case(column => column.Changed(Change(null, binary: true)));
        Assert.That((binary.Title, binary.Line, binary.Mark), Is.EqualTo(("Not Text", (string?)null, RoomKind.None)));
        var withheld = Case(column => column.Changed(Change(null, withheld: true)));
        Assert.That((withheld.Title, withheld.Line, withheld.Mark), Is.EqualTo(("Kept Private", "Its name says it may hold secrets", RoomKind.Folder)));
        Assert.That(withheld.Column.Actions().Select(action => action.Kind), Is.EqualTo(new[] { FileChangeActionKind.Back }), "a withheld file has no Whole File");
        var large = Case(column => { column.Changed(Change(Diff)); column.OpenWhole(); column.FileRead(File(null, ProjectFileKind.TooLarge)); }, whole: true);
        Assert.That((large.Title, large.Line, large.Mark), Is.EqualTo(("Too Large to Show", "Over 256 KB", RoomKind.File)));
        var empty = Case(column => { column.Changed(Change(Diff)); column.OpenWhole(); column.FileRead(File("")); }, whole: true);
        Assert.That((empty.Title, empty.Line, empty.Mark), Is.EqualTo(("Empty File", (string?)null, RoomKind.File)));
    }

    [Test]
    public void AChangeCutAtTheComputersBoundSaysSoOnItsLastPageAndCountsNoLines()
    {
        var column = Opened();
        column.Changed(Change(Diff, complete: false));
        var card = column.Card()!;
        Assert.That(card.More!.Text, Is.EqualTo("The rest is too long to show here"), "in place of the page's place");
        Assert.That(card.Slots.Select(slot => slot.Value.Text), Is.EqualTo(new[] { "Edited" }), "a cut change's counts would not be the whole change's");
    }

    [Test]
    public void NoEarlierVersionNoHistoryAndAFailedReadEachSaySoAndKeepTheWholeFileOrTryAgain()
    {
        var none = new FileChangeColumn(ProjectId, Changed());
        Assert.That(none.Located(new TurnVersions(TurnVersionsFound.None)), Is.False);
        Assert.That((none.Card()!.Status!.Value.Title, none.Card()!.Status!.Value.Line), Is.EqualTo(("Nothing to Compare", "The earlier version was not found")));
        Assert.That(none.Actions().Select(action => (action.Kind, action.Main)), Is.EqualTo(new[] { (FileChangeActionKind.WholeFile, true), (FileChangeActionKind.Back, false) }),
            "with nothing to compare, the whole file still reads");

        var off = new FileChangeColumn(ProjectId, Changed());
        off.Located(new TurnVersions(TurnVersionsFound.NotSetUp));
        Assert.That(off.Card()!.Status!.Value.Title, Is.EqualTo("No History Yet"));

        var failed = Opened();
        failed.ReadFailed();
        Assert.That(failed.Card()!.Status!.Value.Title, Is.EqualTo("Not Read"));
        Assert.That(failed.Actions().Select(action => (action.Kind, action.Main)), Is.EqualTo(new[] { (FileChangeActionKind.TryAgain, true), (FileChangeActionKind.Back, false) }));
        Assert.That(failed.Reread(), Is.False, "the change is read again");
        Assert.That(failed.Stage, Is.EqualTo(FileChangeStage.Reading));

        var removed = new FileChangeColumn(ProjectId, new RoomChange(RoomChangeKind.Removed, Path, 3, null));
        removed.Located(new TurnVersions(TurnVersionsFound.None));
        Assert.That(removed.Actions().Select(action => action.Kind), Is.EqualTo(new[] { FileChangeActionKind.Back }), "a removed file has no whole file to read");
    }

    [Test]
    public async Task TheReaderFindsTheTurnsVersionsThroughTheHistoryPagesAndChecksEveryAnswer()
    {
        var asked = new List<string>();
        var pages = new Queue<string>(new[]
        {
            History(true, Version(9, "turn-9", Id(8)), Version(7, "turn-7", Id(6), execution: Id(77))),
            History(false, Version(5, "turn-5", Id(4))),
        });
        var reader = new FileChangeReader((path, _) => { asked.Add(path); return Task.FromResult(pages.Dequeue()); });
        var found = await reader.FindTurnAsync(ProjectId, JournalId, Run, "turn-5");
        Assert.That((found.Found, found.From, found.To), Is.EqualTo((TurnVersionsFound.Found, Id(4), Id(5))));
        Assert.That(asked, Is.EqualTo(new[]
        {
            $"api/projects/{ProjectId}/history?journal_id={JournalId}&limit=50",
            $"api/projects/{ProjectId}/history?journal_id={JournalId}&limit=50&before={Id(107)}",
        }));

        var first = new FileChangeReader((_, _) => Task.FromResult(History(false, Version(3, "turn-3", null))));
        Assert.That((await first.FindTurnAsync(ProjectId, JournalId, Run, "turn-3")).Found, Is.EqualTo(TurnVersionsFound.First), "a folder's first version has nothing before it");
        var noTurn = new FileChangeReader((_, _) => Task.FromResult(History(false, NotKept(6), Version(5, null, Id(4)))));
        Assert.That((await noTurn.FindTurnAsync(ProjectId, JournalId, Run, null)).Found, Is.EqualTo(TurnVersionsFound.None),
            "without a turn id, a newest turn not kept never lets an older one's change pass for it");
        var newest = new FileChangeReader((_, _) => Task.FromResult(History(false, Version(5, null, Id(4)))));
        Assert.That((await newest.FindTurnAsync(ProjectId, JournalId, Run, null)).To, Is.EqualTo(Id(5)));
        var off = new FileChangeReader((_, _) => Task.FromResult(HalcyonicJson.Serialize(new ProjectHistoryResponse
        {
            ProjectId = ProjectId,
            Result = new UnavailableHistory { Reason = new ErrorInfo { Code = "undo_not_set_up", Message = "Undo is not set up." } },
        })));
        Assert.That((await off.FindTurnAsync(ProjectId, JournalId, Run, "turn-1")).Found, Is.EqualTo(TurnVersionsFound.NotSetUp));

        var files = new FileChangeReader((path, _) =>
        {
            asked.Add(path);
            return Task.FromResult(path.Contains("/changes/file?", StringComparison.Ordinal) ? HalcyonicJson.Serialize(Change(Diff)) : HalcyonicJson.Serialize(File("x\n")));
        });
        asked.Clear();
        Assert.That((await files.FileChangeAsync(ProjectId, JournalId, Id(1), Id(2), Path)).Diff, Is.EqualTo(Diff));
        Assert.That((await files.FileAsync(ProjectId, JournalId, Path)).Text, Is.EqualTo("x\n"));
        Assert.That(asked, Is.EqualTo(new[]
        {
            $"api/projects/{ProjectId}/changes/file?journal_id={JournalId}&from={Id(1)}&to={Id(2)}&path=src%2Fgame%2Ftowers.ts",
            $"api/projects/{ProjectId}/file?journal_id={JournalId}&path=src%2Fgame%2Ftowers.ts",
        }));
        Assert.ThrowsAsync<FormatException>(() => files.FileChangeAsync(ProjectId, JournalId, Id(1), Id(3), Path), "an answer for other versions is refused");
        Assert.ThrowsAsync<FormatException>(() => files.FileAsync(ProjectId, JournalId, "src/other.ts"), "an answer for another file is refused");
    }

    [Test]
    public void TheWrapBreaksAtSpacesAndPunctuationOrBeforeADotAfterANameAndNeverInsideAnOperatorOrAName()
    {
        static string[] Rows(string line, int first, int rest) => FileChangeColumn.Breaks(line, first, rest).Select(row => row.Part).ToArray();
        Assert.That(Rows("const total=a=>b", 14, 14), Is.EqualTo(new[] { "const ", "total=a=>b" }), "a space before an arrow it would split");
        Assert.That(Rows("kite.tail.weight(segments)", 12, 12), Is.EqualTo(new[] { "kite.tail", ".weight", "(segments)" }), "before a dot or bracket that follows a name");
        Assert.That(Rows("point.lerp(kite.at, wind.pull))", 20, 20), Is.EqualTo(new[] { "point.lerp(kite.at, ", "wind.pull))" }),
            "a space or comma first; never between wind and .pull while the row holds a comma");
        Assert.That(Rows("if (a&&b||c===d) go();", 9, 9).All(row => !row.EndsWith("&", StringComparison.Ordinal) || row.EndsWith("&&", StringComparison.Ordinal)), Is.True,
            "&& stays whole");
        foreach (var row in Rows("x===yy===zz===ww", 4, 4)) Assert.That(row.StartsWith("=", StringComparison.Ordinal) && !row.StartsWith("===", StringComparison.Ordinal), Is.False, "=== stays whole: " + row);
        var token = new string('a', 30);
        var rows = Rows(token, 12, 10);
        Assert.That(string.Concat(rows), Is.EqualTo(token), "a name longer than a row breaks where the row ends, every character kept");
        Assert.That(rows.Select(row => row.Length), Is.EqualTo(new[] { 12, 10, 8 }));
        foreach (var scale in new[] { 1f, Comfort.LargerTextScale })
        {
            var wrapped = FileChangeColumn.Wrap(RoomLineKind.Same, 1, "    return kite.tail.weight(TAIL_SEGMENTS, wind.gust) => wind.speed * LIFT;", scale);
            Assert.That(string.Concat(wrapped.Select(row => row.Text!.Text)), Is.EqualTo("return kite.tail.weight(TAIL_SEGMENTS, wind.gust) => wind.speed * LIFT;"));
            foreach (var row in wrapped) Assert.That(row.Indent + row.Text!.Text.TrimEnd(' ').Length, Is.LessThanOrEqualTo(RoomReading.Columns(scale, row.Continued)));
            Assert.That(wrapped.Any(row => row.Text!.Text.TrimEnd().EndsWith("=", StringComparison.Ordinal)), Is.False, "never inside =>");
        }
    }
}
