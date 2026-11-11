#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// The review before Start as cards (the addendum's A17, XR's drawn check of 0750Z): exactly what will be sent, every
    /// item of <see cref="NewWorkReview"/> in a part of a card, never a label joined on a dot. It reads in parts, so no card
    /// stands taller than a panel can hold in view (the critic and the parts check, 2026-11-11):
    /// <list type="bullet">
    /// <item>First the task, in the person's words: the project's name as the title, the first task as the description,
    /// four lines a part (<see cref="RoomCard.DescriptionPageLines"/>), as many parts as it takes.</item>
    /// <item>Last, what else is sent, read right before Yes: what the first task names that the person should check as the
    /// status (Attention); the project's name with its head; the folder, the agent (the model where one is named, else the
    /// agent app) and the level as slots; then rows, each its value alone under its kind's mark: the task's name (the
    /// flag), the agent app where the model took its slot, where the model runs, the model's id (the model's orb), "From"
    /// the folder it moves from.</item>
    /// </list>
    /// Every part's foot line says which it is: "Part 2 of 3". The task's parts are as many as the card's view made of its
    /// description (<c>RoomCardView.DescriptionPages</c>, shown with part 0), so <see cref="Parts"/> is that and one more.
    /// Each part counts as read when its frame is reported drawn with the card standing whole (<c>RoomCardView.LaidWhole</c>).
    /// </summary>
    public static class RoomReview
    {
        public const string CheckTitle = "Check Before Starting";

        /// <summary>The words before the folder a move leaves: "From kite-run".</summary>
        public const string MovesFrom = "From ";

        /// <summary>The foot line naming a part: "Part 2 of 3".</summary>
        public static string PartOf(int part, int parts) => "Part " + (part + 1) + " of " + parts;

        /// <summary>The parts of a review whose first task made <paramref name="taskPages"/> pages: those, and the last of what is sent.</summary>
        public static int Parts(int taskPages) => (taskPages < 1 ? 1 : taskPages) + 1;

        /// <summary>
        /// The review's part <paramref name="part"/> (from 0) of <see cref="Parts"/>(<paramref name="taskPages"/>): a page of
        /// the task while <paramref name="part"/> is under <paramref name="taskPages"/>, else the last part, what is sent.
        /// Measure the task's pages with part 0 at any count, then show each part with the count the view gave.
        /// </summary>
        public static RoomCard Card(NewWorkReview review, int part = 0, int taskPages = 1)
        {
            LineWords? Value(ReviewItemKind kind)
            {
                var item = review.Items.FirstOrDefault(each => each.Kind == kind);
                return item == null || item.ValueRuns.IsEmpty ? null : item.ValueRuns;
            }
            var pages = taskPages < 1 ? 1 : taskPages;
            var parts = Parts(pages);
            var at = part < 0 ? 0 : part >= parts ? parts - 1 : part;
            var foot = LineWords.Ours(PartOf(at, parts));
            var title = Value(ReviewItemKind.Project);
            if (at < pages)
                return new RoomCard(title, Value(ReviewItemKind.FirstTask) ?? LineWords.Ours(""), more: foot, descriptionPage: at);

            var slots = new List<RoomSlot>();
            if (Value(ReviewItemKind.Folder) is LineWords folder) slots.Add(new RoomSlot(RoomKind.Folder, folder));
            if ((Value(ReviewItemKind.Model) ?? Value(ReviewItemKind.AgentApp)) is LineWords agent) slots.Add(new RoomSlot(RoomKind.Agent, agent));
            slots.Add(new RoomSlot(RoomKind.Level, LineWords.Ours(AgentColumn.LevelWords(review.NeverAsk))));

            // Each row its value alone, its kind's own mark saying what it is: no note that names a field (the critic, 2026-11-11).
            var rows = new List<RoomListRow>();
            if (Value(ReviewItemKind.TaskName) is LineWords name) rows.Add(new RoomListRow(RoomKind.Task, name, null));
            // The agent app stands as a row only where the model took its slot, so nothing sent goes unshown.
            if (Value(ReviewItemKind.Model) != null && Value(ReviewItemKind.AgentApp) is LineWords app) rows.Add(new RoomListRow(RoomKind.App, app, null));
            if (Value(ReviewItemKind.ModelLocation) is LineWords where) rows.Add(new RoomListRow(RoomKind.Computer, where, null));
            // The model's id under the model's own mark: the same model, its exact name.
            if (Value(ReviewItemKind.ModelId) is LineWords id) rows.Add(new RoomListRow(RoomKind.Agent, id, null));
            if (Value(ReviewItemKind.FolderNow) is LineWords before) rows.Add(new RoomListRow(RoomKind.Folder, LineWords.Ours(MovesFrom) + before, null));

            var warnings = review.Items.Where(each => each.Kind == ReviewItemKind.Warning).Select(each => each.Label.Trim()).Where(each => each.Length > 0).ToList();
            var status = warnings.Count > 0 ? new RoomCardStatus(CheckTitle, string.Join(" ", warnings), RoomTone.Attention) : (RoomCardStatus?)null;
            return new RoomCard(title, null, RoomCardHead.None, status, slots, list: rows, more: foot);
        }
    }
}
