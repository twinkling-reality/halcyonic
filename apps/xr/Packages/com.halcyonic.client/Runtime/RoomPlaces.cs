#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>How a project's place stands its characters, by how many it holds (FIELD sets the forms on the one sheet).</summary>
    public enum PlaceForm
    {
        /// <summary>One row at full size: every character legible at the place's distance.</summary>
        Full,

        /// <summary>A second row behind the first, still at full size.</summary>
        TwoRows,

        /// <summary>Smaller characters, so the place's width holds them all.</summary>
        Small,

        /// <summary>Some stand; the rest gather behind one press, "{n} More" (AUDIT2, 2026-11-17T18:57Z).</summary>
        Crowd,
    }

    /// <summary>
    /// The counts at which a place changes its form. They are a rule's numbers, not the product's limits: the lead's 2026-11-17T18:56Z
    /// sets no cap on how many agents the room shows, and DEVICE's Quest 3 measures at 3, 6, 10 and 16 characters set the real
    /// thresholds. <see cref="Default"/> holds FIELD's sheet at its worst case until they land.
    /// </summary>
    public sealed class PlaceRule
    {
        public PlaceRule(int fullMost, int twoRowsMost, int smallMost, int crowdShown)
        {
            if (fullMost < 1 || twoRowsMost < fullMost || smallMost < twoRowsMost || crowdShown < 1)
                throw new ArgumentOutOfRangeException(nameof(fullMost), "A place's counts rise from one row to the crowd, each at least one.");
            FullMost = fullMost;
            TwoRowsMost = twoRowsMost;
            SmallMost = smallMost;
            CrowdShown = crowdShown;
        }

        /// <summary>The most one row holds at full size.</summary>
        public int FullMost { get; }

        /// <summary>The most two rows hold at full size.</summary>
        public int TwoRowsMost { get; }

        /// <summary>The most the place holds with smaller characters.</summary>
        public int SmallMost { get; }

        /// <summary>How many stand in front of a crowd's "{n} More", besides every one that needs you.</summary>
        public int CrowdShown { get; }

        /// <summary>
        /// FIELD's corrected numbers (2026-11-17T19:11Z), the worst case at 200 dp names at Larger: 3 in a row (a row's extent holds its outer tags),
        /// 5 in two (the back row one fewer, at 0.85 scale, which is the sheet's "smaller", so <see cref="PlaceForm.Small"/> is never chosen), then
        /// both rows standing and the rest gathered behind "{n} More". DEVICE's Quest 3 numbers can only lower them.
        /// </summary>
        public static PlaceRule Default { get; } = new PlaceRule(3, 5, 5, 5);

        public PlaceForm FormOf(int count) =>
            count <= FullMost ? PlaceForm.Full : count <= TwoRowsMost ? PlaceForm.TwoRows : count <= SmallMost ? PlaceForm.Small : PlaceForm.Crowd;
    }

    /// <summary>
    /// One project's place in the room (the lead, 2026-11-17T18:56Z): every one of its tasks, those that need the person first, then
    /// the shelf's tiers (<see cref="CharacterLineup.TierOf"/>), then the newest. Named once, by the project's own name.
    /// </summary>
    public sealed class RoomPlace
    {
        public RoomPlace(string projectId, string name, IReadOnlyList<string> tasks, int needsYou, PlaceForm form, int standing)
        {
            ProjectId = projectId;
            Name = name;
            Tasks = tasks;
            NeedsYou = needsYou;
            Form = form;
            Standing = standing;
        }

        public string ProjectId { get; }

        /// <summary>The project's name as the person gave it, said once on the place (never on each tag; AUDIT2 18:57Z).</summary>
        public string Name { get; }

        /// <summary>Every task of the project with work, in the place's order: those that need the person first.</summary>
        public IReadOnlyList<string> Tasks { get; }

        /// <summary>How many of <see cref="Tasks"/>, from the first, need the person.</summary>
        public int NeedsYou { get; }

        public PlaceForm Form { get; }

        /// <summary>How many of <see cref="Tasks"/>, from the first, stand: all of them unless the place is a gathered crowd.</summary>
        public int Standing { get; }

        /// <summary>The characters gathered behind "{n} More": none of them ever needs the person.</summary>
        public int More => Tasks.Count - Standing;

        public IEnumerable<string> Shown => Tasks.Take(Standing);
    }

    /// <summary>
    /// Every project with work, each its own place, in an order that holds: a place keeps its order among the rest as others come and go, and a new
    /// project's place stands after the rest. Presentation only: standing a task selects nothing and answers nothing (AUDIT3's MP1).
    /// </summary>
    public sealed class RoomPlaces
    {
        private readonly List<string> order = new List<string>();
        private string? journal;

        public PlaceRule Rule { get; set; } = PlaceRule.Default;

        /// <summary>The places whose gathered crowd the person spread open ("{n} More" pressed); "Show Fewer" gathers it again.</summary>
        public ISet<string> Spread { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The places whose span holds one tag a row as their tags are drawn: each takes the crowd form at once, whatever its count (AUDIT's
        /// 21:4xZ: a place one to a row stacks its team as a floating column, read as labels, not a team), its front and every one that needs
        /// the person standing and the rest behind "{n} More". The stage says which, from its layout; "{n} More" still spreads them.
        /// </summary>
        public ISet<string> OneARow { get; } = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<RoomPlace> Places { get; private set; } = Array.Empty<RoomPlace>();

        /// <summary>Lays the places from <paramref name="work"/>, named by <paramref name="projectName"/> (the project's own name, or null where unknown).</summary>
        /// <param name="selectedId">The task whose bar is open: it always stands, as one that needs the person does (AUDIT3's PL1), so the bar
        /// never names a task with no character.</param>
        public IReadOnlyList<RoomPlace> Update(string? journalId, IEnumerable<WorkstreamView> work, Func<string, string?> projectName, string? selectedId = null)
        {
            if (!string.Equals(journal, journalId, StringComparison.Ordinal))
            {
                order.Clear();
                Spread.Clear();
                OneARow.Clear();
                journal = journalId;
            }
            var byProject = work.Where(each => !string.IsNullOrEmpty(each.ProjectId)).GroupBy(each => each.ProjectId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
            order.RemoveAll(id => !byProject.ContainsKey(id));
            // A new project's place stands after the rest, the oldest work first among those that arrive together.
            foreach (var id in byProject.Keys.Where(id => !order.Contains(id))
                         .OrderBy(id => byProject[id].Min(each => each.CreatedAt ?? ""), StringComparer.Ordinal).ThenBy(id => id, StringComparer.Ordinal))
                order.Add(id);
            Spread.IntersectWith(order);
            OneARow.IntersectWith(order);
            var places = new List<RoomPlace>(order.Count);
            foreach (var id in order)
            {
                var tasks = byProject[id].OrderBy(CharacterLineup.TierOf).ThenByDescending(each => each.UpdatedAt ?? "", StringComparer.Ordinal)
                    .ThenBy(each => each.WorkstreamId, StringComparer.Ordinal).ToList();
                var needsYou = tasks.Count(each => CharacterLineup.TierOf(each) == LineupTier.NeedsYou);
                var form = OneARow.Contains(id) && tasks.Count > 1 ? PlaceForm.Crowd : Rule.FormOf(tasks.Count);
                var front = OneARow.Contains(id) ? 1 : Rule.CrowdShown;
                // A crowd stands its front and every one that needs the person; spread open, all of it.
                var standing = form != PlaceForm.Crowd || Spread.Contains(id) ? tasks.Count : Math.Min(tasks.Count, Math.Max(front, needsYou));
                var ids = tasks.Select(each => each.WorkstreamId).ToList();
                // The selected task past a crowd's front stands at the front's end, and the front grows by it.
                var at = selectedId == null ? -1 : ids.IndexOf(selectedId);
                if (at >= standing)
                {
                    ids.RemoveAt(at);
                    ids.Insert(standing, selectedId!);
                    standing++;
                }
                var name = projectName(id);
                places.Add(new RoomPlace(id, string.IsNullOrWhiteSpace(name) ? "" : LabelText.Plain(name!), ids, needsYou, form, standing));
            }
            Places = places;
            return places;
        }

        /// <summary>The place that stands <paramref name="workstreamId"/>'s character, or null where none does.</summary>
        public RoomPlace? PlaceOf(string workstreamId) => Places.FirstOrDefault(place => place.Tasks.Contains(workstreamId));
    }
}
