using System;
using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>One Ask from the headset (gap 7): each state as a card in A17's language, the three bindings, nothing sent by itself.</summary>
public class AskColumnTests
{
    private const string ProjectId = "01a19000-0000-7000-8000-000000000001";
    private const string TaskId = "01a19000-0000-7000-8000-000000000002";
    private const string ExecutionId = "01a19000-0000-7000-8000-000000000003";
    private const string RequestId = "01a19000-0000-7000-8000-000000000004";
    private readonly CommandFactory commands = new(Samples.Client, () => new DateTimeOffset(2026, 11, 11, 7, 0, 0, TimeSpan.Zero));

    private static JObject Payload(CommandEnvelope command) => (JObject)JObject.Parse(HalcyonicJson.Serialize(command))["payload"]!;

    private static CommandAckMessage Created(string commandId) => new()
    {
        CommandId = commandId,
        Disposition = CommandAckDisposition.Accepted,
        Command = new CommandView { CommandId = commandId, Status = CommandStatus.Completed, Result = new WorkspaceRequestCreatedResult { RequestId = RequestId } },
    };

    private static WorkspaceRequestView View(AskScope scope, string status, string? reply = null) => new()
    {
        RequestId = RequestId,
        JournalId = "01a19000-0000-7000-8000-0000000000aa",
        ProjectId = scope.ProjectId,
        WorkstreamId = scope.WorkstreamId,
        ExecutionId = scope.ExecutionId,
        OriginalText = "What is going on?",
        Status = new JValue(status),
        Reply = reply == null ? null : new WorkspaceRequestReply { Text = reply, Model = "muse-glimmer:30b-mlx" },
        CreatedAt = "2026-11-11T07:00:00.000Z",
        UpdatedAt = "2026-11-11T07:00:00.000Z",
    };

    [Test]
    public void WhileNothingIsWrittenItSaysWhatToAskAndSendsNothing()
    {
        var ask = new AskColumn(commands, AskScope.Project(ProjectId, "Kite Run"));
        var card = ask.Card;
        Assert.That(card.Title!.Text, Is.EqualTo("Kite Run"));
        Assert.That(card.Title.Runs.All(run => run.Theirs), Is.True, "the project's name is theirs");
        Assert.That(card.Head, Is.EqualTo(RoomCardHead.Project));
        Assert.That(card.Description!.Text, Is.EqualTo(AskColumn.Words.EmptyProject));
        Assert.That(card.Status, Is.Null);
        Assert.That(ask.CanSend, Is.False);
        Assert.That(ask.Submit(), Is.Null);
        Assert.That(new AskColumn(commands, AskScope.AllWork()).Card.Description!.Text, Is.EqualTo(AskColumn.Words.EmptyAllWork));
        Assert.That(new AskColumn(commands, AskScope.Task(ProjectId, TaskId, ExecutionId, "Tail")).Card.Description!.Text,
            Is.EqualTo(AskColumn.Words.EmptyTask));
    }

    [Test]
    public void EachBindingSendsItsOwnCommandWithItsNulls()
    {
        foreach (var (scope, expected) in new (AskScope, JObject)[]
        {
            (AskScope.Task(ProjectId, TaskId, ExecutionId, "Tail"),
                new JObject { ["project_id"] = ProjectId, ["workstream_id"] = TaskId, ["execution_id"] = ExecutionId, ["text"] = "Is it working?" }),
            (AskScope.Project(ProjectId, "Kite Run"),
                new JObject { ["project_id"] = ProjectId, ["workstream_id"] = null, ["execution_id"] = null, ["text"] = "Is it working?" }),
            (AskScope.AllWork(),
                new JObject { ["project_id"] = null, ["workstream_id"] = null, ["execution_id"] = null, ["text"] = "Is it working?" }),
        })
        {
            var ask = new AskColumn(commands, scope);
            ask.Write("  Is it working?  ");
            var command = ask.Submit();
            Assert.That(command, Is.Not.Null);
            Assert.That(Payload(command!), Is.EqualTo(expected).Using(JToken.EqualityComparer));
            Assert.That(ask.Stage, Is.EqualTo(AskStage.Sent));
            Assert.That(ask.CommandId, Is.EqualTo(command!.CommandId));
            Assert.That(ask.Submit(), Is.Null, "sent once");
        }
    }

    [Test]
    public void ItWalksSentThinkingWaitingNeedsYouAndAnswered()
    {
        var scope = AskScope.Task(ProjectId, TaskId, ExecutionId, "Tail");
        var ask = new AskColumn(commands, scope);
        ask.Offer(new AvailableWorkspaceAgent { Model = "muse-glimmer:30b-mlx" });
        ask.Write("Make it blue, then tell me if it works.");
        var command = ask.Submit()!;
        Assert.That(ask.Card.Status!.Value.Title, Is.EqualTo(AskColumn.Words.Sent));
        ask.Acknowledge(Created(command.CommandId));
        Assert.That(ask.Stage, Is.EqualTo(AskStage.Thinking));
        Assert.That(ask.RequestId, Is.EqualTo(RequestId));
        Assert.That(ask.Card.Status!.Value.Title, Is.EqualTo(AskColumn.Words.Thinking));
        Assert.That(ask.Card.Slots.Single().Kind, Is.EqualTo(RoomKind.Agent));
        ask.Read(View(scope, "waiting_for_runtime"));
        Assert.That(ask.Card.Status!.Value.Title, Is.EqualTo(AskColumn.Words.Waiting));
        ask.Read(View(scope, "waiting_for_human", "Which colour of blue?"));
        Assert.That(ask.Card.Status!.Value.Title, Is.EqualTo(AskColumn.Words.NeedsYou));
        Assert.That(ask.Card.Description!.Text, Is.EqualTo("Which colour of blue?"));
        ask.Read(View(scope, "responded", "It is blue now, and the check passed."));
        var card = ask.Card;
        Assert.That(ask.Stage, Is.EqualTo(AskStage.Answered));
        Assert.That(card.Status!.Value.Title, Is.EqualTo(AskColumn.Words.Answered));
        Assert.That(card.Status.Value.Tone, Is.EqualTo(RoomTone.Good));
        Assert.That(card.Description!.Text, Is.EqualTo("It is blue now, and the check passed."));
        // The assistant's reply is outside words, reported: never Halcyonic's own.
        Assert.That(card.Description.Runs.All(run => run.Theirs), Is.True);
        Assert.That(card.Slots.Single().Value.Text, Is.EqualTo("muse-glimmer:30b-mlx"));
    }

    [Test]
    public void ARefusalNoAssistantALimitAFailureAndAStopEachSaySo()
    {
        var refused = new AskColumn(commands, AskScope.AllWork());
        refused.Write("What is going on?");
        var command = refused.Submit()!;
        refused.Acknowledge(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Rejected });
        Assert.That(refused.Stage, Is.EqualTo(AskStage.NotAnswered));
        Assert.That(refused.Card.Status!.Value.Title, Is.EqualTo(AskColumn.Words.NotSent));
        Assert.That(refused.Card.Status.Value.Line, Is.EqualTo(AskColumn.Words.Refused));

        var none = new AskColumn(commands, AskScope.AllWork());
        none.Offer(new UnavailableWorkspaceAgent { Reason = new ErrorInfo { Code = "not_configured", Message = "No model." } });
        none.Write("What is going on?");
        Assert.That(none.CanSend, Is.False);
        Assert.That(none.Submit(), Is.Null);
        Assert.That(none.Card.Status!.Value.Line, Is.EqualTo(AskColumn.Words.NoAssistant));

        foreach (var (status, title, line) in new[]
        {
            ("interrupted", AskColumn.Words.NotAnswered, AskColumn.Words.AtLimit),
            ("failed", AskColumn.Words.NotAnswered, AskColumn.Words.CouldNotGoOn),
            ("stopped", AskColumn.Words.Stopped, AskColumn.Words.YouStopped),
        })
        {
            var scope = AskScope.Project(ProjectId, "Kite Run");
            var ask = new AskColumn(commands, scope);
            ask.Write("What is going on?");
            ask.Acknowledge(Created(ask.Submit()!.CommandId));
            ask.Read(View(scope, status));
            Assert.That(ask.Card.Status!.Value.Title, Is.EqualTo(title), status);
            Assert.That(ask.Card.Status.Value.Line, Is.EqualTo(line), status);
            Assert.That(ask.Card.Status.Value.Tone, Is.EqualTo(RoomTone.Bad), status);
        }
    }

    [Test]
    public void AViewOfAnotherRequestOrBindingAndAnotherCommandsAckAreIgnored()
    {
        var scope = AskScope.Project(ProjectId, "Kite Run");
        var ask = new AskColumn(commands, scope);
        ask.Write("What is going on?");
        var command = ask.Submit()!;
        ask.Acknowledge(Created("01a19000-0000-7000-8000-0000000000ff"));
        Assert.That(ask.Stage, Is.EqualTo(AskStage.Sent), "another command's acknowledgement");
        ask.Acknowledge(Created(command.CommandId));
        ask.Read(View(AskScope.AllWork(), "responded", "Elsewhere."));
        Assert.That(ask.Stage, Is.EqualTo(AskStage.Thinking), "a view of another binding");
        var other = View(scope, "responded", "Another request.");
        other.RequestId = "01a19000-0000-7000-8000-0000000000ee";
        ask.Read(other);
        Assert.That(ask.Stage, Is.EqualTo(AskStage.Thinking), "another request");
        ask.Write("Something else");
        Assert.That(ask.Draft, Is.EqualTo("What is going on?"), "words do not change once sent");
    }

    [Test]
    public void NoStateJoinsTwoFactsOnADot()
    {
        var scope = AskScope.Task(ProjectId, TaskId, ExecutionId, "Tail");
        var ask = new AskColumn(commands, scope);
        Assert.That(ask.Card.AnyDot, Is.False, "writing");
        ask.Offer(new AvailableWorkspaceAgent { Model = "muse-glimmer:30b-mlx" });
        ask.Write("Is it working?");
        Assert.That(ask.Card.AnyDot, Is.False, "written");
        ask.Acknowledge(Created(ask.Submit()!.CommandId));
        foreach (var status in new[] { "running", "waiting_for_runtime", "waiting_for_human", "responded" })
        {
            ask.Read(View(scope, status, "Yes, the check passed."));
            Assert.That(ask.Card.AnyDot, Is.False, status);
        }
        ask.Read(View(scope, "failed"));
        Assert.That(ask.Card.AnyDot, Is.False, "failed");
    }
}
