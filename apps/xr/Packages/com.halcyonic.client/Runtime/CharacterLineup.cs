#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>How much a workstream's character matters, most first.</summary>
    public enum LineupTier
    {
        /// <summary>Waiting for its person: an approval is pending.</summary>
        NeedsYou,

        /// <summary>Failed, unknown, or finished with failing tests.</summary>
        Notice,

        /// <summary>Starting, working or running tests.</summary>
        Active,

        /// <summary>Not started, finished or stopped, with nothing to look at.</summary>
        AtRest,
    }

    /// <summary>
    /// Chooses which workstreams have a character on the stage, and where each one stands.
    /// </summary>
    /// <remarks>
    /// Workstreams that need attention come first, then active ones, then the most recently changed.
    /// Characters that need attention (needs you, failed, unknown, failing tests) stand nearest the
    /// middle of the person's view; every other character keeps its slot for as long as it stays
    /// shown. A newcomer takes the free slot nearest the middle, or the slot of the workstream it
    /// replaces; a character that comes to need attention trades places with the character nearest
    /// the middle that does not. A waiting workstream replaces a shown one only if it is in a more
    /// important tier, or if both are at rest and the waiting one changed more recently. Active and
    /// attention workstreams change every few seconds while they work, so ranking them by recency
    /// would swap characters in and out; within those tiers the ones already shown stay.
    /// <para>
    /// The person can ask for a workstream the lineup did not choose, from More work: it then takes
    /// the place of the character that ranks last, and keeps a slot until another is asked for or it
    /// leaves the workstreams given. The one it replaced is not lost; it waits like any other.
    /// </para>
    /// </remarks>
    public sealed class CharacterLineup
    {
        private static readonly Comparison<WorkstreamView> ByRank = Rank;

        private readonly string?[] slots;
        private readonly int[] fillOrder;
        private readonly Dictionary<string, WorkstreamView> present = new Dictionary<string, WorkstreamView>();
        private readonly HashSet<string> shown = new HashSet<string>();
        private readonly List<WorkstreamView> waiting = new List<WorkstreamView>();
        private string? requested;

        public CharacterLineup(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The stage needs at least one slot.");
            slots = new string?[capacity];
            fillOrder = new int[capacity];
            for (var i = 0; i < capacity; i++) fillOrder[i] = i;
            // Middle slots first; of two equally central slots, the left one.
            Array.Sort(fillOrder, (a, b) =>
            {
                var byDistance = Math.Abs(2 * a - (capacity - 1)).CompareTo(Math.Abs(2 * b - (capacity - 1)));
                return byDistance != 0 ? byDistance : a.CompareTo(b);
            });
        }

        public int Capacity => slots.Length;

        /// <summary>
        /// The workstream id standing in each slot, from the person's left to their right, or null
        /// for an empty slot.
        /// </summary>
        public IReadOnlyList<string?> Slots => slots;

        /// <summary>The slot a workstream stands in, or -1 when it has no character.</summary>
        public int SlotOf(string workstreamId) => Array.IndexOf(slots, workstreamId);

        /// <summary>The workstream the person asked to see, which keeps a slot while it is given, or null.</summary>
        public string? Requested => requested;

        /// <summary>
        /// Asks for a workstream's character on the stage, whatever its rank, from the next
        /// <see cref="Update"/>; null withdraws the request. Only one is asked for at a time.
        /// </summary>
        public void Request(string? workstreamId) => requested = workstreamId;

        /// <summary>Brings the lineup up to date with the current workstreams. Returns whether any slot changed.</summary>
        public bool Update(IEnumerable<WorkstreamView> workstreams)
        {
            if (workstreams == null) throw new ArgumentNullException(nameof(workstreams));
            present.Clear();
            foreach (var workstream in workstreams) present[workstream.WorkstreamId] = workstream;
            if (requested != null && !present.ContainsKey(requested)) requested = null;

            var changed = false;
            shown.Clear();
            for (var slot = 0; slot < slots.Length; slot++)
            {
                var id = slots[slot];
                if (id == null) continue;
                if (present.ContainsKey(id))
                {
                    shown.Add(id);
                }
                else
                {
                    slots[slot] = null;
                    changed = true;
                }
            }

            waiting.Clear();
            foreach (var workstream in present.Values)
            {
                if (!shown.Contains(workstream.WorkstreamId)) waiting.Add(workstream);
            }
            waiting.Sort(ByRank);

            var next = 0;
            foreach (var slot in fillOrder)
            {
                if (next == waiting.Count) break;
                if (slots[slot] != null) continue;
                slots[slot] = waiting[next++].WorkstreamId;
                changed = true;
            }

            // The one asked for first, wherever it ranks, then the rest by rank.
            if (requested != null && SlotOf(requested) < 0)
            {
                slots[WeakestSlot()] = requested;
                waiting.RemoveAll(workstream => workstream.WorkstreamId == requested);
                changed = true;
            }
            while (next < waiting.Count)
            {
                var weakest = WeakestSlot();
                if (weakest < 0 || !Replaces(waiting[next], present[slots[weakest]!])) break;
                slots[weakest] = waiting[next++].WorkstreamId;
                changed = true;
            }
            return BringAttentionToTheMiddle() || changed;
        }

        /// <summary>
        /// Orders workstreams as the lineup ranks them, best first: needs you, then failed, unknown or
        /// failing tests, then active, then at rest; within a tier the most recently changed first.
        /// </summary>
        public static int Compare(WorkstreamView a, WorkstreamView b) => Rank(a, b);

        /// <summary>The tier a workstream's character belongs to, from its attention and status.</summary>
        public static LineupTier TierOf(WorkstreamView workstream)
        {
            if (workstream.Attention.Level == AttentionLevel.ActionRequired || workstream.Status == WorkstreamStatus.WaitingForHuman)
            {
                return LineupTier.NeedsYou;
            }
            if (workstream.Attention.Level == AttentionLevel.Notice
                || workstream.Status == WorkstreamStatus.Failed
                || workstream.Status == WorkstreamStatus.Unknown)
            {
                return LineupTier.Notice;
            }
            switch (workstream.Status)
            {
                case WorkstreamStatus.Starting:
                case WorkstreamStatus.Running:
                case WorkstreamStatus.Verifying:
                    return LineupTier.Active;
                default:
                    return LineupTier.AtRest;
            }
        }

        /// <summary>Best first: by tier, then most recently changed, then by id so ties are stable.</summary>
        private static int Rank(WorkstreamView a, WorkstreamView b)
        {
            var byTier = TierOf(a).CompareTo(TierOf(b));
            if (byTier != 0) return byTier;
            var byRecency = string.CompareOrdinal(b.UpdatedAt, a.UpdatedAt);
            return byRecency != 0 ? byRecency : string.CompareOrdinal(a.WorkstreamId, b.WorkstreamId);
        }

        private static bool Replaces(WorkstreamView waiting, WorkstreamView shown)
        {
            var waitingTier = TierOf(waiting);
            var shownTier = TierOf(shown);
            if (waitingTier != shownTier) return waitingTier < shownTier;
            return waitingTier == LineupTier.AtRest && string.CompareOrdinal(waiting.UpdatedAt, shown.UpdatedAt) > 0;
        }

        /// <summary>
        /// Lets every character that needs attention stand nearer the middle than every character
        /// that does not. Going outward from the middle, a slot without an attention character trades
        /// with the most important attention character standing farther out. Attention characters
        /// never trade among themselves, so no one moves without a reason.
        /// </summary>
        private bool BringAttentionToTheMiddle()
        {
            var changed = false;
            for (var i = 0; i < fillOrder.Length; i++)
            {
                var inner = fillOrder[i];
                if (NeedsAttention(inner)) continue;
                var outer = -1;
                for (var j = i + 1; j < fillOrder.Length; j++)
                {
                    var candidate = fillOrder[j];
                    if (!NeedsAttention(candidate)) continue;
                    if (outer < 0 || Rank(present[slots[candidate]!], present[slots[outer]!]) < 0) outer = candidate;
                }
                if (outer < 0) break;
                var moving = slots[outer];
                slots[outer] = slots[inner];
                slots[inner] = moving;
                changed = true;
            }
            return changed;
        }

        private bool NeedsAttention(int slot)
        {
            var id = slots[slot];
            return id != null && TierOf(present[id]) <= LineupTier.Notice;
        }

        /// <summary>
        /// The occupied slot whose workstream ranks last, never the one asked for, or -1 when that is
        /// the only one. Only called when every slot is occupied.
        /// </summary>
        private int WeakestSlot()
        {
            var weakest = -1;
            for (var slot = 0; slot < slots.Length; slot++)
            {
                if (slots[slot] == requested) continue;
                if (weakest < 0 || Rank(present[slots[slot]!], present[slots[weakest]!]) > 0) weakest = slot;
            }
            return weakest;
        }
    }
}
