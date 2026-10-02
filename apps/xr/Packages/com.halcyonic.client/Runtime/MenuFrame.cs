#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>The menu's places (ADR 0026), left to right as its bar shows them.</summary>
    public enum MenuPlace
    {
        Tasks,
        Projects,
        Usage,
        Settings,
    }

    /// <summary>
    /// The menu's bar (ADR 0026): open, its four places in a row, each with an amber dot while
    /// something there waits for the person, the chosen one lit; closed, one rounded shape whose line
    /// says what waits, or that nothing is waiting, with Open at its right.
    /// </summary>
    public sealed class MenuBar
    {
        /// <summary>What choosing a place raises, with the place's name as the key, and what Open raises.</summary>
        public const string ChoosePlace = "place";
        public const string Open = "open-menu";

        private readonly HashSet<MenuPlace> waiting;

        /// <param name="closedLine">What the closed bar says: what waits for the person, or that nothing is waiting.</param>
        /// <param name="waiting">The places where something waits for the person, which carry the amber dot.</param>
        public MenuBar(MenuPlace chosen, string closedLine, params MenuPlace[] waiting)
        {
            if (string.IsNullOrWhiteSpace(closedLine)) throw new ArgumentException("The closed bar says what waits, or that nothing is waiting.", nameof(closedLine));
            Chosen = chosen;
            ClosedLine = closedLine;
            this.waiting = new HashSet<MenuPlace>(waiting);
        }

        /// <summary>Every place, left to right.</summary>
        public static IReadOnlyList<MenuPlace> Places { get; } = new[] { MenuPlace.Tasks, MenuPlace.Projects, MenuPlace.Usage, MenuPlace.Settings };

        public MenuPlace Chosen { get; }

        public string ClosedLine { get; }

        /// <summary>Something in <paramref name="place"/> waits for the person: its amber dot shows.</summary>
        public bool Waits(MenuPlace place) => waiting.Contains(place);

        /// <summary>A place's word on the bar.</summary>
        public static string Word(MenuPlace place) => place switch
        {
            MenuPlace.Tasks => "Tasks",
            MenuPlace.Projects => "Projects",
            MenuPlace.Usage => "Usage",
            MenuPlace.Settings => "Settings",
            _ => throw new ArgumentOutOfRangeException(nameof(place), place, "Unhandled place."),
        };
    }

    /// <summary>
    /// One of a frame's sections, in the row of shapes under its subject (ADR 0026): a file's Waiting,
    /// Activity, Changes and Checks, or New project's steps. Choosing one raises
    /// <see cref="MenuFrame.ChooseSection"/> with its key; choosing an earlier step is the way back, so
    /// there is no Back prompt.
    /// </summary>
    public sealed class FrameSection
    {
        /// <param name="waits">Something in it waits for the person: its amber dot shows.</param>
        /// <param name="reached">
        /// It can be chosen. A step not reached yet stays quiet and takes no press; one the person
        /// passed over stays reached, so it can still be chosen.
        /// </param>
        public FrameSection(string key, string words, bool chosen = false, bool waits = false, bool reached = true)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A section has a key to raise.", nameof(key));
            if (string.IsNullOrWhiteSpace(words)) throw new ArgumentException("A section has its words.", nameof(words));
            if (chosen && !reached) throw new ArgumentException("A chosen section has been reached.", nameof(chosen));
            Key = key;
            Words = words;
            Chosen = chosen;
            Waits = waits;
            Reached = reached;
        }

        public string Key { get; }

        public string Words { get; }

        public bool Chosen { get; }

        public bool Waits { get; }

        public bool Reached { get; }
    }

    /// <summary>
    /// A page line's colour: the text's own, the secondary colour for what is said about the line
    /// before, or a tone. Amber means waiting for the person and nothing else, so a partial, stale or
    /// unavailable answer stays secondary and its words say so (ADR 0026).
    /// </summary>
    public enum LineTone
    {
        Primary,
        Secondary,
        Waiting,
        Good,
        Problem,
    }

    /// <summary>
    /// A line of a frame's page (ADR 0026): an icon in the fixed 24 dp column, words on the left
    /// content line, a small fact ending on the right one. A line only says something, or takes the
    /// person somewhere: pressing it raises its action with its key, as choosing an answer does or a
    /// row that opens a side panel, which shows its chevron. Rows never toggle anything, and carry no
    /// button of their own; while one is chosen, the screen may set the footer's Secondary or far right
    /// prompt for it. Text from outside is shown by <see cref="LabelText"/>'s rule before it is given.
    /// </summary>
    public sealed class PageLine
    {
        /// <param name="wordsAreData">The words are text from outside, which may end in an ellipsis where they don't fit; Halcyonic's own never do.</param>
        /// <param name="fact">A small fact at the line's right, in the Label size, as a time or what a task needs.</param>
        /// <param name="chip">
        /// The claim's evidence class, drawn as a chip: "Agent says", "Subagent says", "Inferred",
        /// "Planned", "Author unknown" or "Model explains". Null for an observed or measured fact and
        /// for Halcyonic's own words, which take none.
        /// </param>
        /// <param name="claim">The agent's own words: quoted and leaning, never read as Halcyonic's or as fact.</param>
        /// <param name="action">What pressing it raises, with <paramref name="key"/>; null for a line that only says something.</param>
        /// <param name="opens">Pressing it opens a side panel beside the page: it shows a chevron.</param>
        /// <param name="choice">An answer to choose: a shape round its words, lit when chosen.</param>
        /// <param name="rows">The most rows its words may wrap to.</param>
        /// <param name="fromRow">
        /// The first of its wrapped rows shown, from 0, as a part of a long request starts where the
        /// one before ended; <paramref name="rows"/> rows show from there.
        /// </param>
        public PageLine(string words, bool wordsAreData = false, GlazeIcon? icon = null, string? fact = null, LineTone tone = LineTone.Primary,
            string? chip = null, bool claim = false, string? action = null, string? key = null, bool opens = false, bool choice = false,
            bool chosen = false, bool available = true, int rows = 1, int fromRow = 0)
        {
            if (string.IsNullOrWhiteSpace(words)) throw new ArgumentException("A line has words.", nameof(words));
            if (string.Equals(chip, "observed", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("An observed fact takes no chip.", nameof(chip));
            if (chip != null && string.IsNullOrWhiteSpace(chip)) throw new ArgumentException("A chip has its word, or is null.", nameof(chip));
            if (opens && action == null) throw new ArgumentException("A line that opens a side panel raises an action.", nameof(opens));
            if (choice && action == null) throw new ArgumentException("An answer to choose raises an action.", nameof(choice));
            if (chosen && action == null) throw new ArgumentException("Only a line that takes a press can be chosen.", nameof(chosen));
            if (!available && action == null) throw new ArgumentException("Only a line that takes a press can be unavailable.", nameof(available));
            if (opens && choice) throw new ArgumentException("An answer is chosen, not opened.", nameof(opens));
            if (rows < 1) throw new ArgumentOutOfRangeException(nameof(rows), rows, "A line takes at least one row.");
            if (fromRow < 0) throw new ArgumentOutOfRangeException(nameof(fromRow), fromRow, "A line shows from its first row or a later one.");
            if (icon == GlazeIcon.HoldToTalk) throw new ArgumentException("A line never shows the microphone: Hold to talk is the footer's Secondary.", nameof(icon));
            Words = words;
            WordsAreData = wordsAreData;
            Icon = icon;
            Fact = fact;
            Tone = tone;
            Chip = chip;
            Claim = claim;
            Action = action;
            Key = key;
            Opens = opens;
            Choice = choice;
            Chosen = chosen;
            Available = available;
            Rows = rows;
            FromRow = fromRow;
        }

        public string Words { get; }

        public bool WordsAreData { get; }

        public GlazeIcon? Icon { get; }

        public string? Fact { get; }

        public LineTone Tone { get; }

        public string? Chip { get; }

        public bool Claim { get; }

        public string? Action { get; }

        public string? Key { get; }

        public bool Opens { get; }

        public bool Choice { get; }

        public bool Chosen { get; }

        /// <summary>Shown but taking no press now, as an answer the recording doesn't hold.</summary>
        public bool Available { get; }

        public int Rows { get; }

        public int FromRow { get; }

        public bool Pressable => Action != null && Available;
    }

    /// <summary>A fact in a side panel: its name in the Label size over its value in the Body size.</summary>
    public sealed class SideFact
    {
        public SideFact(string name, string value, bool valueIsData = false)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A fact has its name.", nameof(name));
            Name = name;
            Value = value ?? throw new ArgumentNullException(nameof(value));
            ValueIsData = valueIsData;
        }

        public string Name { get; }

        public string Value { get; }

        public bool ValueIsData { get; }
    }

    /// <summary>
    /// What a line opened, slid out to the right of its page on the same plane (ADR 0026): a subject,
    /// then facts or lines, its source line last, in parts where it pages. It holds nothing to press
    /// but its own Close, so its lines only say something.
    /// </summary>
    public sealed class SidePanel
    {
        /// <summary>What the side panel's Close raises.</summary>
        public const string Close = "close-side-panel";

        /// <param name="source">Where its words come from, one line, last, in the secondary colour.</param>
        /// <param name="parts">The part showing, from 0, and how many there are, where it pages; null where it doesn't.</param>
        public SidePanel(string subject, bool subjectIsData = false, IReadOnlyList<SideFact>? facts = null, IReadOnlyList<PageLine>? lines = null,
            string? source = null, (int Part, int Parts)? parts = null)
        {
            if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("A side panel has its subject.", nameof(subject));
            facts ??= Array.Empty<SideFact>();
            lines ??= Array.Empty<PageLine>();
            if (facts.Count > 0 && lines.Count > 0) throw new ArgumentException("A side panel shows facts or lines, not both.", nameof(lines));
            if (lines.Any(line => line.Action != null)) throw new ArgumentException("A side panel holds nothing to press but its own Close.", nameof(lines));
            if (parts is (int part, int count) && (count < 1 || part < 0 || part >= count)) throw new ArgumentOutOfRangeException(nameof(parts), parts, "A part is one of its parts.");
            Subject = subject;
            SubjectIsData = subjectIsData;
            Facts = facts;
            Lines = lines;
            Source = source;
            Parts = parts;
        }

        public string Subject { get; }

        public bool SubjectIsData { get; }

        public IReadOnlyList<SideFact> Facts { get; }

        public IReadOnlyList<PageLine> Lines { get; }

        public string? Source { get; }

        public (int Part, int Parts)? Parts { get; }
    }

    /// <summary>Where a prompt stands in a frame's footer, left to right (ADR 0026).</summary>
    public enum PromptSlot
    {
        /// <summary>Close, always far left.</summary>
        Close,

        /// <summary>One rare action beside Close, as Stop.</summary>
        Rare,

        /// <summary>The middle, which only a confirmation's Yes takes, so Yes stands where nothing stood on that page or since.</summary>
        Free,

        /// <summary>At most one secondary action beside the far right; Hold to talk wherever the person can speak.</summary>
        Secondary,

        /// <summary>The one main action, or, where there is none, Next page.</summary>
        FarRight,
    }

    /// <summary>What a prompt is, which says where it may stand.</summary>
    public enum PromptKind
    {
        Action,
        Close,

        /// <summary>A long list's one pager prompt: Next page, and on the last page First page, which goes back to the first.</summary>
        NextPage,

        /// <summary>A confirmation's Yes.</summary>
        Yes,

        /// <summary>A confirmation's Cancel, in the place of the press it would undo.</summary>
        Cancel,
    }

    /// <summary>
    /// An action in a frame's footer (ADR 0026): a round key cap holding its icon, then its words. A
    /// prompt always has an icon. One that can't be taken now keeps its place, drawn quiet, with why on
    /// the page's last content line. Only the main action is drawn as one, its cap filled with the
    /// accent and its words heavier in the accent; it stands only at the far right, and a confirmation's
    /// Yes, whose own words carry it, is drawn plain. The microphone only on a held prompt, as Hold to
    /// talk, never on approving, denying, stopping or a confirmation.
    /// </summary>
    public sealed class Prompt
    {
        /// <param name="id">What pressing it raises, for the screen to act on.</param>
        /// <param name="main">The one main action: drawn with the accent, at the far right. Paging and confirmations never are.</param>
        /// <param name="reason">Why it can't be taken now; null when it can.</param>
        /// <param name="holds">Held rather than pressed, as Hold to talk is.</param>
        public Prompt(string id, string words, GlazeIcon icon, PromptKind kind = PromptKind.Action, bool main = false, bool available = true,
            string? reason = null, bool holds = false)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A prompt raises an id.", nameof(id));
            if (string.IsNullOrWhiteSpace(words)) throw new ArgumentException("A prompt has its words.", nameof(words));
            if (icon == GlazeIcon.HoldToTalk && !holds) throw new ArgumentException("Only a held prompt shows the microphone.", nameof(icon));
            if (main && kind != PromptKind.Action) throw new ArgumentException("Only an action is ever the main action: never paging, Close or a confirmation.", nameof(main));
            if (holds && (main || kind != PromptKind.Action)) throw new ArgumentException("A held prompt is a plain action.", nameof(holds));
            if (!available && string.IsNullOrWhiteSpace(reason) && (kind == PromptKind.Action || kind == PromptKind.Yes))
            {
                throw new ArgumentException("An action that can't be taken now says why, on the page's last content line; only paging is quiet without a reason.", nameof(reason));
            }
            Id = id;
            Words = words;
            Icon = icon;
            Kind = kind;
            Main = main;
            Available = available;
            Reason = available ? null : reason;
            Holds = holds;
        }

        public string Id { get; }

        public string Words { get; }

        public GlazeIcon Icon { get; }

        public PromptKind Kind { get; }

        public bool Main { get; }

        public bool Available { get; }

        public string? Reason { get; }

        public bool Holds { get; }

        /// <summary>Drawn as the main action: the accent on its cap and words. An unavailable main action keeps its place but is drawn quiet.</summary>
        public bool DrawnAsMain => Main && Available;
    }

    /// <summary>
    /// A frame's footer of prompts (ADR 0026): Close far left, a rare action beside it, the free middle
    /// only a confirmation's Yes takes, a secondary action, and at the far right the one main action
    /// or, where there is none, a long list's Next page. It refuses what would put anything else
    /// anywhere: a second prompt in a slot, a main action anywhere but the far right, paging as a main
    /// action, and Yes anywhere but the free middle, which held nothing on that page or since. A footer
    /// is a function of its page and the row chosen on it. Its working limit is three prompts, Close,
    /// one other and the main action, as long as the measured footers say so.
    /// </summary>
    public sealed class Footer
    {
        /// <summary>What a frame's Close and its pager prompt raise.</summary>
        public const string Close = "close";
        public const string NextPage = "next-page";

        private readonly Prompt?[] slots = new Prompt?[5];

        public Footer(Prompt? close = null, Prompt? rare = null, Prompt? secondary = null, Prompt? farRight = null)
        {
            // The far right first: whether Next page may be the secondary prompt depends on it.
            Put(PromptSlot.Close, close);
            Put(PromptSlot.Rare, rare);
            Put(PromptSlot.FarRight, farRight);
            Put(PromptSlot.Secondary, secondary);
        }

        private Footer(Footer copy) => Array.Copy(copy.slots, slots, slots.Length);

        public Prompt? this[PromptSlot slot] => slots[(int)slot];

        /// <summary>Every prompt, left to right, with where it stands.</summary>
        public IEnumerable<(PromptSlot Slot, Prompt Prompt)> All
        {
            get
            {
                for (var index = 0; index < slots.Length; index++)
                {
                    if (slots[index] is Prompt prompt) yield return ((PromptSlot)index, prompt);
                }
            }
        }

        /// <summary>A confirmation is armed: Cancel stands in the place of the press it would undo, and Yes, once it shows, in the free middle.</summary>
        public bool Confirming => All.Any(each => each.Prompt.Kind == PromptKind.Cancel);

        /// <summary>Why a prompt can't be taken now, said on the page's last content line: the leftmost one's.</summary>
        public string? Reason => All.Select(each => each.Prompt.Reason).FirstOrDefault(reason => reason != null);

        /// <summary>This footer with <paramref name="prompt"/> in <paramref name="slot"/>, which must be empty and fit it.</summary>
        public Footer With(PromptSlot slot, Prompt prompt)
        {
            var footer = new Footer(this);
            footer.Put(slot, prompt ?? throw new ArgumentNullException(nameof(prompt)));
            return footer;
        }

        /// <summary>
        /// This footer with a long list's one pager prompt: Next page, or on the last page First page
        /// (<see cref="NextPageWords"/>), at the far right where nothing is the main action, else as the
        /// secondary prompt, never drawn as the main action.
        /// </summary>
        public Footer WithNext(Prompt next)
        {
            if (next.Kind != PromptKind.NextPage) throw new ArgumentException("A list pages by Next page.", nameof(next));
            return With(this[PromptSlot.FarRight] == null ? PromptSlot.FarRight : PromptSlot.Secondary, next);
        }

        /// <summary>The pager prompt's words on page <paramref name="page"/>, from 0, of <paramref name="pages"/>: Next page, or First page on the last, which goes back to the first.</summary>
        public static string NextPageWords(int page, int pages)
        {
            if (pages < 2 || page < 0 || page >= pages) throw new ArgumentOutOfRangeException(nameof(page), page, "A list that pages has two pages or more, and the page is one of them.");
            return page == pages - 1 ? "First page" : "Next page";
        }

        /// <summary>
        /// The footer while a confirmation is armed, after a press on <paramref name="pressed"/> of
        /// <paramref name="before"/>: Close as it was, Cancel in the place of the press it would undo,
        /// and Yes in the free middle, which held nothing on that page or since. The other actions step
        /// aside until it is answered. A request in parts pages by a row at the end of the page, and Yes
        /// appears only once the last part has shown: until then <paramref name="yes"/> is null.
        /// </summary>
        public static Footer Confirm(Footer before, PromptSlot pressed, Prompt? yes, Prompt cancel)
        {
            if (before.Confirming) throw new InvalidOperationException("A confirmation is already armed.");
            if (!(before[pressed] is Prompt first) || first.Kind != PromptKind.Action) throw new ArgumentException("Cancel takes the place of the press it would undo.", nameof(pressed));
            if (yes != null && yes.Kind != PromptKind.Yes) throw new ArgumentException("A confirmation's Yes is a Yes.", nameof(yes));
            if (cancel.Kind != PromptKind.Cancel) throw new ArgumentException("A confirmation's Cancel is a Cancel.", nameof(cancel));
            var footer = new Footer(before[PromptSlot.Close]);
            footer.slots[(int)pressed] = cancel;
            footer.slots[(int)PromptSlot.Free] = yes;
            return footer;
        }

        private void Put(PromptSlot slot, Prompt? prompt)
        {
            if (prompt == null) return;
            if (slots[(int)slot] != null) throw new InvalidOperationException(SlotName(slot) + " holds one prompt.");
            switch (prompt.Kind)
            {
                case PromptKind.Close when slot != PromptSlot.Close:
                    throw new InvalidOperationException("Close stands far left.");
                case PromptKind.Action when slot == PromptSlot.Close:
                case PromptKind.NextPage when slot == PromptSlot.Close:
                    throw new InvalidOperationException("Only Close stands far left.");
                case PromptKind.Yes:
                case PromptKind.Cancel:
                    throw new InvalidOperationException("Yes and Cancel come only with a confirmation (Footer.Confirm).");
                case PromptKind.NextPage when slot == PromptSlot.Rare:
                    throw new InvalidOperationException("Next page stands at the far right, or as the secondary prompt.");
                case PromptKind.NextPage when slot == PromptSlot.Secondary && !(slots[(int)PromptSlot.FarRight] is Prompt right && right.Main):
                    throw new InvalidOperationException("Next page is the secondary prompt only where a main action holds the far right.");
            }
            if (slot == PromptSlot.Free) throw new InvalidOperationException("Only a confirmation's Yes takes the free middle.");
            if (prompt.Main && slot != PromptSlot.FarRight) throw new InvalidOperationException("The main action stands only at the far right.");
            if (prompt.Holds && slot != PromptSlot.Secondary) throw new InvalidOperationException("Hold to talk is the secondary prompt.");
            if (slot == PromptSlot.FarRight && slots[(int)PromptSlot.Secondary] is Prompt secondary && secondary.Kind == PromptKind.NextPage && !prompt.Main)
            {
                throw new InvalidOperationException("Next page is the secondary prompt only where a main action holds the far right.");
            }
            slots[(int)slot] = prompt;
        }

        private static string SlotName(PromptSlot slot) => slot switch
        {
            PromptSlot.Close => "Close's place",
            PromptSlot.Rare => "The rare action's place",
            PromptSlot.Free => "The free middle",
            PromptSlot.Secondary => "The secondary action's place",
            _ => "The far right",
        };
    }

    /// <summary>
    /// One column of the menu's plane (ADR 0026), as the menu open on a place or a task's file: its
    /// subject, the title alone, drawn light, and on a file its task's state pill on the subject's top
    /// edge at its left, the same badge the character's label shows, its word at 18 dp; the row of its
    /// sections; the chosen section's page, a few short lines; why a prompt can't be taken now; the
    /// page's one source line, last; a side panel a line opened; and the footer. What to show, never
    /// where: the Unity layer lays it on the plane by the tokens.
    /// </summary>
    public sealed class MenuFrame
    {
        /// <summary>What choosing a section raises, with its key as the key.</summary>
        public const string ChooseSection = "section";

        /// <param name="pill">On a file, its task's state badge, as its character wears it.</param>
        /// <param name="source">Where the page's words come from, one line, last on the page, in the secondary colour.</param>
        public MenuFrame(string subject, Footer footer, bool subjectIsData = false, StateBadge? pill = null, IReadOnlyList<FrameSection>? sections = null,
            IReadOnlyList<PageLine>? lines = null, string? source = null, SidePanel? side = null)
        {
            if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("A frame has its subject.", nameof(subject));
            sections ??= Array.Empty<FrameSection>();
            lines ??= Array.Empty<PageLine>();
            if (sections.Count > 0 && sections.Count(section => section.Chosen) != 1) throw new ArgumentException("A frame's sections have one chosen.", nameof(sections));
            if (sections.Select(section => section.Key).Distinct().Count() != sections.Count) throw new ArgumentException("Each section has its own key.", nameof(sections));
            if (lines.Count(line => line.Chosen && !line.Choice) > 1) throw new ArgumentException("At most one row is chosen; only answers may be chosen together.", nameof(lines));
            if (side != null && !lines.Any(line => line.Chosen && line.Opens)) throw new ArgumentException("A side panel slides out from the chosen line that opens it.", nameof(side));
            if (source != null && string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A source line has its words, or is null.", nameof(source));
            Subject = subject;
            SubjectIsData = subjectIsData;
            Pill = pill;
            Sections = sections;
            Lines = lines;
            Source = source;
            Side = side;
            Footer = footer ?? throw new ArgumentNullException(nameof(footer));
        }

        public string Subject { get; }

        public bool SubjectIsData { get; }

        public StateBadge? Pill { get; }

        public IReadOnlyList<FrameSection> Sections { get; }

        public IReadOnlyList<PageLine> Lines { get; }

        /// <summary>Why a prompt can't be taken now, the page's last content line, above its source line.</summary>
        public string? Reason => Footer.Reason;

        public string? Source { get; }

        public SidePanel? Side { get; }

        public Footer Footer { get; }
    }
}
