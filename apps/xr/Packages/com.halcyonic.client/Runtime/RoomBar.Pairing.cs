#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// Pairing beside the menu (<see cref="PairingColumn"/>; ADR 0017, amendment of 2026-11-11), in A17's language: the
        /// frame's card in the panel's place, each found computer's row pressed as its line, and the column the frame's
        /// prompts: the main one first (Type Address where nothing was found, Try Again after a refusal), the others, Close
        /// last. Null for any other frame, or one whose rows and lines do not match.
        /// </summary>
        private static RoomBar? Pairing(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Subject != PairingColumn.Subject || frame.Card is not RoomCard card || frame.Side != null) return null;
            var picks = frame.Lines.Where(line => line.Action == PairingColumn.Choose).ToList();
            if (picks.Count != card.List.Count || picks.Any(line => !line.Available)) return null;
            if (picks.Count > 0)
            {
                var rows = card.List.Select((row, at) => new RoomListRow(row.Kind, row.Name, row.Note, row.Tone, row.Mono,
                    new KitControl(KitPlace.Choice, frame, PairingColumn.Choose, picks[at].Key, picks[at].Words, GlazeIcon.Next, true, null, false), row.Chosen)).ToList();
                card = new RoomCard(card.Title, card.Description, card.Head, card.Status, card.Slots, card.Steps, card.TrackAt, card.TrackOf, rows,
                    card.Gauges, card.More);
            }
            RoomAction? main = null;
            var others = new List<RoomAction>();
            var leaving = new List<RoomAction>();
            foreach (var (_, prompt) in frame.Footer.All)
            {
                if (!prompt.Available) continue;
                if (prompt.Holds) return null;
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var leads = main == null && !leaves && prompt.Main;
                var action = new RoomAction(new KitControl(leaves ? KitPlace.Close : KitPlace.Step, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, false),
                    main: leads, icon: prompt.Icon);
                if (leads) main = action;
                else if (leaves) leaving.Add(action);
                else others.Add(action);
            }
            var column = new List<RoomAction>();
            if (main != null) column.Add(main);
            column.AddRange(others);
            column.AddRange(leaving);
            return new RoomBar(RoomBarState.Page, body: LineWords.Ours(PairingColumn.Subject), column: column) { Card = card };
        }
    }
}
