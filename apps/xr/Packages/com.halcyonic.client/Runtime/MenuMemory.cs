#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// What the menu keeps for the app's run on one journal (ADR 0026), its director holding it: what
    /// must outlive a column, which is made afresh each time it opens, and a reconnect, as Projects'
    /// memory of a Connect in flight. Kept across reconnects to the same journal and never renewed then,
    /// since a socket's drop is exactly when an outcome turns unknown; another journal, or a
    /// re-pairing (<see cref="Forget"/>), starts afresh.
    /// </summary>
    public sealed class MenuMemory
    {
        private string? journal;
        private ProjectsMemory projects = new ProjectsMemory();

        /// <summary>
        /// Projects' memory for <paramref name="journalId"/>, the session's journal; null, as before the
        /// first snapshot after a reconnect, keeps the journal it was.
        /// </summary>
        public ProjectsMemory ProjectsFor(string? journalId)
        {
            Follow(journalId);
            return projects;
        }

        /// <summary>The headset was paired again: what was kept belongs to another session.</summary>
        public void Forget()
        {
            journal = null;
            projects = new ProjectsMemory();
        }

        private void Follow(string? journalId)
        {
            if (journalId == null) return;
            if (journal != null && journal != journalId) projects = new ProjectsMemory();
            journal = journalId;
        }
    }
}
