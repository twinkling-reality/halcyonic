#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>Where an action stands on a panel's bar, which also sets its look (ADR 0023).</summary>
    public enum PanelActionRole
    {
        /// <summary>Back to the screen before: the bar's left end.</summary>
        Back,

        /// <summary>The one action the screen leads to: the bar's right end.</summary>
        Primary,

        /// <summary>At most two, left of the primary.</summary>
        Secondary,

        /// <summary>Cannot be taken back: at the bar's left after Back, outlined in red, always confirmed in place.</summary>
        Destructive,

        /// <summary>Goes to work that waits for the person, and only that: in a banner, in the attention colour.</summary>
        Attention,
    }

    /// <summary>
    /// An action a panel offers: its words, its icon, its role, and whether it can be taken now. One
    /// that can't stays in its place, quiet and taking no press, with why beside it, so the bar never
    /// jumps. Its words always show; an icon only stands beside them.
    /// </summary>
    public sealed class PanelAction
    {
        /// <param name="id">What pressing it raises, for the screen to act on.</param>
        /// <param name="reason">Why it can't be taken now, said beside it; null when it says nothing more.</param>
        /// <param name="holds">Held rather than pressed, as hold to talk is.</param>
        /// <param name="icon">
        /// The icon beside its words, by what it does; null for words alone. The microphone only on an
        /// action that is held, as hold to talk is: never on approving, denying, stopping or a confirmation.
        /// </param>
        public PanelAction(string id, string label, PanelActionRole role, bool available = true, string? reason = null, bool holds = false,
            GlazeIcon? icon = null)
        {
            if (icon == GlazeIcon.HoldToTalk && !holds) throw new ArgumentException("Only a held action shows the microphone.", nameof(icon));
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Role = role;
            Available = available;
            Reason = available ? null : reason;
            Holds = holds;
            Icon = icon;
        }

        public string Id { get; }

        public string Label { get; }

        public PanelActionRole Role { get; }

        public bool Available { get; }

        public string? Reason { get; }

        public bool Holds { get; }

        public GlazeIcon? Icon { get; }
    }

    /// <summary>
    /// The actions on a panel's bar: Back and one destructive action at the left, at most two
    /// secondary actions and one primary at the right, the primary last, and nothing more (ADR 0023).
    /// A screen that asks for more is refused when it is built, so no bar grows crowded and the
    /// primary stands in the same place on every screen.
    /// </summary>
    public sealed class ActionSet
    {
        public const int MaxSecondary = 2;

        private readonly List<PanelAction> secondary = new List<PanelAction>();

        /// <summary>The actions in any order; nulls are skipped, so a screen can leave out what does not apply.</summary>
        public ActionSet(params PanelAction?[] actions)
        {
            foreach (var action in actions)
            {
                if (action == null) continue;
                switch (action.Role)
                {
                    case PanelActionRole.Back:
                        if (Back != null) throw new InvalidOperationException("A bar has one Back.");
                        Back = action;
                        break;
                    case PanelActionRole.Destructive:
                        if (Destructive != null) throw new InvalidOperationException("A bar has at most one destructive action.");
                        Destructive = action;
                        break;
                    case PanelActionRole.Primary:
                        if (Primary != null) throw new InvalidOperationException("A bar has at most one primary action.");
                        Primary = action;
                        break;
                    case PanelActionRole.Secondary:
                        if (secondary.Count == MaxSecondary) throw new InvalidOperationException("A bar has at most two secondary actions.");
                        secondary.Add(action);
                        break;
                    default:
                        throw new InvalidOperationException("Attention actions belong in a banner, not on the bar.");
                }
            }
        }

        public static ActionSet None { get; } = new ActionSet();

        public PanelAction? Back { get; }

        public PanelAction? Destructive { get; }

        /// <summary>Left to right, as they stand before the primary.</summary>
        public IReadOnlyList<PanelAction> Secondary => secondary;

        public PanelAction? Primary { get; }

        /// <summary>Every action, left to right as the bar shows them.</summary>
        public IEnumerable<PanelAction> All
        {
            get
            {
                if (Back != null) yield return Back;
                if (Destructive != null) yield return Destructive;
                foreach (var action in secondary) yield return action;
                if (Primary != null) yield return Primary;
            }
        }
    }

    /// <summary>
    /// The second step of an action that needs confirming, shown in place of the bar: the question,
    /// Cancel at the bar's right end, and Yes where no control of the screen stood a moment before, in
    /// the bar's free middle or in a row just above it. Pressing twice in one place therefore lands on
    /// Cancel or on nothing, never on Yes.
    /// </summary>
    public sealed class ConfirmStep
    {
        public ConfirmStep(string? question, PanelAction yes, PanelAction cancel)
        {
            Question = question;
            Yes = yes ?? throw new ArgumentNullException(nameof(yes));
            Cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
            if (yes.Role != PanelActionRole.Primary && yes.Role != PanelActionRole.Destructive)
                throw new ArgumentException("Yes is the step's primary or destructive action.", nameof(yes));
        }

        public string? Question { get; }

        public PanelAction Yes { get; }

        public PanelAction Cancel { get; }
    }

    /// <summary>A line at the top of a panel's body, in place of its lead: news, in a tone, with at most two actions.</summary>
    public sealed class PanelBanner
    {
        public const int MaxActions = 2;

        public PanelBanner(string text, GlazeTone tone, params PanelAction[] actions)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Tone = tone;
            if (actions.Length > MaxActions) throw new ArgumentException("A banner has at most two actions.", nameof(actions));
            Actions = actions;
        }

        public string Text { get; }

        public GlazeTone Tone { get; }

        public IReadOnlyList<PanelAction> Actions { get; }
    }

    /// <summary>
    /// One of a panel's tabs, as the workspace's questions: a short name, whether it is the one
    /// showing, and whether it is for what waits for the person, which only it shows in the
    /// attention colour.
    /// </summary>
    public sealed class PanelTab
    {
        public PanelTab(string id, string label, bool chosen, bool attention = false)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Chosen = chosen;
            Attention = attention;
        }

        public string Id { get; }

        public string Label { get; }

        public bool Chosen { get; }

        public bool Attention { get; }
    }

    /// <summary>The size of a line of text in a panel's list: a question asked, what a person reads, or a note between rows.</summary>
    public enum PanelTextSize
    {
        Caption,
        Body,
        Title,
    }

    /// <summary>
    /// One entry of a panel's list: a choice to press, a fact to change, a step and how it went, or a
    /// line of text between them. What it says is given as it is shown; text from outside is already
    /// shown by <see cref="LabelText"/>'s rule.
    /// </summary>
    public sealed class PanelRow
    {
        /// <summary>A small line over the title, saying what it is, such as "Where its files live".</summary>
        public string? Overline { get; set; }

        public string Title { get; set; } = "";

        /// <summary>The title is text from outside, which may end in an ellipsis where it doesn't fit; Halcyonic's own words never do.</summary>
        public bool TitleIsData { get; set; }

        public int TitleLines { get; set; } = 1;

        public string? Detail { get; set; }

        /// <summary>A shorter detail for where <see cref="Detail"/> doesn't fit whole.</summary>
        public string? ShortDetail { get; set; }

        public int DetailLines { get; set; } = 1;

        /// <summary>The detail's tone, such as attention for work that waits for the person.</summary>
        public GlazeTone? DetailTone { get; set; }

        /// <summary>Chosen, or for a filter shown: an accent edge, and its words say so too, never colour alone.</summary>
        public bool Chosen { get; set; }

        /// <summary>Shows or hides something, as a project does on the stage: the rail chip's look.</summary>
        public bool Filter { get; set; }

        /// <summary>A large choice, as the welcome's: its title in the title size.</summary>
        public bool Card { get; set; }

        /// <summary>What pressing it raises, with <see cref="Key"/>; null for a row that only says something.</summary>
        public string? Action { get; set; }

        /// <summary>Which item it is, such as a project's id, given back with its action.</summary>
        public string? Key { get; set; }

        /// <summary>Shown but taking no press, as a folder the Mac can't find now.</summary>
        public bool Available { get; set; } = true;

        /// <summary>A word at its right end saying what pressing it does, such as "Change".</summary>
        public string? End { get; set; }

        /// <summary>A second action beside it, such as Add a task, raised with the same key.</summary>
        public PanelAction? Side { get; set; }

        /// <summary>
        /// A line of text across the list, such as why it is empty or the one before models that run
        /// elsewhere: no plate, no press, wrapping to at most <see cref="TitleLines"/> lines.
        /// </summary>
        public bool Line { get; set; }

        /// <summary>A line's size.</summary>
        public PanelTextSize Size { get; set; } = PanelTextSize.Body;

        /// <summary>A line's tone; the primary text colour when none.</summary>
        public GlazeTone? Tone { get; set; }

        /// <summary>A line of the agent's own words: quoted and leaning, so it never reads as Halcyonic's or as fact.</summary>
        public bool Claim { get; set; }

        /// <summary>A line that goes on from the one before it, as the next entry of a log: no gap between them.</summary>
        public bool Continues { get; set; }

        /// <summary>
        /// A line's meter, a share from 0 to 1 drawn as a bar at the line's right end, the words kept
        /// left of it: a picture of what the line or the one under it says, never in its place. The
        /// share is an upper bound, as usage left's "at most" is, so the bar's end is drawn open.
        /// </summary>
        public float? Meter { get; set; }

        /// <summary>What the meter measures is being read again: it shows only its track, as a skeleton.</summary>
        public bool MeterWaiting { get; set; }

        /// <summary>
        /// An entry of a log the list may leave out where it has no room, the first of them first, so
        /// the newest stay in view and a log never pages. A line that starts a group, as a log's
        /// caption, goes only after every line that continues it.
        /// </summary>
        public bool Droppable { get; set; }

        public bool Pressable => Action != null && !Line;
    }

    /// <summary>
    /// What one screen of a panel shows (ADR 0023): its title, a lead line or a banner, a list or a
    /// paragraph, a pager, and the actions on its bar or the confirm step in their place. Never where:
    /// the Unity layer draws any model by the tokens, so every screen puts the same things in the same
    /// places.
    /// </summary>
    public sealed class PanelModel
    {
        /// <summary>What the header's window controls raise, the pager of a body the screen pages itself, and a tab, with its id as the key.</summary>
        public const string Move = "move";
        public const string ResetPosition = "reset-position";
        public const string Close = "close";
        public const string PreviousPart = "previous-part";
        public const string NextPart = "next-part";
        public const string Tab = "tab";

        /// <summary>What a prompt under the heading raises, with its id as the key.</summary>
        public const string Prompt = "prompt";

        public PanelModel(string title) => Title = title ?? throw new ArgumentNullException(nameof(title));

        public string Title { get; }

        /// <summary>The title holds text from outside, such as a project's name, which may end in an ellipsis where it doesn't fit.</summary>
        public bool TitleIsData { get; set; }

        /// <summary>A short line under the title, such as "Question 2 of 4".</summary>
        public string? Context { get; set; }

        /// <summary>The context line holds text from outside, such as the work's goal, which may end in an ellipsis where it doesn't fit.</summary>
        public bool ContextIsData { get; set; }

        /// <summary>The state badge at the header's right, as the work's character wears it.</summary>
        public StateBadge? Badge { get; set; }

        /// <summary>Practice, Demo or Recorded beside the badge.</summary>
        public IReadOnlyList<WorkMark> Marks { get; set; } = Array.Empty<WorkMark>();

        /// <summary>What something the person just did came to, or what hold to talk is doing: said in the title's and its context's place, for a few seconds.</summary>
        public string? Notice { get; set; }

        /// <summary>
        /// The panel offers Move and Reset position beside Close. A panel that stays beside its
        /// character, as the workspace, offers only Close, at the end of its tabs; one that stays put
        /// and has no tabs, as Usage left, only Close, in its header.
        /// </summary>
        public bool Movable { get; set; } = true;

        /// <summary>Move and Reset position take a press, and Move a hold: never while a confirmation is armed.</summary>
        public bool CanMove => Movable && Confirm == null;

        /// <summary>The header's close button, "Close" unless the screen says otherwise.</summary>
        public string CloseLabel { get; set; } = EntryText.Close;

        /// <summary>The icon beside the close button's words, as they say what it does.</summary>
        public GlazeIcon CloseIcon { get; set; } = GlazeIcon.Close;

        /// <summary>The icons beside Move's and Reset position's words.</summary>
        public const GlazeIcon MoveIcon = GlazeIcon.Move;

        public const GlazeIcon ResetPositionIcon = GlazeIcon.ResetPosition;

        /// <summary>The panel's tabs, under its title, when it answers more than one question.</summary>
        public IList<PanelTab> Tabs { get; } = new List<PanelTab>();

        /// <summary>
        /// The whole question the screen answers, over its body in the caption size, as each of the
        /// workspace's tabs has under its short name.
        /// </summary>
        public string? Heading { get; set; }

        /// <summary>An action at the heading's right, the body's top right, as Refresh on a section that reads its source.</summary>
        public PanelAction? HeadingAction { get; set; }

        /// <summary>
        /// The questions the heading's answer is asked as, one at a time, as Help me understand's What
        /// changed?: compact pills in the heading's row, left of its action, the one answered chosen.
        /// Each raises <see cref="Prompt"/> with its id.
        /// </summary>
        public IList<PanelTab> Prompts { get; } = new List<PanelTab>();

        /// <summary>The line at the top of the body; a banner, when there is one, shows in its place.</summary>
        public string? Lead { get; set; }

        public GlazeTone? LeadTone { get; set; }

        public PanelBanner? Banner { get; set; }

        public IList<PanelRow> Rows { get; } = new List<PanelRow>();

        /// <summary>The list in one column, or two, as choices that are short enough.</summary>
        public int Columns { get; set; } = 1;

        /// <summary>The body is drawn by the screen itself, as the whole request is, in the space the frame leaves for it.</summary>
        public bool CustomBody { get; set; }

        /// <summary>The screen's own body only says something, as a section does: nothing in it to press, so it needs less room from the bar.</summary>
        public bool CustomBodyIsText { get; set; }

        /// <summary>
        /// A pager for a body the screen pages itself, as the whole request's parts: the page from 0
        /// and the count. It stands at the body's bottom, and at its top while the confirm step shows,
        /// so stepping through the parts never presses where Yes comes.
        /// </summary>
        public (int Page, int Pages)? Parts { get; set; }

        /// <summary>What the pager says beside its buttons, "Part n of m" unless given.</summary>
        public string? PartsCaption { get; set; }

        /// <summary>
        /// A line over the pager's note, as which prompt of the agent's question shows: text from
        /// outside, cut short with an ellipsis where it doesn't fit.
        /// </summary>
        public string? PartsHeading { get; set; }

        /// <summary>Halcyonic's words at the left of the pager's row, under its heading, such as how to answer the question shown: never cut.</summary>
        public string? PartsNote { get; set; }

        /// <summary>A line at the bar's left, as what to do while the keyboard is open.</summary>
        public string? BarNote { get; set; }

        public ActionSet Actions { get; set; } = ActionSet.None;

        /// <summary>The confirm step, shown in place of the bar.</summary>
        public ConfirmStep? Confirm { get; set; }
    }
}
