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
        /// holds) fall to the older view, as before.
        /// </summary>
        private static RoomBar? Menu(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Side != null || frame.Card is not RoomCard card
                || !frame.Sections.Any(section => section.Chosen && section.Key == nameof(MenuPlace.Usage))) return null;
            RoomAction? refresh = null, compact = null, close = null;
            foreach (var (_, prompt) in frame.Footer.All)
            {
                if (!prompt.Available) continue;
                if (prompt.Holds) return null;
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var control = new KitControl(leaves ? KitPlace.Close : KitPlace.Step, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, false);
                if (leaves) close = new RoomAction(control, main: false, icon: prompt.Icon);
                else if (prompt.Id == UsageLeftScreens.Refresh) refresh = new RoomAction(control, main: card.Gauges.Count == 0, icon: prompt.Icon);
                else if (prompt.Id == UsageColumn.Compact) compact = new RoomAction(control, main: false, icon: prompt.Icon);
                // Next Page pages the older view's rows, which the card holds whole; any other prompt the card has no place for.
                else if (prompt.Kind != PromptKind.NextPage) return null;
            }
            var settings = frame.Sections.Where(section => !section.Chosen && section.Reached && section.Key == nameof(MenuPlace.Settings))
                .Select(section => new RoomAction(new KitControl(KitPlace.Section, frame, MenuFrame.ChooseSection, section.Key, section.Words, null, true, null, false),
                    main: false, icon: GlazeIcon.Next));
            var column = new List<RoomAction>();
            if (refresh is { Main: true }) column.Add(refresh);
            column.AddRange(settings);
            if (compact != null) column.Add(compact);
            if (refresh is { Main: false }) column.Add(refresh);
            if (close != null) column.Add(close);
            return new RoomBar(RoomBarState.Page, body: LineWords.Ours(RoomPanelCards.UsageTitle), column: column) { Card = card };
        }
    }
}
