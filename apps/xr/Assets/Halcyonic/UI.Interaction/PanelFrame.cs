#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A foreground panel (ADR 0023), drawn from a <see cref="PanelModel"/> by the tokens, so every
    /// screen puts the same things in the same places: the title and the window controls (Move, Reset
    /// position, Close) along the top; a lead line, or a banner in its place; the body, a list in one
    /// column or two, a page at a time, with the pager at its bottom right; and the action bar along
    /// the bottom, Back and the destructive action at the left, the primary at the right end, or the
    /// confirm step in the bar's place. It is 44 by 26 degrees, built in units of its distance from the
    /// eyes, under a root the panel scales by that distance and places at touch distance.
    /// </summary>
    /// <remarks>
    /// It decides nothing: what a press means is the screen's, raised as <see cref="Acted"/> with the
    /// action's id and, for a row, its key. Its buttons ignore input while <see cref="Accepting"/> says
    /// so, and a button that takes new words, a new role or becomes available waits its settle time
    /// before a press counts (<see cref="GlazeButton"/>). Every word goes on its label by the one rule
    /// for text Halcyonic did not write.
    /// </remarks>
    public sealed class PanelFrame : MonoBehaviour
    {
        /// <summary>From the eyes to a foreground panel: touch distance (ADR 0023).</summary>
        public const float Distance = 0.46f;

        public const float WidthDegrees = 44f;
        public const float HeightDegrees = 26f;

        private const float PaddingDegrees = 1.25f;
        private const float SectionGapDegrees = 0.75f;

        /// <summary>Between targets, as Meta asks: 12 mm at touch distance.</summary>
        private static float TargetGap => Glaze.TargetGapMeters / Distance;

        private const float LineGapDegrees = 0.6f;
        private const float BannerPaddingDegrees = 0.25f;
        private const int LeadLines = 2;
        private const int QuestionLines = 2;
        private const float CardMinimumDegrees = 7.5f;

        /// <summary>
        /// The bar's right end, the primary's place, is at least this wide, and so is Cancel when the
        /// confirm step takes it: Yes, left of it, then never stands where the action that led to it stood.
        /// </summary>
        private const float RightEndDegrees = 14f;
        private const int TitleOrder = 12;

        private readonly List<GlazeButton> rows = new List<GlazeButton>();
        private readonly List<GlazeButton> sides = new List<GlazeButton>();
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly Dictionary<GlazeButton, (string Id, string? Key)> pressing = new Dictionary<GlazeButton, (string, string?)>();
        private readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        private readonly HashSet<TMP_Text> data = new HashSet<TMP_Text>();
        private readonly List<List<Slot>> pages = new List<List<Slot>>();
        private Transform content = null!;
        private Surface plate = null!;
        private PointerTarget background = null!;
        private TextMeshPro title = null!;
        private TextMeshPro context = null!;
        private TextMeshPro lead = null!;
        private TextMeshPro reason = null!;
        private TextMeshPro question = null!;
        private TextMeshPro pageCaption = null!;
        private Surface banner = null!;
        private TextMeshPro bannerText = null!;
        private GlazeButton move = null!;
        private GlazeButton reset = null!;
        private GlazeButton close = null!;
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
        private Vector2 size;
        private int page;

        /// <summary>Something was pressed: the action's id and, for a row or its side action, the row's key.</summary>
        public event Action<string, string?>? Acted;

        /// <summary>A hold action, as hold to talk, began to be held: its id.</summary>
        public event Action<string>? HoldStarted;

        /// <summary>A hold ended: let go on it (true), or dropped (false).</summary>
        public event Action<string, bool>? HoldEnded;

        /// <summary>Presses are ignored while false, as while the panel is hidden or the app lacks focus.</summary>
        public Func<bool> Accepting { get; set; } = () => true;

        /// <summary>The frame's size, in units of its distance.</summary>
        public Vector2 Size => size;

        /// <summary>Where a screen that draws its own body draws it, as the whole request is, in the frame's units, above the pager.</summary>
        public Rect CustomBody => customBody;

        /// <summary>The list's page showing, from 0; a new screen starts at the first.</summary>
        public int Page
        {
            get => page;
            set => page = Math.Max(0, value);
        }

        /// <summary>How many pages the list showing takes.</summary>
        public int Pages => Math.Max(1, pages.Count);

        /// <summary>Where the frame's parts are laid out, for a screen that draws its own body.</summary>
        public Transform Content => content;

        /// <summary>The bar's buttons and the window controls by what they raise, for the editor's checks.</summary>
        public IEnumerable<GlazeButton> Buttons
        {
            get
            {
                foreach (var button in new[] { move, reset, close, bannerFirst, bannerSecond, back, destructive, secondaryFirst, secondarySecond, primary, yes, cancel, previous, next })
                {
                    if (button.gameObject.activeSelf) yield return button;
                }
                foreach (var button in rows)
                {
                    if (button.gameObject.activeSelf) yield return button;
                }
                foreach (var button in sides)
                {
                    if (button.gameObject.activeSelf) yield return button;
                }
            }
        }

        /// <summary>Every label showing, for the editor's checks that each shows what it was given, whole.</summary>
        public IEnumerable<TMP_Text> Labels
        {
            get
            {
                foreach (var label in new[] { title, context, lead, reason, question, pageCaption, bannerText })
                {
                    if (label.gameObject.activeSelf) yield return label;
                }
                foreach (var label in lines)
                {
                    if (label.gameObject.activeSelf) yield return label;
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

        /// <summary>The model showing, for the editor's checks.</summary>
        public PanelModel? Shown => shown;

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

        /// <summary>Back, at the bar's left end, while it shows.</summary>
        public GlazeButton? Back => back.gameObject.activeSelf ? back : null;

        /// <summary>The pager's Next, while it shows.</summary>
        public GlazeButton? NextPage => next.gameObject.activeSelf ? next : null;

        /// <summary>Whether a label shows text from outside, which may end in an ellipsis where it doesn't fit; Halcyonic's own words never do.</summary>
        public bool HoldsData(TMP_Text label) => data.Contains(label);

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
            size = new Vector2(2f * GlazeTokens.Units(WidthDegrees / 2f), 2f * GlazeTokens.Units(HeightDegrees / 2f));
            plate = Surface.Create(transform, "Background", 10);
            plate.transform.localPosition = new Vector3(0f, 0f, GlazeTokens.Units(0.05f));
            plate.Draw(size, GlazeTokens.Units(Glaze.PanelRadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel));
            // The background takes the ray, so nothing behind the panel is pointed at through it.
            background = PointerTarget.Rectangle(gameObject, size, ray: true, poke: false);
            content = new GameObject("Content").transform;
            content.SetParent(transform, false);

            title = Text("Title", GlazeType.Title, GlazeTokens.Text);
            context = Text("Context", GlazeType.Caption, GlazeTokens.TextSecondary);
            lead = Text("Lead", GlazeType.Body, GlazeTokens.Text);
            reason = Text("Reason", GlazeType.Caption, GlazeTokens.TextSecondary);
            reason.alignment = TextAlignmentOptions.TopRight;
            question = Text("Question", GlazeType.Body, GlazeTokens.Text);
            pageCaption = Text("Page", GlazeType.Caption, GlazeTokens.TextSecondary);
            pageCaption.alignment = TextAlignmentOptions.TopRight;
            banner = Surface.Create(content, "Banner", 10);
            bannerText = Text("Banner words", GlazeType.Body, GlazeTokens.Text);

            move = Button("Move", ButtonRole.Secondary, compact: true);
            reset = Button("Reset position", ButtonRole.Secondary, compact: true);
            close = Button("Close", ButtonRole.Secondary, compact: true);
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
            if (model.TitleIsData) data.Add(title);
            var inner = size.x - 2f * Units(PaddingDegrees);
            var left = -size.x / 2f + Units(PaddingDegrees);
            var right = size.x / 2f - Units(PaddingDegrees);
            var top = size.y / 2f - Units(PaddingDegrees);
            var bottom = -size.y / 2f + Units(PaddingDegrees);

            // The header: the window controls at the right, the title and its context in what is left.
            var headerHeight = GlazeButton.HeightOf(true);
            var headerMiddle = top - headerHeight / 2f;
            var x = right;
            x = PutRight(close, PanelModel.Close, model.CloseLabel, x, headerMiddle);
            x = PutRight(reset, PanelModel.ResetPosition, EntryText.ResetPosition, x, headerMiddle);
            x = PutRight(move, PanelModel.Move, EntryText.Move, x, headerMiddle);
            var titleWidth = x - left;
            GlazeText.SetLiteral(title, model.Title);
            GlazeText.Lay(title, titleWidth, 1);
            var titleLine = GlazeText.LineHeight(title);
            if (model.Context != null)
            {
                GlazeText.SetLiteral(context, model.Context);
                GlazeText.Lay(context, titleWidth, 1);
                var both = titleLine + GlazeText.LineHeight(context);
                title.transform.localPosition = new Vector3(left, headerMiddle + both / 2f, -0.0005f);
                context.transform.localPosition = new Vector3(left, headerMiddle + both / 2f - titleLine, -0.0005f);
                context.gameObject.SetActive(true);
            }
            else
            {
                title.transform.localPosition = new Vector3(left, headerMiddle + titleLine / 2f, -0.0005f);
                context.gameObject.SetActive(false);
            }
            title.gameObject.SetActive(true);
            // Targets under the header's keep 12 mm from its buttons; words need less.
            var y = top - headerHeight - (model.Banner != null ? TargetGap - Units(BannerPaddingDegrees) : model.Lead != null ? Units(SectionGapDegrees) : TargetGap);

            // A banner in the lead's place, or the lead.
            lead.gameObject.SetActive(false);
            banner.gameObject.SetActive(false);
            bannerText.gameObject.SetActive(false);
            bannerFirst.Hide();
            bannerSecond.Hide();
            if (model.Banner != null) y = LayBanner(model.Banner, left, inner, y) - (TargetGap - Units(BannerPaddingDegrees));
            else if (model.Lead != null)
            {
                GlazeText.SetLiteral(lead, model.Lead);
                lead.color = model.LeadTone.HasValue ? GlazeTokens.ColorOf(Glaze.Tone(model.LeadTone.Value).Foreground) : GlazeTokens.Text;
                var (leadLines, _) = GlazeText.Lay(lead, inner, LeadLines);
                lead.transform.localPosition = new Vector3(left, y, -0.0005f);
                lead.gameObject.SetActive(true);
                y -= leadLines * GlazeText.LineHeight(lead) + Units(SectionGapDegrees);
            }

            // The bar, from the bottom up, so the body takes what is between.
            var barHeight = GlazeButton.HeightOf(false);
            var barMiddle = bottom + barHeight / 2f;
            LayBar(model, left, right, barMiddle);
            var bodyBottom = bottom + barHeight + TargetGap;
            body = Rect.MinMaxRect(left, bodyBottom, right, y);

            // The body: the screen's own, above the pager's band, or the list a page at a time.
            previous.Hide();
            next.Hide();
            pageCaption.gameObject.SetActive(false);
            if (model.CustomBody)
            {
                var pagerHeight = GlazeButton.HeightOf(true);
                customBody = Rect.MinMaxRect(left, bodyBottom + pagerHeight + TargetGap, right, y);
                if (model.Parts is (int part, int parts)) LayPager(part, parts, EntryText.Part(part, parts), right, bodyBottom + pagerHeight / 2f, external: true);
                HideRows(0, 0, 0);
            }
            else
            {
                customBody = body;
                LayList(model, body);
            }
        }

        /// <summary>Shows the list's next or previous page.</summary>
        private void Turn(int by)
        {
            if (shown == null || !Accepting()) return;
            page = Mathf.Clamp(page + by, 0, Pages - 1);
            Show(shown);
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
                x = PutRight(button, action.Id, action.Label, x, middle, available: action.Available);
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
            }
            return top - height;
        }

        /// <summary>
        /// The bar: Back and the destructive action from the left, the secondary actions and the primary
        /// from the right, the primary last; or the confirm step, its question at the left, Yes, and
        /// Cancel at the right end where the action that led here stood. Why the action the screen leads
        /// to can't be taken now stands just left of it.
        /// </summary>
        private void LayBar(PanelModel model, float left, float right, float middle)
        {
            foreach (var button in new[] { back, destructive, secondaryFirst, secondarySecond, primary, yes, cancel }) button.Hide();
            reason.gameObject.SetActive(false);
            question.gameObject.SetActive(false);
            var gap = TargetGap;
            float x;
            string? why;
            var start = left;
            if (model.Confirm is ConfirmStep step)
            {
                x = PutRight(cancel, step.Cancel.Id, step.Cancel.Label, right, middle, minimum: Units(RightEndDegrees));
                yes.Role = RoleOf(step.Yes.Role);
                // The confirmation of what can't be taken back is solid red; its first step was only outlined.
                yes.On = step.Yes.Role == PanelActionRole.Destructive;
                x = PutRight(yes, step.Yes.Id, step.Yes.Label, x, middle, available: step.Yes.Available);
                why = step.Yes.Reason ?? step.Question;
                if (why != null)
                {
                    GlazeText.SetLiteral(question, why);
                    var (questionLines, _) = GlazeText.Lay(question, x - start, QuestionLines);
                    question.transform.localPosition = new Vector3(start, middle + questionLines * GlazeText.LineHeight(question) / 2f, -0.0005f);
                    question.gameObject.SetActive(true);
                }
                return;
            }
            var actions = model.Actions;
            if (actions.Back != null) start = PutLeft(back, actions.Back, start, middle) + gap;
            if (actions.Destructive != null) start = PutLeft(destructive, actions.Destructive, start, middle) + gap;
            x = right;
            if (actions.Primary != null)
            {
                x = PutRight(primary, actions.Primary.Id, actions.Primary.Label, x, middle, available: actions.Primary.Available, minimum: Units(RightEndDegrees));
            }
            var secondaries = new[] { secondaryFirst, secondarySecond };
            for (var index = actions.Secondary.Count - 1; index >= 0; index--)
            {
                var action = actions.Secondary[index];
                var button = secondaries[index];
                button.Holds = action.Holds;
                x = PutRight(button, action.Id, action.Label, x, middle, available: action.Available);
            }
            why = actions.Primary?.Reason;
            if (why != null)
            {
                GlazeText.SetLiteral(reason, why);
                var (reasonLines, _) = GlazeText.Lay(reason, Mathf.Max(0.01f, x - start), QuestionLines);
                reason.transform.localPosition = new Vector3(start, middle + reasonLines * GlazeText.LineHeight(reason) / 2f, -0.0005f);
                reason.gameObject.SetActive(true);
            }
        }

        /// <summary>
        /// The list, one column or two, a page at a time: rows to press in cells of equal height, 12 mm
        /// apart; a line across the list, and in a single column a row that only says something, in a
        /// row of its own as tall as its words; and while there is more than one page, the pager in the
        /// body's bottom right cell.
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
            for (var index = 0; index < rowsOf.Count; index++)
            {
                var row = rowsOf[index];
                flow[index] = Flows(row, columns);
                if (row.Line) heightOf[index] = MeasureLine(row, area.width);
                else if (flow[index]) heightOf[index] = Measure(row, area.width, 0f);
                else
                {
                    anyCard |= row.Card;
                    cellHeight = Mathf.Max(cellHeight, Measure(row, CellTextWidth(row, cellWidth), GlazeButton.HeightOf(false)));
                }
            }
            if (anyCard) cellHeight = Mathf.Max(cellHeight, Units(CardMinimumDegrees));
            Paginate(rowsOf.Count, flow, heightOf, columns, area, cellHeight);
            page = Mathf.Clamp(page, 0, Pages - 1);

            var used = 0;
            var usedSides = 0;
            var usedLines = 0;
            var y = area.yMax;
            var afterFlow = true;
            if (pages.Count > 0)
            {
                foreach (var slot in pages[page])
                {
                    if (slot.Flow)
                    {
                        if (y < area.yMax) y -= Units(LineGapDegrees);
                        var item = rowsOf[slot.First];
                        if (item.Line) LayLine(LineLabel(usedLines++), item, area.xMin, area.width, y);
                        else ShowRow(RowButton(used++), item, area.xMin, area.width, y, heightOf[slot.First]);
                        y -= heightOf[slot.First];
                        afterFlow = true;
                        continue;
                    }
                    if (y < area.yMax) y -= afterFlow ? Units(LineGapDegrees) : gap;
                    for (var cell = 0; cell < slot.Count; cell++)
                    {
                        var row = rowsOf[slot.First + cell];
                        var cellLeft = area.xMin + cell * (cellWidth + gap);
                        var width = cellWidth;
                        if (row.Side != null)
                        {
                            var side = SideButton(usedSides++);
                            side.Holds = row.Side.Holds;
                            side.Available = row.Side.Available;
                            var sideWidth = side.Measure(row.Side.Label);
                            width = cellWidth - sideWidth - gap;
                            side.Show(row.Side.Label, new Vector2(cellLeft + cellWidth - sideWidth / 2f, y - cellHeight / 2f), sideWidth);
                            pressing[side] = (row.Side.Id, row.Key);
                        }
                        ShowRow(RowButton(used++), row, cellLeft, width, y, cellHeight);
                    }
                    y -= cellHeight;
                    afterFlow = false;
                }
            }
            HideRows(used, usedSides, usedLines);
            if (pages.Count > 1)
            {
                // At the body's bottom right, in its last cell, where every screen's pager stands.
                LayPager(page, pages.Count, EntryText.Page(page, pages.Count), area.xMax, area.yMin + GlazeButton.HeightOf(true) / 2f, external: false);
            }
        }

        /// <summary>A line, or in a single column a row that only says something: as tall as its words, in a row of its own.</summary>
        private static bool Flows(PanelRow row, int columns) => row.Line || (columns == 1 && !row.Pressable && row.Side == null);

        private void ShowRow(GlazeButton button, PanelRow row, float left, float width, float top, float height)
        {
            button.Role = row.Filter ? ButtonRole.Filter : ButtonRole.Choice;
            button.Static = !row.Pressable;
            button.On = row.Chosen;
            button.Available = row.Available;
            button.ShowRow(row, new Vector2(left + width / 2f, top - height / 2f), new Vector2(width, height), row.Pressable ? GlazeButton.HeightOf(false) : 0f);
            if (row.Pressable) pressing[button] = (row.Action!, row.Key);
            if (row.TitleIsData) data.Add(button.Label);
        }

        /// <summary>
        /// Splits the list into pages that fit <paramref name="area"/>: whole rows only. While there is
        /// more than one page, the body's bottom right cell is the pager's on every page.
        /// </summary>
        private void Paginate(int count, bool[] flow, float[] heightOf, int columns, Rect area, float cellHeight)
        {
            var gap = TargetGap;
            var lineGap = Units(LineGapDegrees);
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
                        if (flow[i])
                        {
                            var needed = (current.Count == 0 ? 0f : lineGap) + heightOf[i];
                            if (current.Count > 0 && y + needed > room + 1e-5f) break;
                            current.Add(new Slot(i, 1, true));
                            y += needed;
                            i++;
                            afterFlow = true;
                            continue;
                        }
                        var cells = 1;
                        while (cells < columns && i + cells < count && !flow[i + cells]) cells++;
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

        private void LayPager(int at, int count, string caption, float right, float middle, bool external)
        {
            var x = right;
            x = PutRight(next, external ? PanelModel.NextPart : "", EntryText.Next, x, middle, available: at < count - 1);
            x = PutRight(previous, external ? PanelModel.PreviousPart : "", EntryText.Previous, x, middle, available: at > 0);
            if (!external)
            {
                // The list's own pages turn here, not through the screen.
                pressing.Remove(next);
                pressing.Remove(previous);
            }
            GlazeText.SetLiteral(pageCaption, caption);
            var width = pageCaption.GetPreferredValues(pageCaption.text).x + 0.001f;
            GlazeText.Lay(pageCaption, width, 1);
            pageCaption.transform.localPosition = new Vector3(x - width, middle + GlazeText.LineHeight(pageCaption) / 2f, -0.0005f);
            pageCaption.gameObject.SetActive(true);
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
            return cellWidth - side.Measure(row.Side.Label) - TargetGap;
        }

        private float MeasureLine(PanelRow row, float width)
        {
            var label = LineLabel(0);
            Style(label, row);
            GlazeText.SetLiteral(label, row.Title);
            var (count, _) = GlazeText.Lay(label, width, Mathf.Max(1, row.TitleLines));
            return Mathf.Max(1, count) * GlazeText.LineHeight(label);
        }

        private float LayLine(TextMeshPro label, PanelRow row, float left, float width, float top)
        {
            Style(label, row);
            GlazeText.SetLiteral(label, row.Title);
            var (count, _) = GlazeText.Lay(label, width, Mathf.Max(1, row.TitleLines));
            label.transform.localPosition = new Vector3(left, top, -0.0005f);
            label.gameObject.SetActive(true);
            return Mathf.Max(1, count) * GlazeText.LineHeight(label);
        }

        private static void Style(TextMeshPro label, PanelRow row)
        {
            var type = row.Size == PanelTextSize.Title ? GlazeType.Title : row.Size == PanelTextSize.Caption ? GlazeType.Caption : GlazeType.Body;
            label.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(GlazeText.DegreesOf(type)));
            label.color = row.Tone.HasValue ? GlazeTokens.ColorOf(Glaze.Tone(row.Tone.Value).Foreground)
                : row.Size == PanelTextSize.Caption ? GlazeTokens.TextSecondary : GlazeTokens.Text;
        }

        private void HideRows(int usedRows, int usedSides, int usedLines)
        {
            for (var index = usedRows; index < rows.Count; index++) rows[index].Hide();
            for (var index = usedSides; index < sides.Count; index++) sides[index].Hide();
            for (var index = usedLines; index < lines.Count; index++) lines[index].gameObject.SetActive(false);
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

        private TextMeshPro LineLabel(int index)
        {
            while (lines.Count <= index) lines.Add(Text("Line " + lines.Count, GlazeType.Body, GlazeTokens.Text));
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
        private float PutRight(GlazeButton button, string id, string label, float right, float middle, bool available = true, float minimum = 0f)
        {
            var width = Mathf.Max(minimum, button.Measure(label));
            button.Available = available;
            button.Show(label, new Vector2(right - width / 2f, middle), width);
            if (id.Length > 0) pressing[button] = (id, null);
            return right - width - TargetGap;
        }

        private float PutLeft(GlazeButton button, PanelAction action, float left, float middle)
        {
            var width = button.Measure(action.Label);
            button.Holds = action.Holds;
            button.Available = action.Available;
            button.Show(action.Label, new Vector2(left + width / 2f, middle), width);
            pressing[button] = (action.Id, null);
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

        /// <summary>One row of the list on a page: an item as tall as its words, or up to a row's worth of cells.</summary>
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
