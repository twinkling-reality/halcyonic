#nullable enable
using System;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// A column's host, bound to the session shown when the column was made (ADR 0026), so what the
    /// column sends reaches only that very session: the demonstration's, or the computer's. Once another
    /// session shows, as the computer's live session taking the demonstration's place (ADR 0012: nothing
    /// in the demonstration reaches an agent) or a re-pairing, or that session moves to another journal,
    /// it sends nothing, offers no API and no voice, and reads as away. A column made with no session
    /// never sends. A reconnect keeps the same session, so sends go on. Everything else is the
    /// director's, which makes one for each column it makes.
    /// </summary>
    public sealed class SessionBoundHost : IMenuHost
    {
        private readonly IMenuHost host;
        private readonly Func<RealtimeSession?> shown;
        private readonly Func<RealtimeSession, CommandEnvelope, Task<CommandAckMessage>?> submit;
        private string? journal;

        /// <param name="host">The director, for everything but the session.</param>
        /// <param name="shown">The session shown now, the demonstration's while it plays; read once to bind, then at each send.</param>
        /// <param name="submit">Sends a command to that very session through the host's submissions; null when it can't.</param>
        public SessionBoundHost(IMenuHost host, Func<RealtimeSession?> shown, Func<RealtimeSession, CommandEnvelope, Task<CommandAckMessage>?> submit)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.shown = shown ?? throw new ArgumentNullException(nameof(shown));
            this.submit = submit ?? throw new ArgumentNullException(nameof(submit));
            Session = shown();
            journal = Session?.State.Journal?.JournalId;
        }

        /// <summary>The session the column was made in, or null.</summary>
        public RealtimeSession? Session { get; }

        /// <summary>The session the column was made in is still the one shown, on the journal it was made for.</summary>
        public bool Current
        {
            get
            {
                if (Session == null || shown() != Session) return false;
                var now = Session.State.Journal?.JournalId;
                // Made before the session's first snapshot, it belongs to the first journal the session shows.
                journal ??= now;
                return now == null || now == journal;
            }
        }

        /// <summary>To the session the column was made in, through the host's submissions; null once that session is no longer the one shown.</summary>
        public Task<CommandAckMessage>? Submit(CommandEnvelope command) => Current ? submit(Session!, command) : null;

        public bool Connected => Current && host.Connected;

        public ControlPlaneApi? Api => Current ? host.Api : null;

        public bool VoiceOffered => Current && host.VoiceOffered;

        public ClientProjection? State => host.State;

        public bool Demonstration => host.Demonstration;

        public double Now => host.Now;

        public DateTimeOffset Clock => host.Clock;

        public TimeZoneInfo Zone => host.Zone;

        public TextSize TextSize => host.TextSize;

        public bool KeyboardOffered => host.KeyboardOffered;

        public void OpenKeyboard(string text, string prompt, Action<string> done) => host.OpenKeyboard(text, prompt, done);

        public int RowsOf(string words, float columnDegrees) => host.RowsOf(words, columnDegrees);

        public int RowsOf(PageLine line, float columnDegrees) => host.RowsOf(line, columnDegrees);

        public bool FitsHalf(PageLine answer, float columnDegrees) => host.FitsHalf(answer, columnDegrees);

        public int TitleRows(string subject, float columnDegrees) => host.TitleRows(subject, columnDegrees);

        public int PageRows(bool sourceLine) => host.PageRows(sourceLine);

        public float PageHeight(int subjectRows, bool besideMenu) => host.PageHeight(subjectRows, besideMenu);

        public void OpenFile(string workstreamId) => host.OpenFile(workstreamId);

        public void OpenNewProject(string? projectId, string? projectName) => host.OpenNewProject(projectId, projectName);
    }
}
