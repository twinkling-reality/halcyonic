#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Journal history, for what a snapshot does not carry.</summary>
    public interface IEventHistory
    {
        /// <summary>A workstream's events after a position, oldest first, at most <paramref name="limit"/> (1 to 1000).</summary>
        Task<EventsResponse> ReadAsync(string workstreamId, long after, int limit, CancellationToken cancellationToken);
    }

    public static class EventHistory
    {
        private const int PageSize = 1000;

        /// <summary>
        /// Every event of a workstream in the given journal, oldest first. Fails if the control plane
        /// now serves another journal, because positions from different journals do not mix.
        /// </summary>
        public static async Task<IReadOnlyList<StoredEvent>> ReadAllAsync(
            this IEventHistory history,
            string workstreamId,
            string journalId,
            CancellationToken cancellationToken = default)
        {
            var events = new List<StoredEvent>();
            long after = 0;
            while (true)
            {
                var page = await history.ReadAsync(workstreamId, after, PageSize, cancellationToken).ConfigureAwait(false);
                if (page.Journal.JournalId != journalId)
                {
                    throw new ControlPlaneRequestException("The control plane now serves another journal.");
                }
                events.AddRange(page.Events);
                if (page.Events.Count < PageSize) return events;
                after = page.Events[page.Events.Count - 1].Position;
            }
        }
    }

    /// <summary>A request to the control plane's REST API failed or was refused.</summary>
    public sealed class ControlPlaneRequestException : Exception
    {
        public ControlPlaneRequestException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }
}
