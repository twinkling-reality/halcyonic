#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// A task's page that carries its card (<see cref="MenuFrame.Card"/>; today the recorded run's simulated checks, FIELD's
        /// parts note): the card in the panel and nothing else there; the column the frame's own prompts, the main one first,
        /// its other sections, and the ways out last. Only where the frame has no side panel.
        /// </summary>
        private static RoomBar? Carded(MenuFrame frame, VoiceStage voice)
        {
            // The card stands in place of the page's lines (they say the same for the older view), the line that opens the
            // full answer with them: the card shows the summary whole.
            if (frame.Card == null || frame.IdentityKind != MenuIdentityKind.Task || frame.Side != null) return null;
            RoomAction? main = null;
            var others = new List<RoomAction>();
            var leaving = new List<RoomAction>();
            foreach (var (_, prompt) in frame.Footer.All)
            {
                // Next Page pages the lines the card stands in for, so a press would change nothing a person sees.
                if (!prompt.Available || prompt.Kind == PromptKind.NextPage) continue;
                if (prompt.Holds) return null;
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var leads = main == null && !leaves && prompt.Main;
                var action = new RoomAction(new KitControl(leaves ? KitPlace.Close : KitPlace.Step, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, false),
                    main: leads, icon: prompt.Icon);
                if (leads) main = action;
                else if (leaves) leaving.Add(action);
                else others.Add(action);
            }
            var sections = frame.Sections.Where(section => !section.Chosen && section.Reached)
                .Select(section => new RoomAction(new KitControl(KitPlace.Section, frame, MenuFrame.ChooseSection, section.Key, section.Words, null, true, null, false),
                    main: false, icon: GlazeIcon.Next));
            var column = new List<RoomAction>();
            if (main != null) column.Add(main);
            column.AddRange(others);
            column.AddRange(sections);
            column.AddRange(leaving);
            return new RoomBar(RoomBarState.Page, body: frame.SubjectRuns, column: column) { Card = frame.Card };
        }
    }
}
