#nullable enable
using System;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// What the menu's director gives every column (ADR 0026): the session, the clock, what may be
    /// asked of the computer, the keyboard, the view's measures and the way to open what a column
    /// leads to. Engine-free; the Unity layer implements it. Whatever only one column needs, as New
    /// project's kept drafts, goes in that column's own constructor, not here.
    /// </summary>
    public interface IMenuHost
    {
        /// <summary>The session's projection; null before the first snapshot.</summary>
        ClientProjection? State { get; }

        /// <summary>The realtime session is live.</summary>
        bool Connected { get; }

        /// <summary>The recorded demonstration plays: it connects nothing and starts nothing.</summary>
        bool Demonstration { get; }

        /// <summary>A steady clock, in seconds, for how long something has shown; the renders pass their own.</summary>
        double Now { get; }

        /// <summary>The time of day, for what a page says when, as "Today at 15:18".</summary>
        DateTimeOffset Clock { get; }

        /// <summary>The person's time zone, for the same.</summary>
        TimeZoneInfo Zone { get; }

        /// <summary>The person's reading size (<see cref="Comfort.Text"/>).</summary>
        TextSize TextSize { get; }

        /// <summary>Hold to talk may show: in development builds, connected, never in the demonstration.</summary>
        bool VoiceOffered { get; }

        /// <summary>What may be asked of the computer; null when nothing can be (the demonstration, unpaired).</summary>
        ControlPlaneApi? Api { get; }

        /// <summary>
        /// The session's submit, for a column's own rules to send through; null when there is no
        /// session. The director never sends around a column's rules.
        /// </summary>
        Task<CommandAckMessage>? Submit(CommandEnvelope command);

        /// <summary>The system keyboard can open here; where it can't, as in the editor, <see cref="OpenKeyboard"/> opens nothing.</summary>
        bool KeyboardOffered { get; }

        /// <summary>The system keyboard, holding <paramref name="text"/>; <paramref name="done"/> only when the person finishes, never when they cancel, and never where no keyboard opens.</summary>
        void OpenKeyboard(string text, string prompt, Action<string> done);

        /// <summary>How many rows <paramref name="words"/> wrap to across a column's content, as the view lays them.</summary>
        int RowsOf(string words, float columnDegrees);

        /// <summary>How many rows a line's words wrap to in full, its icon column, chip, fact and chevron aside, as the view lays them.</summary>
        int RowsOf(PageLine line, float columnDegrees);

        /// <summary>Whether an answer's words fit one row in half a column, so it may share its row with the next.</summary>
        bool FitsHalf(PageLine answer, float columnDegrees);

        /// <summary>How many rows a subject's title takes: one, or two, the most it shows.</summary>
        int TitleRows(string subject, float columnDegrees);

        /// <summary>The rows a list's page holds at today's text size (<see cref="MenuFrame.RowsAPage"/>).</summary>
        int PageRows(bool sourceLine);

        /// <summary>
        /// The most a page's lines may take, in units of the plane's distance, on the stage it shows on
        /// (<see cref="MenuPage.Height"/>): its subject in <paramref name="subjectRows"/> rows, beside the
        /// menu or alone. Read when the column opens, so a page never packs again while it shows.
        /// </summary>
        float PageHeight(int subjectRows, bool besideMenu);

        /// <summary>Opens a task's file beside the menu, as a row of Tasks does.</summary>
        void OpenFile(string workstreamId);

        /// <summary>Opens New project beside the menu: Projects' New project (null, null), or Add a task to a project.</summary>
        void OpenNewProject(string? projectId, string? projectName);
    }

    /// <summary>
    /// One column of the menu's plane (ADR 0026): a place of the menu (Tasks, Projects, Usage,
    /// Settings), a task's file, or New project. A plain object the director asks for its frame and
    /// hands every press, draw and word heard, each only while the app has focus; its own rules
    /// decide whether a press may act, and only they send, through <see cref="IMenuHost.Submit"/>. A
    /// place's frame leaves its sections out: the director adds the menu's places
    /// (<see cref="MenuBar.Sections"/>) and handles choosing one. The director runs Hold to talk's one
    /// voice for any held prompt and tells the column whose prompt it was.
    /// </summary>
    public interface IMenuColumn
    {
        /// <summary>
        /// What to show now, its side panel in it, built from the column's state when asked; null when
        /// it has nothing to show. The director asks once each time the column changes and draws that
        /// very object, so <see cref="Drawn"/> hands it back.
        /// </summary>
        MenuFrame? Frame { get; }

        /// <summary>Its frame changed: the director draws it again.</summary>
        event Action? Changed;

        /// <summary>Its Close was pressed: the director takes it off the plane.</summary>
        event Action? Closed;

        /// <summary>
        /// What a press raised, as the view reports it: a section's <see cref="MenuFrame.ChooseSection"/>
        /// with its key, a line's action with its key, a prompt's id, <see cref="SidePanel.Close"/> and
        /// <see cref="Footer.Close"/> included. Whether it acts is the column's to say.
        /// </summary>
        void Act(string id, string? key);

        /// <summary>
        /// The director drew <paramref name="drawn"/>, the very frame this column gave, whole as the plane
        /// has it, and it shows, never while folded: its page, or with <paramref name="sidePanel"/> its
        /// side panel (<see cref="MenuFrame.Side"/>), in its own column or in the frame's place. What
        /// counts as read, as a request's part or a review's, counts only then, and only for the frame
        /// the column still stands by.
        /// </summary>
        void Drawn(MenuFrame drawn, bool sidePanel);

        /// <summary>A held prompt of this column's, as Hold to talk, was held long enough to start; the director's voice records.</summary>
        void HoldStarted(string id);

        /// <summary>That hold ended: let go on the prompt (<paramref name="letGo"/>), so what was said is sent to be heard, or dropped.</summary>
        void HoldEnded(string id, bool letGo);

        /// <summary>Hold to talk: the words the computer heard, a draft to show and confirm.</summary>
        void Heard(string text);

        /// <summary>Hold to talk's own line for the page: listening, or why nothing came of it.</summary>
        void Said(string words);

        /// <summary>Once a frame: what it awaits, as a read, an acknowledgement or a build, is looked at.</summary>
        void Tick();

        /// <summary>Another window took focus, or this column left the plane: an armed confirmation lapses, a review is read again.</summary>
        void FocusLeft();
    }
}
