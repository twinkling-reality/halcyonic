#nullable enable
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>How the computer saw a file of the folder change over a turn.</summary>
    public enum ObservedChange
    {
        Created,
        Changed,
        Deleted,
    }

    /// <summary>
    /// One file of a project's folder the computer observed changed over a turn (ADR 0038): its path from the folder, how it
    /// changed, and its line counts and size as the computer read them. A count is null where the computer treats the file
    /// as binary or too large to count, or where the change has none: a created file has no lines before, a deleted one no
    /// lines after and no size. Observed, never reported; whoever changed it, the agent or a person, the computer cannot tell.
    /// </summary>
    public sealed class ObservedFile
    {
        public ObservedFile(string path, ObservedChange change, long? linesBefore, long? linesAfter, long? bytes)
        {
            Path = path;
            Change = change;
            LinesBefore = linesBefore;
            LinesAfter = linesAfter;
            Bytes = bytes;
        }

        public string Path { get; }

        public ObservedChange Change { get; }

        public long? LinesBefore { get; }

        public long? LinesAfter { get; }

        public long? Bytes { get; }
    }

    /// <summary>
    /// What the computer observed of an execution's folder over one turn (<c>execution.files.observed</c>): the files it
    /// lists, created, changed then deleted as the event gives them, and the totals, which count the files past the
    /// event's lists too. A turn it did not read says why (<see cref="Skipped"/>), never no changes.
    /// </summary>
    public sealed class ObservedTurn
    {
        public ObservedTurn(long position, string occurredAt, FilesObserved observed)
        {
            Position = position;
            OccurredAt = occurredAt;
            TurnId = observed.TurnId;
            var files = new List<ObservedFile>();
            foreach (var file in observed.Created.Files) files.Add(new ObservedFile(file.Path, ObservedChange.Created, null, file.Lines, file.Bytes));
            foreach (var file in observed.Changed.Files) files.Add(new ObservedFile(file.Path, ObservedChange.Changed, file.LinesBefore, file.LinesAfter, file.Bytes));
            foreach (var file in observed.Deleted.Files) files.Add(new ObservedFile(file.Path, ObservedChange.Deleted, file.LinesBefore, null, null));
            Files = files;
            CreatedTotal = observed.Created.Total;
            ChangedTotal = observed.Changed.Total;
            DeletedTotal = observed.Deleted.Total;
            NotRead = observed.NotRead;
            // The skip's message is the control plane's and can hold a path: only its code is kept.
            Skipped = observed.Skipped?.Code;
        }

        /// <summary>The event's place in the journal.</summary>
        public long Position { get; }

        public string OccurredAt { get; }

        public string? TurnId { get; }

        /// <summary>The files the event lists, created, changed, then deleted.</summary>
        public IReadOnlyList<ObservedFile> Files { get; }

        public long CreatedTotal { get; }

        public long ChangedTotal { get; }

        public long DeletedTotal { get; }

        /// <summary>Files counted in the totals that the event's lists leave out.</summary>
        public long Unlisted => CreatedTotal + ChangedTotal + DeletedTotal - Files.Count;

        /// <summary>Entries found in the folder and not read (links, devices), which no list shows.</summary>
        public long NotRead { get; }

        /// <summary>Why the computer could not say what changed this turn; null where it read the folder.</summary>
        public FilesObservedSkippedCode? Skipped { get; }
    }
}
