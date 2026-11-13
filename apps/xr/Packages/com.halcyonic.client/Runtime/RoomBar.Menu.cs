#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// The menu, which opens on usage (the addendum's A4 and A15): the usage card in the panel, and the column Settings,
        /// Compact, Refresh, Close, each the frame's own control; Refresh leads where there is no reading yet, and nothing
        /// else ever does (Compact is never main). Known by the place its frame stands for (the chosen section Usage, R3),
        /// never by its title. A limit's side panel, a held prompt, or a page with no card (more windows than the card
        /// holds) fall to the older view, as before. Use Model's own frames stand here too: the host's list with Back, and the one
        /// review card, its Confirm the main action, Cancel the way out. A press on the card (the Assistant row, a row of the list)
        /// is bound here to the frame drawn, as every press must come from the frame that stands.
        /// </summary>
        private static RoomBar? Menu(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Side != null || frame.Card is not RoomCard card
                || !frame.Sections.Any(section => section.Chosen && section.Key == nameof(MenuPlace.Usage))) return null;
            RoomAction? refresh = null, compact = null, close = null, confirm = null;
            var turns = new List<RoomAction>();
            foreach (var (_, prompt) in frame.Footer.All)
            {
                if (!prompt.Available) continue;
                if (prompt.Holds) return null;
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var control = new KitControl(leaves ? KitPlace.Close : KitPlace.Step, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, false);
                if (leaves) close = new RoomAction(control, main: false, icon: prompt.Icon);
                else if (prompt.Id == UsageLeftScreens.Refresh) refresh = new RoomAction(control, main: card.Gauges.Count == 0, icon: prompt.Icon);
                else if (prompt.Id == UsageColumn.Compact) compact = new RoomAction(control, main: false, icon: prompt.Icon);
                else if (prompt.Id == ReviewCard.Confirm) confirm = new RoomAction(control, main: true, icon: prompt.Icon);
                else if (prompt.Id is ReviewCard.NextPart or ReviewCard.PreviousPart) turns.Add(new RoomAction(control, main: false, icon: prompt.Icon));
                // Next Page pages the older view's rows, which the card holds whole; any other prompt the card has no place for.
                else if (prompt.Kind != PromptKind.NextPage) return null;
            }
            var settings = frame.Sections.Where(section => !section.Chosen && section.Reached && section.Key == nameof(MenuPlace.Settings))
                .Select(section => new RoomAction(new KitControl(KitPlace.Section, frame, MenuFrame.ChooseSection, section.Key, section.Words, null, true, null, false),
                    main: false, icon: GlazeIcon.Next));
            var column = new List<RoomAction>();
            if (confirm != null) column.Add(confirm);
            column.AddRange(turns);
            if (refresh is { Main: true }) column.Add(refresh);
            column.AddRange(settings);
            if (compact != null) column.Add(compact);
            if (refresh is { Main: false }) column.Add(refresh);
            if (close != null) column.Add(close);
            return new RoomBar(RoomBarState.Page, body: card.Title ?? LineWords.Ours(RoomPanelCards.UsageTitle), column: column) { Card = BoundTo(card, frame) };
        }

        /// <summary>The card with every press on it (its own, a row's, a slot's) made from <paramref name="frame"/>, the frame drawn.</summary>
        private static RoomCard BoundTo(RoomCard card, MenuFrame frame)
        {
            KitControl? Bound(KitControl? press) => press == null || ReferenceEquals(press.Frame, frame) ? press
                : new KitControl(press.Place, frame, press.Action, press.Key, press.Words, press.Icon, press.Available, press.Reason, press.Holds, press.Chosen, press.Mark);
            if (card.Press == null && card.List.All(row => row.Press == null) && card.Slots.All(slot => slot.Press == null)) return card;
            return new RoomCard(card.Title, card.Description, card.Head, card.Status,
                card.Slots.Select(slot => new RoomSlot(slot.Kind, slot.Value, slot.Mono, Bound(slot.Press))).ToList(),
                card.Steps, card.TrackAt, card.TrackOf,
                card.List.Select(row => new RoomListRow(row.Kind, row.Name, row.Note, row.Tone, row.Mono, Bound(row.Press), row.Chosen, row.Under, row.Excerpt)).ToList(),
                card.Gauges, card.More, card.Code, card.Reading, card.DescriptionPage, card.TitleMono, Bound(card.Press));
        }
    }
}
