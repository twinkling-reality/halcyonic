#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// The review before Start as cards (the addendum's A17, XR's drawn check of 0750Z): exactly what will be sent, every
    /// item of <see cref="NewWorkReview"/> in a part of a card, never a label joined on a dot. It reads in parts, so no card
    /// stands taller than a panel holds in view (the critics and the parts check, 2026-11-11):
    /// <list type="bullet">
    /// <item>The task, in the person's words: the project's name with its head, the first task as the description, four
    /// lines a part (<see cref="RoomCard.DescriptionPageLines"/>), as many parts as it takes.</item>
    /// <item>How it runs: the project's name; the folder, the model (its exact id where one is named, else its name, else
    /// the agent app) and the level as slots; then rows, each its value alone under its kind's mark: the task's name (the
    /// flag), the agent app where the model took its slot, where the model runs, "From" the folder it moves from.</item>
    /// <item>Where the first task names something to check, a last part right before Yes: the project's name over the status
    /// "Check First" (Attention, the alert's mark), every note to check as its line.</item>
    /// </list>
    /// Every part's foot line says which it is: "Part 2 of 3". The task's parts are as many as the card's view made of its
    /// description (<c>RoomCardView.DescriptionPages</c>, shown with part 0); <see cref="Parts"/> counts them all. Each part
    /// counts as read when its frame is reported drawn with the card standing whole (<c>RoomCardView.LaidWhole</c>).
    /// </summary>
    public static class RoomReview
    {
        public const string CheckTitle = "Check First";

        /// <summary>The words before the folder a move leaves: "From kite-run".</summary>
        public const string MovesFrom = "From ";

        /// <summary>The foot line naming a part: "Part 2 of 3".</summary>
        public static string PartOf(int part, int parts) => "Part " + (part + 1) + " of " + parts;

        /// <summary>The parts of <paramref name="review"/> whose first task made <paramref name="taskPages"/> pages.</summary>
        public static int Parts(NewWorkReview review, int taskPages) => (taskPages < 1 ? 1 : taskPages) + 1 + (Warnings(review).Count > 0 ? 1 : 0);

        private static List<string> Warnings(NewWorkReview review) =>
            review.Items.Where(each => each.Kind == ReviewItemKind.Warning).Select(each => each.Label.Trim()).Where(each => each.Length > 0).ToList();

        /// <summary>
        /// The review's part <paramref name="part"/> (from 0) of <see cref="Parts"/>: a page of the task while
        /// <paramref name="part"/> is under <paramref name="taskPages"/>, then how it runs, then the note to check where
        /// there is one. Measure the task's pages with part 0 at any count, then show each part with the count the view gave.
        /// </summary>
        public static RoomCard Card(NewWorkReview review, int part = 0, int taskPages = 1)
        {
            LineWords? Value(ReviewItemKind kind)
            {
                var item = review.Items.FirstOrDefault(each => each.Kind == kind);
                return item == null || item.ValueRuns.IsEmpty ? null : item.ValueRuns;
            }
            var pages = taskPages < 1 ? 1 : taskPages;
            var parts = Parts(review, pages);
            var at = part < 0 ? 0 : part >= parts ? parts - 1 : part;
            var foot = LineWords.Ours(PartOf(at, parts));
            var title = Value(ReviewItemKind.Project);
            if (at < pages)
                return new RoomCard(title, Value(ReviewItemKind.FirstTask) ?? LineWords.Ours(""), RoomCardHead.Project, more: foot, descriptionPage: at);

            var warnings = Warnings(review);
            if (at == pages + 1)
                return new RoomCard(title, status: new RoomCardStatus(CheckTitle, string.Join(" ", warnings), RoomTone.Attention, RoomKind.Failed), more: foot);

            // The model by the exact id the start sends where it has one: the one the person reads is the one that runs.
            var model = Value(ReviewItemKind.ModelId) ?? Value(ReviewItemKind.Model);
            var slots = new List<RoomSlot>();
            if (Value(ReviewItemKind.Folder) is LineWords folder) slots.Add(new RoomSlot(RoomKind.Folder, folder));
            if ((model ?? Value(ReviewItemKind.AgentApp)) is LineWords agent) slots.Add(new RoomSlot(RoomKind.Agent, agent));
            slots.Add(new RoomSlot(RoomKind.Level, LineWords.Ours(AgentColumn.LevelWords(review.NeverAsk))));

            // Each row its value alone, its kind's own mark saying what it is: no note that names a field (the critic, 2026-11-11).
            var rows = new List<RoomListRow>();
            if (Value(ReviewItemKind.TaskName) is LineWords name) rows.Add(new RoomListRow(RoomKind.Task, name, null));
            // The agent app stands as a row only where the model took its slot, so nothing sent goes unshown.
            if (model != null && Value(ReviewItemKind.AgentApp) is LineWords app) rows.Add(new RoomListRow(RoomKind.App, app, null));
            if (Value(ReviewItemKind.ModelLocation) is LineWords where) rows.Add(new RoomListRow(RoomKind.Computer, where, null));
            if (Value(ReviewItemKind.FolderNow) is LineWords before) rows.Add(new RoomListRow(RoomKind.Folder, LineWords.Ours(MovesFrom) + before, null));
            return new RoomCard(title, slots: slots, list: rows, more: foot);
        }
    }
}
