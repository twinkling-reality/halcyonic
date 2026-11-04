#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Reads of tasks' journal histories, which a snapshot does not carry, into an <see cref="ActivityLog"/>. Each
    /// task's latest read is the one that counts, so a read for one task never discards another's: a launch that
    /// restores other tasks' panels beside the one opened reads theirs too, and a single count across tasks once left
    /// the opened task's activity empty ("Nothing Reported", 2026-11-04). Not thread safe; use it from one thread.
    /// </summary>
    public sealed class HistoryReads
    {
        /// <summary>What a task's file adds to its time line while its history is being read.</summary>
        public const string ReadingNote = " · reading earlier activity…";

        /// <summary>What a task's file adds to its time line when its history could not be read.</summary>
        public const string UnavailableNote = " · earlier activity unavailable, reopen to try again";

        private readonly ActivityLog activity;
        private readonly Dictionary<string, int> latest = new Dictionary<string, int>();
        private readonly Dictionary<string, string> notes = new Dictionary<string, string>();
        private int requests;

        public HistoryReads(ActivityLog activity)
        {
            this.activity = activity;
        }

        /// <summary>What the task's file adds to its time line about its history: empty once it is read.</summary>
        public string NoteFor(string workstreamId) => notes.TryGetValue(workstreamId, out var note) ? note : "";

        /// <summary>
        /// Reads the task's history and records it, unless a later read for the same task started meanwhile or
        /// <paramref name="current"/> says it no longer belongs (another journal is served now).
        /// </summary>
        /// <returns>The failure when this read failed and is still the task's latest; otherwise null.</returns>
        public async Task<Exception?> ReadAsync(string workstreamId, Func<Task<IReadOnlyList<StoredEvent>>> read, Func<bool> current)
        {
            var request = ++requests;
            latest[workstreamId] = request;
            notes[workstreamId] = ReadingNote;
            try
            {
                var events = await read();
                if (!IsLatest(workstreamId, request) || !current()) return null;
                activity.Record(events);
                notes.Remove(workstreamId);
                return null;
            }
            catch (Exception error)
            {
                if (!IsLatest(workstreamId, request)) return null;
                // Live activity still arrives; the file says the older part is missing, and how to read it again.
                notes[workstreamId] = UnavailableNote;
                return error;
            }
        }

        /// <summary>The task's history needs no read, as the recorded demonstration plays it through the session.</summary>
        public void NotNeeded(string workstreamId)
        {
            latest.Remove(workstreamId);
            notes.Remove(workstreamId);
        }

        private bool IsLatest(string workstreamId, int request) => latest.TryGetValue(workstreamId, out var last) && last == request;
    }
}
