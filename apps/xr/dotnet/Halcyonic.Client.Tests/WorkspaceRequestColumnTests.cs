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
        Assert.That(column.Frame!.Lines.Single().Words, Does.Contain("another task or run"));
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
}
