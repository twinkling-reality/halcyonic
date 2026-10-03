#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the menu keeps for the app's run on one journal (ADR 0026), its director holding it: what
    /// must outlive a column, which is made afresh each time it opens, and a reconnect, as Projects'
    /// memory of a Connect in flight and New project's flow with its draft and its build. Kept across
    /// reconnects to the same journal and never renewed then, since a socket's drop is exactly when an
    /// outcome turns unknown; another journal, as the demonstration giving way to a live session, or a
    /// re-pairing (<see cref="Forget"/>), starts afresh.
    /// </summary>
    public sealed class MenuMemory
    {
        private string? journal;
        private ProjectsMemory projects = new ProjectsMemory();

        /// <summary>New project's flow, kept since it was first opened, which a build confirmed in it goes on in; null before.</summary>
        public IMenuColumn? NewProject { get; private set; }

        /// <summary>New project's flow was let go, for another journal or a re-pairing: what showed it takes it off the plane.</summary>
        public event Action<IMenuColumn>? NewProjectDropped;

        /// <summary>
        /// Projects' memory for <paramref name="journalId"/>, the session's journal; null, as before the
        /// first snapshot after a reconnect, keeps the journal it was.
        /// </summary>
        public ProjectsMemory ProjectsFor(string? journalId)
        {
            Journal(journalId);
            return projects;
        }

        /// <summary>
        /// New project's flow for <paramref name="journalId"/>: the one kept, or one made by
        /// <paramref name="make"/> when none is, once a session; a null journal keeps the one it was.
        /// </summary>
        public T NewProjectFor<T>(string? journalId, Func<T> make) where T : class, IMenuColumn
        {
            Journal(journalId);
            if (NewProject is T kept) return kept;
            var made = make();
            NewProject = made;
            return made;
        }

        /// <summary>The session's journal as it stands, read each frame: another lets go of what was kept for the last; null keeps it.</summary>
        public void Journal(string? journalId)
        {
            if (journalId == null) return;
            if (journal != null && journal != journalId) Renew();
            journal = journalId;
        }

        /// <summary>The headset was paired again: what was kept belongs to another session.</summary>
        public void Forget()
        {
            journal = null;
            Renew();
        }

        private void Renew()
        {
            projects = new ProjectsMemory();
            if (NewProject is IMenuColumn dropped)
            {
                NewProject = null;
                NewProjectDropped?.Invoke(dropped);
            }
        }
    }
}
