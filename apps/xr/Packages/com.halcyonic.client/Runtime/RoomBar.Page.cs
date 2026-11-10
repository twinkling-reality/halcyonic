#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// The general rule (the lead, 2026-11-10): a frame with no moment of its own is drawn by the same pair. Its lines
        /// that hold Halcyonic's words stand in the panel, each on a line of its own; its one line wholly of outside
        /// words, or an agent's claim, stands on the band, never in the panel; what keeps its main action waiting is the
        /// panel's sentence; and its actions stand in the column, the main one first, then the lines that act in their
        /// order, its other prompts, its other sections, and last the ways out. An action that cannot act is not drawn.
        ///
        /// It answers only where it shows everything the frame holds, so nothing counts as read that no one saw: null
        /// for a side panel, a line that is a field, a text cut in parts, a line with a fact beside it, a line that acts
        /// but cannot now, a held prompt, a second line of outside words, or a frame holding nothing at all. Then the
        /// older view draws the frame, as it does where the column would pass its six actions.
        /// </summary>
        private static RoomBar? Page(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Side != null) return null;
            var panel = new List<LineWords>();
            string? band = null;
            var acting = new List<RoomAction>();
            foreach (var line in frame.Lines)
            {
                if (line.Control == LineControl.Input || line.FromRow != null || line.Fact != null) return null;
                if (line.Action != null)
                {
                    if (!line.Available) return null;
                    var place = line.Choice ? KitPlace.Choice : KitPlace.Step;
                    acting.Add(new RoomAction(new KitControl(place, frame, line.Action, line.Key, line.Words, line.Icon, true, null, false, line.Chosen, line.AnswerMark),
                        main: false, icon: line.Icon ?? GlazeIcon.Next, mark: line.AnswerMark, chosen: line.Chosen));
                }
                // Outside words, however the frame marks them: an agent's claim, runs all from outside, or a line given whole as
                // data. A name from outside inside Halcyonic's own sentence stays with the sentence, in the panel.
                else if (line.Claim || line.Runs.AllTheirs || (line.WordsAreData && !line.Runs.AnyTheirs))
                {
                    if (band != null) return null;
                    band = line.Words;
                }
                else if (!line.Runs.IsEmpty) panel.Add(line.Runs);
            }
            RoomAction? main = null;
            var others = new List<RoomAction>();
            var leaving = new List<RoomAction>();
            foreach (var (_, prompt) in frame.Footer.All)
            {
                if (!prompt.Available) continue;
                if (prompt.Holds) return null;
                var leaves = prompt.Kind is PromptKind.Close or PromptKind.Cancel;
                var place = prompt.Kind switch { PromptKind.Close => KitPlace.Close, PromptKind.Cancel => KitPlace.Cancel, PromptKind.Yes => KitPlace.Yes, _ => KitPlace.Step };
                var leads = main == null && !leaves && (prompt.Main || prompt.Kind == PromptKind.Yes);
                // A confirmation's Yes stands behind its lock, as every Confirm of the redesign does.
                var action = new RoomAction(new KitControl(place, frame, prompt.Id, null, prompt.Words, prompt.Icon, true, null, false), main: leads,
                    locks: prompt.Kind == PromptKind.Yes, icon: prompt.Icon);
                if (leads) main = action;
                else if (leaves) leaving.Add(action);
                else others.Add(action);
            }
            var sections = frame.Sections.Where(section => !section.Chosen && section.Reached)
                .Select(section => new RoomAction(new KitControl(KitPlace.Section, frame, MenuFrame.ChooseSection, section.Key, section.Words, null, true, null, false),
                    main: false, icon: GlazeIcon.Next));
            var column = new List<RoomAction>();
            if (main != null) column.Add(main);
            column.AddRange(acting);
            column.AddRange(others);
            column.AddRange(sections);
            column.AddRange(leaving);
            if (panel.Count == 0 && band == null && column.Count == 0) return null;
            var body = LineWords.None;
            foreach (var words in panel) body = body.IsEmpty ? words : body + LineWords.Ours(KitMomentWords.Dot) + words;
            var waits = panel.Count > 0 ? frame.Footer.Reason : null;
            return new RoomBar(RoomBarState.Page, body: body, sentence: waits, column: column, band: band, wide: panel.Count == 0,
                lines: panel.Count > 1 ? panel : null);
        }
    }
}
