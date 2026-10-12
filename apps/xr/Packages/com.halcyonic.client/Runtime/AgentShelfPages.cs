#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Ordinary local shelf selection. It grants no task, command, placement or input authority.</summary>
    public sealed class AgentShelfPageInfo
    {
        public AgentShelfPageInfo(int index, int count, string? title) { Index = index; Count = count; Title = title; }
        public int Index { get; }
        public int Count { get; }
        public string? Title { get; }
        public bool Previous => Count > 0 && Index != 0;
        public bool Next => Count > 0 && Index < Count - 1;
        public string Summary => Index >= 0 ? "Showing " + (Index + 1) + " of " + Count : "Choose an agent · " + Count + " available";
    }

    /// <summary>One explicitly paged workstream. Selection survives rank/order changes;
    /// a removed task or another journal never silently retargets its character.</summary>
    public sealed class AgentShelfPages
    {
        private readonly List<WorkstreamView> ordered = new List<WorkstreamView>();
        private bool initialized;
        private string? journal;
        public string? SelectedId { get; private set; }
        public string? ShownId => ordered.Any(work => work.WorkstreamId == SelectedId) ? SelectedId : null;
        public AgentShelfPageInfo Info
        {
            get
            {
                var index = ordered.FindIndex(work => work.WorkstreamId == SelectedId);
                return new AgentShelfPageInfo(index, ordered.Count, index >= 0 ? ordered[index].Title : null);
            }
        }
        public void Update(string? journalId, IEnumerable<WorkstreamView> work)
        {
            var changedJournal = initialized && !string.Equals(journal, journalId, StringComparison.Ordinal);
            if (changedJournal) SelectedId = null;
            ordered.Clear(); ordered.AddRange(work.OrderBy(CharacterLineup.TierOf).ThenByDescending(each => each.UpdatedAt)
                .ThenBy(each => each.WorkstreamId, StringComparer.Ordinal));
            if (!initialized && journalId != null)
            {
                SelectedId = ordered.FirstOrDefault()?.WorkstreamId;
                initialized = true;
            }
            journal = journalId;
        }
        public bool Select(string id)
        {
            if (!ordered.Any(work => work.WorkstreamId == id) || id == SelectedId) return false;
            SelectedId = id; return true;
        }
        public bool Move(int direction)
        {
            if (direction != -1 && direction != 1) return false;
            var index = ordered.FindIndex(work => work.WorkstreamId == SelectedId);
            var next = index < 0 ? direction > 0 ? 0 : ordered.Count - 1 : index + direction;
            return next >= 0 && next < ordered.Count && Select(ordered[next].WorkstreamId);
        }
    }
}
