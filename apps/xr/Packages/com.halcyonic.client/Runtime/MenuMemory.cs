#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the menu keeps for the app's run on one session and journal (ADR 0026), its director holding
    /// it: what must outlive a column, which is made afresh each time it opens, and a reconnect, as
    /// Projects' memory of a Connect in flight and New project's flow with its draft and its build. Kept
    /// across reconnects to the same journal, which keep the same session, and never renewed then, since
    /// a socket's drop is exactly when an outcome turns unknown; another session, as the computer's live
    /// session taking the demonstration's place, another journal, or a re-pairing (<see cref="Forget"/>),
    /// starts afresh, so nothing made for one ticks or sends for another.
    /// </summary>
    public sealed class MenuMemory
    {
        private RealtimeSession? session;
        private bool sessionSeen;
        private string? journal;
        private ProjectsMemory projects = new ProjectsMemory();

        /// <summary>New project's flow, kept since it was first opened, which a build confirmed in it goes on in; null before.</summary>
        public IMenuColumn? NewProject { get; private set; }

        /// <summary>New project's flow was let go, for another session or journal or a re-pairing: what showed it takes it off the plane.</summary>
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
        /// <paramref name="make"/> when none is, once for the session and journal; null when none can be
        /// made. A null journal keeps the one it was.
        /// </summary>
        public IMenuColumn? NewProjectFor(string? journalId, Func<IMenuColumn?> make)
        {
            Journal(journalId);
            if (NewProject != null) return NewProject;
            NewProject = make();
            return NewProject;
        }

        /// <summary>
        /// The session shown now, read each frame: another one, as the computer's live session taking
        /// the demonstration's place or a re-pairing, lets go of what was kept for the last. True when it did.
        /// </summary>
        public bool Session(RealtimeSession? shown)
        {
            if (sessionSeen && shown == session) return false;
            var another = sessionSeen;
            session = shown;
            sessionSeen = true;
            if (another) Forget();
            return another;
        }

        /// <summary>The session's journal as it stands, read each frame: another lets go of what was kept for the last, and says so; null keeps it.</summary>
        public bool Journal(string? journalId)
        {
            if (journalId == null) return false;
            var another = journal != null && journal != journalId;
            if (another) Renew();
            journal = journalId;
            return another;
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
