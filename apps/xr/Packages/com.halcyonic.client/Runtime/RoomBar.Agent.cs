#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// The words of the action at rest that opens the agent and its level, and of the plate that stands in its place
        /// while the agent works (the addendum's A1): the agent by its own name, then its level, "Glimmer · Ask First".
        /// The agent alone where no level is known (a run whose record carries none); null where there is no agent to name.
        /// </summary>
        public static string? AgentWords(string? agent, bool? neverAsk)
        {
            if (string.IsNullOrWhiteSpace(agent)) return null;
            return neverAsk is bool level ? agent + KitMomentWords.Dot + AgentColumn.LevelWords(level) : agent;
        }

        /// <summary>
        /// The agent and its level as the redesign draws it (the addendum's A1), every action the page's own: the panel's one
        /// line is the page's subject, the computer's fact ("Model · {model}"); the column holds the levels as answers with
        /// their marks, the one chosen marked, then Change, or the agents and models as answers with their way to more of
        /// them; and Back last. Never a main action: nothing here is the one thing to do. Null for any other page.
        /// </summary>
        public static RoomBar? Agent(MenuFrame frame, VoiceStage voice)
        {
            if (!frame.Lines.Any(line => line.Action == AgentColumn.Level || line.Action == AgentColumn.Change || line.Action == AgentColumn.Pick)) return null;
            var column = new List<RoomAction>();
            foreach (var line in frame.Lines.Where(line => line.Action != null && line.Available))
            {
                var answer = line.Action == AgentColumn.Level || line.Action == AgentColumn.Pick;
                var place = answer ? KitPlace.Choice : line.Action == Footer.NextPage ? KitPlace.Step : KitPlace.Route;
                var control = new KitControl(place, frame, line.Action!, line.Key, line.Words, line.Icon, available: true, reason: null, holds: false,
                    chosen: line.Chosen, mark: answer ? AnswerMark.Single : AnswerMark.None);
                column.Add(new RoomAction(control, main: false, mark: control.Mark, chosen: control.Chosen));
            }
            if (frame.Footer[PromptSlot.Close] is { Available: true } back)
                column.Add(new RoomAction(new KitControl(KitPlace.Back, frame, back.Id, null, back.Words, back.Icon, available: true, reason: null, holds: false), main: false));
            return new RoomBar(RoomBarState.Choose, body: frame.SubjectRuns, column: column);
        }
    }
}
