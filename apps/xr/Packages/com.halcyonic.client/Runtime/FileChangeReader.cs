#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>How the versions around a turn the computer observed were found in the project's history.</summary>
    public enum TurnVersionsFound
    {
        /// <summary>The version kept after the turn, and the one before it of the same folder: the turn's change.</summary>
        Found,

        /// <summary>The turn's version is the folder's first: the computer kept nothing earlier to compare with.</summary>
        First,

        /// <summary>
        /// No version of the turn to compare was found: not kept, or not among the newest the reader asks for. Never said as
        /// "kept none": an older one may be kept beyond what was read (AUDIT's F3, 2026-11-12).
        /// </summary>
        None,

        /// <summary>The computer keeps no history (undo is not set up there).</summary>
        NotSetUp,
    }

    /// <summary>The two versions a turn's change lies between, or why there are none.</summary>
    public readonly struct TurnVersions
    {
        public TurnVersions(TurnVersionsFound found, string? from = null, string? to = null)
        {
            Found = found;
            From = found == TurnVersionsFound.Found ? from : null;
            To = found == TurnVersionsFound.Found ? to : null;
        }

        public TurnVersionsFound Found { get; }

        /// <summary>The version before the turn; null unless found.</summary>
        public string? From { get; }

        /// <summary>The version kept after the turn; null unless found.</summary>
        public string? To { get; }
    }

    /// <summary>
    /// The reads a file's change and the whole file need from the computer (the parity map's gaps 5 and 10, read only): the
    /// project's history, to find the versions around the turn the computer observed
    /// (<c>GET /api/projects/:id/history</c>), one file's change between them (<c>GET .../changes/file</c>), and the file as
    /// it is now (<c>GET .../file</c>). It reads through <paramref name="get"/>, the control plane's own authorized GET of a
    /// relative path to its JSON body, so it holds no credential of its own. Every answer parses strictly and must be for
    /// the project, versions and path asked; every text in them is outside words.
    /// </summary>
    public sealed class FileChangeReader
    {
        /// <summary>The entries a page of history is asked for: the most the computer gives at once.</summary>
        public const int HistoryLimit = 50;

        /// <summary>How many pages of history are read, newest first, before the turn's version is taken as not among them.</summary>
        public const int MostHistoryPages = 4;

        private readonly Func<string, CancellationToken, Task<string>> get;

        public FileChangeReader(Func<string, CancellationToken, Task<string>> get)
        {
            this.get = get ?? throw new ArgumentNullException(nameof(get));
        }

        /// <summary>The path of a page of the project's history, older than <paramref name="before"/> where it names an entry.</summary>
        public static string HistoryPath(string projectId, string journalId, string? before = null) =>
            Project(projectId) + "/history?journal_id=" + Escaped(journalId, nameof(journalId)) + "&limit=" + HistoryLimit
            + (string.IsNullOrEmpty(before) ? "" : "&before=" + Uri.EscapeDataString(before));

        /// <summary>The path of one file's change from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static string FileChangePath(string projectId, string journalId, string from, string to, string path) =>
            Project(projectId) + "/changes/file?journal_id=" + Escaped(journalId, nameof(journalId)) + "&from=" + Escaped(from, nameof(from))
            + "&to=" + Escaped(to, nameof(to)) + "&path=" + Escaped(path, nameof(path));

        /// <summary>The path of one file of the project's folder as it is now.</summary>
        public static string FilePath(string projectId, string journalId, string path) =>
            Project(projectId) + "/file?journal_id=" + Escaped(journalId, nameof(journalId)) + "&path=" + Escaped(path, nameof(path));

        /// <summary>
        /// The versions around the turn the computer observed of <paramref name="executionId"/>'s run: the version kept after
        /// it (its <c>turn_id</c> the observed turn's) and the version before it of the same folder. Where the observed turn
        /// carries no id, the run's newest turn version stands for it, and only where the newest turn's version was kept (a
        /// version that was not kept never lets an older turn's change pass as this one's). The newest pages are read first.
        /// </summary>
        public async Task<TurnVersions> FindTurnAsync(string projectId, string journalId, string executionId, string? turnId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(executionId)) throw new ArgumentException("A run is required.", nameof(executionId));
            string? before = null;
            for (var page = 0; page < MostHistoryPages; page++)
            {
                var json = await get(HistoryPath(projectId, journalId, before), cancellationToken).ConfigureAwait(false);
                var response = HalcyonicJson.Deserialize<ProjectHistoryResponse>(json, strict: true);
                if (response.ProjectId != projectId) throw new FormatException("The computer answered for another project.");
                if (response.Result is UnavailableHistory) return new TurnVersions(TurnVersionsFound.NotSetUp);
                if (response.Result is not AvailableHistory history) throw new FormatException("The computer's history could not be read.");
                foreach (var entry in history.Entries)
                {
                    switch (entry)
                    {
                        case VersionEntry version when version.Checkpoint.Reason == CheckpointReason.AfterTurn && version.Checkpoint.ExecutionId == executionId:
                            if (turnId != null && version.Checkpoint.TurnId != turnId) continue;
                            return version.Previous is string previous
                                ? new TurnVersions(TurnVersionsFound.Found, previous, version.Checkpoint.CheckpointId)
                                : new TurnVersions(TurnVersionsFound.First);
                        // Without the turn's id, the run's newest turn speaks for it: a newest turn not kept means none.
                        case NotKeptEntry missing when turnId == null && missing.Reason == CheckpointReason.AfterTurn && missing.ExecutionId == executionId:
                            return new TurnVersions(TurnVersionsFound.None);
                    }
                }
                if (!history.More || history.Entries.Count == 0) break;
                before = history.Entries[history.Entries.Count - 1].EntryId;
            }
            return new TurnVersions(TurnVersionsFound.None);
        }

        /// <summary>One file's change from <paramref name="from"/> to <paramref name="to"/>, checked to be the one asked for.</summary>
        public async Task<ProjectFileChangeResponse> FileChangeAsync(string projectId, string journalId, string from, string to, string path, CancellationToken cancellationToken = default)
        {
            var json = await get(FileChangePath(projectId, journalId, from, to, path), cancellationToken).ConfigureAwait(false);
            var response = HalcyonicJson.Deserialize<ProjectFileChangeResponse>(json, strict: true);
            if (response.ProjectId != projectId || response.From != from || response.To != to || response.Path != path)
                throw new FormatException("The computer answered for another file or version.");
            return response;
        }

        /// <summary>The file as it is now, checked to be the one asked for.</summary>
        public async Task<ProjectFileResponse> FileAsync(string projectId, string journalId, string path, CancellationToken cancellationToken = default)
        {
            var json = await get(FilePath(projectId, journalId, path), cancellationToken).ConfigureAwait(false);
            var response = HalcyonicJson.Deserialize<ProjectFileResponse>(json, strict: true);
            if (response.ProjectId != projectId || response.Path != path) throw new FormatException("The computer answered for another file.");
            return response;
        }

        private static string Project(string projectId) => "api/projects/" + Escaped(projectId, nameof(projectId));

        private static string Escaped(string value, string name) =>
            string.IsNullOrEmpty(value) ? throw new ArgumentException("A value is required.", name) : Uri.EscapeDataString(value);
    }
}
