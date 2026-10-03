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

        /// <summary>
        /// The bar's places as the row of sections every place's frame shows under its subject, the
        /// chosen one lit and each with its amber dot while something there waits; choosing one raises
        /// <see cref="MenuFrame.ChooseSection"/> with the place's name as its key.
        /// </summary>
        public IReadOnlyList<FrameSection> Sections() =>
            Places.Select(place => new FrameSection(place.ToString(), Word(place), chosen: place == Chosen, waits: Waits(place))).ToList();

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
        /// Null for words shown from their start, ending in an ellipsis where they run past
        /// <paramref name="rows"/>, as an answer cut to fit. Given, the words are shown in parts, as a
        /// long request is: exactly <paramref name="rows"/> of the rows they wrap to whole, from this
        /// one, from 0, with no ellipsis, so the next part starts where this one ends and no word is
        /// lost between them.
        /// </param>
        /// <param name="besideNext">
        /// It shares its row with the next line, each in half of it, where both fit one row there, as
        /// "Type my answer" beside a question's paging row; else each takes a row. Two answers next to
        /// each other share a row without it.
        /// </param>
        public PageLine(string words, bool wordsAreData = false, GlazeIcon? icon = null, string? fact = null, LineTone tone = LineTone.Primary,
            string? chip = null, bool claim = false, string? action = null, string? key = null, bool opens = false, bool choice = false,
            bool chosen = false, bool available = true, int rows = 1, int? fromRow = null, bool factIsData = false, bool besideNext = false)
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
            FactIsData = factIsData;
            BesideNext = besideNext;
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

        /// <summary>The fact holds text from outside, as a place's name, shown by <see cref="LabelText"/>'s rule before it is given.</summary>
        public bool FactIsData { get; }

        /// <summary>It shares its row with the next line, each in half, where both fit one row there.</summary>
        public bool BesideNext { get; }

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

        public int? FromRow { get; }

        public bool Pressable => Action != null && Available;
    }

    /// <summary>
    /// A fact in a side panel: its name in the secondary colour over its value, both in the Body size,
    /// since type only steps down and a smaller name would stand above larger type (ADR 0026).
    /// </summary>
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

        /// <summary>A side panel's footer: its own Close, which closes the details and leaves the page.</summary>
        public static Footer Footer { get; } = new Footer(new Prompt(Close, "Close details", GlazeIcon.Close, PromptKind.Close));

        /// <param name="source">Where its words come from, one line, last, in the secondary colour.</param>
        /// <param name="parts">The part showing, from 0, and how many there are, where it pages; null where it doesn't.</param>
        /// <param name="sourceIsData">The source line is text from outside, as an answer's provenance or an error a service returned.</param>
        public SidePanel(string subject, bool subjectIsData = false, IReadOnlyList<SideFact>? facts = null, IReadOnlyList<PageLine>? lines = null,
            string? source = null, (int Part, int Parts)? parts = null, bool sourceIsData = false)
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
            SourceIsData = sourceIsData;
            Parts = parts;
        }

        public string Subject { get; }

        public bool SubjectIsData { get; }

        public IReadOnlyList<SideFact> Facts { get; }

        public IReadOnlyList<PageLine> Lines { get; }

        public string? Source { get; }

        /// <summary>The source line holds text from outside, shown by <see cref="LabelText"/>'s rule before it is given.</summary>
        public bool SourceIsData { get; }

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
        /// <param name="pageExplains">
        /// The page itself says why it can't be taken now, as answers waiting to be chosen, so its
        /// reason keeps its words (<see cref="Reason"/>) but isn't drawn on the page's last line.
        /// </param>
        public Prompt(string id, string words, GlazeIcon icon, PromptKind kind = PromptKind.Action, bool main = false, bool available = true,
            string? reason = null, bool holds = false, bool pageExplains = false)
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
            PageExplains = pageExplains;
        }

        public string Id { get; }

        public string Words { get; }

        public GlazeIcon Icon { get; }

        public PromptKind Kind { get; }

        public bool Main { get; }

        public bool Available { get; }

        public string? Reason { get; }

        public bool Holds { get; }

        /// <summary>The page itself says why it can't be taken now: its reason isn't drawn.</summary>
        public bool PageExplains { get; }

        /// <summary>Drawn as the main action: the accent on its cap and words. An unavailable main action keeps its place but is drawn quiet.</summary>
        public bool DrawnAsMain => Main && Available;
    }

    /// <summary>
    /// A frame's footer of prompts (ADR 0026): Close far left, a rare action beside it, the free middle
    /// only a confirmation's Yes takes, a secondary action, and at the far right the one main action
    /// or, where there is none, a long list's Next page. It refuses what would put anything else
    /// anywhere: a second prompt in a slot, a main action anywhere but the far right, paging as a main
    /// action, and Yes anywhere but the free middle, which held nothing on that page or since. A footer
    /// is a function of its page and the row chosen on it. What limits it is its prompts' width, not
    /// their count: four short ones fit in a file, and three long ones may not in the menu; the view
    /// fails a render whose footer does not fit its column.
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

        /// <summary>
        /// Why a prompt can't be taken now, said on the page's last content line: the leftmost one's,
        /// past any whose page says it already (<see cref="Prompt.PageExplains"/>).
        /// </summary>
        public string? Reason => All.Where(each => !each.Prompt.PageExplains).Select(each => each.Prompt.Reason).FirstOrDefault(reason => reason != null);

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
    /// page's one source line, last; a side panel a line opened, or a chosen answer whose words were
    /// cut to fit, holding all of them, so the person reads what Send answer sends; and the footer.
    /// What to show, never where: the Unity layer lays it on the plane by the tokens.
    /// </summary>
    public sealed class MenuFrame
    {
        /// <summary>What choosing a section raises, with its key as the key.</summary>
        public const string ChooseSection = "section";

        /// <summary>
        /// How many rows a page holds, as Tasks, Usage and a file's do (ADR 0026): 4 at the standard
        /// size and 3 with text a step larger, where 4 would take the menu and a file past a Quest 3S's
        /// field. A line that wraps counts each of its rows, and a page's source line counts as one:
        /// with a side panel open, 4 rows and a source line reach past the field.
        /// </summary>
        public static int RowsAPage(TextSize text, bool sourceLine) => (text == TextSize.Larger ? 3 : 4) - (sourceLine ? 1 : 0);

        /// <param name="pill">On a file, its task's state badge, as its character wears it.</param>
        /// <param name="source">Where the page's words come from, one line, last on the page, in the secondary colour.</param>
        /// <param name="sourceIsData">The source line is text from outside, as an answer's provenance or an error a service returned.</param>
        /// <param name="subjectWaits">The subject says what waits for the person, as Tasks' "1 task is waiting for you": it takes the waiting colour, as the closed bar's line does.</param>
        public MenuFrame(string subject, Footer footer, bool subjectIsData = false, StateBadge? pill = null, IReadOnlyList<FrameSection>? sections = null,
            IReadOnlyList<PageLine>? lines = null, string? source = null, SidePanel? side = null, bool sourceIsData = false, bool subjectWaits = false)
        {
            if (string.IsNullOrWhiteSpace(subject)) throw new ArgumentException("A frame has its subject.", nameof(subject));
            sections ??= Array.Empty<FrameSection>();
            lines ??= Array.Empty<PageLine>();
            if (sections.Count > 0 && sections.Count(section => section.Chosen) != 1) throw new ArgumentException("A frame's sections have one chosen.", nameof(sections));
            if (sections.Select(section => section.Key).Distinct().Count() != sections.Count) throw new ArgumentException("Each section has its own key.", nameof(sections));
            if (lines.Count(line => line.Chosen && !line.Choice) > 1) throw new ArgumentException("At most one row is chosen; only answers may be chosen together.", nameof(lines));
            if (side != null && !lines.Any(line => line.Chosen && (line.Opens || line.Choice)))
            {
                throw new ArgumentException("A side panel slides out from the chosen line that opens it, or from a chosen answer cut to fit.", nameof(side));
            }
            if (source != null && string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A source line has its words, or is null.", nameof(source));
            Subject = subject;
            SubjectIsData = subjectIsData;
            SubjectWaits = subjectWaits;
            Pill = pill;
            Sections = sections;
            Lines = lines;
            Source = source;
            SourceIsData = sourceIsData;
            Side = side;
            Footer = footer ?? throw new ArgumentNullException(nameof(footer));
        }

        /// <summary>This frame with <paramref name="sections"/> in place of its own, as the menu's places on a place's page.</summary>
        public MenuFrame WithSections(IReadOnlyList<FrameSection> sections) =>
            new MenuFrame(Subject, Footer, SubjectIsData, Pill, sections, Lines, Source, Side, SourceIsData, SubjectWaits);

        public string Subject { get; }

        public bool SubjectIsData { get; }

        /// <summary>The subject says what waits for the person, in the waiting colour.</summary>
        public bool SubjectWaits { get; }

        public StateBadge? Pill { get; }

        public IReadOnlyList<FrameSection> Sections { get; }

        public IReadOnlyList<PageLine> Lines { get; }

        /// <summary>Why a prompt can't be taken now, the page's last content line, above its source line.</summary>
        public string? Reason => Footer.Reason;

        public string? Source { get; }

        /// <summary>The source line holds text from outside, shown by <see cref="LabelText"/>'s rule before it is given.</summary>
        public bool SourceIsData { get; }

        public SidePanel? Side { get; }

        public Footer Footer { get; }
    }

    /// <summary>
    /// A page's measures (ADR 0026), engine-free, in units of the plane's distance at the designed size,
    /// so a screen can pack a page before the view lays it: how wide its words run, how tall each kind
    /// of line stands and the gaps between them, and how much height a page holds inside a Quest 3S's
    /// field. The view lays pages by the same numbers, and the component render holds the two to agree.
    /// </summary>
    public static class MenuPage
    {
        /// <summary>
        /// Where the plane's top line stands below eye level, under the lineup's labels, where the stage's
        /// own isn't known: where a panel as tall as designed, 26 degrees, has its top with its centre as
        /// low as a Quest 3S allows with the head level (<see cref="ViewField.LowestCenter"/>). The line a
        /// page's height is reckoned down from; the menu's plane reads the stage's own (its TopLine).
        /// </summary>
        public const float TopDegrees = 17.5f;

        /// <summary>A Quest 3S's field, Meta's 96 by 90 degrees split evenly, as the renders' field checks take it.</summary>
        public static readonly ViewField Quest3S = new ViewField(48, 48, 45, 45);

        private static float U(float degrees) => Glaze.MetersAt(degrees, 1f);

        /// <summary>Between two targets, 12 mm on the plane.</summary>
        public static float TargetGap => Glaze.TargetGapMeters / Glaze.Menu.PlaneMeters;

        /// <summary>Between lines of one group, a grid step.</summary>
        public static float Grid => U(Glaze.Menu.GridDegrees);

        /// <summary>Between groups, as before a reason or a source line.</summary>
        public static float GroupGap => U(Glaze.Menu.GroupGapDegrees);

        /// <summary>A column's content width: its width less its padding on each side.</summary>
        public static float ContentWidth(float columnDegrees) => PlaneComposition.Units(columnDegrees) - 2f * U(Glaze.Menu.PaddingDegrees);

        /// <summary>
        /// The width each of two answers sharing a row gives its words: half the row of shapes, 12 mm
        /// between them, less the inset each shape reaches past its words.
        /// </summary>
        public static float HalfWidth(float columnDegrees)
        {
            var inset = U(Glaze.Menu.InsetDegrees);
            return (ContentWidth(columnDegrees) + 2f * inset - TargetGap) / 2f - 2f * inset;
        }

        /// <summary>A line of words <paramref name="rows"/> rows tall, at the content's size.</summary>
        public static float Words(int rows) => rows * U(Glaze.Menu.BodyDegrees) * Glaze.Menu.LineSpacing;

        /// <summary>A row or an answer: 48 dp, or taller for words in more rows than that holds, a grid step above and below them.</summary>
        public static float Target(int rows = 1) => MathF.Max(U(Glaze.MinimumTargetDegrees), Words(rows) + 2f * Grid);

        /// <summary>A page's source line and the gap before it.</summary>
        public static float SourceLine => GroupGap + Words(1);

        /// <summary>
        /// A column's subject: its plate, at least <see cref="Glaze.Menu.SubjectDegrees"/>, round its title
        /// in <paramref name="rows"/> rows; with <paramref name="pill"/>, half a pill above the plate and its
        /// lower half inside it, the title half a grid step under it.
        /// </summary>
        public static float Subject(int rows, bool pill)
        {
            var room = pill ? U(Glaze.Menu.PillHeightDegrees) / 2f : 0f;
            var inner = pill ? room - U(Glaze.Menu.SubjectPaddingDegrees) + Grid / 2f : 0f;
            var title = rows * U(Glaze.Menu.TitleDegrees) * Glaze.Menu.LineSpacing;
            return room + MathF.Max(U(Glaze.Menu.SubjectDegrees), inner + title + 2f * U(Glaze.Menu.SubjectPaddingDegrees));
        }

        /// <summary>The row of sections, a 48 dp target's height.</summary>
        public static float Sections => U(Glaze.MinimumTargetDegrees);

        /// <summary>A content surface <paramref name="page"/> tall in its lines: its top padding, the 12 mm before the footer, the footer and the room under it.</summary>
        public static float Content(float page) => U(Glaze.Menu.PaddingDegrees) + page + TargetGap + U(Glaze.TargetDegrees) + U(1f);

        /// <summary>
        /// Whether <paramref name="composition"/> stands inside a Quest 3S's field less its margin, its top
        /// <see cref="TopDegrees"/> below eye level and the head turned to its centre and tipped down by
        /// <see cref="WorkspacePlacement.ReadingPitch"/>, as the renders' field check sees it.
        /// </summary>
        public static bool Fits(PlaneComposition composition) => Fits(composition, TopDegrees, Quest3S);

        /// <summary>The same with the plane's top <paramref name="topDegrees"/> below eye level, where the stage's labels put it, in <paramref name="field"/>.</summary>
        public static bool Fits(PlaneComposition composition, float topDegrees, ViewField field)
        {
            var half = MathF.Atan(composition.Height / 2f) * 180f / MathF.PI;
            return Inside(composition, new PanelDirection(0f, -topDegrees - half, true, false), field);
        }

        /// <summary>
        /// Whether <paramref name="composition"/>, its centre at <paramref name="direction"/>, stands inside
        /// <paramref name="field"/> less its margin with the head turned to its centre and tipped down by
        /// <see cref="WorkspacePlacement.ReadingPitch"/>, as the renders' field check sees it: where the
        /// stage has placed it, against the headset's measured field.
        /// </summary>
        public static bool Inside(PlaneComposition composition, PanelDirection direction, ViewField field)
        {
            const float DegreesPerRadian = 180f / MathF.PI;
            var pitch = WorkspacePlacement.ReadingPitch(composition.Size, direction.Elevation, field) / DegreesPerRadian;
            var yaw = direction.Yaw / DegreesPerRadian;
            var tolerance = ViewField.EdgeMarginDegrees - 0.01;
            var shrunk = new ViewField(field.Left - tolerance, field.Right - tolerance, field.Up - tolerance, field.Down - tolerance);
            foreach (var right in new[] { -composition.Width / 2f, composition.Width / 2f })
            {
                foreach (var up in new[] { -composition.Height / 2f, composition.Height / 2f })
                {
                    // Seen with the head turned to the centre and tipped down: the point turned back by as much.
                    var (x, y, z) = PlaneComposition.PointOf(direction, right, up);
                    var turnedX = x * MathF.Cos(yaw) - z * MathF.Sin(yaw);
                    var turnedZ = x * MathF.Sin(yaw) + z * MathF.Cos(yaw);
                    var seenY = y * MathF.Cos(pitch) + turnedZ * MathF.Sin(pitch);
                    var seenZ = -y * MathF.Sin(pitch) + turnedZ * MathF.Cos(pitch);
                    var across = MathF.Atan2(turnedX, seenZ) * DegreesPerRadian;
                    var elevation = MathF.Atan2(seenY, MathF.Sqrt(turnedX * turnedX + seenZ * seenZ)) * DegreesPerRadian;
                    if (!shrunk.Shows(across, elevation)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The most a file's page may hold in its lines, standing alone, its subject in
        /// <paramref name="subjectRows"/> rows under its pill, at <paramref name="text"/>'s size: as tall as
        /// keeps it inside the field (<see cref="Fits"/>), its top <paramref name="topDegrees"/> below eye
        /// level, where the stage's labels put it (the menu's plane reads it from where it would stand),
        /// in <paramref name="field"/>, a Quest 3S's unless given. A page packs its lines against it, a
        /// source line included (<see cref="SourceLine"/>). Beside the menu the plane is wider and the
        /// menu's own page counts too, so the plane checks the two together and the menu steps aside
        /// where they don't fit (<see cref="MenuColumns"/>).
        /// </summary>
        /// <param name="besideMenu">The page stands beside the menu, as Tasks' beside a file: the plane is the two columns, each this page.</param>
        public static float Height(TextSize text, int subjectRows, float topDegrees = TopDegrees, ViewField? field = null, bool besideMenu = false)
        {
            if (subjectRows < 1) throw new ArgumentOutOfRangeException(nameof(subjectRows), subjectRows, "A subject takes a row or two.");
            var zoom = text == TextSize.Larger ? Comfort.LargerTextScale : 1f;
            var within = field ?? Quest3S;
            PlaneColumn Column(float degrees, float page) => new PlaneColumn(PlaneComposition.Units(degrees), Subject(subjectRows, pill: true), Sections, Content(page));
            bool Holds(float page) => Fits(new PlaneComposition(besideMenu
                ? new[] { Column(Glaze.Menu.MenuColumnDegrees, page), Column(Glaze.Menu.FileColumnDegrees, page) }
                : new[] { Column(Glaze.Menu.FileColumnDegrees, page) }, zoom), topDegrees, within);
            float low = 0f, high = 2f;
            if (!Holds(low)) return 0f;
            for (var step = 0; step < 40; step++)
            {
                var middle = (low + high) / 2f;
                if (Holds(middle)) low = middle;
                else high = middle;
            }
            return low;
        }

        /// <summary>The height <paramref name="rows"/> rows of a list take, 12 mm apart, as a page of Tasks lays them.</summary>
        public static float Rows(int rows) => rows <= 0 ? 0f : rows * Target() + (rows - 1) * TargetGap;
    }

    /// <summary>A kind of column on the menu's plane (ADR 0026).</summary>
    public enum MenuColumn
    {
        /// <summary>The menu open on a place, <see cref="Glaze.Menu.MenuColumnDegrees"/> wide.</summary>
        Menu,

        /// <summary>A task's file, or New project's steps, <see cref="Glaze.Menu.FileColumnDegrees"/> wide.</summary>
        File,

        /// <summary>What a line opened, beside the frame it came from, <see cref="Glaze.Menu.SideColumnDegrees"/> wide.</summary>
        Side,
    }

    /// <summary>
    /// Which columns stand on the menu's plane (ADR 0026), left to right: never three, since the menu, a
    /// file and a side panel would pass a Quest 3S's field. A side panel belongs to the frame in front,
    /// the file where one is open. With a file beside it, the menu steps aside, leaving the plane with a
    /// short eased slide to the left, while the file's side panel is open or while the two together
    /// would not fit the field, as beside a window, where the two never fit; it comes back once neither
    /// holds, and when the file closes. What waits still shows on the stage and on the file's pill while
    /// it is aside. With text a step larger, a frame and its side panel together are too wide for a Quest
    /// 3S's field, so the side panel takes its frame's place, and Close details brings the frame back.
    /// </summary>
    public static class MenuColumns
    {
        /// <param name="menuOpen">The menu is open, not closed to its bar.</param>
        /// <param name="sidePanel">A side panel is open, the file's where a file is open, else the menu's.</param>
        /// <param name="fitsBeside">The menu and the file together fit the field (<see cref="MenuPage.Fits"/>), which beside a window they never do.</param>
        /// <param name="sideInPlace">The side panel takes its frame's place, as with text a step larger.</param>
        public static IReadOnlyList<MenuColumn> Arrange(bool menuOpen, bool fileOpen, bool sidePanel, bool fitsBeside, bool sideInPlace = false)
        {
            var columns = new List<MenuColumn>();
            if (!(menuOpen || fileOpen)) return columns;
            if (sidePanel && sideInPlace)
            {
                columns.Add(MenuColumn.Side);
                return columns;
            }
            if (menuOpen && !(fileOpen && (sidePanel || !fitsBeside))) columns.Add(MenuColumn.Menu);
            if (fileOpen) columns.Add(MenuColumn.File);
            if (sidePanel) columns.Add(MenuColumn.Side);
            return columns;
        }

        /// <summary>The menu is open but stands aside for the file, as <see cref="Arrange"/> puts it.</summary>
        public static bool MenuAside(bool menuOpen, bool fileOpen, bool sidePanel, bool fitsBeside) =>
            menuOpen && fileOpen && (sidePanel || !fitsBeside);
    }
}
