#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>One instruction waiting for the turn to end: its command, and the person's words where this headset sent them.</summary>
    public readonly struct WaitingInstruction
    {
        public WaitingInstruction(string commandId, string? words)
        {
            CommandId = commandId;
            Words = string.IsNullOrWhiteSpace(words) ? null : words;
        }

        public string CommandId { get; }

        /// <summary>The person's own words, theirs; null where this headset did not send it (or no longer remembers it).</summary>
        public string? Words { get; }

        /// <summary>The words as a card shows them: the person's, leaning; else the product's own placeholder.</summary>
        public LineWords Shown => Words != null ? LineWords.TheirName(Words) : LineWords.Ours(WaitingText.NotKept);
    }

    /// <summary>An instruction that waited and was not sent (<c>command.failed</c>, effect none): its command, words and code.</summary>
    public readonly struct UnsentInstruction
    {
        public UnsentInstruction(string commandId, string? words, string code)
        {
            CommandId = commandId;
            Words = string.IsNullOrWhiteSpace(words) ? null : words;
            Code = code;
        }

        public string CommandId { get; }

        public string? Words { get; }

        public string Code { get; }

        /// <summary>Whether Send Again may stand: never after the person dropped it, never without its words.</summary>
        public bool SendsAgain => Code != WaitingText.Dropped && Words != null;

        /// <summary>The card (FIELD's 1426Z, A6): Not Sent (quiet: nothing changed) over why, by its code; its words under it.</summary>
        public RoomCard Card() => new RoomCard(description: Words != null ? LineWords.TheirName(Words) : LineWords.Ours(WaitingText.NotKept),
            status: new RoomCardStatus(WaitingText.NotSent, WaitingText.Why(Code), RoomTone.Quiet));

        /// <summary>
        /// The run's newest instruction that waited and was not sent, unless the person dismissed it (Back) or sent another
        /// since; null otherwise. Read from the host's own record of the commands, the words from this headset's.
        /// </summary>
        public static UnsentInstruction? Of(ClientProjection? state, string? executionId, Func<string, string?> wordsOf, Func<string, bool> dismissed)
        {
            if (state == null || executionId == null) return null;
            var instructions = state.Commands.Values
                .Where(command => command.ExecutionId == executionId && command.CommandType == CommandType.ExecutionSendInstruction)
                .OrderBy(command => command.IssuedAt, StringComparer.Ordinal).ToList();
            if (instructions.Count == 0) return null;
            var newest = instructions[instructions.Count - 1];
            if (newest.Status != CommandStatus.Failed || newest.Failure is not CommandFailure failure || !failure.Code.StartsWith("queued_", StringComparison.Ordinal)) return null;
            if (dismissed(newest.CommandId)) return null;
            return new UnsentInstruction(newest.CommandId, wordsOf(newest.CommandId), failure.Code);
        }
    }

    /// <summary>
    /// Instructions waiting for the running turn to end on a runtime that cannot steer (CORE's queue, ADR 0056;
    /// <c>ExecutionView.queued_instructions</c>, oldest first, without words): FIELD's waiting list as a card beside Stop, each
    /// row its own page with Drop (nothing was sent, so Drop asks nothing), in FIELD's words. Never "queued" and never "sent"
    /// on a screen for one that waits. The words are the person's, kept by this headset when it sent them, never the host's.
    /// </summary>
    public sealed class WaitingInstructions
    {
        public WaitingInstructions(string executionId, IReadOnlyList<ExecutionViewQueuedInstructionsItem>? queued, Func<string, string?> wordsOf, string? opened = null,
            UnsentInstruction? notSent = null)
        {
            NotSent = notSent;
            ExecutionId = executionId;
            Items = (queued ?? new List<ExecutionViewQueuedInstructionsItem>()).Select(item => new WaitingInstruction(item.CommandId, wordsOf(item.CommandId))).ToList();
            Opened = opened != null && Items.Any(item => item.CommandId == opened) ? Items.First(item => item.CommandId == opened) : (WaitingInstruction?)null;
        }

        public string ExecutionId { get; }

        /// <summary>The waiting instructions, oldest first: the first is sent next.</summary>
        public IReadOnlyList<WaitingInstruction> Items { get; }

        /// <summary>The one whose page shows; null on the list, or where it no longer waits (sent, failed or dropped).</summary>
        public WaitingInstruction? Opened { get; }

        public bool Any => Items.Count > 0;

        /// <summary>At rest: the newest instruction that waited and was not sent, until the person goes Back from it.</summary>
        public UnsentInstruction? NotSent { get; }

        /// <summary>
        /// The waiting list (FIELD's 1426Z, A2): its title, its one line, a row each, oldest first, pressed to its page, each
        /// with the waiting mark. Only the first carries a note, Next: the title already says the rest wait.
        /// </summary>
        public RoomCard ListCard(Func<WaitingInstruction, KitControl> press) => new RoomCard(LineWords.Ours(WaitingText.Title), LineWords.Ours(WaitingText.Line),
            list: Items.Select((item, index) => new RoomListRow(RoomKind.Waiting, item.Shown, index == 0 ? WaitingText.Next : null, press: press(item))).ToList());

        /// <summary>One waiting instruction's page (A3): its status, its words as the line under it.</summary>
        public RoomCard? PageCard() => Opened is WaitingInstruction item
            ? new RoomCard(description: item.Shown, status: new RoomCardStatus(WaitingText.ItsTurn, WaitingText.ItsTurnLine, RoomTone.Quiet, RoomKind.Waiting))
            : null;
    }

    /// <summary>FIELD's words for an instruction waiting its turn (1426Z, A).</summary>
    public static class WaitingText
    {
        public const string Title = "Waiting";
        public const string Line = "Each is sent when this turn ends.";
        public const string Next = "Next";
        public const string ItsTurn = "Waiting for Its Turn";
        public const string ItsTurnLine = "It is sent when this turn ends.";
        public const string Drop = "Drop";
        public const string NotSent = "Not Sent";
        public const string SendAgain = "Send Again";
        public const string Dropped = "queued_dropped";

        /// <summary>Why a waiting instruction was not sent, by its code (FIELD's 1426Z, A6); the plainest line for a code not listed.</summary>
        public static string Why(string code) => code switch
        {
            "queued_turn_failed" => "The turn before it ended in an error.",
            "queued_stopped" => "The turn was stopped before it ended.",
            "queued_session_lost" => "The agent's session could not be picked up again.",
            "queued_files_restored" => "The project's files were put back.",
            Dropped => "You dropped it.",
            _ => "Nothing was sent.",
        };
        public const string Back = "Back";

        /// <summary>PLACEHOLDER, for FIELD: a waiting instruction this headset did not send, whose words it does not have.</summary>
        public const string NotKept = "Your Instruction";
    }
}
