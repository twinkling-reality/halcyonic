using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class TaskResultsColumnTests
{
    internal const string TaskId = "0192f1a0-0000-7000-8000-000000000101", RunId = "0192f1a0-0000-7000-8000-000000000102",
        EventId = "0192f1a0-0000-7000-8000-000000000103";
    internal static WorkspaceContinuation Context() => new()
    { HandoffId = Guid.NewGuid().ToString("D"), ConnectionKey = new string('a', 64), JournalId = Samples.JournalId,
        ProjectId = Samples.ProjectId, WorkstreamId = TaskId, ExecutionId = RunId };
    internal static ToolResultSummary Summary() => new()
    { EventId = EventId, Position = 5, OccurredAt = Samples.Time, RuntimeId = "mock", Kind = ToolResultSummaryKind.Command,
        Completion = ToolResultCompletion.Completed, ExitCode = 0, BodyAvailable = true, RetainedComplete = true, Title = "node check.js" };
    internal static ToolResultsResponse Listing() => new()
    { JournalId = Samples.JournalId, ProjectId = Samples.ProjectId, WorkstreamId = TaskId, ExecutionId = RunId,
        Availability = ToolResultsAvailability.Available, Total = 1, Earlier = 0, Results = new List<ToolResultSummary> { Summary() } };
    internal static ToolResultResponse Output(string text = "  checked\r\n\n    done\\n\n") => new()
    { JournalId = Samples.JournalId, ProjectId = Samples.ProjectId, WorkstreamId = TaskId, ExecutionId = RunId, Summary = Summary(),
        Result = new CommandToolResult { ToolCallId = "command-1", TurnId = "turn-1", Completion = ToolResultCompletion.Completed,
            Redaction = new ToolResultRedaction(), ExitCode = 0,
            Output = new ToolResultBody { Text = text, Availability = ToolResultBodyAvailability.Available, Complete = true, Truncation = ToolResultBodyTruncation.None } } };
    private static TaskResultsColumn Open(FileMenuHost host, ToolResultsResponse? listing = null, ToolResultResponse? output = null, Func<bool>? current = null)
    {
        var column = new TaskResultsColumn(host, Context(), "mock", "Mock runtime", current ?? (() => true),
            _ => Task.FromResult(listing ?? Listing()), (_, _) => Task.FromResult(output ?? Output()));
        _ = column.Frame; column.Tick(); _ = column.Frame;
        return column;
    }
    private static void Draw(TaskResultsColumn column) { var frame = column.Frame!; column.Drawn(frame, null); }
    private static string Words(TaskResultsColumn column) => string.Join("\n", column.Frame!.Lines.Select(line => line.Words));
    private static void Read(TaskResultsColumn column)
    { Draw(column); column.Act(TaskResultsColumn.Read, EventId); column.Tick(); _ = column.Frame; }
    private static void ReadFile(TaskResultsColumn column, int index = 0)
    { Draw(column); column.Act(TaskResultsColumn.ReadFile, index.ToString(System.Globalization.CultureInfo.InvariantCulture)); _ = column.Frame; }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void OnlyAnExactDrawnResultOpensAndEveryBodyPartKeepsStructuredOutsideWords(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var text = "\n  first\r\n\n" + string.Concat(Enumerable.Repeat("    line <b>literal</b>\\n\u202E\t😀  \n", 70)) + "\n";
        var column = Open(host, output: Output(text));
        column.Act(TaskResultsColumn.Read, EventId);
        Assert.That(Words(column), Does.Not.Contain("    line"), "undrawn navigation has no authority");
        var list = column.Frame!; Read(column);
        column.Act(TaskResultsColumn.Read, EventId); column.Drawn(list, null);
        var from = 0; var parts = 0;
        while (true)
        {
            var frame = column.Frame!; var line = frame.Lines.Single();
            Assert.That(line.FromRow, Is.EqualTo(from));
            Assert.That(line.Runs.AnyTheirs, Is.True);
            Assert.That(line.Words, Does.Contain("\n  first‹U+000D›\n\n    line <b>literal</b>\\n‹U+202E›‹U+0009›😀  \n"));
            Assert.That(line.Words, Does.EndWith("\n\n"), "trailing and blank LF survive actual page construction");
            Assert.That(line.Runs.ForTextMeshPro().Text, Does.Contain("<b>literal</b>\\\\n‹U+202E›"), "one renderer escaping step");
            from += line.Rows; parts++;
            if (!frame.Footer.All.Any(slot => slot.Prompt.Id == TaskResultsColumn.Next)) break;
            Draw(column); column.Act(TaskResultsColumn.Next, null);
        }
        Assert.That(parts, Is.GreaterThan(1));
        Assert.That(host.Sent, Is.Empty); Assert.That(host.Keyboard, Is.Null);
        column.Heard("send this"); column.HoldStarted("anything"); column.Said("anything");
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase("connection")]
    [TestCase("journal")]
    [TestCase("project")]
    [TestCase("task")]
    [TestCase("execution")]
    [TestCase("runtime")]
    [TestCase("api")]
    [TestCase("host-api")]
    public void ExplicitReaderReuseRequiresTheOriginalCopiedScopeRuntimeAndApi(string changed)
    {
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token");
        using var otherApi = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token");
        var host = new FileMenuHost { Api = api }; var original = Context();
        var column = new TaskResultsColumn(host, original, "mock", "Mock runtime", () => false);
        original.ExecutionId = "caller-mutated";
        var candidate = Context(); var runtime = "mock"; var candidateApi = api;
        Assert.That(column.CanReuse(candidate, runtime, candidateApi), Is.True, "handoff identity is not the reader's scope");
        switch (changed)
        {
            case "connection": candidate.ConnectionKey = new string('b', 64); break;
            case "journal": candidate.JournalId = "foreign"; break;
            case "project": candidate.ProjectId = "foreign"; break;
            case "task": candidate.WorkstreamId = "foreign"; break;
            case "execution": candidate.ExecutionId = "foreign"; break;
            case "runtime": runtime = "other"; break;
            case "api": candidateApi = otherApi; break;
            case "host-api": host.Api = otherApi; break;
        }
        Assert.That(column.CanReuse(candidate, runtime, candidateApi), Is.False);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void DefaultReadsUseTheCopiedOriginalContextAfterCallerMutation()
    {
        var handler = new ControlPlaneApiTests.CannedHandler(HttpStatusCode.OK, HalcyonicJson.Serialize(Listing()));
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", handler);
        var host = new FileMenuHost { Api = api }; var context = Context();
        var column = new TaskResultsColumn(host, context, "mock", "Mock runtime", () => true);
        context.ExecutionId = "0192f1a0-0000-7000-8000-000000000199"; context.JournalId = "foreign"; context.ProjectId = "foreign"; context.WorkstreamId = "foreign";
        _ = column.Frame; column.Tick();
        Assert.That(handler.Requests.Single().RequestUri!.AbsolutePath, Is.EqualTo("/api/executions/" + RunId + "/results"));
        Assert.That(handler.Requests.Single().RequestUri!.Query, Is.EqualTo("?journal_id=" + Samples.JournalId + "&project_id=" + Samples.ProjectId + "&workstream_id=" + TaskId));
        Assert.That(Words(column), Does.Contain("Command output"));
    }

    [TestCase("journal")]
    [TestCase("project")]
    [TestCase("task")]
    [TestCase("execution")]
    [TestCase("runtime")]
    [TestCase("count")]
    [TestCase("earlier")]
    [TestCase("positions")]
    [TestCase("reported")]
    public void ForeignOrInconsistentListsNeverOfferAnyResult(string changed)
    {
        var found = Listing();
        switch (changed)
        {
            case "journal": found.JournalId = "foreign"; break;
            case "project": found.ProjectId = "foreign"; break;
            case "task": found.WorkstreamId = "foreign"; break;
            case "execution": found.ExecutionId = "foreign"; break;
            case "runtime": found.Results[0].RuntimeId = "foreign"; break;
            case "reported": found.Results[0].Epistemic = "reported"; break;
            case "count": found.Total = 0; break;
            case "earlier": found.Earlier = 1; break;
            case "positions": var another = Summary(); another.EventId = "0192f1a0-0000-7000-8000-000000000104"; found.Results.Add(another); found.Total = 2; break;
        }
        var host = new FileMenuHost(); var column = Open(host, found);
        Assert.That(column.Frame!.Lines.Any(line => line.Action == TaskResultsColumn.Read), Is.False);
        Assert.That(Words(column), Does.Contain("could not be read"));
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase("journal")]
    [TestCase("event")]
    [TestCase("position")]
    [TestCase("completion")]
    [TestCase("exit")]
    [TestCase("body")]
    [TestCase("retention")]
    public void ExactReadsMustMatchTheOriginalSummaryAndActualBodyMetadata(string changed)
    {
        var output = Output("DO NOT ADOPT");
        switch (changed)
        {
            case "journal": output.JournalId = "foreign"; break;
            case "event": output.Summary.EventId = "foreign"; break;
            case "position": output.Summary.Position++; break;
            case "completion": output.Result.Completion = ToolResultCompletion.Failed; break;
            case "exit": ((CommandToolResult)output.Result).ExitCode = 7; break;
            case "body": ((CommandToolResult)output.Result).Output.Availability = ToolResultBodyAvailability.Absent; break;
            case "retention": ((CommandToolResult)output.Result).Output.Complete = false; break;
        }
        var host = new FileMenuHost(); var column = Open(host, output: output); Read(column);
        Assert.That(Words(column), Does.Not.Contain("DO NOT ADOPT"));
        Assert.That(Words(column), Does.Contain("could not be read")); Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HiddenOrCompactedPendingReadsCancelAndNeverAdoptOldCompletion(bool body)
    {
        var host = new FileMenuHost(); var shown = true; CancellationToken captured = default;
        var pendingList = new TaskCompletionSource<ToolResultsResponse>(); var pendingBody = new TaskCompletionSource<ToolResultResponse>();
        var column = new TaskResultsColumn(host, Context(), "mock", "Mock runtime", () => shown,
            token => { captured = token; return body ? Task.FromResult(Listing()) : pendingList.Task; },
            (_, token) => { captured = token; return pendingBody.Task; });
        _ = column.Frame; column.Tick();
        if (body) { Draw(column); column.Act(TaskResultsColumn.Read, EventId); }
        shown = false; column.Compact();
        Assert.That(captured.IsCancellationRequested, Is.True);
        pendingList.SetResult(Listing()); pendingBody.SetResult(Output("STALE COMPLETION")); column.Tick();
        Assert.That(Words(column), Does.Not.Contain("STALE COMPLETION"));
        Assert.That(column.Frame!.Lines.Any(line => line.Action == TaskResultsColumn.Read), Is.False);
        column.Act(TaskResultsColumn.Read, EventId); Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void MinimizedDetailKeepsItsExactEventAndPageButRequiresAFreshReadAfterRecentEviction(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size }; var shown = true; var listCalls = 0; var readIds = new List<string>();
        var fresh = new TaskCompletionSource<ToolResultResponse>(); CancellationToken freshToken = default;
        var text = string.Concat(Enumerable.Repeat("  recorded line\n", 150));
        var column = new TaskResultsColumn(host, Context(), "mock", "Mock runtime", () => shown,
            _ =>
            {
                listCalls++; var list = Listing();
                if (listCalls > 1)
                {
                    list.Results = Enumerable.Range(0, 16).Select(index =>
                    { var summary = Summary(); summary.EventId = "0192f1a0-0000-7000-8000-0000000002" + index.ToString("D2"); summary.Position = 25 - index; return summary; }).ToList();
                    list.Total = 17; list.Earlier = 1;
                }
                return Task.FromResult(list);
            },
            (id, token) => { readIds.Add(id); freshToken = token; return readIds.Count == 1 ? Task.FromResult(Output(text)) : fresh.Task; });
        _ = column.Frame; column.Tick(); Read(column);
        Draw(column); column.Act(TaskResultsColumn.Next, null); Draw(column); column.Act(TaskResultsColumn.Next, null);
        var originalRow = column.Frame!.Lines.Single().FromRow;
        Assert.That(originalRow, Is.GreaterThan(0));
        var oldFrame = column.Frame;
        shown = false; column.Compact(); _ = column.Frame;
        Assert.That(Words(column), Does.Not.Contain("recorded line"));
        Assert.That(column.CanReuse(Context(), "mock", host.Api), Is.True);
        Assert.That(readIds, Has.Count.EqualTo(1), "compatibility inspection neither reads nor restores authority");
        shown = true; _ = column.Frame; column.Tick();
        Assert.That(readIds, Is.EqualTo(new[] { EventId, EventId }), "recent eviction never retargets the retained event");
        Assert.That(freshToken.IsCancellationRequested, Is.False);
        Assert.That(Words(column), Does.Contain("Reading recorded result"));
        column.Drawn(oldFrame!, null); column.Act(TaskResultsColumn.Next, null);
        fresh.SetResult(Output(text)); column.Tick();
        Assert.That(column.Frame!.Lines.Single().FromRow, Is.EqualTo(originalRow));
        Assert.That(Words(column), Does.Contain("recorded line")); Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void APreHideLateBodyCannotPublishIntoTheNewReadGeneration()
    {
        var host = new FileMenuHost(); var shown = true; var calls = 0;
        var old = new TaskCompletionSource<ToolResultResponse>(); var fresh = new TaskCompletionSource<ToolResultResponse>();
        var column = new TaskResultsColumn(host, Context(), "mock", "Mock runtime", () => shown,
            _ => Task.FromResult(Listing()), (_, _) => ++calls == 1 ? old.Task : fresh.Task);
        _ = column.Frame; column.Tick(); Draw(column); column.Act(TaskResultsColumn.Read, EventId);
        shown = false; column.FocusLeft(); shown = true; _ = column.Frame;
        old.SetResult(Output("STALE BEFORE HIDE")); column.Tick();
        Assert.That(Words(column), Does.Not.Contain("STALE BEFORE HIDE"));
        fresh.SetResult(Output("FRESH EXACT READ")); column.Tick();
        Assert.That(Words(column), Does.Contain("FRESH EXACT READ"));
        Assert.That(calls, Is.EqualTo(2)); Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void AChangedCurrentExecutionCancelsTheActualPendingScopeRead()
    {
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(6, new[] { Samples.Workstream(TaskId, executionId: RunId) },
            new[] { Samples.Execution(RunId, TaskId, ExecutionStatus.Running) }), new StateChanges());
        var host = new FileMenuHost { Projection = state }; var pending = new TaskCompletionSource<ToolResultsResponse>(); CancellationToken captured = default;
        var column = new TaskResultsColumn(host, Context(), "mock", "Mock runtime", () => true,
            token => { captured = token; return pending.Task; });
        _ = column.Frame;
        state.ApplySnapshot(Samples.Snapshot(7, new[] { Samples.Workstream(TaskId, executionId: Samples.ProjectId) },
            new[] { Samples.Execution(Samples.ProjectId, TaskId, ExecutionStatus.Running) }), new StateChanges());
        column.Tick(); Assert.That(captured.IsCancellationRequested, Is.True);
        pending.SetResult(Listing()); column.Tick();
        Assert.That(column.Frame!.Lines.Any(line => line.Action == TaskResultsColumn.Read), Is.False);
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void ListPageSurvivesHiddenLoadingUntilItsFreshBoundIsKnown(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size }; var shown = true; var calls = 0;
        var listing = Listing(); listing.Results = Enumerable.Range(0, 16).Select(index =>
        { var item = Summary(); item.EventId = "0192f1a0-0000-7000-8000-0000000002" + index.ToString("D2"); item.Position = 25 - index; return item; }).ToList();
        listing.Total = 16;
        var fresh = new TaskCompletionSource<ToolResultsResponse>();
        var column = new TaskResultsColumn(host, Context(), "mock", "Mock runtime", () => shown,
            _ => ++calls == 1 ? Task.FromResult(listing) : fresh.Task);
        _ = column.Frame; column.Tick(); Draw(column); column.Act(TaskResultsColumn.Next, null);
        var before = column.Frame!.Lines.Where(line => line.Action == TaskResultsColumn.Read).Select(line => line.Key).ToArray();
        shown = false; column.Compact(); _ = column.Frame; shown = true; _ = column.Frame;
        Assert.That(Words(column), Does.Contain("Reading recorded results"));
        fresh.SetResult(listing); column.Tick();
        Assert.That(column.Frame!.Lines.Where(line => line.Action == TaskResultsColumn.Read).Select(line => line.Key), Is.EqualTo(before));
        Assert.That(calls, Is.EqualTo(2));
    }

    [TestCase(TextSize.Standard, false)]
    [TestCase(TextSize.Larger, false)]
    [TestCase(TextSize.Standard, true)]
    [TestCase(TextSize.Larger, true)]
    public void ChosenPatchKeepsExactFileAndPageAndRejectsChangedFileMetadataOnFreshReturn(TextSize size, bool changedFile)
    {
        var host = new FileMenuHost { TextSize = size }; var shown = true; var calls = 0; var back = false;
        var response = Output(); response.Summary.Kind = ToolResultSummaryKind.FileChanges; response.Summary.ExitCode = null; response.Summary.FilesTotal = 1;
        response.Result = new FileChangesToolResult { ToolCallId = "patch-1", TurnId = "turn-1", Completion = ToolResultCompletion.Completed,
            Redaction = new ToolResultRedaction(), FilesTotal = 1, ListComplete = true, Files = new List<ToolResultFile>
            { new() { Kind = ToolResultFileKind.Update, Path = "src/original.cs", PathScope = ToolResultPathScope.ProjectRelative, PathComplete = true,
                Patch = new ToolResultBody { Text = string.Concat(Enumerable.Repeat("+    PATCH ONLY\n", 150)), Availability = ToolResultBodyAvailability.Available,
                    Complete = true, Truncation = ToolResultBodyTruncation.None } } } };
        var listing = Listing(); listing.Results[0] = response.Summary;
        var fresh = new TaskCompletionSource<ToolResultResponse>();
        var column = new TaskResultsColumn(host, Context(), "mock", "Mock runtime", () => shown,
            _ => Task.FromResult(listing), (_, _) => ++calls == 1 ? Task.FromResult(response) : fresh.Task) { BackToTask = () => back = true };
        _ = column.Frame; column.Tick(); Read(column);
        Assert.That(Words(column), Does.Not.Contain("PATCH ONLY"), "file choice never publishes unchosen bodies");
        ReadFile(column); Draw(column); column.Act(TaskResultsColumn.Next, null); Draw(column); column.Act(TaskResultsColumn.Next, null);
        var row = column.Frame!.Lines.Single().FromRow; Assert.That(row, Is.GreaterThan(0));
        shown = false; column.Compact(); _ = column.Frame; shown = true; _ = column.Frame;
        var restored = HalcyonicJson.Deserialize<ToolResultResponse>(HalcyonicJson.Serialize(response));
        if (changedFile) ((FileChangesToolResult)restored.Result).Files[0].Path = "src/another.cs";
        fresh.SetResult(restored); column.Tick();
        if (changedFile)
        { Assert.That(Words(column), Does.Contain("could not be read")); Assert.That(Words(column), Does.Not.Contain("PATCH ONLY")); }
        else
        {
            Assert.That(column.Frame!.Lines.Single().FromRow, Is.EqualTo(row));
            Assert.That(Words(column), Does.Contain("PATCH ONLY"));
            Draw(column); column.Act(Footer.Close, null); column.Tick();
            Assert.That(column.Frame!.Lines.Any(line => line.Action == TaskResultsColumn.ReadFile), Is.True);
            Assert.That(Words(column), Does.Not.Contain("PATCH ONLY"));
            Draw(column); column.Act(Footer.Close, null); column.Tick();
            Assert.That(column.Frame!.Lines.Any(line => line.Action == TaskResultsColumn.Read), Is.True);
            Draw(column); column.Act(Footer.Close, null); Assert.That(back, Is.True);
        }
        Assert.That(host.Sent, Is.Empty);
    }

    [TestCase(TextSize.Standard, false)]
    [TestCase(TextSize.Larger, false)]
    [TestCase(TextSize.Standard, true)]
    [TestCase(TextSize.Larger, true)]
    public void DrawnEditorResultsNavigationKeepsBothDraftsAndRevokesOldKeyboardAndSendFrames(TextSize size, bool keyboard)
    {
        var snapshot = Samples.Snapshot(6, new[] { Samples.Workstream(TaskId, executionId: RunId) }, new[] { Samples.Execution(RunId, TaskId, ExecutionStatus.Running) });
        snapshot.Runtimes[0].Capabilities.InstructWhileRunning = true;
        var state = new ClientProjection(); state.ApplySnapshot(snapshot, new StateChanges());
        var host = new FileMenuHost { TextSize = size, Projection = state }; var work = state.Workstreams[TaskId];
        var file = new FileColumn(host, () => WorkspacePresenter.Present(work, state, new ActivityLog(), true), new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        var navigator = new MenuNavigator(MenuBar.Places.ToDictionary(place => place, place => (Func<IMenuColumn>)(() => file)));
        TaskResultsColumn? results = null;
        file.OfferResults(() =>
        {
            results = new TaskResultsColumn(host, Context(), "mock", "Mock runtime", () => navigator.Beside == results && !navigator.IsCompact,
                _ => Task.FromResult(Listing()), (_, _) => Task.FromResult(Output()));
            results.BackToTask = () => navigator.ShowBeside(file, TaskId);
            navigator.ShowBeside(results, TaskId);
        });
        var context = Context();
        context.Instruction = new WorkspaceInstructionDraft { Text = string.Join(" ", Enumerable.Repeat("Preserve the exact ordinary words", 50)), Spoken = true, Part = 2 };
        context.Request = new WorkspaceRequestContinuation { Text = "Preserve the separate request words", Spoken = true, Part = 3,
            RequestId = EventId, PendingCommandId = "550e8400-e29b-41d4-a716-446655440000" };
        Assert.That(file.RestoreContinuation(context), Is.True);
        navigator.ShowBeside(file, TaskId);
        file.Act(FileScreens.OpenResults, null); Assert.That(results, Is.Null, "an undrawn editor route cannot navigate");
        file.Drawn(file.Frame!, null); file.Drawn(file.Frame!, null);
        if (keyboard) { file.Act(FileScreens.EditInstruction, null); file.Drawn(file.Frame!, null); file.Drawn(file.Frame!, null); }
        var before = HalcyonicJson.Serialize(file.ContinuationDraft()!); var requestBefore = HalcyonicJson.Serialize(file.ContinuationRequest()!);
        var sendFrame = file.Frame!;
        file.Act(FileScreens.OpenResults, null);
        Assert.That(navigator.Beside, Is.SameAs(results));
        host.Keyboard?.Done("STALE KEYBOARD WORDS"); file.Heard("STALE VOICE WORDS"); file.Act(FileScreens.SendInstruction, null);
        _ = results!.Frame; results.Tick(); Draw(results); results.Act(Footer.Close, null);
        Assert.That(navigator.Beside, Is.SameAs(file));
        Assert.That(HalcyonicJson.Serialize(file.ContinuationDraft()!), Is.EqualTo(before));
        Assert.That(HalcyonicJson.Serialize(file.ContinuationRequest()!), Is.EqualTo(requestBefore));
        file.Drawn(sendFrame, null); file.Act(FileScreens.SendInstruction, null);
        Assert.That(host.Sent, Is.Empty); Assert.That(file.Steering.Armed, Is.Null);
        Assert.That(file.Frame!.Lines.Any(line => line.Action == FileScreens.OpenResults), Is.True);
        Assert.That(file.Frame.Footer.All.Count(), Is.LessThanOrEqualTo(4));
    }

    [TestCase(ToolResultPathScope.OutsideProject, "Outside project")]
    [TestCase(ToolResultPathScope.Unknown, "Path unknown")]
    public void OmittedPathsNeverPretendToBeShortenedPaths(ToolResultPathScope scope, string label)
    {
        var response = Output(); response.Summary.Kind = ToolResultSummaryKind.FileChanges; response.Summary.ExitCode = null;
        response.Summary.FilesTotal = 1; response.Summary.BodyAvailable = false; response.Summary.RetainedComplete = false;
        response.Result = new FileChangesToolResult { ToolCallId = "patch-1", TurnId = "turn-1", Completion = ToolResultCompletion.Completed,
            Redaction = new ToolResultRedaction(), FilesTotal = 1, ListComplete = true, Files = new List<ToolResultFile>
            { new() { Kind = ToolResultFileKind.Update, Path = null, PathScope = scope, PathComplete = false,
                Patch = new ToolResultBody { Availability = ToolResultBodyAvailability.OmittedScope, Truncation = ToolResultBodyTruncation.Scope, Complete = false } } } };
        var listing = Listing(); listing.Results[0] = response.Summary;
        var column = Open(new FileMenuHost(), listing, response); Read(column); ReadFile(column);
        Assert.That(Words(column), Does.Contain(label)); Assert.That(Words(column), Does.Not.Contain("Path shortened"));
        Assert.That(Words(column), Does.Contain("files may have changed since"));
        if (scope == ToolResultPathScope.Unknown)
        { Assert.That(Words(column), Does.Contain("project scope is unknown")); Assert.That(Words(column), Does.Not.Contain("Patch outside")); }
    }

    [TestCase(ToolResultsAvailability.Available, "recorded yet")]
    [TestCase(ToolResultsAvailability.Unknown, "support is unknown")]
    [TestCase(ToolResultsAvailability.Unsupported, "does not record")]
    [TestCase(ToolResultsAvailability.Unavailable, "app is unavailable")]
    public void EmptyHistoryDoesNotInventCurrentRecordingSupport(ToolResultsAvailability availability, string words)
    {
        var listing = Listing(); listing.Results.Clear(); listing.Total = 0; listing.Availability = availability;
        Assert.That(Words(Open(new FileMenuHost(), listing)), Does.Contain(words));
    }

    [Test]
    public void UndefinedAvailabilityNeverReadsAsAvailable()
    {
        var listing = Listing(); listing.Availability = (ToolResultsAvailability)99;
        var column = Open(new FileMenuHost(), listing);
        Assert.That(column.Frame!.Lines.Any(line => line.Action == TaskResultsColumn.Read), Is.False);
        Assert.That(Words(column), Does.Contain("could not be read"));
    }

    [TestCase(ToolResultBodyAvailability.Absent, ToolResultBodyTruncation.Absent, "not supplied")]
    [TestCase(ToolResultBodyAvailability.SourceOverLimit, ToolResultBodyTruncation.SourceLimit, "too large")]
    [TestCase(ToolResultBodyAvailability.Malformed, ToolResultBodyTruncation.Malformed, "could not be read")]
    [TestCase(ToolResultBodyAvailability.Absent, ToolResultBodyTruncation.AggregateBound, "limit saved result size")]
    public void BodyOmissionShowsItsRecordedReason(ToolResultBodyAvailability availability, ToolResultBodyTruncation truncation, string reason)
    {
        var response = Output(); var command = (CommandToolResult)response.Result;
        command.Output = new ToolResultBody { Text = null, Availability = availability, Truncation = truncation, Complete = false };
        response.Summary.BodyAvailable = false; response.Summary.RetainedComplete = false;
        var listing = Listing(); listing.Results[0] = response.Summary;
        var column = Open(new FileMenuHost(), listing, response); Read(column);
        Assert.That(Words(column), Does.Contain(reason));
        Assert.That(Words(column), Does.Not.Contain("tests passed"));
    }
}
