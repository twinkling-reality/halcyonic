using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Instructions waiting for the running turn to end (SCREENS's <see cref="WaitingInstructions"/>, CORE's queue, ADR 0056): the
/// waiting list oldest first with the person's own words, each row's page, Drop as one command, and at rest the newest one
/// not sent, said by its code in FIELD's words, with Send Again where it may go again. Never "queued" on a screen.
/// </summary>
public class WaitingInstructionsTests
{
    private static List<ExecutionViewQueuedInstructionsItem> Queued(params string[] ids) =>
        ids.Select((id, index) => new ExecutionViewQueuedInstructionsItem { CommandId = id, QueuedAt = "2026-11-11T18:0" + index + ":00.000Z" }).ToList();

    private static readonly Dictionary<string, string> Sent = new() { ["c1"] = "Make the tail longer.", ["c2"] = "And blue." };

    [Test]
    public void TheListShowsEachWaitingInstructionOldestFirstInThePersonsWordsAndItsPageOffersDrop()
    {
        var waiting = new WaitingInstructions("e1", Queued("c1", "c2", "c3"), id => Sent.TryGetValue(id, out var words) ? words : null);
        var card = waiting.ListCard(item => new KitControl(KitPlace.Step, MenuFrameStub(), RoomResult.WaitingOpen, item.CommandId, "", GlazeIcon.Next, true, null, false));
        Assert.That((card.Title!.Text, card.Description!.Text), Is.EqualTo((WaitingText.Title, WaitingText.Line)));
        Assert.That(card.List.Select(row => (row.Name.Text, row.Note, row.Press!.Key)), Is.EqualTo(new[]
        {
            ("Make the tail longer.", (string?)WaitingText.Next, (string?)"c1"), ("And blue.", WaitingText.Waiting, "c2"), (WaitingText.NotKept, WaitingText.Waiting, "c3"),
        }));
        Assert.That(card.List.Take(2).All(row => row.Name.AllTheirs), Is.True, "the person's words are theirs, never the product's");
        Assert.That(card.AnyDot, Is.False);
        Assert.That(card.Words().Any(words => words.Contains("ueue") || words.Contains("Sent")), Is.False, "never queued, never sent for one that waits");

        var page = new WaitingInstructions("e1", Queued("c1", "c2"), id => Sent.GetValueOrDefault(id), opened: "c2").PageCard()!;
        Assert.That((page.Status!.Value.Title, page.Status.Value.Line, page.Description!.Text), Is.EqualTo((WaitingText.ItsTurn, WaitingText.ItsTurnLine, "And blue.")));
        Assert.That(new WaitingInstructions("e1", Queued("c1"), id => null, opened: "c2").Opened, Is.Null, "one no longer waiting opens nothing");

        var drop = new CommandFactory(Samples.Client).DropQueuedInstruction("e1", "c2");
        Assert.That((drop.Payload.ExecutionId, drop.Payload.CommandId), Is.EqualTo(("e1", "c2")));
    }

    [Test]
    public void AtRestTheNewestInstructionNotSentSaysWhyAndOffersSendAgainOnlyWhereItMayGoAgain()
    {
        CommandView Command(string id, string issued, CommandStatus status, string? code = null) => new()
        {
            CommandId = id, CommandType = CommandType.ExecutionSendInstruction, Status = status, ExecutionId = "e1", IssuedAt = issued, UpdatedAt = issued,
            Failure = code == null ? null : new CommandFailure { Code = code, Message = "Synthetic.", Effect = FailureEffect.None },
        };
        ClientProjection With(params CommandView[] commands)
        {
            var state = new ClientProjection();
            var snapshot = Samples.Snapshot(1);
            snapshot.Commands = commands.ToList();
            state.ApplySnapshot(snapshot, new StateChanges());
            return state;
        }
        var stopped = UnsentInstruction.Of(With(Command("c1", "2026-11-11T18:00:00.000Z", CommandStatus.Failed, "queued_stopped")), "e1", Sent.GetValueOrDefault, _ => false)!.Value;
        var card = stopped.Card();
        Assert.That((card.Status!.Value.Title, card.Status.Value.Line, card.Description!.Text), Is.EqualTo((WaitingText.NotSent, "The turn was stopped before it ended.", "Make the tail longer.")));
        Assert.That(stopped.SendsAgain, Is.True);
        Assert.That(UnsentInstruction.Of(With(Command("c1", "2026-11-11T18:00:00.000Z", CommandStatus.Failed, "queued_dropped")), "e1", Sent.GetValueOrDefault, _ => false)!.Value.SendsAgain,
            Is.False, "after You dropped it, Back alone");
        Assert.That(UnsentInstruction.Of(With(Command("c1", "2026-11-11T18:00:00.000Z", CommandStatus.Failed, "queued_stopped"),
            Command("c2", "2026-11-11T18:05:00.000Z", CommandStatus.Completed)), "e1", Sent.GetValueOrDefault, _ => false), Is.Null, "another sent since: nothing to say");
        Assert.That(UnsentInstruction.Of(With(Command("c1", "2026-11-11T18:00:00.000Z", CommandStatus.Failed, "queued_stopped")), "e1", Sent.GetValueOrDefault, id => id == "c1"),
            Is.Null, "gone Back from: the result shows again");
        Assert.That(UnsentInstruction.Of(With(Command("c1", "2026-11-11T18:00:00.000Z", CommandStatus.Failed, "runtime_refused")), "e1", Sent.GetValueOrDefault, _ => false),
            Is.Null, "only an instruction that waited");
        foreach (var code in new[] { "queued_turn_failed", "queued_session_lost", "queued_files_restored" }) Assert.That(WaitingText.Why(code), Does.EndWith("."));
    }

    private static MenuFrame MenuFrameStub() => new(LineWords.Ours("Task"), new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)));
}
