#nullable enable
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// The first visit (ADR 0026): until a computer's first task, the menu shows one question, "What
    /// would you like to work on?", with Settings alone in its row, and opens on it by itself once each
    /// time the app starts, when the live session is first ready, never over work already open; a
    /// reconnect doesn't open it again. The demonstration, which starts with recorded work, never asks
    /// and opens nothing: it opens closed. Whether a computer has had a task is kept on this device for
    /// each journal, since another computer's journal is another one: a computer that has had one never
    /// asks again here, whatever becomes of its work, while another computer with none still asks.
    /// </summary>
    public sealed class FirstVisit
    {
        /// <summary>How many journals that have had a task are remembered, the most recently seen kept.</summary>
        public const int Journals = 8;

        private readonly List<string> started = new List<string>();

        /// <param name="started">The journals that have had a task, as <see cref="Started"/> wrote them; anything unreadable is none.</param>
        public FirstVisit(string? started = null)
        {
            foreach (var journal in (started ?? "").Split(' '))
            {
                if (journal.Length > 0 && !this.started.Contains(journal) && this.started.Count < Journals) this.started.Add(journal);
            }
        }

        /// <summary>This app start's first visit has come: the live session was first ready.</summary>
        public bool Visited { get; private set; }

        /// <summary>The journals that have had a task, the most recently seen first, as text for a device preference.</summary>
        public string Started => string.Join(" ", started);

        /// <summary>
        /// Whether the menu shows the first question in place of its places now: never in the
        /// demonstration; while the session isn't live, or before its first snapshot, null, since what
        /// the computer holds isn't known and nothing is decided on a stale state; else only while its
        /// journal has never had a task, which a task now counts as having had for good.
        /// </summary>
        public bool? Asks(bool live, bool demonstration, string? journalId, bool anyTask)
        {
            if (demonstration) return false;
            if (!live || string.IsNullOrEmpty(journalId)) return null;
            var at = started.IndexOf(journalId!);
            if (anyTask)
            {
                if (at == 0) return false;
                if (at > 0) started.RemoveAt(at);
                started.Insert(0, journalId!);
                if (started.Count > Journals) started.RemoveRange(Journals, started.Count - Journals);
                return false;
            }
            return at < 0;
        }

        /// <summary>
        /// Whether the menu opens by itself now, on the first question (<paramref name="asks"/>, from
        /// <see cref="Asks"/>): once the computer's state is known, with nothing open, and once only in
        /// this app start. A first visit to a computer that already has work counts, and opens nothing;
        /// so does one with something open. The demonstration's never counts.
        /// </summary>
        public bool Due(bool demonstration, bool somethingOpen, bool? asks)
        {
            if (demonstration || asks == null || Visited) return false;
            Visited = true;
            return asks.Value && !somethingOpen;
        }
    }

    /// <summary>What showing or hiding a project's work on the stage asks of the stage.</summary>
    public readonly struct ShowingChange
    {
        public ShowingChange(bool changed, bool withdrawRequest)
        {
            Changed = changed;
            WithdrawRequest = withdrawRequest;
        }

        /// <summary>Which projects show changed, so the stage refreshes and the device keeps it.</summary>
        public bool Changed { get; }

        /// <summary>The work the person brought forward belongs to the project just hidden, so it leaves with it.</summary>
        public bool WithdrawRequest { get; }
    }

    /// <summary>Projects' Show on stage and Hide from stage (ADR 0026), as the stage's choice of projects takes them.</summary>
    public static class ProjectShowing
    {
        /// <summary>
        /// Shows or hides <paramref name="projectId"/>'s work in <paramref name="visibility"/>. Hiding
        /// withdraws <paramref name="requested"/>, the work brought forward from Tasks, when it is that
        /// project's. Nothing changes when the project already stands as asked.
        /// </summary>
        public static ShowingChange Apply(StageVisibility visibility, string projectId, bool shown, ClientProjection state, string? requested)
        {
            if (visibility.Shows(projectId) == shown) return new ShowingChange(false, false);
            if (shown)
            {
                visibility.Show(projectId);
                return new ShowingChange(true, false);
            }
            visibility.Hide(projectId, state.Projects.Keys);
            var withdraw = requested != null && state.Workstreams.TryGetValue(requested, out var work) && work.ProjectId == projectId;
            return new ShowingChange(true, withdraw);
        }
    }
}
