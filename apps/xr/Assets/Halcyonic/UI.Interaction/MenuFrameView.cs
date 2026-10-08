#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// One column of the menu's plane (ADR 0026), drawn from a <see cref="MenuFrame"/> or a
    /// <see cref="SidePanel"/>: its parts, each a shape of its own centred on its own transform, built
    /// in units of the plane's distance at the designed size, for <see cref="PlaneComposition"/> to lay
    /// on the plane. The subject's glass plate holds its title, drawn light, with a file's state pill on
    /// its top edge; the sections stand in a row of shapes, the chosen one lit; the content's glass holds
    /// the page's lines, why a prompt can't be taken now, its one source line and its footer, its light
    /// ending in its top padding so no row under it looks lit. A line's words wrap in the same room
    /// wherever the column stands, so <see cref="RowsOf(PageLine, float)"/> counts a page's rows before
    /// it is built, and a request shown in parts never loses a word between them. What a press raises
    /// comes back through <see cref="Acted"/>: a section's <see cref="MenuFrame.ChooseSection"/>, a
    /// line's action, or a prompt's id, each with its key.
    /// </summary>
    public sealed class MenuFrameView : MonoBehaviour
    {
        /// <summary>The rows a subject wraps to at most; its plate grows for the second.</summary>
        private const int SubjectRows = 2;

        /// <summary>The waiting dot before a section's word, amber.</summary>
        private const float DotDegrees = 0.42f;

        /// <summary>The most rows a side panel's fact value wraps to; the reason and source lines take two.</summary>
        private const int FactRows = 3;

        private const int NoteRows = 2;

        /// <summary>The most rows any words are laid in to count them.</summary>
        private const int Unlimited = 999;

        private const int Order = 20;

        private static TextMeshPro? measuring;

        private readonly List<Line> lines = new List<Line>();
        private readonly List<Section> sections = new List<Section>();
        private Transform subjectPart = null!;
        private Transform sectionsPart = null!;
        private Transform contentPart = null!;
        private Surface subjectPlate = null!;
        private TextMeshPro subjectTitle = null!;
        private StateBadgeView pill = null!;
        private Surface content = null!;
        private Surface footerLine = null!;
        private TextMeshPro reason = null!;
        private GlazeShimmer reasonShimmer = null!;
        private TextMeshPro source = null!;
        private FooterView footer = null!;
        private MenuFrame? frame;
        private SidePanel? side;

        /// <summary>
        /// A side panel's footer and reason while it stands in its frame's place: its frame's as it carries
        /// there (<see cref="Footer.InPlace"/>), Close details where Close stood and only what the side
        /// panel shows everything of; null in a column of its own.
        /// </summary>
        private Footer? carried;
        private string? carriedReason;
        private bool carriedReasonWaits;
        private MenuFrame? inPlaceOf;
        private float width;
        private float reserve;
        private float subjectHeight;
        private float contentHeight;
        private bool subjectCut;

        /// <summary>The subject plate's hold, once <see cref="EnableSubjectHold"/>: pressed when, and held past <see cref="GlazeButton.HoldSeconds"/>.</summary>
        private PointerTarget? subjectHold;
        private float subjectPressedAt = -1f;
        private bool subjectHolding;

        /// <summary>When the subject plate was last made to wait to settle, as a column newly on the plane does.</summary>
        private float subjectUnsettledAt = float.NegativeInfinity;

        /// <summary>The subject plate's cue while it can be held: the pointed frame, or the lit treatment from the press until let go.</summary>
        private Surface? subjectCue;
        private int subjectCuePainted = -1;

        /// <summary>The subject plate as last laid, its centre and size in the subject part's units: where the hold and its cue stand.</summary>
        private Vector3 subjectPlateAt;
        private Vector2 subjectPlateSize;

        /// <summary>Something was pressed: an action's id and its key.</summary>
        public event Action<string, string?>? Acted;

        /// <summary>
        /// The column's words are laid as the plane has them, after <see cref="Settle"/>: a frame's page or
        /// a side panel's part now shows whole. The director passes it on to the screen that counts what
        /// was read, as a request's parts and a cut answer's side panel are.
        /// </summary>
        public event Action<MenuFrameView>? Drawn;

        /// <summary>The subject plate was pressed, once <see cref="EnableSubjectHold"/>: whether a drag may start is judged now as well as when the hold matures.</summary>
        public event Action? SubjectPressed;

        /// <summary>
        /// The subject plate was held past <see cref="GlazeButton.HoldSeconds"/>, once
        /// <see cref="EnableSubjectHold"/>, at this point in the world, the hand still on it: the plane may be dragged by it.
        /// </summary>
        public event Action<Vector3>? SubjectHeld;

        /// <summary>While the subject is held: the point the hand holds moved, to here in the world.</summary>
        public event Action<Vector3>? SubjectDragged;

        /// <summary>The subject's press ended, let go or cancelled, whether or not its hold had matured: nothing pressed there counts any more.</summary>
        public event Action? SubjectLetGo;

        /// <summary>A held prompt, as Hold to talk, started.</summary>
        public event Action<Prompt>? HoldStarted;

        /// <summary>A held prompt's hold ended: let go on it (true), or dropped (false).</summary>
        public event Action<Prompt, bool>? HoldEnded;

        /// <summary>The column's parts, top to bottom: its subject, its sections where it has them, and its content.</summary>
        public IReadOnlyList<Transform> Parts { get; private set; } = Array.Empty<Transform>();

        /// <summary>Each part's height as it needs it, in units of the plane's distance at the designed size: a <see cref="PlaneColumn"/>'s.</summary>
        public IReadOnlyList<float> Heights { get; private set; } = Array.Empty<float>();

        /// <summary>The column's width, in units of the plane's distance at the designed size.</summary>
        public float Width => width;

        /// <summary>The footer, for the renders' checks.</summary>
        public FooterView Footer => footer;

        /// <summary>
        /// Where Hold to talk's one voice stands for this view's column, as its next laying draws it (ADR 0027):
        /// the held prompt's id and stage; null while the voice is idle or another column's.
        /// </summary>
        public (string Id, VoiceStage Stage)? Voice { get; set; }

        /// <summary>Labels showing text from outside, which may end in an ellipsis where it doesn't fit; Halcyonic's own words never do.</summary>
        public IEnumerable<TMP_Text> MayCut
        {
            get
            {
                foreach (var line in lines)
                {
                    if (line.MayCut && line.Words.gameObject.activeSelf) yield return line.Words;
                    if (line.FactMayCut && line.Fact.gameObject.activeSelf) yield return line.Fact;
                }
                if (subjectCut) yield return subjectTitle;
                if ((frame?.SourceIsData ?? side?.SourceIsData ?? false) && source.gameObject.activeSelf) yield return source;
            }
        }

        /// <summary>The content's glass, whose light must end above its first target.</summary>
        public Surface Content => content;

        /// <summary>Every place to press, a section's, a line's or a prompt's, for the renders' checks.</summary>
        public IEnumerable<GlazeButton> Targets
        {
            get
            {
                foreach (var section in sections)
                {
                    if (section.Button.gameObject.activeSelf) yield return section.Button;
                }
                foreach (var line in lines)
                {
                    if (line.Button != null && line.Button.gameObject.activeSelf) yield return line.Button;
                }
                foreach (var (_, button) in footer.Shown) yield return button;
            }
        }

        private static float U(float degrees) => GlazeTokens.Units(degrees);

        /// <summary>Between two targets on the plane, 12 mm, in units of its distance.</summary>
        private static float TargetGap => Glaze.TargetGapMeters / Glaze.Menu.PlaneMeters;

        private static float Inset => U(Glaze.Menu.InsetDegrees);

        /// <summary>A column's content width (<see cref="MenuPage.ContentWidth"/>).</summary>
        public static float ContentWidth(float columnDegrees) => MenuPage.ContentWidth(columnDegrees);

        /// <summary>The width each of two answers sharing a row gives its words, in a content <paramref name="content"/> wide (<see cref="MenuPage.HalfWidth"/>).</summary>
        private static float HalfWidth(float content) => (content + 2f * Inset - TargetGap) / 2f - 2f * Inset;

        /// <summary>
        /// How many rows <paramref name="line"/>'s words wrap to in full, its <see cref="PageLine.Rows"/>
        /// and <see cref="PageLine.FromRow"/> aside, in a column <paramref name="columnDegrees"/> wide:
        /// in the room its icon column, chip, fact and chevron leave, as the view lays it. Frames grow
        /// whole with larger text, so the count holds at both sizes.
        /// </summary>
        public static int RowsOf(PageLine line, float columnDegrees) => Count(Quoted(line), WordsRoom(line, ContentWidth(columnDegrees)));

        /// <summary>How many rows <paramref name="words"/> alone wrap to across a column's content, as a side panel's fact value or a source line.</summary>
        public static int RowsOf(string words, float columnDegrees) => Count(words, ContentWidth(columnDegrees));

        /// <summary>Whether an answer's words fit one row in half a column <paramref name="columnDegrees"/> wide, so it may share its row with the next.</summary>
        public static bool FitsHalf(PageLine answer, float columnDegrees) => Count(Quoted(answer), WordsRoom(answer, MenuPage.HalfWidth(columnDegrees))) == 1;

        /// <summary>
        /// How many rows <paramref name="shown"/> takes on a page in a column
        /// <paramref name="columnDegrees"/> wide, as the view lays it: each line its rows, at most its
        /// <see cref="PageLine.Rows"/>, a part exactly its rows, and two answers next to each other, or a
        /// line beside the next (<see cref="PageLine.BesideNext"/>), that each fit half the row in one
        /// row sharing it.
        /// </summary>
        public static int RowsOf(IReadOnlyList<PageLine> shown, float columnDegrees)
        {
            var content = ContentWidth(columnDegrees);
            var rows = 0;
            for (var at = 0; at < shown.Count; at++)
            {
                if (at + 1 < shown.Count && Pairs(shown[at], shown[at + 1], content))
                {
                    rows++;
                    at++;
                    continue;
                }
                var line = shown[at];
                rows += line.FromRow != null ? line.Rows : Mathf.Min(line.Rows, Count(Quoted(line), WordsRoom(line, content)));
            }
            return rows;
        }

        /// <summary>How many rows a subject's title takes in a column <paramref name="columnDegrees"/> wide: one, or two, the most it shows.</summary>
        public static int TitleRows(string subject, float columnDegrees) =>
            Mathf.Clamp(Count(Measuring(Glaze.Menu.TitleDegrees), subject, ContentWidth(columnDegrees)), 1, SubjectRows);

        /// <summary>
        /// How tall a column's subject is (<see cref="MenuPage.Subject"/>): its plate round its title in
        /// one row or two, and with <paramref name="pillRoom"/> half a pill above the plate and its lower
        /// half inside it. Every column beside a file keeps the room, and the composition takes the
        /// tallest, so the plates and titles stay level.
        /// </summary>
        public static float SubjectHeight(string subject, float columnDegrees, bool pillRoom) => MenuPage.Subject(TitleRows(subject, columnDegrees), pillRoom);

        /// <summary>
        /// The room a pill's lower half takes inside the plate above the title's padding: the title
        /// stands half a grid step under the pill, not a whole padding, so the menu and a file with the
        /// split header stay inside a Quest 3S's field.
        /// </summary>
        private static float Inner(float pillRoom) => pillRoom > 0f ? pillRoom - U(Glaze.Menu.SubjectPaddingDegrees) + U(Glaze.Menu.GridDegrees) / 2f : 0f;

        public static MenuFrameView Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MenuFrameView>();
            view.Build();
            return view;
        }

        /// <summary>
        /// Shows <paramref name="shown"/> in a column <paramref name="columnDegrees"/> wide, its subject
        /// <paramref name="subject"/> tall (<see cref="SubjectHeight"/>, the tallest of the composition's),
        /// keeping a pill's room when <paramref name="pillRoom"/>. Its content stands as tall as it needs
        /// until <see cref="Settle"/> stretches it.
        /// </summary>
        public void Show(MenuFrame shown, float columnDegrees, float subject, bool pillRoom)
        {
            frame = shown;
            side = null;
            inPlaceOf = null;
            carried = null;
            carriedReason = null;
            carriedReasonWaits = false;
            Begin(columnDegrees, subject, pillRoom || shown.Pill != null);
            subjectCut = shown.SubjectIsData;
            subjectTitle.color = shown.SubjectWaits ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground) : GlazeTokens.Text;
            LaySubject(shown.Subject, shown.Pill);
            LaySections(shown.Sections);
            contentHeight = LayContent(0f, Vector2.zero, 1f);
            Collect();
        }

        /// <summary>
        /// Shows a side panel in a column <paramref name="columnDegrees"/> wide: its subject, then its facts
        /// or lines, its source line and its own Close. Standing in a file's place, it wears the file's
        /// <paramref name="pill"/>, so it still reads as that task's. Standing in its frame's place, it
        /// carries <paramref name="inPlaceOf"/>'s footer as <see cref="Footer.InPlace"/> has it, Close
        /// details where Close stood, with its reason, so whatever the side panel shows everything of
        /// stays drawn and pressable, and Close details brings the page back for the rest.
        /// </summary>
        public void Show(SidePanel shown, float columnDegrees, float subject, bool pillRoom, StateBadge? pill = null, MenuFrame? inPlaceOf = null)
        {
            frame = null;
            side = shown;
            this.inPlaceOf = inPlaceOf;
            carried = inPlaceOf?.Footer.InPlace(shown);
            carriedReason = carried?.Reason;
            carriedReasonWaits = carried?.ReasonWaits == true;
            Begin(columnDegrees, subject, pillRoom || pill != null);
            subjectCut = shown.SubjectIsData;
            subjectTitle.color = GlazeTokens.Text;
            LaySubject(shown.Subject, pill);
            LaySections(Array.Empty<FrameSection>());
            contentHeight = LayContent(0f, Vector2.zero, 1f);
            Collect();
        }

        /// <summary>
        /// Lays the content again as the composition has it (<paramref name="placed"/>, at
        /// <paramref name="zoom"/>): stretched down so the columns end on one line, and each small fact
        /// at 15 dp where that reads as the eyes see it from its place on the plane, else at 18 dp
        /// (<see cref="PlaneComposition.SmallTextDegreesAt"/>), in the room kept for it either way.
        /// </summary>
        public void Settle(PlanePart placed, float zoom)
        {
            LayContent(placed.Height / zoom, new Vector2(placed.Right, placed.Up), zoom);
            Drawn?.Invoke(this);
        }

        /// <summary>The subject's part, whose plate's top edge the light line ends on.</summary>
        public Transform Subject => subjectPart;

        /// <summary>The subject plate's top edge, in its part's units from its centre: under a split header, below the pill's room.</summary>
        public float PlateTop => subjectHeight / 2f - reserve;

        /// <summary>The reason drawn as the page's last content line, if any: the frame's own, or, in its place, its frame's.</summary>
        public string? ReasonShown => reason.gameObject.activeSelf ? frame?.Reason ?? carriedReason : null;

        /// <summary>The side panel this column shows, or null for a frame.</summary>
        public SidePanel? Side => side;

        /// <summary>The frame this column shows, or null for a side panel.</summary>
        public MenuFrame? Frame => frame;

        /// <summary>The frame whose place this side panel stands in, carrying its footer; null beside its frame, or for a frame.</summary>
        public MenuFrame? InPlaceOf => inPlaceOf;

        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// Back on the plane from stepping aside: every button it shows waits to settle again, its prompts,
        /// rows and all, so nothing takes a press begun as it slides back, however like before it reads. A
        /// button that waits drops a press before its flash, its sound or a hold's timer starts. Newly on the
        /// plane, <paramref name="subjectToo"/>: its subject plate waits as well, its cue unlit.
        /// </summary>
        public void Unsettle(bool subjectToo = false)
        {
            Unsettles++;
            // Its hidden buttons too, as while the plane is folded away, so none takes a press once it shows.
            foreach (var button in GetComponentsInChildren<GlazeButton>(true)) button.Unsettle();
            if (subjectToo) subjectUnsettledAt = Time.unscaledTime;
        }

        /// <summary>The subject plate is still waiting to settle, taking no press, as a button that waits.</summary>
        public bool SubjectSettling => Time.unscaledTime - subjectUnsettledAt < GlazeButton.SettleSeconds;

        /// <summary>Every press on it begun at <paramref name="since"/> or later ends, as presses begun under a plane a drag moved.</summary>
        public void EndPressesSince(float since)
        {
            foreach (var button in GetComponentsInChildren<GlazeButton>()) button.EndPressSince(since);
        }

#if UNITY_EDITOR
        /// <summary>For the editor's renders: a press on <paramref name="action"/>, raised past its button to whatever hears this column.</summary>
        public void PressForRender(string action, string? key = null) => Acted?.Invoke(action, key);

        /// <summary>For the editor's renders: a hold starting on <paramref name="prompt"/>, raised past its button.</summary>
        public void HoldForRender(Prompt prompt) => HoldStarted?.Invoke(prompt);

        /// <summary>For the editor's renders: the subject plate pressed, raised past its own settling.</summary>
        public void PressSubjectForRender() => SubjectPressed?.Invoke();

        /// <summary>For the editor's renders: the subject plate held at <paramref name="point"/>, raised past its own timing.</summary>
        public void HoldSubjectForRender(Vector3 point) => SubjectHeld?.Invoke(point);

        /// <summary>For the editor's renders, whose clock barely moves: the subject plate settled long ago.</summary>
        public void SettleSubjectForRender() => subjectUnsettledAt = float.NegativeInfinity;
#endif

        /// <summary>How many times its buttons were made to wait to settle again, for the editor's renders.</summary>
        public int Unsettles { get; private set; }

        private void Begin(float columnDegrees, float subject, bool pillRoom)
        {
            gameObject.SetActive(true);
            width = PlaneComposition.Units(columnDegrees);
            reserve = pillRoom ? StateBadgeView.PillHeight / 2f : 0f;
            subjectHeight = subject;
        }

        private void Build()
        {
            subjectPart = Part("Subject");
            sectionsPart = Part("Sections");
            contentPart = Part("Content");
            subjectPlate = Surface.Create(subjectPart, "Plate", Order);
            subjectTitle = GlazeText.Create(subjectPart, "Subject", GlazeType.Subject, GlazeTokens.Text, TextAlignmentOptions.TopLeft, Order + 2);
            subjectTitle.rectTransform.pivot = new Vector2(0f, 1f);
            pill = StateBadgeView.Create(subjectPart, "State pill", Order + 3, pill: true);
            content = Surface.Create(contentPart, "Plate", Order);
            footerLine = Surface.Create(contentPart, "Footer line", Order + 1);
            reason = Words(contentPart, "Reason", Glaze.Menu.QuietText);
            reasonShimmer = GlazeShimmer.On(reason);
            source = Words(contentPart, "Source", Glaze.TextSecondary);
            footer = FooterView.Create(contentPart, "Footer", Order + 4);
            footer.Pressed += prompt => Acted?.Invoke(prompt.Id, null);
            footer.HoldStarted += prompt => HoldStarted?.Invoke(prompt);
            footer.HoldEnded += (prompt, letGo) => HoldEnded?.Invoke(prompt, letGo);
        }

        private Transform Part(string name)
        {
            var part = new GameObject(name).transform;
            part.SetParent(transform, false);
            return part;
        }

        private static TextMeshPro Words(Transform parent, string name, GlazeColor colour)
        {
            var label = GlazeText.Create(parent, name, GlazeType.Body, GlazeTokens.ColorOf(colour), TextAlignmentOptions.TopLeft, Order + 3);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            return label;
        }

        // ---------------------------------------------------------------------------------------------
        // Measuring: one hidden label lays words out as the view's own do.

        /// <summary>The hidden label that counts rows, at <paramref name="degrees"/>.</summary>
        private static TextMeshPro Measuring(float degrees)
        {
            if (measuring == null)
            {
                measuring = GlazeText.Create(null!, "Menu measure", GlazeType.Body, Color.white, TextAlignmentOptions.TopLeft, 0);
                measuring.gameObject.hideFlags = HideFlags.HideAndDontSave;
                measuring.gameObject.SetActive(false);
            }
            measuring.fontSize = GlazeTokens.FontSize(U(degrees));
            return measuring;
        }

        private static int Count(string words, float room) => Count(Measuring(Glaze.Menu.BodyDegrees), words, room);

        private static int Count(TextMeshPro label, string words, float room)
        {
            GlazeText.SetLiteral(label, words);
            return Mathf.Max(1, GlazeText.Lay(label, room, Unlimited).Lines);
        }

        /// <summary>How wide words are at the content's size: the room a small fact or chip keeps, wherever it stands.</summary>
        private static float BodyWidth(string words) => Measuring(Glaze.Menu.BodyDegrees).GetPreferredValues(LabelText.ForTextMeshPro(words)).x;

        /// <summary>The most of a line a fact from outside takes, as a long folder name; it ends in an ellipsis past it.</summary>
        private const float DataFactShare = 0.4f;

        /// <summary>A small fact's words as shown: a name from outside by <see cref="LabelText.Name"/>, which never leaves it empty.</summary>
        private static string FactWords(PageLine line) => line.FactIsData ? LabelText.Name(line.Fact) : line.Fact!;

        /// <summary>The room a line's small fact keeps at the content's size: its words', or for a fact from outside at most <see cref="DataFactShare"/> of the line.</summary>
        private static float FactRoom(PageLine line, float content)
        {
            var wide = BodyWidth(FactWords(line));
            return line.FactIsData ? Mathf.Min(wide, DataFactShare * content) : wide;
        }

        /// <summary>A chip's shape round its words at the content's size: a grid step each side.</summary>
        private static float ChipWidth(string words) => BodyWidth(words) + 2f * U(Glaze.Menu.GridDegrees);

        /// <summary>The room a line's words wrap in: the content line's, less its icon column, chip, fact and chevron, each kept at 18 dp.</summary>
        private static float WordsRoom(PageLine line, float content)
        {
            var grid = U(Glaze.Menu.GridDegrees);
            var room = content;
            if (line.Icon != null) room -= U(Glaze.Menu.IconColumnDegrees) + grid;
            if (line.Opens) room -= ChevronRoom;
            if (line.Fact != null) room -= FactRoom(line, content) + grid;
            if (line.Chip != null) room -= ChipWidth(line.Chip) + grid;
            return Mathf.Max(U(2f), room);
        }

        /// <summary>
        /// What a chevron takes at the right content line: its glyph, about four tenths of its em, which
        /// ends on the line, and a grid step before what stands left of it.
        /// </summary>
        private static float ChevronRoom => ChevronGlyph * U(Glaze.Menu.BodyDegrees) + U(Glaze.Menu.GridDegrees);

        private const float ChevronGlyph = 0.4f;

        /// <summary>A line's words as shown (<see cref="PageLine.Drawn"/>): a claim leans, quoted by its builder around the outside words alone.</summary>
        private static string Quoted(PageLine line) => line.Drawn;

        /// <summary>
        /// Two answers next to each other share a row where each fits half of it in one row, and so does a
        /// line that asks to (<see cref="PageLine.BesideNext"/>) with the next, as "Type my answer" beside
        /// a question's paging row.
        /// </summary>
        private static bool Pairs(PageLine first, PageLine second, float content)
        {
            if (!(first.Choice && second.Choice) && !first.BesideNext) return false;
            if (first.FromRow != null || second.FromRow != null) return false;
            var half = HalfWidth(content);
            return Count(Quoted(first), WordsRoom(first, half)) == 1 && Count(Quoted(second), WordsRoom(second, half)) == 1;
        }

        // ---------------------------------------------------------------------------------------------
        // The subject.

        /// <summary>
        /// Lets the subject plate be held to drag the plane by (ADR 0026): a press held there past
        /// <see cref="GlazeButton.HoldSeconds"/> raises <see cref="SubjectHeld"/>, then
        /// <see cref="SubjectDragged"/> as the hand moves, and <see cref="SubjectLetGo"/>; a shorter press
        /// does nothing. It is apart from every prompt, so it never starts Hold to talk.
        /// </summary>
        public void EnableSubjectHold()
        {
            if (subjectHold != null) return;
            var host = new GameObject("Subject hold");
            host.transform.SetParent(subjectPart, false);
            // Over the plate's glass, under its words: the one selection treatment, as a held prompt shows it.
            subjectCue = Surface.Create(subjectPart, "Subject cue", Order + 1);
            subjectHold = PointerTarget.Rectangle(host, Vector2.one, ray: true, poke: true);
            subjectHold.EnableDrag();
            subjectHold.Selected += () =>
            {
                // A hold that ends lets go, whatever ends it.
                if (subjectHolding) SubjectLetGo?.Invoke();
                subjectHolding = false;
                // Still settling, as a column newly on the plane: the press is dropped, its cue never lit.
                if (SubjectSettling)
                {
                    subjectPressedAt = -1f;
                    return;
                }
                subjectPressedAt = Time.unscaledTime;
                SubjectPressed?.Invoke();
            };
            subjectHold.Dragged += point =>
            {
                if (subjectHolding) SubjectDragged?.Invoke(point);
            };
            subjectHold.Released += _ =>
            {
                subjectPressedAt = -1f;
                subjectHolding = false;
                // Every press's end, a short one's too, so what took the press lets go of it.
                SubjectLetGo?.Invoke();
            };
            PlaceSubjectHold();
        }

        /// <summary>For the editor's renders: the subject plate's hold, to send it a finger's pointer events.</summary>
        public PointerTarget? SubjectHoldForRender => subjectHold;

        /// <summary>The hold and its cue over the subject plate as laid: the hold in front of the glass, the cue on it, both behind its words.</summary>
        private void PlaceSubjectHold()
        {
            if (subjectHold == null || subjectCue == null) return;
            subjectHold.Resize(subjectPlateSize);
            subjectHold.transform.localPosition = subjectPlateAt + new Vector3(0f, 0f, -U(0.02f));
            subjectCue.transform.localPosition = subjectPlateAt + new Vector3(0f, 0f, -U(0.01f));
            subjectCuePainted = -1;
        }

        /// <summary>For the editor's renders, which have no hands: paints the subject plate's cue as pointed at or pressed.</summary>
        public void PaintSubjectForRender(bool pointed, bool pressed)
        {
            subjectCuePainted = -1;
            PaintSubjectCue(pointed, pressed);
        }

        /// <summary>The subject plate's cue, as a held prompt's (ADR 0026): the pointed frame alone, or held, from the press until let go, the lit fill and frame; only when that changed.</summary>
        private void PaintSubjectCue(bool pointed, bool pressed)
        {
            if (subjectCue == null) return;
            var away = FocusGuard.InputSuspended;
            var state = (pressed ? 1 : 0) | (pointed ? 2 : 0) | (away ? 4 : 0);
            if (state == subjectCuePainted) return;
            subjectCuePainted = state;
            var radius = U(Glaze.Menu.RadiusDegrees);
            if (away || (!pressed && !pointed))
            {
                subjectCue.Draw(Vector2.zero, 0f, Color.clear);
                subjectCue.Selection = SurfaceSelection.None;
            }
            else if (pressed)
            {
                subjectCue.Draw(subjectPlateSize, radius, new Color(1f, 1f, 1f, Glaze.Menu.LitFillOpacity), new Color(1f, 1f, 1f, Glaze.Menu.LitFrameOpacity),
                    U(Glaze.Menu.LitFrameDegrees));
                subjectCue.Selection = SurfaceSelection.Lit;
            }
            else
            {
                subjectCue.Draw(subjectPlateSize, radius, Color.clear, new Color(1f, 1f, 1f, Glaze.Menu.PointedFrameOpacity), U(Glaze.Menu.PointedFrameDegrees));
                subjectCue.Selection = SurfaceSelection.Pointed;
            }
        }

        private void Update()
        {
            if (subjectHold == null) return;
            PaintSubjectCue(subjectHold.Hovered, subjectPressedAt >= 0f);
            if (subjectHolding || subjectPressedAt < 0f) return;
            // As a held prompt: the hand that pressed must still be on the plate, else the press lapses.
            if (!subjectHold.HandHovered)
            {
                subjectPressedAt = -1f;
                return;
            }
            if (Time.unscaledTime - subjectPressedAt < GlazeButton.HoldSeconds) return;
            if (!(subjectHold.HeldPoint is Vector3 point)) return;
            subjectHolding = true;
            SubjectHeld?.Invoke(point);
        }

        private void LaySubject(string words, StateBadge? badge)
        {
            var padding = U(Glaze.Menu.PaddingDegrees);
            var left = -width / 2f + padding;
            GlazeText.SetLiteral(subjectTitle, words);
            var (rows, _) = GlazeText.Lay(subjectTitle, width - 2f * padding, SubjectRows);
            var titleHeight = Mathf.Max(1, rows) * GlazeText.LineHeight(subjectTitle);
            var top = subjectHeight / 2f;
            var plate = subjectHeight - reserve;
            subjectPlate.DrawGlass(new Vector2(width, plate));
            subjectPlate.transform.localPosition = new Vector3(0f, top - reserve - plate / 2f, 0f);
            subjectPlateAt = new Vector3(0f, top - reserve - plate / 2f, 0f);
            subjectPlateSize = new Vector2(width, plate);
            PlaceSubjectHold();
            // In the plate, under the pill's lower half, centred.
            var inner = Inner(reserve);
            subjectTitle.transform.localPosition = new Vector3(left, top - reserve - inner - (plate - inner - titleHeight) / 2f, -U(0.05f));
            pill.gameObject.SetActive(badge != null);
            if (badge != null)
            {
                pill.Show(badge);
                pill.transform.localPosition = new Vector3(left + pill.Width / 2f, top - reserve, -U(0.06f));
            }
        }

        // ---------------------------------------------------------------------------------------------
        // The sections: a row of shapes 12 mm apart, the chosen one lit, an amber dot on one that waits.

        private void LaySections(IReadOnlyList<FrameSection> shown)
        {
            while (sections.Count < shown.Count) sections.Add(NewSection(sections.Count));
            var height = GlazeButton.HeightOf(true);
            var each = shown.Count == 0 ? 0f : (width - TargetGap * (shown.Count - 1)) / shown.Count;
            var grid = U(Glaze.Menu.GridDegrees);
            for (var index = 0; index < sections.Count; index++)
            {
                var section = sections[index];
                if (index >= shown.Count)
                {
                    section.Hide();
                    continue;
                }
                var model = shown[index];
                section.Model = model;
                var x = -width / 2f + each / 2f + index * (each + TargetGap);
                section.Shape.gameObject.SetActive(true);
                section.Shape.Draw(new Vector2(each, height), U(Glaze.Menu.RadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel, Glaze.Menu.GlassOpacity),
                    new Color(1f, 1f, 1f, Glaze.Menu.HairlineOpacity), U(Glaze.Menu.HairlineDegrees));
                section.Shape.transform.localPosition = new Vector3(x, 0f, 0f);
                section.Button.On = model.Chosen;
                section.Button.Available = model.Reached;
                section.Button.ShowArea(new Vector2(x, 0f), new Vector2(each, height));

                // Only the chosen section is drawn heavier; a step not reached yet is quiet, and never lit.
                section.Words.gameObject.SetActive(true);
                GlazeText.SetStrong(section.Words, model.Chosen);
                section.Words.color = model.Chosen ? GlazeTokens.Text : model.Reached ? GlazeTokens.ColorOf(Glaze.Menu.QuietText) : GlazeTokens.ColorOf(Glaze.TextDisabled);
                GlazeText.SetLiteral(section.Words, model.Words);
                var dot = model.Waits ? U(DotDegrees) + grid : 0f;
                var (_, wordsWidth) = GlazeText.Lay(section.Words, each - dot - 2f * grid, 1);
                var start = x - (wordsWidth + dot) / 2f;
                section.Words.transform.localPosition = new Vector3(start + dot, 0f, -U(0.05f));
                section.Dot.gameObject.SetActive(model.Waits);
                if (model.Waits)
                {
                    var size = U(DotDegrees);
                    section.Dot.Draw(Vector2.one * size, size / 2f, GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Strong));
                    section.Dot.transform.localPosition = new Vector3(start + size / 2f, 0f, -U(0.05f));
                }
            }
            sectionsPart.gameObject.SetActive(shown.Count > 0);
        }

        private Section NewSection(int index)
        {
            var section = new Section(sectionsPart, "Section " + index);
            section.Button.Pressed += () =>
            {
                if (section.Model != null) Acted?.Invoke(MenuFrame.ChooseSection, section.Model.Key);
            };
            return section;
        }

        // ---------------------------------------------------------------------------------------------
        // The content.

        /// <summary>
        /// Lays the content's glass and everything on it, at least <paramref name="height"/> tall, its
        /// centre <paramref name="center"/> from the composition's at <paramref name="zoom"/>; returns
        /// how tall it needs to be.
        /// </summary>
        private float LayContent(float height, Vector2 center, float zoom)
        {
            var padding = U(Glaze.Menu.PaddingDegrees);
            var grid = U(Glaze.Menu.GridDegrees);
            var group = U(Glaze.Menu.GroupGapDegrees);
            var left = -width / 2f + padding;
            var right = width / 2f - padding;
            var models = frame?.Lines ?? side!.Lines;
            var facts = side?.Facts ?? Array.Empty<SideFact>();

            // How tall it needs to be first, from a top at 0, then laid from its own top.
            // The last target and the prompts stand 12 mm apart, the footer's line between them.
            var needed = -LayFrom(0f) + TargetGap + FooterView.Height + U(1f);
            var tall = Mathf.Max(height, needed);
            var top = tall / 2f;
            LayFrom(top);

            // Lays everything above the footer from the content's top at the given height; returns its foot.
            float LayFrom(float start)
            {
                while (lines.Count < models.Count + facts.Count) lines.Add(NewLine(lines.Count));
                var y = start - padding;
                var index = 0;
                foreach (var fact in facts)
                {
                    if (index > 0) y -= group;
                    y = LayFact(lines[index++], fact, left, right, y);
                }
                for (var line = 0; line < models.Count; line++)
                {
                    var model = models[line];
                    if (line > 0) y -= models[line - 1].Action != null && model.Action != null ? TargetGap : grid;
                    if (line + 1 < models.Count && Pairs(model, models[line + 1], right - left))
                    {
                        // Two answers share the row, each in half of it.
                        var half = HalfWidth(right - left);
                        var foot = LayLine(lines[index++], model, left, left + half, y, center, zoom);
                        y = Mathf.Min(foot, LayLine(lines[index++], models[++line], left + half + 2f * Inset + TargetGap, right, y, center, zoom));
                    }
                    else y = LayLine(lines[index++], model, left, right, y, center, zoom);
                }
                for (; index < lines.Count; index++) lines[index].Hide();
                var why = frame?.Reason ?? carriedReason;
                reason.gameObject.SetActive(why != null);
                // A reason that says what is under way, as "Sent. Waiting for the agent…", shimmers.
                reasonShimmer.Waits = why != null && (frame?.Reason != null ? frame.ReasonWaits : carriedReasonWaits);
                if (why != null) y = Note(reason, why, left, right, y - group);
                var from = frame?.Source ?? side?.Source;
                source.gameObject.SetActive(from != null);
                if (from != null) y = Note(source, from, left, right, y - group);
                return y;
            }

            var footerMiddle = -top + U(1f) + FooterView.Height / 2f;
            // Hold to talk shows where the voice stands, the same in every column (Footer.Voiced).
            var prompts = frame?.Footer ?? carried ?? SidePanel.Footer;
            footer.Show(Voice is (string held, VoiceStage stage) ? prompts.Voiced(held, stage) : prompts, left, right, footerMiddle);
            // A faint line between the page and its footer, too faint to mark anything.
            footerLine.Draw(new Vector2(width - 2f * U(1f), U(0.05f)), 0f, new Color(1f, 1f, 1f, 0.09f));
            footerLine.transform.localPosition = new Vector3(0f, footerMiddle + FooterView.Height / 2f + TargetGap / 2f, -U(0.02f));
            // The light ends within the top padding, above the first row: a row looks lit only when chosen.
            content.DrawGlass(new Vector2(width, tall), padding);
            content.transform.localPosition = Vector3.zero;
            return needed;
        }

        private float LayFact(Line line, SideFact fact, float left, float right, float top)
        {
            line.ShowFact(fact.ValueIsData);
            var y = Note(line.Name, fact.Name, left, right, top, 1) - U(Glaze.Menu.LabelToValueDegrees);
            return Note(line.Words, fact.ValueIsData ? LabelText.Name(fact.Value) : fact.Value, left, right, y, FactRows);
        }

        /// <summary>
        /// Lays a page line from <paramref name="top"/> between the content lines at
        /// <paramref name="left"/> and <paramref name="right"/>; returns its foot.
        /// </summary>
        private float LayLine(Line line, PageLine model, float left, float right, float top, Vector2 center, float zoom)
        {
            var grid = U(Glaze.Menu.GridDegrees);
            var role = model.Action == null ? (ButtonRole?)null : model.Choice ? ButtonRole.Answer : ButtonRole.Row;
            line.Show(model, role);
            var lineHeight = GlazeText.LineHeight(line.Words);
            var rows = Wrap(line.Words, model, WordsRoom(model, right - left));
            var height = role == null ? rows * lineHeight : Mathf.Max(GlazeButton.HeightOf(true), rows * lineHeight + 2f * grid);
            var middle = top - height / 2f;
            // A line reused from one that took a press keeps its target hidden unless this one takes a press too.
            if (role != null && line.Button != null)
            {
                line.Button.On = model.Chosen;
                line.Button.Available = model.Available;
                line.Button.ShowArea(new Vector2((left + right) / 2f, middle), new Vector2(right - left + 2f * Inset, height));
            }

            // The right content line: the chevron, then the fact, then the chip, each in the room kept for it at 18 dp.
            var end = right;
            if (model.Opens)
            {
                // The chevron's glyph ends on the content line; its em reaches past it.
                line.Chevron.gameObject.SetActive(true);
                line.Chevron.color = model.Chosen ? GlazeTokens.Text : GlazeTokens.TextSecondary;
                GlazeIcons.Show(line.Chevron, GlazeIcon.OpensMore);
                line.Chevron.transform.localPosition = new Vector3(end - ChevronGlyph * U(Glaze.Menu.BodyDegrees) / 2f, middle, -U(0.05f));
                end -= ChevronRoom;
            }
            // The tone stands on the icon, and on the fact only where the row waits, in amber; any other
            // fact is in the secondary colour, so a list's facts keep one colour. The words stay the
            // text's own where an icon or a fact carries the tone.
            var carried = model.Icon != null || model.Fact != null;
            if (model.Fact != null)
            {
                var kept = FactRoom(model, right - left);
                Small(line.Fact, FactWords(model), end, middle, center, zoom, ColourOf(model.Tone == LineTone.Waiting ? LineTone.Waiting : LineTone.Secondary, model.Available), kept);
                line.FactMayCut = model.FactIsData;
                end -= kept + grid;
            }
            if (model.Chip != null)
            {
                var kept = ChipWidth(model.Chip);
                var words = Small(line.Chip, model.Chip, end - grid, middle, center, zoom, GlazeTokens.TextSecondary);
                var shape = new Vector2(words.x + 2f * grid, words.y + grid);
                line.ChipShape.gameObject.SetActive(true);
                line.ChipShape.Draw(shape, shape.y / 2f, Color.clear, new Color(1f, 1f, 1f, 2f * Glaze.Menu.HairlineOpacity), U(Glaze.Menu.HairlineDegrees));
                line.ChipShape.transform.localPosition = new Vector3(end - shape.x / 2f, middle, -U(0.04f));
                end -= kept + grid;
            }

            // The left content line: the icon in its column, then the words.
            var wordsLeft = left;
            if (model.Icon is GlazeIcon icon)
            {
                line.Icon.gameObject.SetActive(true);
                GlazeIcons.Show(line.Icon, icon);
                line.Icon.color = ColourOf(model.Tone, model.Available);
                line.Icon.transform.localPosition = new Vector3(left + U(Glaze.Menu.IconColumnDegrees) / 2f, middle, -U(0.05f));
                wordsLeft = left + U(Glaze.Menu.IconColumnDegrees) + grid;
            }
            line.Words.color = ColourOf(carried && model.Tone != LineTone.Secondary ? LineTone.Primary : model.Tone, model.Available);
            line.Lean = model.Claim;
            // A line that says what is under way, as "Sending…", shimmers while it shows (ADR 0027).
            line.Shimmer.Waits = model.Waits;
            line.MayCut = model.WordsAreData;
            line.Words.transform.localPosition = new Vector3(wordsLeft, middle + rows * lineHeight / 2f, -U(0.05f));
            return top - height;
        }

        /// <summary>
        /// Lays a line's words in <paramref name="room"/>: from their start, ending in an ellipsis past
        /// its rows, or, for a part, exactly its rows of the words wrapped whole. Returns the rows shown.
        /// </summary>
        private static int Wrap(TextMeshPro label, PageLine model, float room)
        {
            var text = LabelText.ForTextMeshPro(Quoted(model));
            label.richText = false;
            label.parseCtrlCharacters = true;
            label.text = text;
            if (model.FromRow is int from)
            {
                GlazeText.Lay(label, room, Unlimited);
                var info = label.textInfo;
                var count = Mathf.Max(1, info.lineCount);
                var first = Mathf.Min(from, count - 1);
                var last = Mathf.Min(from + model.Rows, count);
                var start = info.characterInfo[info.lineInfo[first].firstCharacterIndex].index;
                var end = last < count ? info.characterInfo[info.lineInfo[last].firstCharacterIndex].index : text.Length;
                label.text = text.Substring(start, end - start);
            }
            return Mathf.Max(1, GlazeText.Lay(label, room, model.Rows).Lines);
        }

        /// <summary>
        /// Lays small words ending at <paramref name="end"/>, centred on <paramref name="middle"/>: 15 dp
        /// where that reads as the eyes see it from its place on the plane, else 18 dp, at most
        /// <paramref name="most"/> wide, past which they end in an ellipsis. Returns their size.
        /// </summary>
        private static Vector2 Small(TextMeshPro label, string words, float end, float middle, Vector2 center, float zoom, Color colour, float most = float.MaxValue)
        {
            var degrees = PlaneComposition.SmallTextDegreesAt(center.x + end * zoom, center.y + middle * zoom);
            label.gameObject.SetActive(true);
            label.fontSize = GlazeTokens.FontSize(U(degrees));
            label.color = colour;
            GlazeText.SetLiteral(label, words);
            var wide = Mathf.Min(label.GetPreferredValues(label.text).x, most);
            GlazeText.Lay(label, wide + U(0.1f), 1);
            var tall = GlazeText.LineHeight(label);
            label.transform.localPosition = new Vector3(end - wide, middle + tall / 2f, -U(0.05f));
            return new Vector2(wide, tall);
        }

        private static Color ColourOf(LineTone tone, bool available)
        {
            if (!available) return GlazeTokens.ColorOf(Glaze.Menu.QuietText);
            return tone switch
            {
                LineTone.Secondary => GlazeTokens.TextSecondary,
                LineTone.Waiting => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground),
                LineTone.Good => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Success).Foreground),
                LineTone.Problem => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Failure).Foreground),
                _ => GlazeTokens.Text,
            };
        }

        /// <summary>Lays words from <paramref name="top"/> down, left-aligned, in at most <paramref name="rows"/> rows; returns their foot.</summary>
        private static float Note(TextMeshPro label, string words, float left, float right, float top, int rows = NoteRows)
        {
            label.gameObject.SetActive(true);
            GlazeText.SetLiteral(label, words);
            var (count, _) = GlazeText.Lay(label, right - left, rows);
            label.transform.localPosition = new Vector3(left, top, -U(0.05f));
            return top - Mathf.Max(1, count) * GlazeText.LineHeight(label);
        }

        private void Collect()
        {
            var parts = new List<Transform> { subjectPart };
            var heights = new List<float> { subjectHeight };
            if (sectionsPart.gameObject.activeSelf)
            {
                parts.Add(sectionsPart);
                heights.Add(GlazeButton.HeightOf(true));
            }
            parts.Add(contentPart);
            heights.Add(contentHeight);
            Parts = parts;
            Heights = heights;
        }

        private Line NewLine(int index)
        {
            var line = new Line(contentPart, "Line " + index);
            line.Pressed += (action, key) => Acted?.Invoke(action, key);
            return line;
        }

        /// <summary>A section's shape, its place to press, its words and its waiting dot.</summary>
        private sealed class Section
        {
            public Section(Transform parent, string name)
            {
                Shape = Surface.Create(parent, name, Order);
                Button = GlazeButton.Create(parent, name + " target", ButtonRole.Row, compact: true, order: Order + 1);
                Words = GlazeText.Create(parent, name + " words", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, Order + 3);
                Words.rectTransform.pivot = new Vector2(0f, 0.5f);
                Words.textWrappingMode = TextWrappingModes.NoWrap;
                Dot = Surface.Create(parent, name + " waiting dot", Order + 3);
            }

            public Surface Shape { get; }

            public GlazeButton Button { get; }

            public TextMeshPro Words { get; }

            public Surface Dot { get; }

            public FrameSection? Model { get; set; }

            public void Hide()
            {
                Model = null;
                Shape.gameObject.SetActive(false);
                Button.Hide();
                Words.gameObject.SetActive(false);
                Dot.gameObject.SetActive(false);
            }
        }

        /// <summary>A page line's labels and its place to press, made once and shown again.</summary>
        private sealed class Line
        {
            private readonly Transform parent;
            private readonly string name;
            private PageLine? model;
            private bool lean;

            public Line(Transform parent, string name)
            {
                this.parent = parent;
                this.name = name;
                Icon = GlazeIcons.Create(parent, name + " icon", Glaze.Menu.BodyDegrees, GlazeTokens.Text, Order + 3);
                Words = Label(name + " words", GlazeTokens.Text);
                Words.OnPreRenderText += info =>
                {
                    if (lean) GlazeText.Lean(info);
                };
                Shimmer = GlazeShimmer.On(Words);
                Name = Label(name + " name", GlazeTokens.TextSecondary);
                Fact = Label(name + " fact", GlazeTokens.TextSecondary);
                Fact.textWrappingMode = TextWrappingModes.NoWrap;
                Chip = Label(name + " chip", GlazeTokens.TextSecondary);
                Chip.textWrappingMode = TextWrappingModes.NoWrap;
                ChipShape = Surface.Create(parent, name + " chip shape", Order + 2);
                Chevron = GlazeIcons.Create(parent, name + " chevron", Glaze.Menu.BodyDegrees, GlazeTokens.TextSecondary, Order + 3);
                Hide();
            }

            public event Action<string, string?>? Pressed;

            public TextMeshPro Icon { get; }

            /// <summary>Its words' shimmer, while the line says what is under way.</summary>
            public GlazeShimmer Shimmer { get; }

            public TextMeshPro Words { get; }

            public TextMeshPro Name { get; }

            public TextMeshPro Fact { get; }

            public TextMeshPro Chip { get; }

            public Surface ChipShape { get; }

            public TextMeshPro Chevron { get; }

            public GlazeButton? Button { get; private set; }

            /// <summary>The words are text from outside, which may end in an ellipsis.</summary>
            public bool MayCut { get; set; }

            /// <summary>The small fact is text from outside, which may end in an ellipsis past its share of the line.</summary>
            public bool FactMayCut { get; set; }

            /// <summary>The words lean, as the agent's own do; a change draws them again.</summary>
            public bool Lean
            {
                set
                {
                    if (lean == value) return;
                    lean = value;
                    Words.havePropertiesChanged = true;
                }
            }

            private TextMeshPro Label(string label, Color colour)
            {
                var text = GlazeText.Create(parent, label, GlazeType.Body, colour, TextAlignmentOptions.TopLeft, Order + 3);
                text.rectTransform.pivot = new Vector2(0f, 1f);
                return text;
            }

            /// <summary>Shows the line for <paramref name="shown"/>, with a place to press in <paramref name="role"/>, or none, and nothing else yet.</summary>
            public void Show(PageLine shown, ButtonRole? role)
            {
                Hide();
                model = shown;
                Words.gameObject.SetActive(true);
                if (role is not ButtonRole pressing) return;
                if (Button == null)
                {
                    Button = GlazeButton.Create(parent, name + " target", pressing, compact: true, order: Order + 1);
                    Button.Pressed += () =>
                    {
                        if (model?.Action is string action) Pressed?.Invoke(action, model.Key);
                    };
                }
                Button.Role = pressing;
            }

            /// <summary>Shows a side panel's fact: its name over its value, both at the content's size.</summary>
            public void ShowFact(bool valueIsData)
            {
                Hide();
                MayCut = valueIsData;
                Name.gameObject.SetActive(true);
                Words.gameObject.SetActive(true);
                Words.color = GlazeTokens.Text;
                Lean = false;
            }

            public void Hide()
            {
                model = null;
                MayCut = false;
                FactMayCut = false;
                Words.gameObject.SetActive(false);
                Name.gameObject.SetActive(false);
                Icon.gameObject.SetActive(false);
                Fact.gameObject.SetActive(false);
                Chip.gameObject.SetActive(false);
                ChipShape.gameObject.SetActive(false);
                Chevron.gameObject.SetActive(false);
                Button?.Hide();
            }
        }
    }
}
