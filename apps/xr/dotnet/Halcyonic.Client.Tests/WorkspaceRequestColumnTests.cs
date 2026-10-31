using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class WorkspaceRequestColumnTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);
    private const string TaskId = "0192f1a0-0000-7000-8000-000000000101", RunId = "0192f1a0-0000-7000-8000-000000000102",
        RequestId = "0192f1a0-0000-7000-8000-000000000104";
    private static WorkspaceContinuation Context() => new()
    { HandoffId = Guid.NewGuid().ToString("D"), ConnectionKey = new string('a', 64), JournalId = Samples.JournalId,
        ProjectId = Samples.ProjectId, WorkstreamId = TaskId, ExecutionId = RunId, Surface = WorkspaceSurface.Request };
    private static Task<WorkspaceAgentStatus> Offered(CancellationToken _) => Task.FromResult<WorkspaceAgentStatus>(new AvailableWorkspaceAgent { Model = "Local model" });
    private static WorkspaceRequestView Result(WorkspaceContinuation context) => new()
    { RequestId = RequestId, JournalId = context.JournalId!, ProjectId = context.ProjectId!, WorkstreamId = context.WorkstreamId!,
        ExecutionId = context.ExecutionId!, OriginalText = "Inspect this task", Status = new JValue("responded"),
        Reply = new WorkspaceRequestReply { Text = "Here is my interpretation.", Model = "Local model" }, CreatedAt = Samples.Time, UpdatedAt = Samples.Time };
    private static void Draw(FileMenuHost host, WorkspaceRequestColumn column)
    { var frame = column.Frame!; column.Drawn(frame, null); host.Wait(0.5); }
    private static void Read(FileMenuHost host, WorkspaceRequestColumn column)
    {
        for (var tries = 0; tries < 10000; tries++)
        {
            Draw(host, column);
            var frame = column.Frame!;
            if (frame.Lines.Any(line => line.Action == WorkspaceRequestColumn.Next && line.Available))
            { Draw(host, column); column.Act(WorkspaceRequestColumn.Next, null); continue; }
            Draw(host, column); return;
        }
        Assert.Fail("The actual offered request pager did not reach its last part.");
    }
    private static void Write(FileMenuHost host, WorkspaceRequestColumn column, string text)
    { Draw(host, column); column.Act(WorkspaceRequestColumn.Edit, null); host.Keyboard!.Value.Done(text); }
    private static WorkspaceRequestColumn Open(FileMenuHost host, WorkspaceContinuation? context = null, Func<bool>? current = null,
        Func<string, CancellationToken, Task<WorkspaceRequestResponse>>? read = null)
    {
        var column = new WorkspaceRequestColumn(host, Commands, context ?? Context(), current ?? (() => true), Offered, read);
        column.BeforeSubmit = _ => true;
        _ = column.Frame; column.Tick(); return column;
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void AnOpenEndedTypedDraftSendsOnlyFromExplicitCurrentReadStart(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var column = Open(host);
        var text = string.Join(" ", Enumerable.Repeat("Compare the current approach, inspect the relevant files and propose a correction.", 20));
        Write(host, column, text);
        column.Act(WorkspaceRequestColumn.Start, null);
        Assert.That(host.Sent, Is.Empty, "typing and an undrawn stale Start do not submit");
        Read(host, column);
        column.Act(WorkspaceRequestColumn.Start, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<WorkspaceRequestSubmitCommand>());
        var payload = ((WorkspaceRequestSubmitCommand)host.Sent.Single()).Payload;
        Assert.That((payload.ProjectId, payload.WorkstreamId, payload.ExecutionId, payload.Text), Is.EqualTo((Samples.ProjectId, TaskId, RunId, text)));
        Draw(host, column); column.Act(WorkspaceRequestColumn.Start, null); column.Tick();
        Assert.That(host.Sent, Has.Count.EqualTo(1), "accepted is pending recorded outcome, never a second submit");
        Assert.That(column.Frame!.Footer.All.Where(each => each.Prompt.Id == WorkspaceRequestColumn.Start).Select(each => each.Prompt.Available), Has.All.False);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void RestoredSpokenWordsNeedFreshReadAndExplicitReviewAtTheCurrentSize(TextSize size)
    {
        var host = new FileMenuHost { TextSize = size };
        var context = Context(); context.Request = new WorkspaceRequestContinuation { Text = "Inspect this task and fix the issue", Spoken = true, Part = 0 };
        var column = Open(host, context);
        column.Act(WorkspaceRequestColumn.Start, null);
        Assert.That(host.Sent, Is.Empty);
        Read(host, column); column.Act(WorkspaceRequestColumn.Start, null);
        Assert.That(host.Sent, Is.Empty, "spoken Start only opens the review");
        var yes = column.Frame!;
        Assert.That(yes.Footer.All.Any(each => each.Prompt.Id == WorkspaceRequestColumn.Yes), Is.True);
        column.Act(WorkspaceRequestColumn.Yes, null);
        Assert.That(host.Sent, Is.Empty, "the new Yes has not actually drawn");
        column.FocusLeft(); column.Act(WorkspaceRequestColumn.Yes, null);
        Assert.That(host.Sent, Is.Empty, "focus departure revokes the review");
        _ = column.Frame; column.Tick(); Read(host, column); column.Act(WorkspaceRequestColumn.Start, null);
        Draw(host, column); column.Act(WorkspaceRequestColumn.Yes, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<WorkspaceRequestSubmitCommand>());
    }

    [Test]
    public void ForeignTupleUnderTheSameOwnedReferenceNeverOffersResumeOrCancel()
    {
        var host = new FileMenuHost(); var context = Context();
        context.Request = new WorkspaceRequestContinuation { RequestId = RequestId, Text = "My retained local draft" };
        var foreign = Result(context); foreign.WorkstreamId = "0192f1a0-0000-7000-8000-000000000199";
        var column = Open(host, context, read: (_, _) => Task.FromResult(new WorkspaceRequestResponse { Request = foreign }));
        Draw(host, column);
        Assert.That(column.Frame!.Lines.Single().Words, Does.Contain("Another Task or Run"));
        Assert.That(column.Frame.Footer.All.Any(each => each.Prompt.Id == WorkspaceRequestColumn.Resume || each.Prompt.Id == WorkspaceRequestColumn.Stop), Is.False);
        column.Act(WorkspaceRequestColumn.Resume, null); column.Act(WorkspaceRequestColumn.Stop, null);
        Assert.That(host.Sent, Is.Empty); Assert.That(column.Draft.Text, Is.EqualTo("My retained local draft"));
    }

    [Test]
    public void UnavailableOfferReadsOnceAndKeepsPrivateErrorOutOfTheView()
    {
        var host = new FileMenuHost(); var calls = 0;
        var column = new WorkspaceRequestColumn(host, Commands, Context(), () => true, _ =>
        { calls++; return Task.FromException<WorkspaceAgentStatus>(new Exception("PRIVATE transport credential and text")); });
        for (var tick = 0; tick < 20; tick++) { _ = column.Frame; column.Tick(); }
        Write(host, column, "Retain this request locally");
        Read(host, column); column.Act(WorkspaceRequestColumn.Start, null);
        Assert.That(calls, Is.EqualTo(1), "unavailability is not a timer retry or model invocation");
        Assert.That(host.Sent, Is.Empty);
        Assert.That(column.Frame!.Footer.All.Select(each => each.Prompt.Reason).Where(reason => reason != null), Has.None.Contains("PRIVATE"));
        Assert.That(column.Draft.Text, Is.EqualTo("Retain this request locally"));
    }

    [Test]
    public void DepartedKeyboardAndVoiceCallbacksCannotChangeTheRetainedDraftOrSend()
    {
        var host = new FileMenuHost(); var current = true;
        var column = Open(host, current: () => current);
        Write(host, column, "Keep these words");
        Draw(host, column); column.Act(WorkspaceRequestColumn.Edit, null); var late = host.Keyboard!.Value.Done;
        column.FocusLeft(); late("Late keyboard words");
        Assert.That(column.Draft.Text, Is.EqualTo("Keep these words"));
        _ = column.Frame; column.Tick(); Draw(host, column); column.HoldStarted(WorkspaceRequestColumn.Talk); column.HoldEnded(WorkspaceRequestColumn.Talk, true);
        current = false; column.Tick(); column.Heard("Late voice words");
        Assert.That(column.Draft.Text, Is.EqualTo("Keep these words"));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void AVerifiedReplyCanContinueDeliberatelyAndCancelDoesNotInterruptTheExecutor()
    {
        var host = new FileMenuHost(); var context = Context();
        context.Request = new WorkspaceRequestContinuation { RequestId = RequestId, Text = "Continue with the correction" };
        var result = Result(context);
        var column = Open(host, context, read: (_, _) => Task.FromResult(new WorkspaceRequestResponse { Request = result }));
        Draw(host, column); column.Act(WorkspaceRequestColumn.Resume, null);
        Read(host, column); column.Act(WorkspaceRequestColumn.Resume, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<WorkspaceRequestResumeCommand>());
        Assert.That(((WorkspaceRequestResumeCommand)host.Sent.Single()).Payload.RequestId, Is.EqualTo(RequestId));
        var stopHost = new FileMenuHost(); result.Status = new JValue("running");
        var stop = Open(stopHost, context, read: (_, _) => Task.FromResult(new WorkspaceRequestResponse { Request = result }));
        Draw(stopHost, stop); stop.Act(WorkspaceRequestColumn.Stop, null);
        Assert.That(stopHost.Sent.Single(), Is.InstanceOf<WorkspaceRequestCancelCommand>());
        Assert.That(stopHost.Sent.OfType<ExecutionInterruptCommand>(), Is.Empty);
    }
    [Test]
    public void SectionDepartureRevokesTheActualOutstandingKeyboardAndVoiceDestinations()
    {
        var host = new FileMenuHost(); var column = Open(host);
        Write(host, column, "Keep the latest local request");
        Draw(host, column); column.Act(WorkspaceRequestColumn.Edit, null); var keyboard = host.Keyboard!.Value.Done;
        Draw(host, column); column.Act(MenuFrame.ChooseSection, "response");
        keyboard("Late hidden keyboard words");
        Assert.That(column.Draft.Text, Is.EqualTo("Keep the latest local request"));
        Draw(host, column); column.Act(MenuFrame.ChooseSection, "draft");
        Draw(host, column);
        var busy = false; var drops = 0;
        var voice = new MenuVoice(() => busy, () => busy = true, () => { }, () => { busy = false; drops++; });
        column.InputContextLeft = () => voice.Left(column);
        Assert.That(voice.Hold(column, WorkspaceRequestColumn.Talk), Is.True);
        var transcription = voice.CaptureHeard();
        Draw(host, column); column.Act(MenuFrame.ChooseSection, "response");
        transcription("Late hidden voice words"); column.Heard("Another late transcription");
        Assert.That(voice.Speaking, Is.Null, "the production-used callback drops MenuVoice's destination too");
        Assert.That(drops, Is.EqualTo(1));
        Assert.That(column.Draft.Text, Is.EqualTo("Keep the latest local request"));
        Assert.That(host.Sent, Is.Empty);
    }

    private sealed class PendingHost : IMenuHost
    {
        public FileMenuHost View { get; } = new();
        public ClientProjection? State { get; set; }
        public Func<CommandEnvelope, Task<CommandAckMessage>>? Ack { get; set; }
        public Action? Posting { get; set; }
        public bool Connected => View.Connected;
        public bool Demonstration => false;
        public double Now => View.Now;
        public DateTimeOffset Clock => View.Clock;
        public TimeZoneInfo Zone => View.Zone;
        public TextSize TextSize => View.TextSize;
        public bool VoiceOffered => View.VoiceOffered;
        public ControlPlaneApi? Api => null;
        public bool KeyboardOffered => View.KeyboardOffered;
        public Task<CommandAckMessage>? Submit(CommandEnvelope command)
        {
            Posting?.Invoke();
            if (Ack == null) return View.Submit(command);
            View.Sent.Add(command); return Ack(command);
        }
        public void OpenKeyboard(string text, string prompt, Action<string> done, Action? dismissed = null) => View.OpenKeyboard(text, prompt, done, dismissed);
        public int RowsOf(string words, float degrees) => View.RowsOf(words, degrees);
        public int RowsOf(PageLine line, float degrees) => View.RowsOf(line, degrees);
        public bool FitsHalf(PageLine line, float degrees) => View.FitsHalf(line, degrees);
        public int TitleRows(string words, float degrees) => View.TitleRows(words, degrees);
        public int PageRows(bool source) => View.PageRows(source);
        public float PageHeight(int rows, bool besideMenu, bool focused = false) => View.PageHeight(rows, besideMenu, focused);
        public void OpenFile(string id) { }
        public void OpenNewProject(string? id, string? name) { }
    }
    private static WorkspaceCommandResponse Created(WorkspaceContinuation context, string commandId) => new()
    {
        JournalId = context.JournalId!, Command = new CommandView
        {
            CommandId = commandId, CommandType = CommandType.WorkspaceRequestSubmit, Status = CommandStatus.Completed,
            ProjectId = context.ProjectId, WorkstreamId = context.WorkstreamId, ExecutionId = context.ExecutionId,
            IssuedAt = Samples.Time, UpdatedAt = Samples.Time, Result = new WorkspaceRequestCreatedResult { RequestId = RequestId },
        },
    };

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void TheRequestNamesItsActualBoundTaskAsOutsideText(TextSize size)
    {
        var context = Context();
        var task = Samples.Workstream(TaskId, executionId: RunId);
        task.Title = "Review <b>authentication</b> and the deployment checklist";
        var state = new ClientProjection();
        state.ApplySnapshot(Samples.Snapshot(1, new[] { task }, new[] { Samples.Execution(RunId, TaskId, ExecutionStatus.Running) }), new StateChanges());
        var host = new PendingHost { State = state }; host.View.TextSize = size;
        var column = new WorkspaceRequestColumn(host, Commands, context, () => true, Offered);
        var frame = column.Frame!;
        Assert.That(frame.Subject, Is.EqualTo(LabelText.Name(task.Title)));
        Assert.That(frame.SubjectRuns.AllTheirs, Is.True, "the actual task title is data, never an invented destination label");
        Assert.That(frame.Sections.Select(section => section.Words), Is.EqualTo(new[] { "Request", "Reply", "Actions" }));
        Assert.That(host.View.Sent, Is.Empty);
    }

    [Test]
    public void FailedDurableCaptureBlocksPostAndSuccessfulCapturePrecedesTheExactPost()
    {
        var host = new PendingHost(); var context = Context();
        context.Request = new WorkspaceRequestContinuation { Text = "Inspect this task before changing it" };
        var column = new WorkspaceRequestColumn(host, Commands, context, () => true, Offered);
        var saved = false; string? captured = null;
        column.BeforeSubmit = draft =>
        {
            Assert.That(host.View.Sent, Is.Empty, "the private write must precede POST");
            Assert.That(draft.PendingCommandId, Is.Not.Null);
            captured = draft.PendingCommandId; return saved;
        };
        _ = column.Frame; column.Tick(); Read(host.View, column);
        column.Act(WorkspaceRequestColumn.Start, null);
        Assert.That(host.View.Sent, Is.Empty);
        Assert.That(column.Draft.Text, Is.EqualTo(context.Request.Text));
        Assert.That(column.Draft.PendingCommandId, Is.Null, "a failed save proves POST was never attempted");
        saved = true;
        Draw(host.View, column); column.Act(WorkspaceRequestColumn.Refresh, null); _ = column.Frame; column.Tick();
        Read(host.View, column); column.Act(WorkspaceRequestColumn.Start, null);
        Assert.That(host.View.Sent.Single().CommandId, Is.EqualTo(captured));
        Assert.That(Guid.Parse(captured!).ToString("D"), Is.EqualTo(captured), "the actual factory UUIDv4 is retained verbatim");
        Assert.That(column.Draft.PendingCommandId, Is.EqualTo(captured));
    }

    [TestCase(CommandAckDisposition.Duplicate)]
    [TestCase(CommandAckDisposition.Rejected)]
    [TestCase(CommandAckDisposition.Conflict)]
    public void AnAcknowledgementCannotClearTheOriginalPendingIdentityOrMintAnotherSubmit(CommandAckDisposition disposition)
    {
        var host = new PendingHost(); var context = Context(); context.Request = new WorkspaceRequestContinuation { Text = "Keep the original request identity" };
        host.Ack = command => Task.FromResult(new CommandAckMessage
        { CommandId = command.CommandId, Disposition = disposition, Command = Created(context, command.CommandId).Command });
        var column = new WorkspaceRequestColumn(host, Commands, context, () => true, Offered,
            readCommand: (_, _) => Task.FromException<WorkspaceCommandResponse>(new Exception("PRIVATE failure"))) { BeforeSubmit = _ => true };
        _ = column.Frame; column.Tick(); Read(host.View, column); column.Act(WorkspaceRequestColumn.Start, null);
        var sent = host.View.Sent.Single().CommandId;
        for (var turn = 0; turn < 4; turn++) { _ = column.Frame; column.Tick(); Draw(host.View, column); column.Act(WorkspaceRequestColumn.Start, null); }
        Assert.That(column.Draft.PendingCommandId, Is.EqualTo(sent));
        Assert.That(column.RequestId, Is.Null, "even a complete duplicate ack is resolved through original-owner GET");
        Assert.That(host.View.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void RecreationResolvesTheSavedUuidByExactOwnerReadWithoutPostingAgain()
    {
        var context = Context(); context.Request = new WorkspaceRequestContinuation { Text = "Recover this exact request" };
        var firstHost = new PendingHost { Ack = _ => Task.FromException<CommandAckMessage>(new Exception("lost response")) };
        WorkspaceContinuation? stored = null;
        var first = new WorkspaceRequestColumn(firstHost, Commands, context, () => true, Offered);
        first.BeforeSubmit = draft =>
        {
            context.Request = draft;
            Assert.That(WorkspaceContinuity.TryParse(HalcyonicJson.Serialize(context), out stored), Is.True); return true;
        };
        _ = first.Frame; first.Tick(); Read(firstHost.View, first); first.Act(WorkspaceRequestColumn.Start, null);
        var pending = firstHost.View.Sent.Single().CommandId;
        Assert.That(stored!.Request!.PendingCommandId, Is.EqualTo(pending));
        var recreatedHost = new PendingHost(); var reads = 0;
        var recreated = new WorkspaceRequestColumn(recreatedHost, Commands, stored, () => true, Offered,
            readCommand: (id, _) => { reads++; Assert.That(id, Is.EqualTo(pending)); return Task.FromResult(Created(stored, id)); });
        _ = recreated.Frame; recreated.Tick();
        Assert.That(recreated.RequestId, Is.EqualTo(RequestId));
        Assert.That(recreated.Draft.PendingCommandId, Is.Null);
        Assert.That(recreated.Draft.Text, Is.EqualTo("Recover this exact request"));
        Assert.That((reads, recreatedHost.View.Sent.Count), Is.EqualTo((1, 0)));
    }

    [TestCase("journal")]
    [TestCase("task")]
    [TestCase("execution")]
    [TestCase("id")]
    [TestCase("type")]
    [TestCase("unknown")]
    [TestCase("not-found")]
    public void AForeignOrUnresolvedCommandNeverDropsPendingAuthority(string change)
    {
        var context = Context(); var pending = Guid.NewGuid().ToString("D");
        context.Request = new WorkspaceRequestContinuation { Text = "Retain my pending words", PendingCommandId = pending };
        var response = Created(context, pending);
        if (change == "journal") response.JournalId = "0192f1a0-0000-7000-8000-000000000111";
        if (change == "task") response.Command.WorkstreamId = "0192f1a0-0000-7000-8000-000000000112";
        if (change == "execution") response.Command.ExecutionId = "0192f1a0-0000-7000-8000-000000000113";
        if (change == "id") response.Command.CommandId = Guid.NewGuid().ToString("D");
        if (change == "type") response.Command.CommandType = CommandType.ExecutionInterrupt;
        if (change == "unknown") { response.Command.Status = CommandStatus.Failed; response.Command.Failure = new CommandFailure { Code = "unavailable", Message = "PRIVATE", Effect = FailureEffect.Unknown }; }
        var host = new PendingHost();
        var column = new WorkspaceRequestColumn(host, Commands, context, () => true, Offered,
            readCommand: (_, _) => change == "not-found" ? Task.FromException<WorkspaceCommandResponse>(new Exception("PRIVATE")) : Task.FromResult(response));
        _ = column.Frame; column.Tick(); Draw(host.View, column); column.Act(WorkspaceRequestColumn.Start, null);
        Assert.That((column.RequestId, column.Draft.PendingCommandId, column.Draft.Text), Is.EqualTo(((string?)null, pending, "Retain my pending words")));
        Assert.That(host.View.Sent, Is.Empty);
        Assert.That(column.Frame!.Footer.All.Where(each => each.Prompt.Reason != null).Select(each => each.Prompt.Reason), Has.None.Contains("PRIVATE"));
    }

    [Test]
    public void NormalActionsUseMeaningfulStatusAndOmitInternalCursorOrReceiptTerms()
    {
        var context = Context(); context.Request = new WorkspaceRequestContinuation { RequestId = RequestId, Text = "Retained words" };
        var response = Result(context);
        response.Steps.Add(new WorkspaceRequestStep { Status = new JValue("prepared"), Summary = "An instruction is ready for dispatch.", ObservationPosition = 987654 });
        response.Steps.Add(new WorkspaceRequestStep { Status = new JValue("unknown"), Summary = "Its outcome remains unknown.", ObservationPosition = 987655 });
        var host = new FileMenuHost(); var column = Open(host, context, read: (_, _) => Task.FromResult(new WorkspaceRequestResponse { Request = response }));
        Draw(host, column); column.Act(MenuFrame.ChooseSection, "receipts");
        var words = column.Frame!.Lines.Single().Words;
        Assert.That(column.Frame!.Sections.Single(section => section.Chosen).Words, Is.EqualTo("Actions"));
        Assert.That(words, Does.Contain("Not Sent").And.Contain("Unknown").And.Not.Contain("Actions"));
        Assert.That(words, Does.Not.Contain("position").And.Not.Contain("receipt").And.Not.Contain("987654"));
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void ActionsDistinguishTurnReadAndPersonDirectionWithoutClaimingVerifiedChanges(TextSize size)
    {
        var context = Context(); context.Request = new WorkspaceRequestContinuation { RequestId = RequestId, Text = "Retained words" };
        var response = Result(context);
        response.Steps.Add(new WorkspaceRequestStep { ToolName = "continue_task", Status = new JValue("confirmed"), CommandId = Guid.NewGuid().ToString("D"), RuntimeStatus = ExecutionStatus.Completed, Summary = "A relevant native turn ended; software correctness remains unverified." });
        response.Steps.Add(new WorkspaceRequestStep { ToolName = "read_results", Status = new JValue("confirmed"), Summary = "Only command results were read; patch coverage remains unknown." });
        response.Steps.Add(new WorkspaceRequestStep { ToolName = "person_response", Status = new JValue("confirmed"), CommandId = Guid.NewGuid().ToString("D"), Summary = "The person supplied additional direction." });
        var host = new FileMenuHost { TextSize = size };
        var column = Open(host, context, read: (_, _) => Task.FromResult(new WorkspaceRequestResponse { Request = response }));
        Draw(host, column); column.Act(MenuFrame.ChooseSection, "receipts");
        var words = column.Frame!.Lines.Single().Words;
        Assert.That(words, Does.Contain("Round Ended").And.Contain("Changes Unverified").And.Contain("Read Confirmed").And.Contain("Direction Recorded"));
        Assert.That(words, Does.Not.Contain("\nConfirmed\n"));
        Assert.That(host.Sent, Is.Empty);
        var starts = new System.Collections.Generic.HashSet<int>();
        for (var page = 0; page < 32; page++)
        {
            var frame = column.Frame!;
            var line = frame.Lines.Single();
            Assert.That(line.Rows, Is.GreaterThan(0));
            Assert.That(line.Words, Is.EqualTo(words), "paging never shortens the source-verification caveat or summaries");
            var start = line.FromRow ?? 0;
            if (!starts.Add(start)) { Assert.That(start, Is.Zero); break; }
            if (!frame.Footer.All.Any(prompt => prompt.Prompt.Id == WorkspaceRequestColumn.Next && prompt.Prompt.Available)) break;
            Draw(host, column); column.Act(WorkspaceRequestColumn.Next, null);
            Assert.That(page, Is.LessThan(31), "the size-specific measured pager must wrap finitely");
        }
    }

}
