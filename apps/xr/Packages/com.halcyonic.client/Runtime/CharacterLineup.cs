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
    /// Each shown workstream keeps its slot for as long as it stays shown, so characters never move
    /// to make room: a newcomer takes a free slot, the one nearest the middle, or the slot of the
    /// workstream it replaces. A waiting workstream replaces a shown one only if it is in a more
    /// important tier, or if both are at rest and the waiting one changed more recently. Active and
    /// attention workstreams change every few seconds while they work, so ranking them by recency
    /// would swap characters in and out; within those tiers the ones already shown stay.
    /// </remarks>
    public sealed class CharacterLineup
    {
        private static readonly Comparison<WorkstreamView> ByRank = Rank;

        private readonly string?[] slots;
        private readonly int[] fillOrder;
        private readonly Dictionary<string, WorkstreamView> present = new Dictionary<string, WorkstreamView>();
        private readonly HashSet<string> shown = new HashSet<string>();
        private readonly List<WorkstreamView> waiting = new List<WorkstreamView>();

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

        /// <summary>Brings the lineup up to date with the current workstreams. Returns whether any slot changed.</summary>
        public bool Update(IEnumerable<WorkstreamView> workstreams)
        {
            if (workstreams == null) throw new ArgumentNullException(nameof(workstreams));
            present.Clear();
            foreach (var workstream in workstreams) present[workstream.WorkstreamId] = workstream;

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

            while (next < waiting.Count)
            {
                var weakest = WeakestSlot();
                if (!Replaces(waiting[next], present[slots[weakest]!])) break;
                slots[weakest] = waiting[next++].WorkstreamId;
                changed = true;
            }
            return changed;
        }

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

        /// <summary>The occupied slot whose workstream ranks last. Only called when every slot is occupied.</summary>
        private int WeakestSlot()
        {
            var weakest = 0;
            for (var slot = 1; slot < slots.Length; slot++)
            {
                if (Rank(present[slots[slot]!], present[slots[weakest]!]) > 0) weakest = slot;
            }
            return weakest;
        }
    }
}
