#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A foreground panel (ADR 0023), drawn from a <see cref="PanelModel"/> by the tokens, so every
    /// screen puts the same things in the same places: the header along the top, the title and its
    /// context at its left and Move, Reset position and Close at its right, or Close alone on a panel
    /// that stays put, or, on a panel that stays beside its character, the work's state at its right
    /// and under it a row of tabs that ends in Close; a banner, a heading with the questions it is
    /// asked as and its action, or a lead line; the body, a
    /// list in one column or two, a page at a time, its lines with a meter at their right where they
    /// have one, with the pager at its bottom right; and the action bar along the bottom, Back and the
    /// destructive action at the left, the primary at the right end, or the confirm step in the bar's
    /// place. It is 44 by 26 degrees, built in units of its distance from the eyes, under a root the
    /// panel scales by that distance and places at touch distance.
    /// </summary>
    /// <remarks>
    /// It decides nothing: what a press means is the screen's, raised as <see cref="Acted"/> with the
    /// action's id and, for a row or a tab, its key. Its buttons ignore input while
    /// <see cref="Accepting"/> says so, and a button that takes new words, a new role or becomes
    /// available waits its settle time before a press counts (<see cref="GlazeButton"/>). The confirm
    /// step's Yes stands where no control of the screen stood a moment before, nor any shown since the
    /// step began (<see cref="Recorded"/>), so pressing twice in one place never confirms; while the
    /// step pages through what it confirms, the pager stands at the top, away from Yes. Targets keep
    /// 12 mm apart; words need less room. Every word goes on its label by the one rule for text
    /// Halcyonic did not write.
    /// </remarks>
    public sealed class PanelFrame : MonoBehaviour
    {
        /// <summary>From the eyes to a foreground panel: touch distance (ADR 0023).</summary>
        public const float Distance = 0.46f;

        public const float WidthDegrees = 44f;
        public const float HeightDegrees = 26f;

        /// <summary>
        /// How much larger than designed a foreground panel is drawn: the reading text's step
        /// (<see cref="GlazeText.Scale"/>, the person's comfort setting), the whole panel growing with
        /// it at the same distance, so its layout, pages and parts stay as designed.
        /// </summary>
        public static float Zoom => GlazeText.Scale;

        /// <summary>A foreground panel's scale in the world: built in units of its distance, at its zoom.</summary>
        public static float Scale => Distance * Zoom;

        /// <summary>Every frame's size, in units of its distance.</summary>
        public static Vector2 UnitSize => new Vector2(2f * GlazeTokens.Units(WidthDegrees / 2f), 2f * GlazeTokens.Units(HeightDegrees / 2f));

        private const float PaddingDegrees = 1.25f;
        private const float SectionGapDegrees = 0.75f;

        /// <summary>Between targets, as Meta asks: 12 mm at touch distance.</summary>
        private static float TargetGap => Glaze.TargetGapMeters / Distance;

        private const float LineGapDegrees = 0.6f;

        /// <summary>Between the heading's action and the words that run beside it: words, not a target, so less than 12 mm.</summary>
        private const float NotchGapDegrees = 0.4f;
        private const float BannerPaddingDegrees = 0.25f;
        private const float MarkGapDegrees = 0.5f;
        private const int LeadLines = 2;
        private const int QuestionLines = 2;
        private const int NoteLines = 2;
        private const int NoticeLines = 2;
        private const float CardMinimumDegrees = 7.5f;

        /// <summary>Between a line's words and its meter.</summary>
        private const float MeterGapDegrees = 1f;

        /// <summary>
        /// The bar's right end, the primary's place, is at least this wide, and so is Cancel when the
        /// confirm step takes it: a second press where the action stood lands on Cancel.
        /// </summary>
        private const float RightEndDegrees = 14f;
        private const int TitleOrder = 12;

        private readonly List<GlazeButton> rows = new List<GlazeButton>();
        private readonly List<GlazeButton> sides = new List<GlazeButton>();
        private readonly List<GlazeButton> tabs = new List<GlazeButton>();
        private readonly List<GlazeButton> prompts = new List<GlazeButton>();
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly List<MarkTag> marks = new List<MarkTag>();
        private readonly Dictionary<GlazeButton, (string Id, string? Key)> pressing = new Dictionary<GlazeButton, (string, string?)>();
        private readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        private readonly HashSet<TMP_Text> data = new HashSet<TMP_Text>();
        private readonly HashSet<TMP_Text> leaning = new HashSet<TMP_Text>();
        private readonly List<List<Slot>> pages = new List<List<Slot>>();
        private readonly List<int> kept = new List<int>();
        private readonly List<Rect> laidOut = new List<Rect>();
        private readonly List<Rect> recorded = new List<Rect>();
        private readonly List<(GlazeButton Button, PanelRow Row)> shownRows = new List<(GlazeButton, PanelRow)>();
        private readonly List<(TextMeshPro Label, PanelRow Row)> shownLines = new List<(TextMeshPro, PanelRow)>();
        private readonly List<MeterView> meters = new List<MeterView>();
        private readonly List<(Rect Track, float Filled, PanelRow Row)> shownMeters = new List<(Rect, float, PanelRow)>();
        private Transform content = null!;
        private Surface plate = null!;
        private PointerTarget background = null!;
        private TextMeshPro title = null!;
        private TextMeshPro context = null!;
        private TextMeshPro notice = null!;
        private TextMeshPro heading = null!;
        private TextMeshPro lead = null!;
        private TextMeshPro reason = null!;
        private TextMeshPro barNote = null!;
        private TextMeshPro question = null!;
        private TextMeshPro pageCaption = null!;
        private TextMeshPro partsHeading = null!;
        private TextMeshPro partsNote = null!;
        private TextMeshPro splitter = null!;
        private TextMeshPro lineMeasure = null!;
        private StateBadgeView badge = null!;
        private Surface banner = null!;
        private TextMeshPro bannerText = null!;
        private GlazeButton move = null!;
        private GlazeButton reset = null!;
        private GlazeButton close = null!;
        private GlazeButton headingAction = null!;
        private GlazeButton bannerFirst = null!;
        private GlazeButton bannerSecond = null!;
        private GlazeButton back = null!;
        private GlazeButton destructive = null!;
        private GlazeButton secondaryFirst = null!;
        private GlazeButton secondarySecond = null!;
        private GlazeButton primary = null!;
        private GlazeButton yes = null!;
        private GlazeButton cancel = null!;
        private GlazeButton previous = null!;
        private GlazeButton next = null!;
        private GlazeButton measure = null!;
        private PanelModel? shown;
        private Rect body;
        private Rect customBody;
        private Rect? notch;
        private Rect listArea;
        private Vector2 size;
        private int page;
        private int dropped;

        /// <summary>Something was pressed: the action's id and, for a row, its side action or a tab, its key.</summary>
        public event Action<string, string?>? Acted;

        /// <summary>A hold action, as hold to talk, began to be held: its id.</summary>
        public event Action<string>? HoldStarted;

        /// <summary>A hold ended: let go on it (true), or dropped (false).</summary>
        public event Action<string, bool>? HoldEnded;

        /// <summary>
        /// While Move is held (its hold started, <see cref="HoldStarted"/> with <see cref="PanelModel.Move"/>):
        /// the point the hand holds moved, to here in the world, for the panel's owner to follow.
        /// </summary>
        public event Action<Vector3>? Dragged;

        /// <summary>The point the hand holds on Move, in the world, while a press is on it.</summary>
        public Vector3? MoveHeldPoint => move.Target.HeldPoint;

        /// <summary>Presses are ignored while false, as while the panel is hidden or the app lacks focus.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        /// <summary>The frame's size, in units of its distance.</summary>
        public Vector2 Size => size;

        /// <summary>Where a screen that draws its own body draws it, as the whole request is, in the frame's units, clear of the pager.</summary>
        public Rect CustomBody => customBody;

        /// <summary>
        /// Where the heading's action reaches down into the top right of a body the screen draws
        /// itself, which starts under the heading's words: what the screen draws there keeps left of it.
        /// </summary>
        public Rect? Notch => notch;

        /// <summary>Where the list was laid out in the last show, in the frame's units, clear of the pager.</summary>
        public Rect ListArea => listArea;

        /// <summary>The body's width, a line's across the list, in the frame's units.</summary>
        public float InnerWidth => size.x - 2f * Units(PaddingDegrees);

        /// <summary>The list's page showing, from 0; a new screen starts at the first.</summary>
        public int Page
        {
            get => page;
            set => page = Math.Max(0, value);
        }

        /// <summary>How many pages the list showing takes.</summary>
        public int Pages => Math.Max(1, pages.Count);

        /// <summary>How many of the list's rows were left out for room in the last show, as a log's older lines are.</summary>
        public int Dropped => dropped;

        /// <summary>Where the frame's parts are laid out, for a screen that draws its own body.</summary>
        public Transform Content => content;

        /// <summary>The bar's buttons and the window controls by what they raise, for the editor's checks.</summary>
        public IEnumerable<GlazeButton> Buttons
        {
            get
            {
                foreach (var button in new[] { move, reset, close, headingAction, bannerFirst, bannerSecond, back, destructive, secondaryFirst, secondarySecond, primary, yes, cancel, previous, next })
                {
                    if (button.gameObject.activeSelf) yield return button;
                }
                foreach (var list in new[] { tabs, prompts, rows, sides })
                {
                    foreach (var button in list)
                    {
                        if (button.gameObject.activeSelf) yield return button;
                    }
                }
            }
        }

        /// <summary>Every label showing, for the editor's checks that each shows what it was given, whole.</summary>
        public IEnumerable<TMP_Text> Labels
        {
            get
            {
                foreach (var label in new[] { title, context, notice, heading, lead, reason, barNote, question, pageCaption, partsHeading, partsNote, bannerText })
                {
                    if (label.gameObject.activeSelf) yield return label;
                }
                foreach (var label in lines)
                {
                    if (label.gameObject.activeSelf) yield return label;
                }
                if (badge.gameObject.activeSelf) yield return badge.Word;
                foreach (var mark in marks)
                {
                    if (mark.gameObject.activeSelf) yield return mark.Word;
                }
            }
        }

        /// <summary>The button that raises <paramref name="id"/>, showing now, or null.</summary>
        public GlazeButton? ButtonFor(string id, string? key = null)
        {
            foreach (var pair in pressing)
            {
                if (pair.Key.gameObject.activeSelf && pair.Value.Id == id && (key == null || pair.Value.Key == key)) return pair.Key;
            }
            return null;
        }

        /// <summary>The title's label, whose text can come from outside, as a project's name.</summary>
        public TextMeshPro Title => title;

        /// <summary>The notice in the title's place, while one shows.</summary>
        public TextMeshPro? Notice => notice.gameObject.activeSelf ? notice : null;

        /// <summary>The state badge in the header, while one shows.</summary>
        public StateBadgeView? Badge => badge.gameObject.activeSelf ? badge : null;

        /// <summary>The model showing, for the editor's checks.</summary>
        public PanelModel? Shown => shown;

        /// <summary>The tabs showing, left to right, for the editor's checks.</summary>
        public IReadOnlyList<GlazeButton> Tabs => tabs.FindAll(tab => tab.gameObject.activeSelf);

        /// <summary>The prompts showing in the heading's row, left to right, for the editor's checks.</summary>
        public IReadOnlyList<GlazeButton> Prompts => prompts.FindAll(prompt => prompt.gameObject.activeSelf);

        /// <summary>The list's rows that show, with what each was given, for the editor's checks.</summary>
        public IReadOnlyList<(GlazeButton Button, PanelRow Row)> ShownRows => shownRows;

        /// <summary>The list's lines that show, with what each was given, for the editor's checks.</summary>
        public IReadOnlyList<(TextMeshPro Label, PanelRow Row)> ShownLines => shownLines;

        /// <summary>
        /// The meters that show, for the editor's checks: each track's place in the frame's units, how
        /// much of it is filled, the open end included, and the line it stands on.
        /// </summary>
        public IReadOnlyList<(Rect Track, float Filled, PanelRow Row)> ShownMeters => shownMeters;

        /// <summary>
        /// Where every control stood on the screen before the confirm step and since it began, in the
        /// frame's units: the places Yes must keep clear of.
        /// </summary>
        public IReadOnlyList<Rect> Recorded => recorded;

        /// <summary>Whether the bar shows its actions' icons: false where they would not all fit, and each says its word alone.</summary>
        public bool BarIcons { get; private set; } = true;

        /// <summary>The bar's buttons showing, or the confirm step's, for the editor's checks that the right end never moves.</summary>
        public IEnumerable<GlazeButton> BarButtons
        {
            get
            {
                foreach (var button in new[] { back, destructive, secondaryFirst, secondarySecond, primary, yes, cancel })
                {
                    if (button.gameObject.activeSelf) yield return button;
                }
            }
        }

        /// <summary>The button at the bar's right end, the primary's place, or Cancel's in the confirm step, while one shows.</summary>
        public GlazeButton? RightEnd
        {
            get
            {
                if (cancel.gameObject.activeSelf) return cancel;
                if (primary.gameObject.activeSelf) return primary;
                if (secondarySecond.gameObject.activeSelf) return secondarySecond;
                return secondaryFirst.gameObject.activeSelf ? secondaryFirst : null;
            }
        }

        /// <summary>The confirm step's Yes, while it shows.</summary>
        public GlazeButton? Yes => yes.gameObject.activeSelf ? yes : null;

        /// <summary>Back, at the bar's left end, while it shows.</summary>
        public GlazeButton? Back => back.gameObject.activeSelf ? back : null;

        /// <summary>The pager's Next, while it shows.</summary>
        public GlazeButton? NextPage => next.gameObject.activeSelf ? next : null;

        /// <summary>The pager's Previous, while it shows.</summary>
        public GlazeButton? PreviousPage => previous.gameObject.activeSelf ? previous : null;

        /// <summary>Whether a label shows text from outside, which may end in an ellipsis where it doesn't fit; Halcyonic's own words never do.</summary>
        public bool HoldsData(TMP_Text label) => data.Contains(label);

        /// <summary>Whether a label's letters lean, as the agent's words do.</summary>
        public bool Leans(TMP_Text label) => leaning.Contains(label);

        /// <summary>A control's place in the frame's units, centred on its transform.</summary>
        public static Rect RectOf(GlazeButton button)
        {
            var center = (Vector2)button.transform.localPosition;
            return new Rect(center - button.Size / 2f, button.Size);
        }

        public static PanelFrame Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var frame = go.AddComponent<PanelFrame>();
            frame.Build();
            return frame;
        }

        private void Build()
        {
            size = UnitSize;
            plate = Surface.Create(transform, "Background", 10);
            plate.transform.localPosition = new Vector3(0f, 0f, GlazeTokens.Units(0.05f));
            plate.Draw(size, GlazeTokens.Units(Glaze.PanelRadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel));
            // The background takes the ray, so nothing behind the panel is pointed at through it.
            background = PointerTarget.Rectangle(gameObject, size, ray: true, poke: false);
            content = new GameObject("Content").transform;
            content.SetParent(transform, false);

            title = Text("Title", GlazeType.Title, GlazeTokens.Text);
            context = Text("Context", GlazeType.Caption, GlazeTokens.TextSecondary);
            notice = Text("Notice", GlazeType.Body, GlazeTokens.Text);
            heading = Text("Heading", GlazeType.Caption, GlazeTokens.TextSecondary);
            lead = Text("Lead", GlazeType.Body, GlazeTokens.Text);
            reason = Text("Reason", GlazeType.Caption, GlazeTokens.TextSecondary);
            reason.alignment = TextAlignmentOptions.TopRight;
            barNote = Text("Bar note", GlazeType.Body, GlazeTokens.TextSecondary);
            question = Text("Question", GlazeType.Body, GlazeTokens.Text);
            pageCaption = Text("Page", GlazeType.Caption, GlazeTokens.TextSecondary);
            partsHeading = Text("Pager heading", GlazeType.Caption, GlazeTokens.Text);
            partsNote = Text("Pager note", GlazeType.Caption, GlazeTokens.TextSecondary);
            // Never shown: it lays text out at the list's width to split it into parts of whole lines.
            splitter = Text("Line splitter", GlazeType.Body, GlazeTokens.Text);
            // Never shown: it lays a line of the list out to measure how tall it is, so no line shown is laid out twice.
            lineMeasure = Text("Line measure", GlazeType.Body, GlazeTokens.Text);
            banner = Surface.Create(content, "Banner", 10);
            bannerText = Text("Banner words", GlazeType.Body, GlazeTokens.Text);
            badge = StateBadgeView.Create(content, "Badge", 11);
            badge.gameObject.SetActive(false);

            move = Button("Move", ButtonRole.Secondary, compact: true);
            // A press steps the panel aside; held, it moves with the hand.
            move.Holds = true;
            move.Target.EnableDrag();
            move.Target.Dragged += point =>
            {
                if (move.Holding) Dragged?.Invoke(point);
            };
            reset = Button("Reset position", ButtonRole.Secondary, compact: true);
            close = Button("Close", ButtonRole.Secondary, compact: true);
            headingAction = Button("Heading action", ButtonRole.Secondary, compact: true);
            bannerFirst = Button("Banner action 1", ButtonRole.Attention, compact: true);
            bannerSecond = Button("Banner action 2", ButtonRole.Secondary, compact: true);
            back = Button("Back", ButtonRole.Secondary);
            destructive = Button("Destructive", ButtonRole.Destructive);
            secondaryFirst = Button("Secondary 1", ButtonRole.Secondary);
            secondarySecond = Button("Secondary 2", ButtonRole.Secondary);
            primary = Button("Primary", ButtonRole.Primary);
            yes = Button("Yes", ButtonRole.Primary);
            cancel = Button("Cancel", ButtonRole.Secondary);
            previous = Button("Previous page", ButtonRole.Secondary, compact: true);
            next = Button("Next page", ButtonRole.Secondary, compact: true);
            previous.Pressed += () => Turn(-1);
            next.Pressed += () => Turn(1);
            // Never shown: it lays each row out at its width to measure how tall it is.
            measure = GlazeButton.Create(content, "Row measure", ButtonRole.Choice);
            measure.gameObject.SetActive(false);
        }

        /// <summary>
        /// Draws <paramref name="model"/>: every part it uses shown and laid out by the tokens, every
        /// other part hidden. Called again whenever what the screen shows may have changed; a button
        /// whose words stay the same keeps its settle time.
        /// </summary>
        public void Show(PanelModel model)
        {
            shown = model;
            pressing.Clear();
            data.Clear();
            laidOut.Clear();
            if (model.TitleIsData) data.Add(title);
            if (model.ContextIsData) data.Add(context);
            var inner = size.x - 2f * Units(PaddingDegrees);
            var left = -size.x / 2f + Units(PaddingDegrees);
            var right = size.x / 2f - Units(PaddingDegrees);
            var top = size.y / 2f - Units(PaddingDegrees);
            var bottom = -size.y / 2f + Units(PaddingDegrees);
            var compact = GlazeButton.HeightOf(true);

            // The header, then, on a panel that stays beside its character, its row of tabs and Close;
            // a confirmation that pages shows its pager there, or under the header, away from Yes. A
            // panel that stays put with no tabs keeps Close in its header.
            var pagerOnTop = model.Confirm != null && model.Parts != null;
            var tabbed = !model.Movable && model.Tabs.Count > 0;
            var y = tabbed ? LayStatusHeader(model, left, right, top) : LayWindowHeader(model, left, right, top);
            var above = !tabbed;
            foreach (var tab in tabs) tab.Hide();
            previous.Hide();
            next.Hide();
            pageCaption.gameObject.SetActive(false);
            if (tabbed)
            {
                y -= Units(SectionGapDegrees);
                var middle = y - compact / 2f;
                var end = PutRight(close, PanelModel.Close, model.CloseLabel, right, middle, icon: model.CloseIcon);
                laidOut.Add(RectOf(close));
                if (pagerOnTop) LayTopPager(model, left, middle);
                else LayTabs(model, left, end, middle);
                y -= compact;
                above = true;
            }
            else if (pagerOnTop)
            {
                // A row of its own under the header, kept for one part too, so the body's room is the
                // same whatever the count, as a screen that measures its parts by it needs.
                y -= TargetGap;
                LayTopPager(model, left, y - compact / 2f);
                y -= compact;
            }

            // A banner, or the screen's heading, with its action beside it, and its lead.
            lead.gameObject.SetActive(false);
            heading.gameObject.SetActive(false);
            headingAction.Hide();
            foreach (var prompt in prompts) prompt.Hide();
            notch = null;
            banner.gameObject.SetActive(false);
            bannerText.gameObject.SetActive(false);
            bannerFirst.Hide();
            bannerSecond.Hide();
            if (model.Banner != null)
            {
                y -= above ? TargetGap - Units(BannerPaddingDegrees) : Units(SectionGapDegrees);
                y = LayBanner(model.Banner, left, inner, y) - (TargetGap - Units(BannerPaddingDegrees));
                above = false;
            }
            else
            {
                if (model.Heading != null)
                {
                    // Targets in the heading's row, its action, prompts or pager, keep 12 mm from those above.
                    var acts = model.HeadingAction != null || model.Prompts.Count > 0 || PagesInHeading(model);
                    y -= Gap(above, acts);
                    y = LayHeading(model, left, right, y);
                    // A body the screen draws itself, or one that starts with lines, starts under the
                    // heading's words, beside its action; any other under the action, and any body a
                    // target's gap under prompts.
                    above = (acts && notch == null) || model.Prompts.Count > 0;
                }
                if (model.Lead != null)
                {
                    y -= Gap(above, false);
                    GlazeText.SetLiteral(lead, model.Lead);
                    lead.color = model.LeadTone.HasValue ? GlazeTokens.ColorOf(Glaze.Tone(model.LeadTone.Value).Foreground) : GlazeTokens.Text;
                    var (leadLines, _) = GlazeText.Lay(lead, inner, LeadLines);
                    lead.transform.localPosition = new Vector3(left, y, -0.0005f);
                    lead.gameObject.SetActive(true);
                    y -= leadLines * GlazeText.LineHeight(lead);
                    above = false;
                }
            }
            var bodyTop = y;

            // The bar, from the bottom up, and Yes where nothing stood; the body takes what is between.
            var barHeight = GlazeButton.HeightOf(false);
            var barMiddle = bottom + barHeight / 2f;
            var (floor, floorTargets) = LayBar(model, left, right, bottom, barMiddle);

            // The body: the screen's own, or the list a page at a time, with the pager's row at its
            // bottom while the screen pages it and no confirmation shows.
            var band = !pagerOnTop && !PagesInHeading(model) && (model.Parts != null || model.PartsNote != null || model.PartsHeading != null);
            var bandTargets = band && model.Parts is (_, var parts) && parts > 1;
            partsHeading.gameObject.SetActive(false);
            partsNote.gameObject.SetActive(false);
            var listBottom = floor;
            var lastTargets = floorTargets;
            if (band)
            {
                var bandBottom = floor + Gap(bandTargets, floorTargets);
                LayBand(model, left, right, bandBottom + compact / 2f);
                listBottom = bandBottom + compact;
                lastTargets = bandTargets;
            }
            if (model.CustomBody)
            {
                var bottomGap = band ? Gap(true, lastTargets) : Gap(!model.CustomBodyIsText, lastTargets);
                customBody = Rect.MinMaxRect(left, listBottom + bottomGap, right, bodyTop - Gap(above, false));
                body = customBody;
                listArea = customBody;
                pages.Clear();
                dropped = 0;
                shownMeters.Clear();
                HideRows(0, 0, 0, 0);
            }
            else
            {
                var firstTargets = model.Rows.Count > 0 && !Flows(model.Rows[0], Mathf.Clamp(model.Columns, 1, 2));
                var anyTargets = false;
                foreach (var row in model.Rows) anyTargets |= row.Pressable || row.Side != null;
                var areaTop = bodyTop - Gap(above, firstTargets);
                listArea = Rect.MinMaxRect(left, listBottom + Gap(anyTargets, lastTargets), right, areaTop);
                LayList(model, listArea);
                if (pages.Count > 1 && !anyTargets && !lastTargets)
                {
                    // The list pages after all: its pager is a target, kept 12 mm from what is below.
                    listArea = Rect.MinMaxRect(left, listBottom + TargetGap, right, areaTop);
                    LayList(model, listArea);
                }
                body = listArea;
                customBody = listArea;
            }

            // Yes keeps clear of every control of the screen before the step and of every one shown since.
            if (model.Confirm == null) recorded.Clear();
            recorded.AddRange(laidOut);
        }

        /// <summary>Shows the list's next or previous page.</summary>
        private void Turn(int by)
        {
            if (shown == null || !Accepting()) return;
            page = Mathf.Clamp(page + by, 0, Pages - 1);
            Show(shown);
        }

        private static float Gap(bool above, bool below) => above && below ? TargetGap : Units(SectionGapDegrees);

        /// <summary>
        /// The title and its context at the left, Move, Reset position and Close at the right, or Close
        /// alone on a panel that stays put: returns the header's bottom.
        /// </summary>
        private float LayWindowHeader(PanelModel model, float left, float right, float top)
        {
            notice.gameObject.SetActive(false);
            badge.gameObject.SetActive(false);
            foreach (var mark in marks) mark.gameObject.SetActive(false);
            var headerHeight = GlazeButton.HeightOf(true);
            var middle = top - headerHeight / 2f;
            var x = right;
            x = PutRight(close, PanelModel.Close, model.CloseLabel, x, middle, icon: model.CloseIcon);
            reset.Hide();
            move.Hide();
            if (model.Movable)
            {
                // Neither moves the panel while a confirmation is armed.
                x = PutRight(reset, PanelModel.ResetPosition, EntryText.ResetPosition, x, middle, available: model.CanMove, icon: PanelModel.ResetPositionIcon);
                x = PutRight(move, PanelModel.Move, EntryText.Move, x, middle, available: model.CanMove, icon: PanelModel.MoveIcon);
            }
            foreach (var button in new[] { close, reset, move })
            {
                if (button.gameObject.activeSelf) laidOut.Add(RectOf(button));
            }
            LayTitle(model, left, x - left, middle);
            return top - headerHeight;
        }

        /// <summary>
        /// The header of a panel that stays beside its character: the title and its context at the
        /// left, or a notice in their place for a few seconds, and the work's state badge and marks at
        /// the right, as its character wears them. Two lines tall, so a notice shows whole. Returns its bottom.
        /// </summary>
        private float LayStatusHeader(PanelModel model, float left, float right, float top)
        {
            move.Hide();
            reset.Hide();
            var height = NoticeLines * GlazeText.LineHeight(notice);
            var middle = top - height / 2f;
            var x = right;
            for (var index = model.Marks.Count - 1; index >= 0; index--)
            {
                var mark = Mark(index);
                mark.Show(model.Marks[index]);
                mark.transform.localPosition = new Vector3(x - mark.Width / 2f, middle, -0.0005f);
                mark.gameObject.SetActive(true);
                x -= mark.Width + Units(MarkGapDegrees);
            }
            for (var index = model.Marks.Count; index < marks.Count; index++) marks[index].gameObject.SetActive(false);
            if (model.Badge != null)
            {
                badge.Show(model.Badge);
                badge.transform.localPosition = new Vector3(x - badge.Width / 2f, middle, -0.0005f);
                badge.gameObject.SetActive(true);
                x -= badge.Width + Units(MarkGapDegrees);
            }
            else badge.gameObject.SetActive(false);
            var width = x - Units(MarkGapDegrees) - left;
            if (model.Notice != null)
            {
                title.gameObject.SetActive(false);
                context.gameObject.SetActive(false);
                GlazeText.SetLiteral(notice, model.Notice);
                var (count, _) = GlazeText.Lay(notice, width, NoticeLines);
                notice.transform.localPosition = new Vector3(left, middle + count * GlazeText.LineHeight(notice) / 2f, -0.0005f);
                notice.gameObject.SetActive(true);
            }
            else
            {
                notice.gameObject.SetActive(false);
                LayTitle(model, left, width, middle);
            }
            return top - height;
        }

        /// <summary>The title, and its context under it, centred on <paramref name="middle"/>.</summary>
        private void LayTitle(PanelModel model, float left, float width, float middle)
        {
            GlazeText.SetLiteral(title, model.Title);
            GlazeText.Lay(title, width, 1);
            var titleLine = GlazeText.LineHeight(title);
            if (model.Context != null)
            {
                GlazeText.SetLiteral(context, model.Context);
                GlazeText.Lay(context, width, 1);
                var both = titleLine + GlazeText.LineHeight(context);
                title.transform.localPosition = new Vector3(left, middle + both / 2f, -0.0005f);
                context.transform.localPosition = new Vector3(left, middle + both / 2f - titleLine, -0.0005f);
                context.gameObject.SetActive(true);
            }
            else
            {
                title.transform.localPosition = new Vector3(left, middle + titleLine / 2f, -0.0005f);
                context.gameObject.SetActive(false);
            }
            title.gameObject.SetActive(true);
        }

        /// <summary>The tabs from the left, the one showing edged in the accent, 12 mm apart and clear of Close.</summary>
        private void LayTabs(PanelModel model, float left, float end, float middle)
        {
            var x = left;
            for (var index = 0; index < model.Tabs.Count; index++)
            {
                var tab = model.Tabs[index];
                var button = Tab(index);
                button.Role = tab.Attention ? ButtonRole.Attention : ButtonRole.Filter;
                button.On = tab.Chosen;
                button.Available = true;
                var width = button.Measure(tab.Label);
                button.Show(tab.Label, new Vector2(x + width / 2f, middle), width);
                pressing[button] = (PanelModel.Tab, tab.Id);
                laidOut.Add(RectOf(button));
                x += width + TargetGap;
            }
            if (x - TargetGap > end + 1e-5f) Debug.LogWarning("Halcyonic: the tabs run into Close.");
        }

        /// <summary>The screen's whole question, and its action at the right in a row of a target's height: returns the bottom.</summary>
        /// <summary>
        /// The screen's whole question, with its action at the right in a row of a target's height:
        /// returns the row's bottom, or, over a body the screen draws itself, the bottom of the
        /// heading's words, the action reaching down beside that body's first lines (<see cref="Notch"/>).
        /// </summary>
        private float LayHeading(PanelModel model, float left, float right, float top)
        {
            notch = null;
            var width = right - left;
            var line = GlazeText.LineHeight(heading);
            var height = line;
            var middle = top - line / 2f;
            var end = right;
            var paged = PagesInHeading(model);
            if (model.HeadingAction != null || paged)
            {
                height = GlazeButton.HeightOf(true);
                var row = top - height / 2f;
                // A body the screen draws itself and pages has its pager here, at the row's right,
                // with the heading's action left of it, rather than in a row of its own under the body.
                var x = right;
                Rect? controls = null;
                void Took(GlazeButton button)
                {
                    laidOut.Add(RectOf(button));
                    var rect = RectOf(button);
                    controls = controls is Rect before ? Rect.MinMaxRect(Mathf.Min(before.xMin, rect.xMin), Mathf.Min(before.yMin, rect.yMin),
                        Mathf.Max(before.xMax, rect.xMax), Mathf.Max(before.yMax, rect.yMax)) : rect;
                }
                if (paged && model.Parts is (int at, int count))
                {
                    x = PutRight(next, PanelModel.NextPart, EntryText.Next, x, row, available: at < count - 1);
                    Took(next);
                    x = PutRight(previous, PanelModel.PreviousPart, EntryText.Previous, x, row, available: at > 0);
                    Took(previous);
                }
                if (model.HeadingAction is PanelAction action)
                {
                    x = PutRight(headingAction, action.Id, action.Label, x, row, available: action.Available, icon: action.Icon);
                    Took(headingAction);
                }
                width = x - left;
                end = x;
                // Beside a body the screen draws itself, or lines, which can run short of it; never
                // beside prompts, which stand in the heading's row.
                if ((model.CustomBody || (model.Rows.Count > 0 && model.Rows[0].Line)) && model.Prompts.Count == 0) notch = controls;
                else middle = row;
            }
            GlazeText.SetLiteral(heading, model.Heading!);
            var words = width;
            if (model.Prompts.Count > 0)
            {
                // The words' own width, measured in the room they have.
                var (_, measured) = GlazeText.Lay(heading, width, 1);
                words = Mathf.Min(width, measured + TargetGap);
                height = GlazeButton.HeightOf(true);
                middle = top - height / 2f;
                // The prompts stand after the heading's words where all of them fit; where they don't,
                // in the words' place, since each prompt is a whole question of its own.
                var room = end - left;
                if (words + PromptsWidth(model) > room + 1e-5f) words = 0f;
                LayPrompts(model, left + words, end, middle);
            }
            if (words > 0f)
            {
                GlazeText.Lay(heading, words, 1);
                heading.transform.localPosition = new Vector3(left, middle + line / 2f, -0.0005f);
                heading.gameObject.SetActive(true);
            }
            return notch != null ? top - line : top - height;
        }

        /// <summary>A body the screen draws itself pages from the heading's row: Previous and Next at its right, beside its action, and no row under the body.</summary>
        private static bool PagesInHeading(PanelModel model) =>
            model.CustomBody && model.Confirm == null && model.Heading != null && model.Parts is (_, int count) && count > 1;

        /// <summary>How wide the prompts stand, each 12 mm from the next and the last 12 mm from what follows.</summary>
        private float PromptsWidth(PanelModel model)
        {
            var width = 0f;
            for (var index = 0; index < model.Prompts.Count; index++) width += Prompt(index).Measure(model.Prompts[index].Label) + TargetGap;
            return width;
        }

        /// <summary>The prompts from <paramref name="left"/>, the one answered edged in the accent, 12 mm apart and clear of the heading's action.</summary>
        private void LayPrompts(PanelModel model, float left, float end, float middle)
        {
            var x = left;
            for (var index = 0; index < model.Prompts.Count; index++)
            {
                var prompt = model.Prompts[index];
                var button = Prompt(index);
                button.Role = ButtonRole.Filter;
                button.On = prompt.Chosen;
                button.Available = true;
                var width = button.Measure(prompt.Label);
                button.Show(prompt.Label, new Vector2(x + width / 2f, middle), width);
                pressing[button] = (PanelModel.Prompt, prompt.Id);
                laidOut.Add(RectOf(button));
                x += width + TargetGap;
            }
            if (x > end + 1e-5f) Debug.LogWarning("Halcyonic: the prompts run into the heading's action.");
        }

        private float LayBanner(PanelBanner model, float left, float width, float top)
        {
            var tone = Glaze.Tone(model.Tone);
            var height = GlazeButton.HeightOf(true) + 2f * Units(BannerPaddingDegrees);
            var padding = Units(BannerPaddingDegrees);
            var middle = top - height / 2f;
            var x = left + width - padding;
            var buttons = new[] { bannerFirst, bannerSecond };
            for (var index = model.Actions.Count - 1; index >= 0; index--)
            {
                var action = model.Actions[index];
                var button = buttons[index];
                button.Role = RoleOf(action.Role);
                x = PutRight(button, action.Id, action.Label, x, middle, available: action.Available, icon: action.Icon);
            }
            GlazeText.SetLiteral(bannerText, model.Text);
            bannerText.color = GlazeTokens.ColorOf(tone.Foreground);
            var textWidth = x - (left + Units(1f));
            var (textLines, _) = GlazeText.Lay(bannerText, textWidth, 2);
            var textHeight = textLines * GlazeText.LineHeight(bannerText);
            height = Mathf.Max(height, textHeight + 2f * padding);
            middle = top - height / 2f;
            bannerText.transform.localPosition = new Vector3(left + Units(1f), middle + textHeight / 2f, -0.0005f);
            bannerText.gameObject.SetActive(true);
            banner.transform.localPosition = new Vector3(left + width / 2f, middle, 0f);
            banner.Draw(new Vector2(width, height), Units(Glaze.RowRadiusDegrees), GlazeTokens.ColorOf(tone.Container),
                GlazeTokens.ColorOf(tone.Strong, 0.45f), Units(0.1f));
            banner.gameObject.SetActive(true);
            // The buttons stand in the banner's middle, whatever its height.
            foreach (var button in buttons)
            {
                if (!button.gameObject.activeSelf) continue;
                var place = button.transform.localPosition;
                button.transform.localPosition = new Vector3(place.x, middle, place.z);
                laidOut.Add(RectOf(button));
            }
            return top - height;
        }

        /// <summary>
        /// The bar: Back and the destructive action from the left, the secondary actions and the primary
        /// from the right, the primary last, with what the screen notes at the left; or the confirm
        /// step, Cancel at the right end and Yes where no control stood. Returns the top of what stands
        /// above the bottom edge, the bar or Yes's row, and whether it holds targets.
        /// </summary>
        private (float Top, bool Targets) LayBar(PanelModel model, float left, float right, float bottom, float middle)
        {
            foreach (var button in new[] { back, destructive, secondaryFirst, secondarySecond, primary, yes, cancel }) button.Hide();
            reason.gameObject.SetActive(false);
            barNote.gameObject.SetActive(false);
            question.gameObject.SetActive(false);
            var barHeight = GlazeButton.HeightOf(false);
            var gap = TargetGap;
            if (model.Confirm is ConfirmStep step)
            {
                BarIcons = true;
                var cancelLeft = PutRight(cancel, step.Cancel.Id, step.Cancel.Label, right, middle, minimum: Units(RightEndDegrees), icon: step.Cancel.Icon) + gap;
                laidOut.Add(RectOf(cancel));
                return PlaceYes(step, left, right, cancelLeft, bottom, middle);
            }
            var actions = model.Actions;
            // Each action's icon beside its words where every one fits so, 12 mm apart; else the bar's words stand alone.
            var icons = FitsWithIcons(actions, left, right);
            BarIcons = icons;
            GlazeIcon? IconOf(PanelAction action) => icons ? action.Icon : null;
            var start = left;
            if (actions.Back != null) start = PutLeft(back, actions.Back, start, middle, IconOf(actions.Back)) + gap;
            if (actions.Destructive != null) start = PutLeft(destructive, actions.Destructive, start, middle, IconOf(actions.Destructive)) + gap;
            var x = right;
            if (actions.Primary != null)
            {
                x = PutRight(primary, actions.Primary.Id, actions.Primary.Label, x, middle, available: actions.Primary.Available, minimum: Units(RightEndDegrees),
                    icon: IconOf(actions.Primary));
            }
            var secondaries = new[] { secondaryFirst, secondarySecond };
            for (var index = actions.Secondary.Count - 1; index >= 0; index--)
            {
                var action = actions.Secondary[index];
                var button = secondaries[index];
                button.Holds = action.Holds;
                x = PutRight(button, action.Id, action.Label, x, middle, available: action.Available, icon: IconOf(action));
            }
            foreach (var button in BarButtons) laidOut.Add(RectOf(button));
            // What the screen notes at the bar's left, else why the action it leads to can't be taken now.
            var (label, why) = model.BarNote != null ? (barNote, model.BarNote) : (reason, actions.Primary?.Reason);
            if (why != null)
            {
                GlazeText.SetLiteral(label, why);
                var (count, _) = GlazeText.Lay(label, Mathf.Max(0.01f, x - start), QuestionLines);
                label.transform.localPosition = new Vector3(start, middle + count * GlazeText.LineHeight(label) / 2f, -0.0005f);
                label.gameObject.SetActive(true);
            }
            return (bottom + barHeight, actions.All.GetEnumerator().MoveNext());
        }

        /// <summary>
        /// Whether the bar's actions fit between <paramref name="left"/> and <paramref name="right"/>
        /// with their icons, Back and the destructive action at the left and the rest at the right,
        /// 12 mm apart.
        /// </summary>
        private bool FitsWithIcons(ActionSet actions, float left, float right)
        {
            float Width(GlazeButton button, PanelAction action, float minimum = 0f) => Mathf.Max(minimum, button.Measure(action.Label, null, action.Icon));
            var leftEnd = left;
            if (actions.Back != null) leftEnd += Width(back, actions.Back) + TargetGap;
            if (actions.Destructive != null) leftEnd += Width(destructive, actions.Destructive) + TargetGap;
            var rightEnd = right;
            if (actions.Primary != null) rightEnd -= Width(primary, actions.Primary, Units(RightEndDegrees)) + TargetGap;
            var secondaries = new[] { secondaryFirst, secondarySecond };
            for (var index = 0; index < actions.Secondary.Count; index++) rightEnd -= Width(secondaries[index], actions.Secondary[index]) + TargetGap;
            // Each end carries a gap past its last button: 12 mm between the two ends, or none needed past the left edge.
            return rightEnd + TargetGap >= leftEnd - 1e-5f;
        }

        /// <summary>
        /// Yes, at the right-most place in the bar's row where no control stood a moment before and
        /// its question fits beside it; else in a row just above the bar, its question beside it,
        /// the bar's row keeping only Cancel. Returns the top of what stands above the bottom edge.
        /// </summary>
        private (float Top, bool Targets) PlaceYes(ConfirmStep step, float left, float right, float cancelLeft, float bottom, float barMiddle)
        {
            var barHeight = GlazeButton.HeightOf(false);
            var gap = TargetGap;
            var avoid = new List<Rect>(recorded);
            avoid.AddRange(laidOut);
            yes.Role = RoleOf(step.Yes.Role);
            // The confirmation of what can't be taken back is solid red; its first step was only outlined.
            yes.On = step.Yes.Role == PanelActionRole.Destructive;
            var width = yes.Measure(step.Yes.Label, null, step.Yes.Icon);
            var why = step.Yes.Reason ?? step.Question;

            // In the bar's row, where the question fits whole to Yes's left.
            if (FreeRight(avoid, left, cancelLeft - gap, barMiddle, barHeight, width) is float inBar
                && (why == null || FitsQuestion(why, inBar - width - gap - left)))
            {
                ShowYes(step, inBar - width / 2f, barMiddle, width);
                if (why != null) LayQuestion(why, left, inBar - width - gap - left, barMiddle);
                return (bottom + barHeight, true);
            }
            // In a row just above the bar; the body makes room.
            var rowMiddle = barMiddle + barHeight + gap;
            var free = FreeRight(avoid, left, right, rowMiddle, barHeight, width);
            if (free == null) Debug.LogWarning("Halcyonic: no place for Yes clear of every control; it stands at the row's right end.");
            var place = free ?? right;
            ShowYes(step, place - width / 2f, rowMiddle, width);
            if (why != null)
            {
                // Beside Yes when it fits there, else along the bar's row, left of Cancel.
                if (FitsQuestion(why, place - width - gap - left)) LayQuestion(why, left, place - width - gap - left, rowMiddle);
                else LayQuestion(why, left, cancelLeft - gap - left, barMiddle);
            }
            return (rowMiddle + barHeight / 2f, true);
        }

        private void ShowYes(ConfirmStep step, float center, float middle, float width)
        {
            yes.Available = step.Yes.Available;
            yes.Show(step.Yes.Label, new Vector2(center, middle), width, withIcon: step.Yes.Icon);
            pressing[yes] = (step.Yes.Id, null);
        }

        private bool FitsQuestion(string why, float width)
        {
            if (width <= 0.01f) return false;
            GlazeText.SetLiteral(question, why);
            GlazeText.Lay(question, width, QuestionLines);
            return !question.isTextTruncated;
        }

        private void LayQuestion(string why, float left, float width, float middle)
        {
            GlazeText.SetLiteral(question, why);
            var (count, _) = GlazeText.Lay(question, Mathf.Max(0.01f, width), QuestionLines);
            question.transform.localPosition = new Vector3(left, middle + count * GlazeText.LineHeight(question) / 2f, -0.0005f);
            question.gameObject.SetActive(true);
        }

        /// <summary>
        /// The right edge of the right-most stretch of a row, between <paramref name="xMin"/> and
        /// <paramref name="xMax"/>, at least <paramref name="width"/> wide and 12 mm clear of every
        /// rectangle in <paramref name="avoid"/>; null when there is none.
        /// </summary>
        private static float? FreeRight(List<Rect> avoid, float xMin, float xMax, float middle, float height, float width)
        {
            var gap = TargetGap;
            var rowBottom = middle - height / 2f;
            var rowTop = middle + height / 2f;
            var blocked = new List<(float Min, float Max)>();
            foreach (var rect in avoid)
            {
                if (rect.yMin - gap >= rowTop - 1e-5f || rect.yMax + gap <= rowBottom + 1e-5f) continue;
                blocked.Add((rect.xMin - gap, rect.xMax + gap));
            }
            blocked.Sort((a, b) => b.Max.CompareTo(a.Max));
            var edge = xMax;
            while (true)
            {
                // The right-most blocker still reaching past what is left of the row's free end.
                var moved = false;
                foreach (var (min, max) in blocked)
                {
                    if (max <= edge - width + 1e-5f || min >= edge - 1e-5f) continue;
                    edge = min;
                    moved = true;
                }
                if (!moved) break;
            }
            return edge - width >= xMin - 1e-5f ? edge : (float?)null;
        }

        /// <summary>The pager of a confirmation that pages: its caption at the row's left, then Previous and Next, away from Yes.</summary>
        private void LayTopPager(PanelModel model, float left, float middle)
        {
            var (at, count) = model.Parts!.Value;
            pageCaption.alignment = TextAlignmentOptions.TopLeft;
            GlazeText.SetLiteral(pageCaption, model.PartsCaption ?? EntryText.Part(at, count));
            var width = pageCaption.GetPreferredValues(pageCaption.text).x + 0.001f;
            GlazeText.Lay(pageCaption, width, 1);
            pageCaption.transform.localPosition = new Vector3(left, middle + GlazeText.LineHeight(pageCaption) / 2f, -0.0005f);
            pageCaption.gameObject.SetActive(true);
            if (count <= 1) return;
            var x = left + width + TargetGap;
            x = PutLeft(previous, PanelModel.PreviousPart, EntryText.Previous, x, middle, at > 0) + TargetGap;
            PutLeft(next, PanelModel.NextPart, EntryText.Next, x, middle, at < count - 1);
            laidOut.Add(RectOf(previous));
            laidOut.Add(RectOf(next));
        }

        /// <summary>
        /// The row under a body the screen pages: what it says at the left, a heading over a note,
        /// and the pager at the right while there is more than one part.
        /// </summary>
        private void LayBand(PanelModel model, float left, float right, float middle)
        {
            var end = right;
            if (model.Parts is (int at, int count))
            {
                if (count > 1)
                {
                    end = LayPager(at, count, model.PartsCaption ?? EntryText.Part(at, count), right, middle, external: true);
                }
            }
            var width = end - left;
            var stack = 0f;
            if (model.PartsHeading != null)
            {
                GlazeText.SetLiteral(partsHeading, model.PartsHeading);
                GlazeText.Lay(partsHeading, width, 1);
                data.Add(partsHeading);
                stack += GlazeText.LineHeight(partsHeading);
            }
            var noteLines = 0;
            if (model.PartsNote != null)
            {
                GlazeText.SetLiteral(partsNote, model.PartsNote);
                (noteLines, _) = GlazeText.Lay(partsNote, width, NoteLines);
                stack += noteLines * GlazeText.LineHeight(partsNote);
            }
            var y = middle + stack / 2f;
            if (model.PartsHeading != null)
            {
                partsHeading.transform.localPosition = new Vector3(left, y, -0.0005f);
                partsHeading.gameObject.SetActive(true);
                y -= GlazeText.LineHeight(partsHeading);
            }
            if (model.PartsNote != null)
            {
                partsNote.transform.localPosition = new Vector3(left, y, -0.0005f);
                partsNote.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// The list, one column or two, a page at a time: rows to press in cells of equal height, 12 mm
        /// apart; a line across the list, and in a single column a row that only says something, in a
        /// row of its own as tall as its words; lines that go on from the one before without a gap; a
        /// log's older lines left out where there is no room, so it never pages; and while there is
        /// more than one page, the pager in the body's bottom right cell.
        /// </summary>
        private void LayList(PanelModel model, Rect area)
        {
            var columns = Mathf.Clamp(model.Columns, 1, 2);
            var gap = TargetGap;
            var cellWidth = columns == 1 ? area.width : (area.width - gap) / 2f;
            var cellHeight = GlazeButton.HeightOf(false);
            var anyCard = false;
            var rowsOf = model.Rows;
            var flow = new bool[rowsOf.Count];
            var heightOf = new float[rowsOf.Count];
            // Lines that start beside the heading's action run short of it, measured at that width.
            var besideWidth = notch is Rect action ? action.xMin - Units(NotchGapDegrees) - area.xMin : area.width;
            var measuredTop = area.yMax;
            for (var index = 0; index < rowsOf.Count; index++)
            {
                var row = rowsOf[index];
                flow[index] = Flows(row, columns);
                if (row.Line)
                {
                    if (index > 0 && !row.Continues) measuredTop -= Units(LineGapDegrees);
                    heightOf[index] = MeasureLine(row, Beside(measuredTop) ? besideWidth : area.width);
                    measuredTop -= heightOf[index];
                    continue;
                }
                // Only lines run beside the action; nothing after anything else does.
                measuredTop = float.MinValue;
                if (flow[index]) heightOf[index] = Measure(row, area.width, 0f);
                else
                {
                    anyCard |= row.Card;
                    cellHeight = Mathf.Max(cellHeight, Measure(row, CellTextWidth(row, cellWidth), GlazeButton.HeightOf(false)));
                }
            }
            if (anyCard) cellHeight = Mathf.Max(cellHeight, Units(CardMinimumDegrees));
            kept.Clear();
            for (var index = 0; index < rowsOf.Count; index++) kept.Add(index);
            Paginate(rowsOf, flow, heightOf, columns, area, cellHeight);
            dropped = 0;
            if (pages.Count > 1)
            {
                // A log's older lines give way, the lines that continue a group before the line that starts it.
                foreach (var startsGroup in new[] { false, true })
                {
                    for (var index = 0; index < rowsOf.Count && pages.Count > 1; index++)
                    {
                        if (!rowsOf[index].Droppable || rowsOf[index].Continues == startsGroup || !kept.Contains(index)) continue;
                        kept.Remove(index);
                        dropped++;
                        Paginate(rowsOf, flow, heightOf, columns, area, cellHeight);
                    }
                }
            }
            page = Mathf.Clamp(page, 0, Pages - 1);

            shownRows.Clear();
            shownLines.Clear();
            shownMeters.Clear();
            var used = 0;
            var usedSides = 0;
            var usedLines = 0;
            var usedMeters = 0;
            var y = area.yMax;
            var afterFlow = true;
            if (pages.Count > 0)
            {
                foreach (var slot in pages[page])
                {
                    if (slot.Flow)
                    {
                        var item = rowsOf[kept[slot.First]];
                        if (y < area.yMax) y -= item.Continues && afterFlow ? 0f : Units(LineGapDegrees);
                        if (item.Line)
                        {
                            var label = LineLabel(usedLines++);
                            LayLine(label, item, area.xMin, Beside(y) ? besideWidth : area.width, y);
                            if (item.Meter is float share) LayMeter(Meter(usedMeters++), item, share, area.xMax, y - GlazeText.LineHeight(label) / 2f);
                        }
                        else ShowRow(RowButton(used++), item, area.xMin, area.width, y, heightOf[kept[slot.First]]);
                        y -= heightOf[kept[slot.First]];
                        afterFlow = true;
                        continue;
                    }
                    if (y < area.yMax) y -= afterFlow ? Units(LineGapDegrees) : gap;
                    for (var cell = 0; cell < slot.Count; cell++)
                    {
                        var row = rowsOf[kept[slot.First + cell]];
                        var cellLeft = area.xMin + cell * (cellWidth + gap);
                        var width = cellWidth;
                        if (row.Side != null)
                        {
                            var side = SideButton(usedSides++);
                            side.Holds = row.Side.Holds;
                            side.Available = row.Side.Available;
                            var sideWidth = side.Measure(row.Side.Label, null, row.Side.Icon);
                            width = cellWidth - sideWidth - gap;
                            side.Show(row.Side.Label, new Vector2(cellLeft + cellWidth - sideWidth / 2f, y - cellHeight / 2f), sideWidth, withIcon: row.Side.Icon);
                            pressing[side] = (row.Side.Id, row.Key);
                            laidOut.Add(RectOf(side));
                        }
                        ShowRow(RowButton(used++), row, cellLeft, width, y, cellHeight);
                    }
                    y -= cellHeight;
                    afterFlow = false;
                }
            }
            HideRows(used, usedSides, usedLines, usedMeters);
            if (pages.Count > 1)
            {
                // At the body's bottom right, in its last cell, where every screen's pager stands.
                LayPager(page, pages.Count, EntryText.Page(page, pages.Count), area.xMax, area.yMin + GlazeButton.HeightOf(true) / 2f, external: false);
            }
        }

        /// <summary>Whether a line whose top is at <paramref name="top"/> starts beside the heading's action.</summary>
        private bool Beside(float top) => notch is Rect action && top > action.yMin + 1e-5f;

        /// <summary>A line, or in a single column a row that only says something: as tall as its words, in a row of its own.</summary>
        private static bool Flows(PanelRow row, int columns) => row.Line || (columns == 1 && !row.Pressable && row.Side == null);

        private void ShowRow(GlazeButton button, PanelRow row, float left, float width, float top, float height)
        {
            button.Role = row.Filter ? ButtonRole.Filter : ButtonRole.Choice;
            button.Static = !row.Pressable;
            button.On = row.Chosen;
            button.Available = row.Available;
            button.ShowRow(row, new Vector2(left + width / 2f, top - height / 2f), new Vector2(width, height), row.Pressable ? GlazeButton.HeightOf(false) : 0f);
            if (row.Pressable)
            {
                pressing[button] = (row.Action!, row.Key);
                laidOut.Add(RectOf(button));
            }
            if (row.TitleIsData) data.Add(button.Label);
            shownRows.Add((button, row));
        }

        /// <summary>
        /// Splits the list into pages that fit <paramref name="area"/>: whole rows only, the rows still
        /// kept. While there is more than one page, the body's bottom right cell is the pager's on
        /// every page.
        /// </summary>
        private void Paginate(IList<PanelRow> rowsOf, bool[] flow, float[] heightOf, int columns, Rect area, float cellHeight)
        {
            var gap = TargetGap;
            var lineGap = Units(LineGapDegrees);
            var count = kept.Count;
            for (var pass = 0; pass < 2; pass++)
            {
                var paged = pass == 1;
                pages.Clear();
                var i = 0;
                while (i < count)
                {
                    var current = new List<Slot>();
                    var y = 0f;
                    var afterFlow = true;
                    // A one-column list keeps a row of its own for the pager.
                    var room = area.height - (paged && columns == 1 ? GlazeButton.HeightOf(true) + gap : 0f);
                    while (i < count)
                    {
                        var index = kept[i];
                        if (flow[index])
                        {
                            var before = current.Count == 0 ? 0f : rowsOf[index].Continues && afterFlow ? 0f : lineGap;
                            var needed = before + heightOf[index];
                            if (current.Count > 0 && y + needed > room + 1e-5f) break;
                            current.Add(new Slot(i, 1, true));
                            y += needed;
                            i++;
                            afterFlow = true;
                            continue;
                        }
                        var cells = 1;
                        while (cells < columns && i + cells < count && !flow[kept[i + cells]]) cells++;
                        var space = (current.Count == 0 ? 0f : afterFlow ? lineGap : gap) + cellHeight;
                        if (current.Count > 0 && y + space > room + 1e-5f) break;
                        current.Add(new Slot(i, cells, false));
                        y += space;
                        i += cells;
                        afterFlow = false;
                    }
                    // The pager stands in the body's bottom right cell: a row that reaches down to it keeps that cell free.
                    if (paged && columns == 2 && current.Count > 0 && y > area.height - cellHeight + 1e-5f)
                    {
                        var last = current[current.Count - 1];
                        if (!last.Flow && last.Count == 2)
                        {
                            current[current.Count - 1] = new Slot(last.First, 1, false);
                            i = last.First + 1;
                        }
                        else if (last.Flow && current.Count > 1)
                        {
                            current.RemoveAt(current.Count - 1);
                            i = last.First;
                        }
                    }
                    pages.Add(current);
                }
                if (pages.Count <= 1) break;
            }
        }

        /// <summary>The list's own pager at the body's bottom right, or a screen's at its pager row's right: returns its left edge, less the gap.</summary>
        private float LayPager(int at, int count, string caption, float right, float middle, bool external)
        {
            var x = right;
            x = PutRight(next, external ? PanelModel.NextPart : "", EntryText.Next, x, middle, available: at < count - 1);
            x = PutRight(previous, external ? PanelModel.PreviousPart : "", EntryText.Previous, x, middle, available: at > 0);
            laidOut.Add(RectOf(next));
            laidOut.Add(RectOf(previous));
            if (!external)
            {
                // The list's own pages turn here, not through the screen.
                pressing.Remove(next);
                pressing.Remove(previous);
            }
            pageCaption.alignment = TextAlignmentOptions.TopRight;
            GlazeText.SetLiteral(pageCaption, caption);
            var width = pageCaption.GetPreferredValues(pageCaption.text).x + 0.001f;
            GlazeText.Lay(pageCaption, width, 1);
            pageCaption.transform.localPosition = new Vector3(x - width, middle + GlazeText.LineHeight(pageCaption) / 2f, -0.0005f);
            pageCaption.gameObject.SetActive(true);
            return x - width - TargetGap;
        }

        /// <summary>
        /// Splits text into parts of <paramref name="linesPerPart"/> whole lines as a line of the list
        /// lays it out at <paramref name="width"/>: together the parts are the text by the one rule for
        /// text Halcyonic did not write, every character once, so a part shown alone wraps as it did.
        /// </summary>
        public List<string> SplitLines(string text, int linesPerPart, float width, PanelTextSize size = PanelTextSize.Body)
        {
            Style(splitter, new PanelRow { Size = size });
            GlazeText.SetLiteral(splitter, text);
            splitter.overflowMode = TextOverflowModes.Overflow;
            splitter.rectTransform.sizeDelta = new Vector2(width, 1000f);
            splitter.ForceMeshUpdate(true);
            var info = splitter.textInfo;
            var source = splitter.text;
            var parts = new List<string>();
            if (info.characterCount == 0 || info.lineCount == 0)
            {
                parts.Add(LabelText.Plain(text));
                return parts;
            }
            var each = Math.Max(1, linesPerPart);
            for (var first = 0; first < info.lineCount; first += each)
            {
                var from = first == 0 ? 0 : info.characterInfo[info.lineInfo[first].firstCharacterIndex].index;
                var to = first + each < info.lineCount ? info.characterInfo[info.lineInfo[first + each].firstCharacterIndex].index : source.Length;
                // The label holds every backslash doubled; a part never splits a pair, as each pair is one character.
                parts.Add(source.Substring(from, to - from).Replace("\\\\", "\\"));
            }
            return parts;
        }

        /// <summary>How far apart a list's lines of a size are, in the frame's units.</summary>
        public float LineHeightOf(PanelTextSize size)
        {
            Style(splitter, new PanelRow { Size = size });
            return GlazeText.LineHeight(splitter);
        }

        /// <summary>How tall a row is at <paramref name="width"/>, at least <paramref name="minimum"/>, measured once for the same words.</summary>
        private float Measure(PanelRow row, float width, float minimum)
        {
            var key = string.Join("\u0001", row.Overline, row.Title, row.TitleLines, row.Detail, row.ShortDetail, row.DetailLines, row.End, row.Card,
                Mathf.RoundToInt(width * 100000f), Mathf.RoundToInt(minimum * 100000f));
            if (heights.TryGetValue(key, out var height)) return height;
            if (heights.Count > 2000) heights.Clear();
            height = measure.LayRow(row, width, minimum);
            heights[key] = height;
            return height;
        }

        /// <summary>The width a row's own words get in a cell: less its side action and the gap before it.</summary>
        private float CellTextWidth(PanelRow row, float cellWidth)
        {
            if (row.Side == null) return cellWidth;
            var side = SideButton(0);
            return cellWidth - side.Measure(row.Side.Label, null, row.Side.Icon) - TargetGap;
        }

        /// <summary>How tall a line is at <paramref name="width"/>, measured once for the same words.</summary>
        private float MeasureLine(PanelRow row, float width)
        {
            var key = string.Join("\u0001", "line", row.Title, row.TitleLines, row.Size, row.Meter.HasValue, Mathf.RoundToInt(width * 100000f));
            if (heights.TryGetValue(key, out var height)) return height;
            if (heights.Count > 2000) heights.Clear();
            Style(lineMeasure, row);
            GlazeText.SetLiteral(lineMeasure, row.Title);
            var (count, _) = GlazeText.Lay(lineMeasure, WordsWidth(row, width), Mathf.Max(1, row.TitleLines));
            height = Mathf.Max(1, count) * GlazeText.LineHeight(lineMeasure);
            heights[key] = height;
            return height;
        }

        /// <summary>The width a line's words get: less its meter and the gap before it.</summary>
        private static float WordsWidth(PanelRow row, float width) =>
            row.Meter.HasValue ? width - MeterView.Width - Units(MeterGapDegrees) : width;

        private float LayLine(TextMeshPro label, PanelRow row, float left, float width, float top)
        {
            Style(label, row);
            GlazeText.SetLiteral(label, row.Title);
            Lean(label, row.Claim);
            var (count, _) = GlazeText.Lay(label, WordsWidth(row, width), Mathf.Max(1, row.TitleLines));
            label.transform.localPosition = new Vector3(left, top, -0.0005f);
            label.gameObject.SetActive(true);
            if (row.TitleIsData) data.Add(label);
            shownLines.Add((label, row));
            return Mathf.Max(1, count) * GlazeText.LineHeight(label);
        }

        /// <summary>The agent's words lean; a label that changes from one to the other is drawn again.</summary>
        private void Lean(TextMeshPro label, bool claim)
        {
            if (claim == leaning.Contains(label)) return;
            if (claim) leaning.Add(label);
            else leaning.Remove(label);
            label.havePropertiesChanged = true;
        }

        private static void Style(TextMeshPro label, PanelRow row)
        {
            var type = row.Size == PanelTextSize.Title ? GlazeType.Title : row.Size == PanelTextSize.Caption ? GlazeType.Caption : GlazeType.Body;
            label.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(GlazeText.DegreesOf(type)));
            label.color = row.Tone.HasValue ? GlazeTokens.ColorOf(Glaze.Tone(row.Tone.Value).Foreground)
                : row.Size == PanelTextSize.Caption ? GlazeTokens.TextSecondary : GlazeTokens.Text;
        }

        /// <summary>A line's meter at the list's right end, centred on the line's first row.</summary>
        private void LayMeter(MeterView meter, PanelRow row, float share, float right, float middle)
        {
            meter.Show(share, row.MeterWaiting);
            meter.transform.localPosition = new Vector3(right - MeterView.Width / 2f, middle, -0.0005f);
            meter.gameObject.SetActive(true);
            shownMeters.Add((new Rect(right - MeterView.Width, middle - MeterView.Height / 2f, MeterView.Width, MeterView.Height), meter.Filled, row));
        }

        private void HideRows(int usedRows, int usedSides, int usedLines, int usedMeters)
        {
            for (var index = usedRows; index < rows.Count; index++) rows[index].Hide();
            for (var index = usedSides; index < sides.Count; index++) sides[index].Hide();
            for (var index = usedLines; index < lines.Count; index++) lines[index].gameObject.SetActive(false);
            for (var index = usedMeters; index < meters.Count; index++) meters[index].gameObject.SetActive(false);
        }

        private MeterView Meter(int index)
        {
            while (meters.Count <= index) meters.Add(MeterView.Create(content, "Meter " + meters.Count, 11));
            return meters[index];
        }

        private GlazeButton RowButton(int index)
        {
            while (rows.Count <= index)
            {
                var button = GlazeButton.Create(content, "Row " + rows.Count, ButtonRole.Choice);
                Wire(button);
                rows.Add(button);
            }
            return rows[index];
        }

        private GlazeButton SideButton(int index)
        {
            while (sides.Count <= index)
            {
                var button = GlazeButton.Create(content, "Row action " + sides.Count, ButtonRole.Secondary);
                Wire(button);
                sides.Add(button);
            }
            return sides[index];
        }

        private GlazeButton Prompt(int index)
        {
            while (prompts.Count <= index)
            {
                var button = GlazeButton.Create(content, "Prompt " + prompts.Count, ButtonRole.Filter, compact: true);
                Wire(button);
                prompts.Add(button);
            }
            return prompts[index];
        }

        private GlazeButton Tab(int index)
        {
            while (tabs.Count <= index)
            {
                var button = GlazeButton.Create(content, "Tab " + tabs.Count, ButtonRole.Filter, compact: true);
                Wire(button);
                tabs.Add(button);
            }
            return tabs[index];
        }

        private MarkTag Mark(int index)
        {
            while (marks.Count <= index)
            {
                var mark = MarkTag.Create(content, "Mark " + marks.Count, 11);
                mark.gameObject.SetActive(false);
                marks.Add(mark);
            }
            return marks[index];
        }

        private TextMeshPro LineLabel(int index)
        {
            while (lines.Count <= index)
            {
                var label = Text("Line " + lines.Count, GlazeType.Body, GlazeTokens.Text);
                label.OnPreRenderText += info =>
                {
                    if (leaning.Contains(label)) GlazeText.Lean(info);
                };
                lines.Add(label);
            }
            return lines[index];
        }

        private GlazeButton Button(string name, ButtonRole role, bool compact = false)
        {
            var button = GlazeButton.Create(content, name, role, compact);
            Wire(button);
            return button;
        }

        private void Wire(GlazeButton button)
        {
            button.Accepting = () => Accepting();
            button.Pressed += () =>
            {
                if (pressing.TryGetValue(button, out var action)) Acted?.Invoke(action.Id, action.Key);
            };
            button.HoldStarted += () =>
            {
                if (pressing.TryGetValue(button, out var action)) HoldStarted?.Invoke(action.Id);
            };
            button.HoldEnded += released =>
            {
                if (pressing.TryGetValue(button, out var action)) HoldEnded?.Invoke(action.Id, released);
            };
        }

        /// <summary>Shows a button with its right edge at <paramref name="right"/>; returns where its left edge is, less the gap.</summary>
        private float PutRight(GlazeButton button, string id, string label, float right, float middle, bool available = true, float minimum = 0f,
            GlazeIcon? icon = null)
        {
            var width = Mathf.Max(minimum, button.Measure(label, null, icon));
            button.Available = available;
            button.Show(label, new Vector2(right - width / 2f, middle), width, withIcon: icon);
            if (id.Length > 0) pressing[button] = (id, null);
            return right - width - TargetGap;
        }

        private float PutLeft(GlazeButton button, PanelAction action, float left, float middle, GlazeIcon? icon)
        {
            button.Holds = action.Holds;
            return PutLeft(button, action.Id, action.Label, left, middle, action.Available, icon);
        }

        /// <summary>Shows a button with its left edge at <paramref name="left"/>; returns where its right edge is.</summary>
        private float PutLeft(GlazeButton button, string id, string label, float left, float middle, bool available, GlazeIcon? icon = null)
        {
            var width = button.Measure(label, null, icon);
            button.Available = available;
            button.Show(label, new Vector2(left + width / 2f, middle), width, withIcon: icon);
            pressing[button] = (id, null);
            return left + width;
        }

        private static ButtonRole RoleOf(PanelActionRole role) => role switch
        {
            PanelActionRole.Primary => ButtonRole.Primary,
            PanelActionRole.Destructive => ButtonRole.Destructive,
            PanelActionRole.Attention => ButtonRole.Attention,
            _ => ButtonRole.Secondary,
        };

        private TextMeshPro Text(string name, GlazeType type, Color color)
        {
            var label = GlazeText.Create(content, name, type, color, TextAlignmentOptions.TopLeft, TitleOrder);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            label.gameObject.SetActive(false);
            return label;
        }

        private static float Units(float degrees) => GlazeTokens.Units(degrees);

        /// <summary>One row of the list on a page: an item as tall as its words, or up to a row's worth of cells, by its place among the rows kept.</summary>
        private readonly struct Slot
        {
            public Slot(int first, int count, bool flow)
            {
                First = first;
                Count = count;
                Flow = flow;
            }

            public int First { get; }

            public int Count { get; }

            public bool Flow { get; }
        }
    }
}
