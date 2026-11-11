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
        /// <see cref="AgentWords"/> for a label that tells whose words are whose: the agent's name is its runtime's own,
        /// the level Halcyonic's. Null where there is no agent to name.
        /// </summary>
        public static LineWords? AgentRuns(string? agent, bool? neverAsk)
        {
            if (string.IsNullOrWhiteSpace(agent)) return null;
            var name = LineWords.TheirName(agent);
            return neverAsk is bool level ? name + LineWords.Ours(KitMomentWords.Dot + AgentColumn.LevelWords(level)) : name;
        }

        /// <summary>
        /// While the agent works and the row does not stand (its runtime takes no instruction then), what the plate in
        /// the row's place reads (the addendum's A1): the agent and the level of the run under way, from the run's own
        /// record, never from the choice for the next one; the agent alone where the record carries no level. Null
        /// otherwise, and no plate stands.
        /// </summary>
        public LineWords? Plate { get; private set; }

        /// <summary>Gives the task's own bar its plate while the agent works, and answers nothing: the frame stays the task's.</summary>
        private static RoomBar? AgentAtWork(KitFileBinding binding, VoiceStage voice, RoomBar tasks)
        {
            if (tasks.Working && tasks.Row.Count == 0) tasks.Plate = AgentRuns(binding.Model.Agent, binding.Model.RunNeverAsk);
            return null;
        }

        /// <summary>
        /// The row at rest with its third action (the addendum's A1): where the file's model has a level to choose or
        /// another agent to change to, the action that opens the agent's page joins the task's own row, after it, reading
        /// the agent and the level the next start will carry (the model's own, never the choice alone: ADR 0043); where the
        /// runtime offers no level it names the agent alone. It decorates the bar it is given and answers nothing, so
        /// everything else of that bar stands as the task's loop made it: a result at rest keeps its panel, its sentence,
        /// its band and its column (AUDIT's probe, 2026-11-11).
        /// </summary>
        private static RoomBar? AgentAtRest(KitFileBinding binding, VoiceStage voice, RoomBar tasks)
        {
            if (tasks.State != RoomBarState.Rest || tasks.Row.Count == 0 || tasks.Row.Any(action => action.Control.Action == AgentColumn.Open)) return null;
            var model = binding.Model;
            if (!model.OffersNeverAsk && !model.AgentChangeable) return null;
            var words = AgentWords(model.Agent, model.OffersNeverAsk ? model.NeverAsk : (bool?)null);
            if (words == null) return null;
            var open = new KitControl(KitPlace.Step, binding.Frame, AgentColumn.Open, null, words, GlazeIcon.CodeFile, available: true, reason: null, holds: false);
            tasks.Row = tasks.Row.Concat(new[] { new RoomAction(open, main: false) }).ToList();
            return null;
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
