#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Notices work that comes to need the person while they are busy elsewhere, such as creating a
    /// project, so the entry panel can offer to open it now or keep going. It never switches by
    /// itself. Work that already needed the person when the watch began is on the stage and in the
    /// rail already, and is not offered again; work that stops needing them and needs them again is.
    /// Hidden projects count too, since hiding a project never hides its attention.
    /// </summary>
    public sealed class AttentionWatch
    {
        private readonly HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Starts watching from the state now: what needs the person already is not news.</summary>
        public void Begin(ClientProjection state)
        {
            known.Clear();
            foreach (var workstream in NeedingYou(state)) known.Add(workstream.WorkstreamId);
        }

        /// <summary>
        /// The work that newly needs the person, the most pressing first, or null. It stays offered
        /// until <see cref="Dismiss"/> or until it no longer needs them.
        /// </summary>
        public WorkstreamView? Next(ClientProjection state)
        {
            var needing = NeedingYou(state).ToList();
            known.IntersectWith(needing.Select(workstream => workstream.WorkstreamId));
            return needing
                .Where(workstream => !known.Contains(workstream.WorkstreamId))
                .OrderBy(workstream => workstream, Comparer<WorkstreamView>.Create(CharacterLineup.Compare))
                .FirstOrDefault();
        }

        /// <summary>The person chose to keep going, or opened it: it is not offered again while it keeps needing them.</summary>
        public void Dismiss(string workstreamId) => known.Add(workstreamId);

        private static IEnumerable<WorkstreamView> NeedingYou(ClientProjection state) =>
            state.Workstreams.Values.Where(workstream => CharacterLineup.TierOf(workstream) == LineupTier.NeedsYou);
    }
}
