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
    /// <item>What else is sent stands as list rows, each its value alone under its kind's mark: the task's name (the flag),
    /// the agent app where the model took its slot, where the model runs, the model's id (the model's orb), "From" the
    /// folder it moves from.</item>
    /// <item>What the first task names that the person should check is the status, Attention: read it before Yes.</item>
    /// <item>Where the first task pages, the foot line says which part: "Part 2 of 4".</item>
    /// </list>
    /// The card is one part of the review. Each page counts as read when its frame is reported drawn with the card standing
    /// whole (<c>RoomCardView.LaidWhole</c>).
    /// </summary>
    public static class RoomReview
    {
        public const string CheckTitle = "Check Before Starting";

        /// <summary>The words before the folder a move leaves: "From kite-run".</summary>
        public const string MovesFrom = "From ";

        /// <summary>The page marker where the first task pages: "Part 2 of 4".</summary>
        public static string PartOf(int page, int pages) => "Part " + (page + 1) + " of " + pages;

        /// <summary>
        /// The review as a card. Where its first task pages, <paramref name="page"/> is the page shown (from 0) and
        /// <paramref name="pages"/> how many the card's view made of it (<c>RoomCardView.DescriptionPages</c>), so the foot
        /// line says which part this is; null both where it stands whole.
        /// </summary>
        public static RoomCard Card(NewWorkReview review, int? page = null, int? pages = null)
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
            var paged = page != null && pages is int of && of > 1;
            var more = paged ? LineWords.Ours(PartOf(page!.Value, pages!.Value)) : null;

            return new RoomCard(Value(ReviewItemKind.Project), Value(ReviewItemKind.FirstTask), RoomCardHead.Project, status, slots, list: rows, more: more,
                descriptionPage: page);
        }
    }
}
