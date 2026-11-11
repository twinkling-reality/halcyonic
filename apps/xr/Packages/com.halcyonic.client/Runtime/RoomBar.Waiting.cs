#nullable enable
using System.Collections.Generic;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// At rest, an instruction that waited and was not sent (SCREENS's <see cref="UnsentInstruction"/>): its card, then Send
        /// Again (the main action, the same words, where the task takes an instruction and it was not dropped) and Back, which
        /// leaves it for the result. Null where there is none.
        /// </summary>
        private static RoomBar? Unsent(KitFileBinding binding, RoomResult result, RoomBar loop)
        {
            if (result.Waiting?.NotSent is not UnsentInstruction unsent || loop.Pair || loop.State is RoomBarState.Sent or RoomBarState.Words) return null;
            var frame = binding.Frame;
            KitControl Control(string action, string? key, string words, GlazeIcon icon) => new KitControl(KitPlace.Step, frame, action, key, words, icon, true, null, false);
            var column = new List<RoomAction>();
            if (unsent.SendsAgain && binding[KitPlace.Field] is { Available: true })
                column.Add(new RoomAction(Control(RoomResult.WaitingAgain, unsent.CommandId, WaitingText.SendAgain, GlazeIcon.Next), main: true, icon: GlazeIcon.Next));
            column.Add(new RoomAction(Control(RoomResult.WaitingBack, unsent.CommandId, WaitingText.Back, GlazeIcon.Back), main: false, icon: GlazeIcon.Back));
            return new RoomBar(loop.State, row: loop.Row, column: column) { Result = true, Known = result, Card = unsent.Card() };
        }

        /// <summary>
        /// While the turn runs, instructions waiting for it to end (SCREENS's <see cref="WaitingInstructions"/>): the waiting
        /// list as the card beside Stop, each row pressed to its page; on a page, its card, then Drop and Back (Stop stands
        /// again on the list). Nothing changes where none waits.
        /// </summary>
        private static RoomBar Waited(RoomBar working, WaitingInstructions waiting, MenuFrame frame)
        {
            if (!waiting.Any) return working;
            KitControl Control(string action, string? key, string words, GlazeIcon icon) => new KitControl(KitPlace.Step, frame, action, key, words, icon, true, null, false);
            if (waiting.PageCard() is RoomCard page && waiting.Opened is WaitingInstruction opened)
            {
                working.Card = page;
                working.Column = new List<RoomAction>
                {
                    new RoomAction(Control(RoomResult.WaitingDrop, opened.CommandId, WaitingText.Drop, GlazeIcon.Close), main: false, icon: GlazeIcon.Close),
                    new RoomAction(Control(RoomResult.WaitingBack, null, WaitingText.Back, GlazeIcon.Back), main: false, icon: GlazeIcon.Back),
                };
                return working;
            }
            working.Card = waiting.ListCard(item => Control(RoomResult.WaitingOpen, item.CommandId, item.Words ?? WaitingText.NotKept, GlazeIcon.Next));
            return working;
        }
    }
}
