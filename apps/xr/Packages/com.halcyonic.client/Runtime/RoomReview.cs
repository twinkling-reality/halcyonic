#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// The review before Start as a card (the addendum's A17, XR's drawn check of 0750Z): exactly what will be sent, every
    /// item of <see cref="NewWorkReview"/> in a part of the card, never a label joined on a dot.
    /// <list type="bullet">
    /// <item>The project's name is the title; the first task, the person's words, is the description, paged where it is long.</item>
    /// <item>The folder it goes to, the agent (the model where one is named, else the agent app) and the level are the slots.</item>
    /// <item>What else is sent stands as list rows, each its value as the name and what it is as the quiet note: the task's
    /// name, where the model runs, the model's id, the folder it moves from.</item>
    /// <item>A note of what the first task names that the person should check is the status, Attention: read it before Yes.</item>
    /// </list>
    /// The card is one part of the review. Each page counts as read when its frame is reported drawn with the card standing
    /// whole (<c>RoomCardView.LaidWhole</c>).
    /// </summary>
    public static class RoomReview
    {
        public const string TaskNameNote = "Task Name";
        public const string RunsOnNote = "Runs On";
        public const string ModelIdNote = "Model ID";
        public const string MovesFromNote = "Moves From";
        public const string CheckTitle = "Check Before Starting";

        /// <summary>The review as a card, its description at <paramref name="page"/> where it pages (null: it stands whole).</summary>
        public static RoomCard Card(NewWorkReview review, int? page = null)
        {
            LineWords? Value(ReviewItemKind kind)
            {
                var item = review.Items.FirstOrDefault(each => each.Kind == kind);
                return item == null || item.ValueRuns.IsEmpty ? null : item.ValueRuns;
            }
            var slots = new List<RoomSlot>();
            if (Value(ReviewItemKind.Folder) is LineWords folder) slots.Add(new RoomSlot(RoomKind.Folder, folder));
            if ((Value(ReviewItemKind.Model) ?? Value(ReviewItemKind.AgentApp)) is LineWords agent) slots.Add(new RoomSlot(RoomKind.Agent, agent));
            slots.Add(new RoomSlot(RoomKind.Level, LineWords.Ours(AgentColumn.LevelWords(review.NeverAsk))));

            var rows = new List<RoomListRow>();
            if (Value(ReviewItemKind.TaskName) is LineWords name) rows.Add(new RoomListRow(RoomKind.Started, name, TaskNameNote));
            // The agent app stands as a row only where the model took its slot, so nothing sent goes unshown.
            if (Value(ReviewItemKind.Model) != null && Value(ReviewItemKind.AgentApp) is LineWords app) rows.Add(new RoomListRow(RoomKind.App, app, AgentColumn.AgentAppWords));
            if (Value(ReviewItemKind.ModelLocation) is LineWords where) rows.Add(new RoomListRow(RoomKind.Computer, where, RunsOnNote));
            if (Value(ReviewItemKind.ModelId) is LineWords id) rows.Add(new RoomListRow(RoomKind.App, id, ModelIdNote, mono: true));
            if (Value(ReviewItemKind.FolderNow) is LineWords before) rows.Add(new RoomListRow(RoomKind.Folder, before, MovesFromNote));

            var warnings = review.Items.Where(each => each.Kind == ReviewItemKind.Warning).Select(each => each.Label.Trim()).Where(each => each.Length > 0).ToList();
            var status = warnings.Count > 0 ? new RoomCardStatus(CheckTitle, warnings[0], RoomTone.Attention) : (RoomCardStatus?)null;
            var more = warnings.Count > 1 ? LineWords.Ours(string.Join(" ", warnings.Skip(1))) : null;

            var title = Value(ReviewItemKind.Project);
            return new RoomCard(title, Value(ReviewItemKind.FirstTask), RoomCardHead.Project, status, slots, list: rows, more: more, descriptionPage: page);
        }
    }
}
