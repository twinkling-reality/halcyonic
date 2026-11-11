using System;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>Ask beside the menu: the column's frame, its presses, and the redesign's reader of it.</summary>
public class AskMenuColumnTests
{
    private const string ProjectId = "01a19000-0000-7000-8000-000000000001";
    private const string RequestId = "01a19000-0000-7000-8000-000000000004";
    private readonly CommandFactory commands = new(Samples.Client, () => new DateTimeOffset(2026, 11, 11, 7, 0, 0, TimeSpan.Zero));

    private (AskMenuColumn Column, FileMenuHost Host) Make()
    {
        var host = new FileMenuHost();
        var column = new AskMenuColumn(host, commands, AskScope.Project(ProjectId, "Kite Run"));
        column.Ask.Offer(new AvailableWorkspaceAgent { Model = "muse-glimmer:30b-mlx" });
        return (column, host);
    }

    private static string[] Ids(MenuFrame frame) => frame.Footer.All.Select(each => each.Prompt).Where(prompt => prompt.Available).Select(prompt => prompt.Id).ToArray();

    private void Held(AskMenuColumn column, string id, WorkspaceRequestView view)
    {
        var ack = new CommandAckMessage
        {
            CommandId = id,
            Disposition = CommandAckDisposition.Accepted,
            Command = new CommandView { CommandId = id, Status = CommandStatus.Completed, Result = new WorkspaceRequestCreatedResult { RequestId = RequestId } },
        };
        column.Ask.Acknowledge(ack);
        column.Ask.Read(view);
    }

    private static WorkspaceRequestView View(string status, string? reply = null) => new()
    {
        RequestId = RequestId,
        JournalId = "01a19000-0000-7000-8000-0000000000aa",
        ProjectId = ProjectId,
        OriginalText = "What is going on?",
        Status = new JValue(status),
        Reply = reply == null ? null : new WorkspaceRequestReply { Text = reply, Model = "muse-glimmer:30b-mlx" },
        CreatedAt = "2026-11-11T07:00:00.000Z",
        UpdatedAt = "2026-11-11T07:00:00.000Z",
    };

    [Test]
    public void WritingOffersTalkAndTypeAndSendsOnlyOnThePressWithWords()
    {
        var (column, host) = Make();
        var frame = column.Frame!;
        Assert.That(frame.Card, Is.Not.Null);
        Assert.That(Ids(frame), Does.Contain(AskMenuColumn.Talk));
        Assert.That(Ids(frame), Does.Not.Contain(AskMenuColumn.Send), "nothing to send yet");
        Assert.That(frame.Lines.Single().Action, Is.EqualTo(AskMenuColumn.Type));
        column.Act(AskMenuColumn.Type, null);
        host.Keyboard!.Value.Done("What is going on?");
        Assert.That(column.Ask.Draft, Is.EqualTo("What is going on?"));
        Assert.That(host.Sent, Is.Empty, "typing sends nothing");
        Assert.That(Ids(column.Frame!), Does.Contain(AskMenuColumn.Send));
        column.Act(AskMenuColumn.Send, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<WorkspaceRequestSubmitCommand>());
        Assert.That(column.Ask.Stage, Is.EqualTo(AskStage.Sent));
        column.Act(AskMenuColumn.Send, null);
        Assert.That(host.Sent.Count, Is.EqualTo(1), "sent once");
    }

    [Test]
    public void TheAssistantsQuestionStandsOnTheBandAndTheAnswerGoesAsAResume()
    {
        var (column, host) = Make();
        column.Ask.Write("Is it working?");
        column.Act(AskMenuColumn.Send, null);
        Held(column, host.Sent.Single().CommandId, View("waiting_for_human", "Which page do you mean?"));
        var frame = column.Frame!;
        Assert.That(frame.Lines.Single(line => line.Claim).Words, Is.EqualTo("Which page do you mean?"));
        Assert.That(Ids(frame), Does.Contain(AskMenuColumn.Stop));
        Assert.That(Ids(frame), Does.Not.Contain(AskMenuColumn.Answer), "no answer written yet");
        column.HoldStarted(AskMenuColumn.Talk);
        column.Heard("The home page");
        Assert.That(column.AnswerDraft, Is.EqualTo("The home page"));
        column.Act(AskMenuColumn.Answer, null);
        var resume = host.Sent.Last() as WorkspaceRequestResumeCommand;
        Assert.That(resume, Is.Not.Null);
        Assert.That(resume!.Payload.RequestId, Is.EqualTo(RequestId));
        Assert.That(resume.Payload.Text, Is.EqualTo("The home page"));
        var bar = RoomBar.OfFrame(column.Frame!, VoiceStage.Idle);
        Assert.That(bar!.Band, Is.EqualTo("Which page do you mean?"));
        Assert.That(bar.Card, Is.SameAs(column.Frame!.Card).Or.Not.Null);
    }

    [Test]
    public void StopCancelsWhileItWorksAndTheReplyIsTheBandsNeverTheCards()
    {
        var (column, host) = Make();
        column.Ask.Write("Is it working?");
        column.Act(AskMenuColumn.Send, null);
        Held(column, host.Sent.Single().CommandId, View("running"));
        column.Act(AskMenuColumn.Stop, null);
        Assert.That(host.Sent.Last(), Is.InstanceOf<WorkspaceRequestCancelCommand>());
        column.Ask.Read(View("responded", "It is blue now."));
        var bar = RoomBar.OfFrame(column.Frame!, VoiceStage.Idle)!;
        Assert.That(bar.Band, Is.EqualTo("It is blue now."));
        Assert.That(bar.Card!.Words(), Has.None.Contains("It is blue now"));
        Assert.That(bar.Column.Select(action => action.Control.Action), Is.EqualTo(new[] { Footer.Close }), "answered: only Close");
    }

    [Test]
    public void TheReaderPutsTheMainActionFirstAndCloseLast()
    {
        var (column, _) = Make();
        column.Ask.Write("What is going on?");
        var bar = RoomBar.OfFrame(column.Frame!, VoiceStage.Idle)!;
        var ids = bar.Column.Select(action => action.Control.Action).ToArray();
        Assert.That(ids.First(), Is.EqualTo(AskMenuColumn.Send));
        Assert.That(ids.Last(), Is.EqualTo(Footer.Close));
        Assert.That(ids, Does.Contain(AskMenuColumn.Talk).And.Contain(AskMenuColumn.Type));
        Assert.That(bar.Card!.AnyDot, Is.False);
        var closed = false;
        column.Closed += () => closed = true;
        column.Act(Footer.Close, null);
        Assert.That(closed, Is.True);
    }

    [Test]
    public void ARefusedAnswerIsKeptAndSaysItDidNotGo()
    {
        // AUDIT SHOULD 1.
        var (column, host) = Make();
        column.Ask.Write("Is it working?");
        column.Act(AskMenuColumn.Send, null);
        Held(column, host.Sent.Single().CommandId, View("waiting_for_human", "Which page?"));
        host.Refuse = true;
        column.HoldStarted(AskMenuColumn.Talk);
        column.Heard("The home page");
        column.Act(AskMenuColumn.Answer, null);
        column.Tick();
        Assert.That(column.AnswerDraft, Is.EqualTo("The home page"), "the refused answer is kept");
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Does.Contain(AskMenuColumn.Words.AnswerDidNotGo));
    }

    [Test]
    public void WithNoAssistantThereIsNothingToWriteOrSay()
    {
        // AUDIT SHOULD 2.
        var host = new FileMenuHost();
        var column = new AskMenuColumn(host, commands, AskScope.AllWork());
        column.Ask.Offer(new UnavailableWorkspaceAgent { Reason = new ErrorInfo { Code = "not_configured", Message = "No model." } });
        var frame = column.Frame!;
        Assert.That(Ids(frame), Does.Not.Contain(AskMenuColumn.Talk));
        Assert.That(frame.Lines.Any(line => line.Action == AskMenuColumn.Type), Is.False);
        column.Ask.Offer(new AvailableWorkspaceAgent { Model = "muse-glimmer:30b-mlx" });
        Assert.That(Ids(column.Frame!), Does.Contain(AskMenuColumn.Talk));
    }

    [Test]
    public void ASendWithNoSessionStaysWrittenAndSaysItDidNotGo()
    {
        // AUDIT MUST: the host gives no task once the session the column was made in is no longer shown.
        var (column, host) = Make();
        column.Ask.Write("What is going on?");
        host.Live = false;
        column.Act(AskMenuColumn.Send, null);
        Assert.That(column.Ask.Stage, Is.EqualTo(AskStage.Writing));
        Assert.That(column.Ask.Draft, Is.EqualTo("What is going on?"));
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Does.Contain(AskMenuColumn.Words.SendDidNotGo));
        host.Live = true;
        column.Act(AskMenuColumn.Send, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<WorkspaceRequestSubmitCommand>(), "it can be sent again");
    }

    [Test]
    public void ASendWhoseOutcomeIsUnknownStaysSentAndGoesAgainUnderItsOwnId()
    {
        // AUDIT's reread MUST: the acknowledgement fails after the command left; it may have reached the computer.
        var (column, host) = Make();
        column.Ask.Write("What is going on?");
        host.Fault = new CommandOutcomeUnknownException("unknown", "The command may or may not have arrived.");
        column.Act(AskMenuColumn.Send, null);
        column.Tick();
        Assert.That(column.Ask.Stage, Is.EqualTo(AskStage.Sent), "unknown stays unknown, never Not Sent");
        Assert.That(column.Ask.Card.Status!.Value.Line, Is.EqualTo(AskColumn.Words.MayNotHaveReached));
        var bar = RoomBar.OfFrame(column.Frame!, VoiceStage.Idle)!;
        Assert.That(bar.Column[0].Control.Action, Is.EqualTo(AskMenuColumn.SendAgain));
        host.Fault = null;
        column.Act(AskMenuColumn.SendAgain, null);
        Assert.That(host.Sent.Count, Is.EqualTo(2));
        Assert.That(host.Sent[1].CommandId, Is.EqualTo(host.Sent[0].CommandId), "the same command, never a second request");
    }

    [Test]
    public void ASendWithNoSessionAtAllIsWrittenAgain()
    {
        var (column, host) = Make();
        column.Ask.Write("What is going on?");
        host.Fault = new SessionUnavailableException("The command was not sent.");
        column.Act(AskMenuColumn.Send, null);
        column.Tick();
        Assert.That(column.Ask.Stage, Is.EqualTo(AskStage.Writing), "nothing was sent");
        Assert.That(column.Frame!.Lines.Select(line => line.Words), Does.Contain(AskMenuColumn.Words.SendDidNotGo));
    }

    [Test]
    public void AnUnknownSendMovesOnOnceItsCommandShowsInTheSession()
    {
        var (column, host) = Make();
        column.Ask.Write("What is going on?");
        host.Fault = new CommandOutcomeUnknownException("unknown", "No acknowledgement in time.");
        column.Act(AskMenuColumn.Send, null);
        column.Tick();
        var commandId = host.Sent.Single().CommandId;
        var state = new ClientProjection();
        state.ApplySnapshot(new Snapshot
        {
            Journal = new JournalInfo { JournalId = "j", Origin = JournalOrigin.Live },
            Position = 1,
            Commands = new System.Collections.Generic.List<CommandView>
            {
                new() { CommandId = commandId, Status = CommandStatus.Completed, Result = new WorkspaceRequestCreatedResult { RequestId = RequestId } },
            },
        }, new StateChanges());
        host.Projection = state;
        column.Tick();
        Assert.That(column.Ask.Stage, Is.EqualTo(AskStage.Thinking));
        Assert.That(column.Ask.RequestId, Is.EqualTo(RequestId));
    }

    [Test]
    public void AResendThatFindsNoSessionKeepsTheFirstSendUnknownUnderItsId()
    {
        // AUDIT's reread MUST: the first send's outcome stays unknown whatever happens to a resend.
        var (column, host) = Make();
        column.Ask.Write("What is going on?");
        host.Fault = new CommandOutcomeUnknownException("unknown", "No acknowledgement in time.");
        column.Act(AskMenuColumn.Send, null);
        column.Tick();
        var first = host.Sent.Single().CommandId;
        host.Fault = new SessionUnavailableException("The command was not sent.");
        column.Act(AskMenuColumn.SendAgain, null);
        column.Tick();
        Assert.That(column.Ask.OutcomeUnknown, Is.True, "still unknown");
        Assert.That(column.Ask.CommandId, Is.EqualTo(first));
        host.Fault = null;
        column.Act(AskMenuColumn.SendAgain, null);
        Assert.That(host.Sent.Last().CommandId, Is.EqualTo(first), "Send Again carries the first id");
        Assert.That(host.Sent.Select(command => command.CommandId).Distinct().Count(), Is.EqualTo(1));
    }
}
