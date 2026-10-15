#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
        private static TextMeshPro? measuringHero;

        private readonly List<Line> lines = new List<Line>();
        private readonly List<Section> sections = new List<Section>();
        private Transform subjectPart = null!;
        private Transform sectionsPart = null!;
        private Transform contentPart = null!;
        private Surface subjectPlate = null!;
        private TextMeshPro subjectTitle = null!;
        private TextMeshPro identityCaption = null!;
        private StateBadgeView pill = null!;
        private Surface content = null!;
        private Surface footerLine = null!;
        private TextMeshPro reason = null!;
        private GlazeShimmer reasonShimmer = null!;
        private TextMeshPro source = null!;

        /// <summary>The grounds under outside text in the subject, and in the source line.</summary>
        private readonly List<Surface> subjectGrounds = new List<Surface>();
        private readonly List<Surface> sourceGrounds = new List<Surface>();

        /// <summary>How many grounds the view has made, and how many it had when it last gathered its parts to fade.</summary>
        private int GroundsMade => subjectGrounds.Count + sourceGrounds.Count + lines.Sum(line => line.WordGrounds.Count + line.FactGrounds.Count);

        private int groundsGathered;
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
        private OrganizationControlsView controls = null!;
        public bool OrganizationControls { get; set; }
        public event Action? CompactPressed;
        public event Action? MovePressed;

        /// <summary>
        /// The column opening or closing on the plane (ADR 0027), counted on the plane's clock: the one value its
        /// parts' fade, its light line, its buttons, its subject plate and the plane's own refusal all read.
        /// </summary>
        private Motion motion;

        /// <summary>The seconds counted so far into the opening or closing.</summary>
        private float movedFor;

        /// <summary>The light line draws from the column's character as it opens, and its parts wait for it.</summary>
        private bool drawsFromLine;

        /// <summary>The light line meets the column's bottom edge, as over a desk, so its parts arrive from the bottom up.</summary>
        private bool fromBottom;

        /// <summary>A <see cref="Settle"/> came while the column opened: it reports drawn as the opening ends.</summary>
        private bool drawnOwed;

        /// <summary>Every shape and every label on each part, gathered as the column is laid, to fade each part by itself without allocating.</summary>
        private readonly List<(Surface Shape, int Part)> fadingShapes = new List<(Surface, int)>();

        private readonly List<(TMP_Text Words, int Part)> fadingWords = new List<(TMP_Text, int)>();
        private readonly List<Surface> gatheringShapes = new List<Surface>();
        private readonly List<TMP_Text> gatheringWords = new List<TMP_Text>();
        private readonly List<GlazeButton> gatheringButtons = new List<GlazeButton>();

        /// <summary>Every button on the column asks this before taking a press: none while the column opens or closes.</summary>
        private Func<bool> still = null!;

        /// <summary>When the subject plate was last made to wait to settle, as a column newly on the plane does.</summary>
        private float subjectUnsettledAt = float.NegativeInfinity;

        public bool SpatialWorkspace { get; set; }
        public bool ExternalPlaces { get; set; }
        private Transform unifiedPart = null!;
        private Surface unifiedPlate = null!;
        private Surface unifiedBacking = null!;
        private float UnifiedSections => sectionsPart.gameObject.activeSelf ? GlazeButton.HeightOf(true) : 0f;
        private float UnifiedGap => U(0.35f);

        private Vector3 subjectPlateAt;

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

        /// <summary>
        /// Every page line shown, for the renders' checks of words from outside: its model, the label of
        /// its words, its icon where one shows, and the grounds drawn under its words and its fact.
        /// </summary>
        public IEnumerable<(PageLine Model, TextMeshPro Words, TextMeshPro? Icon, TextMeshPro? Fact, TextMeshPro? Chip, IReadOnlyList<Surface> Grounds)> ShownLines
        {
            get
            {
                foreach (var line in lines)
                {
                    if (line.Model == null || !line.Words.gameObject.activeSelf) continue;
                    yield return (line.Model, line.Words, line.Icon.gameObject.activeSelf ? line.Icon : null,
                        line.Fact.gameObject.activeSelf ? line.Fact : null, line.Chip.gameObject.activeSelf ? line.Chip : null, line.Grounds.ToList());
                }
            }
        }

        /// <summary>The subject's title and the grounds under outside text in it, for the renders' checks.</summary>
        public Vector2 SubjectSize => new Vector2(width, subjectHeight);

        public (TextMeshPro Title, IReadOnlyList<Surface> Grounds) SubjectShown => (subjectTitle, subjectGrounds.Where(ground => ground.gameObject.activeSelf).ToList());

        /// <summary>The source line's label while it shows, for the renders' checks.</summary>
        public TextMeshPro? SourceShown => source.gameObject.activeSelf ? source : null;

        /// <summary>The grounds under outside text in the source line, for the renders' checks.</summary>
        public IReadOnlyList<Surface> SourceGrounds => sourceGrounds.Where(ground => ground.gameObject.activeSelf).ToList();

        /// <summary>Every prompt's and section's label, which hold Halcyonic's words alone, for the renders' checks.</summary>
        public IEnumerable<TMP_Text> OursOnly =>
            sections.Where(section => section.Words.gameObject.activeSelf).Select(section => (TMP_Text)section.Words)
                .Concat(footer.GetComponentsInChildren<TMP_Text>(false)).Concat(controls.GetComponentsInChildren<TMP_Text>(false));

        /// <summary>The content's glass, whose light must end above its first target.</summary>
        public Surface Content => content;

        /// <summary>Every place to press, a section's, a line's or a prompt's, for the renders' checks.</summary>
        public IEnumerable<GlazeButton> Targets
        {
            get
            {
                if (controls.gameObject.activeSelf) { yield return controls.Move; yield return controls.Toggle; }
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
        public static int RowsOf(PageLine line, float columnDegrees) => CountLine(line, WordsRoom(line, ContentWidth(columnDegrees)));

        /// <summary>How many rows <paramref name="words"/> alone wrap to across a column's content, as a side panel's fact value or a source line.</summary>
        public static int RowsOf(string words, float columnDegrees) => Count(words, ContentWidth(columnDegrees));

        /// <summary>Whether an answer's words fit one row in half a column <paramref name="columnDegrees"/> wide, so it may share its row with the next.</summary>
        public static bool FitsHalf(PageLine answer, float columnDegrees) => CountLine(answer, WordsRoom(answer, MenuPage.HalfWidth(columnDegrees))) == 1;

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
                rows += line.FromRow != null ? line.Rows : Mathf.Min(line.Rows, CountLine(line, WordsRoom(line, content)));
            }
            return rows;
        }

        /// <summary>How many rows a subject's title takes in a column <paramref name="columnDegrees"/> wide: one, or two, the most it shows.</summary>
        public static int TitleRows(string subject, float columnDegrees, bool organizationControls = true) =>
            Mathf.Clamp(Count(Measuring(Glaze.Menu.TitleDegrees), subject, organizationControls ? TitleWidth(columnDegrees) : ContentWidth(columnDegrees)), 1, SubjectRows);

        /// <summary>
        /// How tall a column's subject is (<see cref="MenuPage.Subject"/>): its plate round its title in
        /// one row or two, and with <paramref name="pillRoom"/> half a pill above the plate and its lower
        /// half inside it. Every column beside a file keeps the room, and the composition takes the
        /// tallest, so the plates and titles stay level.
        /// </summary>
        public static float SubjectHeight(string subject, float columnDegrees, bool pillRoom, bool organizationControls = true) => MenuPage.Subject(TitleRows(subject, columnDegrees, organizationControls), pillRoom);

        private static TextMeshPro HeroMeasure()
        {
            if (measuringHero == null)
            {
                measuringHero = GlazeText.Create(null!, "Work title measure", GlazeType.Hero, Color.white, TextAlignmentOptions.TopLeft, 0);
                measuringHero.gameObject.hideFlags = HideFlags.HideAndDontSave;
                measuringHero.gameObject.SetActive(false);
            }
            return measuringHero;
        }

        public static int SpatialTitleRows(string subject, float columnDegrees) =>
            Mathf.Clamp(Count(HeroMeasure(), subject, ContentWidth(columnDegrees)), 1, SubjectRows);

        public static float SpatialSubjectHeightOfRows(int rows, bool pillRoom)
        {
            var room = pillRoom ? U(Glaze.Menu.PillHeightDegrees) / 2f : 0f;
            var inner = SpatialInner(room);
            return room + Mathf.Max(U(Glaze.TargetDegrees), inner + rows * GlazeText.LineHeight(HeroMeasure()) + 2f * U(Glaze.Menu.SubjectPaddingDegrees));
        }

        public float SpatialSubjectHeight(string subject, float columnDegrees, bool pillRoom)
        {
            subjectTitle.fontSize = GlazeTokens.FontSize(U(Glaze.HeroDegrees));
            return SpatialSubjectHeightOfRows(SpatialTitleRows(subject, columnDegrees), pillRoom);
        }

        public static float TitleWidth(float columnDegrees) => ContentWidth(columnDegrees) - OrganizationControlsView.Width() - MenuPage.TargetGap;

        /// <summary>
        /// The room a pill's lower half takes inside the plate above the title's padding: the title
        /// stands half a grid step under the pill, not a whole padding, so the menu and a file with the
        /// split header stay inside a Quest 3S's field.
        /// </summary>
        private static float Inner(float pillRoom) => pillRoom > 0f ? pillRoom - U(Glaze.Menu.SubjectPaddingDegrees) + U(Glaze.Menu.GridDegrees) / 2f : 0f;

        // Caption and Hero use different font metrics. Keep two grid steps between their full
        // drawn bounds, including the SDF padding at larger text, in both pagination and layout.
        private static float SpatialInner(float pillRoom) => Inner(pillRoom) + (pillRoom > 0f ? 2f * MenuPage.Grid : 0f);

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
        public void Show(MenuFrame shown, float columnDegrees, float subject, bool pillRoom, bool stateInHeader = false)
        {
            controls.Bind(shown);
            frame = shown;
            side = null;
            inPlaceOf = null;
            carried = null;
            carriedReason = null;
            carriedReasonWaits = false;
            Begin(columnDegrees, subject, pillRoom || shown.Pill != null && !stateInHeader);
            subjectCut = shown.SubjectIsData;
            subjectTitle.color = shown.SubjectWaits ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground) : GlazeTokens.Text;
            LaySubject(shown.SubjectRuns, stateInHeader ? null : shown.Pill, shown.IdentityKind);
            LaySections(shown.Sections, shown.SectionSlots);
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
            controls.Bind(shown);
            frame = null;
            side = shown;
            this.inPlaceOf = inPlaceOf;
            carried = inPlaceOf?.Footer.InPlace(shown);
            carriedReason = carried?.Reason;
            carriedReasonWaits = carried?.ReasonWaits == true;
            Begin(columnDegrees, subject, pillRoom || pill != null);
            subjectCut = shown.SubjectIsData;
            subjectTitle.color = GlazeTokens.Text;
            LaySubject(shown.SubjectRuns, pill, shown.IdentityKind);
            LaySections(Array.Empty<FrameSection>(), 0);
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
            if (SpatialWorkspace)
            {
                var total = placed.Height / zoom;
                var section = UnifiedSections;
                var gap = UnifiedGap;
                var body = Mathf.Max(contentHeight, total - subjectHeight - section - gap * (section > 0f ? 2f : 1f));
                LayContent(body, new Vector2(placed.Right, placed.Up - (subjectHeight + section) * zoom / 2f), zoom);
                LayUnified(total, body);
            }
            else LayContent(placed.Height / zoom, new Vector2(placed.Right, placed.Up), zoom);
            // A ground under outside text first made as the plane lays the page fades with its part too.
            if (GroundsMade != groundsGathered) Gather();
            // Opening, its page shows whole only as the opening ends, and only then counts as drawn.
            if (motion == Motion.Opening) drawnOwed = true;
            else Drawn?.Invoke(this);
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

        public void Hide()
        {
            Stop();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Starts the column's opening (ADR 0027), counted from <paramref name="frame"/>, the plane's frame it
        /// opened in: where <paramref name="fromLine"/>, the light line draws from its character first; then its
        /// parts fade in from the edge the line meets, <see cref="Glaze.StaggerSeconds"/> apart, each over
        /// <see cref="Glaze.AppearSeconds"/>, by opacity alone: from the top, or from the bottom where
        /// <paramref name="fromBottom"/>, as over a desk, where the line rises to the page. Until it ends nothing
        /// on it takes a press, a hold or a subject press, and its page counts as drawn only then.
        /// </summary>
        public void Open(bool fromLine, int frame, bool fromBottom = false)
        {
            motion = Motion.Opening;
            movedFor = 0f;
            drawsFromLine = fromLine;
            this.fromBottom = fromBottom;
            OpenedIn = frame;
            drawnOwed = false;
            Unsettle(subjectToo: true);
            Paint();
        }

        /// <summary>Starts the column's closing: every part fades together over <see cref="Glaze.LeaveSeconds"/>, eased in, then it hides. It takes nothing meanwhile.</summary>
        public void Close()
        {
            if (!gameObject.activeSelf) return;
            if (motion == Motion.Closing) return;
            motion = Motion.Closing;
            movedFor = 0f;
            drawnOwed = false;
            Paint();
        }

        /// <summary>
        /// Moves the opening or closing on by <paramref name="seconds"/> of the plane's clock. An opening that
        /// ends shows every part whole and reports its page drawn; a closing that ends hides the column.
        /// </summary>
        public void Step(float seconds)
        {
            if (motion == Motion.Still) return;
            movedFor += seconds;
            if (motion == Motion.Opening && movedFor >= OpeningSeconds)
            {
                var owed = drawnOwed;
                Stop();
                if (owed) Drawn?.Invoke(this);
                return;
            }
            if (motion == Motion.Closing && movedFor >= Glaze.LeaveSeconds)
            {
                Hide();
                return;
            }
            Paint();
        }

        /// <summary>Ends any opening or closing where it stands, every part whole, reporting nothing: as when the column leaves the plane by stepping aside.</summary>
        public void Stop()
        {
            if (motion == Motion.Still) return;
            motion = Motion.Still;
            drawnOwed = false;
            Paint();
        }

        /// <summary>The column is opening, taking nothing yet.</summary>
        public bool Opening => motion == Motion.Opening;

        /// <summary>The column is closing, taking nothing.</summary>
        public bool Closing => motion == Motion.Closing;

        /// <summary>The seconds counted into the opening or closing under way.</summary>
        public float MovedFor => movedFor;

        /// <summary>The light line draws from the column's character as this opening starts.</summary>
        public bool DrawsFromLine => drawsFromLine;

        /// <summary>The plane's frame the opening started in, which counts nothing toward it.</summary>
        public int OpenedIn { get; private set; }

        /// <summary>How long this column's opening lasts, by its parts and whether its light line draws first.</summary>
        public float OpeningSeconds => Glaze.OpeningSeconds(Parts.Count, drawsFromLine);

        /// <summary>Where part <paramref name="index"/> (<see cref="Parts"/>, top to bottom) comes in its opening's order: counted from the edge its light line meets.</summary>
        public int ArrivesAt(int index) => fromBottom ? Parts.Count - 1 - index : index;

        /// <summary>The column's parts arrive from the bottom up, as over a desk.</summary>
        public bool FromBottom => fromBottom;

        /// <summary>How visible part <paramref name="index"/> is now (<see cref="Parts"/>): 1 at rest.</summary>
        public float PartShown(int index) => motion switch
        {
            Motion.Opening => Glaze.PartShown(movedFor, ArrivesAt(index), drawsFromLine),
            Motion.Closing => Glaze.LeftShown(movedFor),
            _ => 1f,
        };

        /// <summary>Every shape and every label as far into view as its part is now; whole at rest, every label's fade dropped.</summary>
        private void Paint()
        {
            for (var index = 0; index < fadingShapes.Count; index++)
            {
                var (shape, part) = fadingShapes[index];
                if (shape != null) shape.Shown = PartShown(part);
            }
            for (var index = 0; index < fadingWords.Count; index++)
            {
                var (words, part) = fadingWords[index];
                if (words != null) GlazeText.Show(words, PartShown(part));
            }
        }

        /// <summary>
        /// Gathers every shape and label on each part, shown or not, so one shown later in the opening fades too,
        /// and has every button ask <see cref="still"/>; then paints them as far into view as their parts are.
        /// </summary>
        private void Gather()
        {
            groundsGathered = GroundsMade;
            fadingShapes.Clear();
            fadingWords.Clear();
            var parts = Parts;
            for (var part = 0; part < parts.Count; part++)
            {
                parts[part].GetComponentsInChildren(true, gatheringShapes);
                for (var index = 0; index < gatheringShapes.Count; index++) fadingShapes.Add((gatheringShapes[index], part));
                parts[part].GetComponentsInChildren(true, gatheringWords);
                for (var index = 0; index < gatheringWords.Count; index++) fadingWords.Add((gatheringWords[index], part));
            }
            GetComponentsInChildren(true, gatheringButtons);
            for (var index = 0; index < gatheringButtons.Count; index++) gatheringButtons[index].Accepting = still;
            Paint();
        }

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
        public bool SubjectSettling => motion != Motion.Still || Time.unscaledTime - subjectUnsettledAt < GlazeButton.SettleSeconds;

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

        /// <summary>For the editor's renders: the column <paramref name="seconds"/> into an opening, or a closing, as stepped so far, to count what a frame of it allocates.</summary>
        public void MovedForRender(float seconds, bool closing = false)
        {
            motion = closing ? Motion.Closing : Motion.Opening;
            movedFor = seconds;
        }

        /// <summary>For the editor's renders, whose clock barely moves: the subject plate settled long ago.</summary>
        public void SettleSubjectForRender() => subjectUnsettledAt = float.NegativeInfinity;
#endif

        /// <summary>How many times its buttons were made to wait to settle again, for the editor's renders.</summary>
        public int Unsettles { get; private set; }

        private void Begin(float columnDegrees, float subject, bool pillRoom)
        {
            // Laid anew as it closes, as when it is measured to open again: it stands whole until the plane decides.
            if (motion == Motion.Closing) Stop();
            gameObject.SetActive(true);
            width = PlaneComposition.Units(columnDegrees);
            reserve = pillRoom ? StateBadgeView.PillHeight / 2f : 0f;
            subjectHeight = subject;
        }

        private void Build()
        {
            still = () => motion == Motion.Still;
            unifiedPart = Part("Work instrument");
            unifiedPlate = Surface.Create(unifiedPart, "Reading surface", Order - 1);
            unifiedBacking = Surface.Create(unifiedPart, "Instrument depth", Order - 2);
            unifiedPart.gameObject.SetActive(false);
            subjectPart = Part("Subject");
            controls = OrganizationControlsView.Create(subjectPart);
            controls.Accepting = () => !SubjectSettling && !FocusGuard.InputSuspended;
            controls.MovePressed += () => MovePressed?.Invoke();
            controls.PressStarted += () => SubjectPressed?.Invoke();
            controls.Held += point => SubjectHeld?.Invoke(point);
            controls.Dragged += point => SubjectDragged?.Invoke(point);
            controls.LetGo += () => SubjectLetGo?.Invoke();
            controls.Toggled += () => CompactPressed?.Invoke();
            sectionsPart = Part("Sections");
            contentPart = Part("Content");
            subjectPlate = Surface.Create(subjectPart, "Plate", Order);
            subjectTitle = GlazeText.Create(subjectPart, "Subject", GlazeType.Subject, GlazeTokens.Text, TextAlignmentOptions.TopLeft, Order + 2);
            subjectTitle.rectTransform.pivot = new Vector2(0f, 1f);
            identityCaption = GlazeText.Create(subjectPart, "Identity kind", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Left, Order + 2);
            identityCaption.rectTransform.pivot = new Vector2(0f, 0.5f);
            identityCaption.gameObject.SetActive(false);
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

        /// <summary>How many rows a line's words wrap to in <paramref name="room"/>, from the very text its label draws: its runs, with the room beside each ground.</summary>
        private static int CountLine(PageLine line, float room)
        {
            if (line.ProjectIdentity) return 3;
            var label = Measuring(Glaze.Menu.BodyDegrees);
            label.richText = false;
            label.parseCtrlCharacters = true;
            label.text = line.Runs.ForTextMeshPro().Text;
            return Mathf.Max(1, GlazeText.Lay(label, room, Unlimited).Lines);
        }

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
        private static string FactWords(PageLine line) => line.FactRuns!.Text;

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
            if (line.AnswerMark != AnswerMark.None) room -= AnswerMarkRoom;
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

        private static float AnswerMarkRoom => U(Glaze.Menu.BodyDegrees) + U(Glaze.Menu.GridDegrees);

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
            return CountLine(first, WordsRoom(first, half)) == 1 && CountLine(second, WordsRoom(second, half)) == 1;
        }

        // ---------------------------------------------------------------------------------------------
        // The subject.

        /// <summary>
        /// Lets the subject plate be held to drag the plane by (ADR 0026): a press held there past
        /// <see cref="GlazeButton.HoldSeconds"/> raises <see cref="SubjectHeld"/>, then
        /// <see cref="SubjectDragged"/> as the hand moves, and <see cref="SubjectLetGo"/>; a shorter press
        /// does nothing. It is apart from every prompt, so it never starts Hold to talk.
        /// </summary>
        public void EnableSubjectHold() => OrganizationControls = true;

        public PointerTarget? SubjectHoldForRender => controls.Move.Target;

        public void PaintSubjectForRender(bool pointed, bool pressed) => controls.Move.PaintForRender(pointed, pressed);

        private void LaySubject(LineWords words, StateBadge? badge, MenuIdentityKind? identityKind)
        {
            var padding = U(Glaze.Menu.PaddingDegrees);
            var left = -width / 2f + padding;
            var room = width - 2f * padding - (OrganizationControls ? OrganizationControlsView.Width() + MenuPage.TargetGap : 0f);
            // A title all of outside text leans in its plate, which is its ground; the pill on the plate's edge stays upright.
            var namedIdentity = SpatialWorkspace && identityKind != null;
            var projectList = SpatialWorkspace && frame?.Side == null && frame?.Subject == "Projects"
                && frame.Lines.Any(line => line.ProjectIdentity);
            if (SpatialWorkspace) subjectTitle.fontSize = GlazeTokens.FontSize(U(projectList ? Glaze.CaptionDegrees : Glaze.HeroDegrees));
            var theirs = namedIdentity
                ? GlazeRuns.Of(subjectTitle).SetIdentity(words.Text, shown => GlazeText.Lay(subjectTitle, room, Unlimited).Lines <= SubjectRows)
                : GlazeRuns.Of(subjectTitle).Set(words, shown => GlazeText.Lay(subjectTitle, room, Unlimited).Lines <= SubjectRows);
            identityCaption.gameObject.SetActive(namedIdentity);
            if (namedIdentity)
            {
                GlazeText.SetLiteral(identityCaption, identityKind == MenuIdentityKind.Task ? "Task" : "Project");
                GlazeText.Lay(identityCaption, room / 2f, 1);
                identityCaption.transform.localPosition = new Vector3(left, subjectHeight / 2f - reserve, -U(0.06f));
            }
            var (rows, _) = GlazeText.Lay(subjectTitle, room, SubjectRows);
            var titleHeight = Mathf.Max(1, rows) * GlazeText.LineHeight(subjectTitle);
            var top = subjectHeight / 2f;
            var plate = subjectHeight - reserve;
            subjectPlate.DrawGlass(new Vector2(width, plate), radius: 0f);
            subjectPlate.transform.localPosition = new Vector3(0f, top - reserve - plate / 2f, 0f);
            subjectPlateAt = new Vector3(0f, top - reserve - plate / 2f, 0f);
            controls.gameObject.SetActive(OrganizationControls);
            if (OrganizationControls) controls.Show(width / 2f - padding, subjectPlateAt.y, compact: false);
            // In the plate, under the pill's lower half, centred.
            var inner = SpatialWorkspace ? SpatialInner(reserve) : Inner(reserve);
            subjectTitle.transform.localPosition = new Vector3(left, top - reserve - inner - (plate - inner - titleHeight) / 2f, -U(0.05f));
            GlazeRuns.TokenGrounds(subjectTitle, words.AllTheirs ? Array.Empty<(int, int)>() : theirs, subjectGrounds, subjectPart, Order + 1);
            pill.gameObject.SetActive(badge != null);
            if (badge != null)
            {
                pill.Show(badge);
                pill.transform.localPosition = new Vector3(namedIdentity ? width / 2f - padding - pill.Width / 2f : left + pill.Width / 2f, top - reserve, -U(0.06f));
            }
        }

        // ---------------------------------------------------------------------------------------------
        // The sections: a row of shapes 12 mm apart, the chosen one lit, an amber dot on one that waits; laid
        // in the frame's slots, those shown in the last of them, so a section keeps its place and size while
        // others are yet to join it.

        private void LaySections(IReadOnlyList<FrameSection> shown, int slots)
        {
            if (ExternalPlaces) { shown = Array.Empty<FrameSection>(); slots = 0; }
            while (sections.Count < shown.Count) sections.Add(NewSection(sections.Count));
            var height = GlazeButton.HeightOf(true);
            slots = Mathf.Max(slots, shown.Count);
            var first = slots - shown.Count;
            var each = slots == 0 ? 0f : (width - TargetGap * (slots - 1)) / slots;
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
                var x = -width / 2f + each / 2f + (first + index) * (each + TargetGap);
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
                // The line a block of outside text began on, and its top: lines of it in a row stand on one ground.
                Line? block = null;
                var blockTop = 0f;
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
                        block = null;
                        continue;
                    }
                    var top = y;
                    var laid = lines[index++];
                    y = LayLine(laid, model, left, right, top, center, zoom);
                    if (!IsBlock(model)) block = null;
                    else if (block == null) (block, blockTop) = (laid, top);
                    else JoinBlock(block, laid, left, right, blockTop, y);
                }
                for (; index < lines.Count; index++) lines[index].Hide();
                var why = frame?.Reason ?? carriedReason;
                reason.gameObject.SetActive(why != null);
                // A reason that says what is under way, as "Sent. Waiting for the agent…", shimmers.
                reasonShimmer.Waits = why != null && (frame?.Reason != null ? frame.ReasonWaits : carriedReasonWaits);
                if (why != null) y = Note(reason, why, left, right, y - group);
                var from = frame?.SourceRuns ?? side?.SourceRuns;
                source.gameObject.SetActive(from != null);
                foreach (var ground in sourceGrounds) ground.gameObject.SetActive(false);
                if (from != null)
                {
                    var (foot, theirs) = NoteRuns(source, from, left, right, y - group, NoteRows);
                    GlazeRuns.TokenGrounds(source, theirs, sourceGrounds, contentPart, Order + 2);
                    GlazeRuns.Of(source).Grounded = theirs.Count > 0 ? Grounded(true) : (Color32?)null;
                    y = foot;
                }
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
            content.DrawGlass(new Vector2(width, tall), padding, radius: 0f);
            content.transform.localPosition = Vector3.zero;
            return needed;
        }

        private float LayFact(Line line, SideFact fact, float left, float right, float top)
        {
            line.ShowFact(fact.ValueIsData);
            var y = Note(line.Name, fact.Name, left, right, top, 1) - U(Glaze.Menu.LabelToValueDegrees);
            var (foot, theirs) = NoteRuns(line.Words, fact.ValueRuns, left, right, y, FactRows);
            GlazeRuns.TokenGrounds(line.Words, theirs, line.WordGrounds, contentPart, Order + 2);
            GlazeRuns.Of(line.Words).Grounded = theirs.Count > 0 ? Grounded(true) : (Color32?)null;
            return foot;
        }

        /// <summary>
        /// Lays a page line from <paramref name="top"/> between the content lines at
        /// <paramref name="left"/> and <paramref name="right"/>; returns its foot.
        /// </summary>
        private float LayLine(Line line, PageLine model, float left, float right, float top, Vector2 center, float zoom)
        {
            if (model.ProjectIdentity) return LayProjectIdentity(line, model, left, right, top);
            var grid = U(Glaze.Menu.GridDegrees);
            var role = model.Action == null ? (ButtonRole?)null : model.Control == LineControl.Input ? ButtonRole.Input
                : model.Control == LineControl.Action ? ButtonRole.Navigation : model.Choice ? ButtonRole.Answer : ButtonRole.Row;
            line.Show(model, role);
            var lineHeight = GlazeText.LineHeight(line.Words);
            var (rows, theirs) = Wrap(line.Words, model, WordsRoom(model, right - left));
            var height = role == null ? rows * lineHeight : Mathf.Max(GlazeButton.HeightOf(true), rows * lineHeight + 2f * grid);
            var middle = top - height / 2f;
            // A line reused from one that took a press keeps its target hidden unless this one takes a press too.
            if (role != null && line.Button != null)
            {
                line.Button.On = model.Control == LineControl.Input ? model.InputFocused : model.Chosen;
                line.Button.Available = model.Available;
                line.Button.ShowArea(new Vector2((left + right) / 2f, middle), new Vector2(right - left + 2f * Inset, height));
            }

            // The right content line: the chevron, then the fact, then the chip, each in the room kept for it at 18 dp.
            var end = right;
            if (model.AnswerMark != AnswerMark.None)
            {
                line.ShowAnswerMark(model.AnswerMark, model.Chosen, model.Available, end - U(Glaze.Menu.BodyDegrees) / 2f, middle);
                end -= AnswerMarkRoom;
            }
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
                var (_, factTheirs) = Small(line.Fact, model.FactRuns!, end, middle, center, zoom,
                    ColourOf(model.Tone == LineTone.Waiting ? LineTone.Waiting : LineTone.Secondary, model.Available), kept);
                // A row all of outside text needs no ground: its quote mark, or its icon, marks it beside Halcyonic's rows.
                var allTheirs = model.Runs.AllTheirs && model.FactRuns!.AllTheirs;
                var factGrounded = role != null && allTheirs ? Array.Empty<(int, int)>() : factTheirs;
                GlazeRuns.TokenGrounds(line.Fact, factGrounded, line.FactGrounds, contentPart, Order + 2);
                GlazeRuns.Of(line.Fact).Grounded = factGrounded.Count > 0 ? Grounded(model.Available) : (Color32?)null;
                line.FactMayCut = model.FactIsData;
                end -= kept + grid;
            }
            if (model.Chip != null)
            {
                var kept = ChipWidth(model.Chip);
                var (words, _) = Small(line.Chip, LineWords.Ours(model.Chip), end - grid, middle, center, zoom, GlazeTokens.TextSecondary);
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
                // Halcyonic's quote mark beside outside text stands in the secondary colour; any other icon carries the tone.
                line.Icon.color = icon == GlazeIcon.Quote ? ColourOf(LineTone.Secondary, model.Available) : ColourOf(model.Tone, model.Available);
                line.Icon.transform.localPosition = new Vector3(left + U(Glaze.Menu.IconColumnDegrees) / 2f, middle, -U(0.05f));
                wordsLeft = left + U(Glaze.Menu.IconColumnDegrees) + grid;
            }
            line.Words.color = ColourOf(carried && model.Tone != LineTone.Secondary ? LineTone.Primary : model.Tone, model.Available);
            GlazeRuns.Of(line.Words).Lean(theirs);
            // Outside text on a ground takes the primary text colour, so it holds 4.5:1 there; a row all of it has no ground.
            GlazeRuns.Of(line.Words).Grounded = theirs.Count > 0 && !(role != null && model.Runs.AllTheirs) ? Grounded(model.Available) : (Color32?)null;
            // A line that says what is under way, as "Sending…", shimmers while it shows (ADR 0027).
            line.Shimmer.Waits = model.Waits;
            line.MayCut = model.WordsAreData;
            line.Words.transform.localPosition = new Vector3(wordsLeft, middle + rows * lineHeight / 2f, -U(0.05f));
            LayGrounds(line, model, role != null, theirs, left, right, middle, height);
            if (model.Control == LineControl.Input && model.InputFocused && !model.Runs.AnyTheirs) line.ShowCaret(wordsLeft, end, middle, rows * lineHeight);
            return top - height;
        }

        /// <summary>Actual project-list typography plus conservative inter-target spacing, in plane units.</summary>
        public static float ProjectListLineHeight(PageLine line, float columnDegrees)
        {
            var grid = U(Glaze.Menu.GridDegrees);
            if (!line.ProjectIdentity)
            {
                var rows = Mathf.Min(line.Rows, CountLine(line, WordsRoom(line, ContentWidth(columnDegrees))));
                var text = rows * GlazeText.LineHeight(Measuring(Glaze.Menu.BodyDegrees));
                return (line.Action == null ? text : Mathf.Max(GlazeButton.HeightOf(true), text + 2f * grid)) + TargetGap;
            }
            var room = ContentWidth(columnDegrees) - U(Glaze.Menu.IconColumnDegrees) - grid - ChevronRoom;
            var fact = Measuring(Glaze.Menu.BodyDegrees);
            GlazeRuns.Of(fact).Set(line.FactRuns!);
            var factRows = GlazeText.Lay(fact, room, Unlimited).Lines;
            var summaryHeight = Mathf.Max(1, factRows) * GlazeText.LineHeight(fact);
            return Mathf.Max(GlazeButton.HeightOf(true), GlazeText.LineHeight(Measuring(Glaze.TitleDegrees))
                + summaryHeight + 3f * grid) + TargetGap;
        }

        private float LayProjectIdentity(Line line, PageLine model, float left, float right, float top)
        {
            var grid = U(Glaze.Menu.GridDegrees);
            line.Show(model, ButtonRole.Row);
            line.Shimmer.Waits = false;
            var title = line.ProjectTitle;
            title.gameObject.SetActive(true);
            var titleHeight = GlazeText.LineHeight(title);
            line.Fact.fontSize = GlazeTokens.FontSize(U(GlazeText.ScaledDegreesOf(GlazeType.Body)));
            var wordsLeft = left + U(Glaze.Menu.IconColumnDegrees) + grid;
            var room = right - wordsLeft - ChevronRoom;
            line.Fact.textWrappingMode = TextWrappingModes.Normal;
            GlazeRuns.Of(line.Fact).Set(model.FactRuns!);
            var factRows = GlazeText.Lay(line.Fact, room, Unlimited).Lines;
            var factHeight = Mathf.Max(1, factRows) * GlazeText.LineHeight(line.Fact);
            var height = Mathf.Max(GlazeButton.HeightOf(true), titleHeight + factHeight + 3f * grid);
            var middle = top - height / 2f;
            var runs = GlazeRuns.Of(title);
            runs.SetIdentity(model.Words, words => GlazeText.Lay(title, room, Unlimited).Lines <= 1);
            GlazeText.Lay(title, room, 1);
            title.color = ColourOf(LineTone.Primary, model.Available);
            title.transform.localPosition = new Vector3(wordsLeft, top - grid, -U(0.05f));
            line.Icon.gameObject.SetActive(true);
            GlazeIcons.Show(line.Icon, GlazeIcon.Quote);
            line.Icon.color = GlazeTokens.TextSecondary;
            line.Icon.transform.localPosition = new Vector3(left + U(Glaze.Menu.IconColumnDegrees) / 2f, top - grid - titleHeight / 2f, -U(0.05f));
            line.Fact.gameObject.SetActive(true);
            GlazeRuns.Of(line.Fact).Set(model.FactRuns!);
            line.Fact.color = ColourOf(LineTone.Secondary, model.Available);
            line.Fact.alignment = TextAlignmentOptions.TopLeft;
            GlazeText.Lay(line.Fact, room, Mathf.Max(1, factRows));
            line.Fact.transform.localPosition = new Vector3(wordsLeft, top - 2f * grid - titleHeight, -U(0.05f));
            line.Chevron.color = GlazeTokens.TextSecondary;
            line.Chevron.gameObject.SetActive(true);
            GlazeIcons.Show(line.Chevron, GlazeIcon.OpensMore);
            line.Chevron.transform.localPosition = new Vector3(right - ChevronGlyph * U(Glaze.Menu.BodyDegrees) / 2f, middle, -U(0.05f));
            line.Button!.On = model.Chosen;
            line.Button.Available = model.Available;
            line.Button.ShowArea(new Vector2((left + right) / 2f, middle), new Vector2(right - left + 2f * Inset, height));
            line.MayCut = true;
            return top - height;
        }

        /// <summary>
        /// Halcyonic's ground under outside text where it meets Halcyonic's words (lane V's treatment):
        /// a token hugging each run of it on each row it reaches, so both its edges show; under a line all
        /// of outside text that only says something, one ground for the block, its quote mark at the
        /// corner, as far as the content line; none for a row all of outside text, whose quote mark in
        /// the icon column marks it beside Halcyonic's rows.
        /// </summary>
        private void LayGrounds(Line line, PageLine model, bool row, IReadOnlyList<(int Start, int End)> theirs, float left, float right, float middle, float height)
        {
            if (theirs.Count == 0) return;
            if (model.Runs.AllTheirs || model.Claim)
            {
                if (row) return;
                // One ground under the block that hugs its words, so a chip or a time beside them stands on the glass (lane V).
                var at = line.Words.transform.localPosition;
                var pad = GlazeRuns.GroundPadding;
                var boxes = GlazeText.RunBoxes(line.Words, theirs);
                if (boxes.Count == 0) return;
                var area = Rect.MinMaxRect(at.x + boxes.Min(box => box.xMin) - pad, at.y + boxes.Min(box => box.yMin) - pad,
                    at.x + boxes.Max(box => box.xMax) + pad, at.y + boxes.Max(box => box.yMax) + pad);
                line.BlockArea = area;
                GlazeRuns.DrawGround(line.BlockGround(), area.size, new Vector3(area.center.x, area.center.y, -U(0.04f)));
                return;
            }
            GlazeRuns.TokenGrounds(line.Words, theirs, line.WordGrounds, contentPart, Order + 2);
        }

        /// <summary>A line all of outside text that only says something: a block, on a ground of its own.</summary>
        private static bool IsBlock(PageLine model) => model.Action == null && (model.Runs.AllTheirs || model.Claim);

        /// <summary>
        /// A block's next line joins its first on one ground, from the first's top to this one's foot at
        /// <paramref name="foot"/>, under the one quote mark at the block's corner.
        /// </summary>
        private void JoinBlock(Line first, Line next, float left, float right, float top, float foot)
        {
            foreach (var ground in next.Grounds.ToList()) ground.gameObject.SetActive(false);
            next.Icon.gameObject.SetActive(false);
            if (!(first.BlockArea is Rect above) || !(next.BlockArea is Rect below)) return;
            // From the first line's top to this one's foot, as wide as the block's words reach on either.
            var area = Rect.MinMaxRect(Mathf.Min(above.xMin, below.xMin), Mathf.Min(above.yMin, below.yMin), Mathf.Max(above.xMax, below.xMax), Mathf.Max(above.yMax, below.yMax));
            first.BlockArea = area;
            GlazeRuns.DrawGround(first.BlockGround(), area.size, new Vector3(area.center.x, area.center.y, -U(0.04f)));
        }



        /// <summary>
        /// Lays a line's words in <paramref name="room"/>: from their start, ending in an ellipsis past
        /// its rows, or, for a part, exactly its rows of the words wrapped whole. A line with Halcyonic's
        /// words after outside text is cut inside the outside text instead (<see cref="LineWords.CutToFit"/>),
        /// so Halcyonic's are never cut while it shows. Returns the rows shown, and where outside text
        /// stands in the label's text as shown, to lean and to ground.
        /// </summary>
        private static (int Rows, IReadOnlyList<(int Start, int End)> Theirs) Wrap(TextMeshPro label, PageLine model, float room)
        {
            label.richText = false;
            label.parseCtrlCharacters = true;
            var runs = model.Runs;
            if (model.FromRow == null && runs.OursAfterTheirs)
            {
                runs = runs.CutToFit(words =>
                {
                    label.text = words.ForTextMeshPro().Text;
                    return GlazeText.Lay(label, room, Unlimited).Lines <= model.Rows;
                });
            }
            var (text, theirs) = runs.ForTextMeshPro();
            // A claim of the old kind leans whole until its builder gives runs.
            if (model.Claim) theirs = new[] { (0, text.Length) };
            label.text = text;
            if (model.FromRow is int from)
            {
                GlazeText.Lay(label, room, Unlimited);
                var info = label.textInfo;
                var count = Mathf.Max(1, info.lineCount);
                var first = Mathf.Min(from, count - 1);
                var last = Mathf.Min(from + model.Rows, count);
                // Never between the two backslashes of a pair, which would leave a lone one to turn the next characters into another.
                var start = LabelText.CharacterStart(text, info.characterInfo[info.lineInfo[first].firstCharacterIndex].index);
                var end = last < count ? LabelText.CharacterStart(text, info.characterInfo[info.lineInfo[last].firstCharacterIndex].index) : text.Length;
                label.text = text.Substring(start, end - start);
                theirs = theirs.Select(range => (Mathf.Max(range.Start, start) - start, Mathf.Min(range.End, end) - start))
                    .Where(range => range.Item2 > range.Item1).ToList();
            }
            return (Mathf.Max(1, GlazeText.Lay(label, room, model.Rows).Lines), theirs);
        }

        /// <summary>
        /// Lays small words ending at <paramref name="end"/>, centred on <paramref name="middle"/>: 15 dp
        /// where that reads as the eyes see it from its place on the plane, else 18 dp, at most
        /// <paramref name="most"/> wide, past which they end in an ellipsis. Returns their size.
        /// </summary>
        private static (Vector2 Size, IReadOnlyList<(int Start, int End)> Theirs) Small(TextMeshPro label, LineWords words, float end, float middle, Vector2 center,
            float zoom, Color colour, float most = float.MaxValue)
        {
            var degrees = PlaneComposition.SmallTextDegreesAt(center.x + end * zoom, center.y + middle * zoom);
            label.gameObject.SetActive(true);
            label.fontSize = GlazeTokens.FontSize(U(degrees));
            label.color = colour;
            var theirs = GlazeRuns.Of(label).Set(words, text => label.GetPreferredValues(text).x <= most);
            var wide = Mathf.Min(label.GetPreferredValues(label.text).x, most);
            GlazeText.Lay(label, wide + U(0.1f), 1);
            var tall = GlazeText.LineHeight(label);
            label.transform.localPosition = new Vector3(end - wide, middle + tall / 2f, -U(0.05f));
            // The right edge is only an estimate: a left-column fact extends away from the
            // composition's centre. Judge its actual laid text bounds and finite em before
            // keeping the small size. Space for the body-size alternative is already reserved.
            var localMiddle = label.transform.localPosition + Vector3.Scale(label.textBounds.center, label.transform.localScale);
            var at = new Vector3(center.x + localMiddle.x * zoom, center.y + localMiddle.y * zoom, 1f);
            var halfEm = Vector3.up * (label.fontSize * 0.1f * label.transform.localScale.y * zoom / 2f);
            if (degrees < Glaze.Menu.BodyDegrees && Vector3.Angle(at + halfEm, at - halfEm) < Glaze.MinimumTextDegrees)
            {
                label.fontSize = GlazeTokens.FontSize(U(Glaze.Menu.BodyDegrees));
                theirs = GlazeRuns.Of(label).Set(words, text => label.GetPreferredValues(text).x <= most);
                wide = Mathf.Min(label.GetPreferredValues(label.text).x, most);
                GlazeText.Lay(label, wide + U(0.1f), 1);
                tall = GlazeText.LineHeight(label);
                label.transform.localPosition = new Vector3(end - wide, middle + tall / 2f, -U(0.05f));
            }
            return (new Vector2(wide, tall), theirs);
        }

        /// <summary>Outside text on a ground: the primary text colour, or quiet where its line takes no press now.</summary>
        private static Color32 Grounded(bool available) => ColourOf(LineTone.Primary, available);

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

        /// <summary>
        /// Lays runs from <paramref name="top"/> down, left-aligned, in at most <paramref name="rows"/> rows,
        /// cut inside outside text before Halcyonic's words and leaning it; returns their foot and where outside text stands.
        /// </summary>
        private static (float Foot, IReadOnlyList<(int Start, int End)> Theirs) NoteRuns(TextMeshPro label, LineWords words, float left, float right, float top, int rows)
        {
            label.gameObject.SetActive(true);
            var theirs = GlazeRuns.Of(label).Set(words, shown => GlazeText.Lay(label, right - left, Unlimited).Lines <= rows);
            var (count, _) = GlazeText.Lay(label, right - left, rows);
            label.transform.localPosition = new Vector3(left, top, -U(0.05f));
            return (top - Mathf.Max(1, count) * GlazeText.LineHeight(label), theirs);
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

        private void LayUnified(float total, float body)
        {
            var section = UnifiedSections;
            var top = total / 2f;
            subjectPart.localPosition = new Vector3(0f, top - subjectHeight / 2f, -0.001f);
            top -= subjectHeight + UnifiedGap;
            if (section > 0f) { sectionsPart.localPosition = new Vector3(0f, top - section / 2f, -0.001f); top -= section + UnifiedGap; }
            contentPart.localPosition = new Vector3(0f, top - body / 2f, -0.001f);
            subjectPlate.gameObject.SetActive(false);
            content.gameObject.SetActive(false);
            foreach (var sectionView in sections) sectionView.Shape.gameObject.SetActive(false);
            // The heading belongs to the work; its reading body is the only full surface.
            // Keep the rear contact dark: a white slab behind translucent glass washes the whole
            // instrument gray, defeating both the palette and the separation of reading levels.
            subjectPlate.gameObject.SetActive(true);
            subjectPlate.Draw(new Vector2(width, subjectHeight - reserve), 0f, GlazeTokens.ColorOf(Glaze.Panel, 0.94f));
            unifiedPlate.Draw(new Vector2(width, body), 0f, GlazeTokens.ColorOf(Glaze.Panel, 0.98f));
            unifiedPlate.transform.localPosition = new Vector3(0f, contentPart.localPosition.y, 0f);
            unifiedBacking.Draw(new Vector2(width, body), 0f, GlazeTokens.ColorOf(Glaze.Panel));
            unifiedBacking.transform.localPosition = new Vector3(0f, contentPart.localPosition.y, 0.008f / PlaneComposition.Distance);
        }

        private void Collect()
        {
            if (SpatialWorkspace)
            {
                subjectPart.SetParent(unifiedPart, false);
                sectionsPart.SetParent(unifiedPart, false);
                contentPart.SetParent(unifiedPart, false);
                unifiedPart.gameObject.SetActive(true);
                var total = subjectHeight + UnifiedSections + contentHeight + UnifiedGap * (UnifiedSections > 0f ? 2f : 1f);
                LayUnified(total, contentHeight);
                Parts = new[] { unifiedPart };
                Heights = new[] { total };
                Gather();
                return;
            }
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
            Gather();
        }

        private Line NewLine(int index)
        {
            var line = new Line(contentPart, "Line " + index);
            line.Pressed += (action, key) => Acted?.Invoke(action, key);
            return line;
        }

        private enum Motion
        {
            Still,
            Opening,
            Closing,
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

            public Line(Transform parent, string name)
            {
                this.parent = parent;
                this.name = name;
                Icon = GlazeIcons.Create(parent, name + " icon", Glaze.Menu.BodyDegrees, GlazeTokens.Text, Order + 3);
                ordinaryWords = Label(name + " words", GlazeTokens.Text);
                ProjectTitle = GlazeText.Create(parent, name + " project identity", GlazeType.Title, GlazeTokens.Text, TextAlignmentOptions.TopLeft, Order + 3, scaled: true);
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

            private readonly TextMeshPro ordinaryWords;
            public TextMeshPro Words => model?.ProjectIdentity == true ? ProjectTitle : ordinaryWords;
            public TextMeshPro ProjectTitle { get; }

            public TextMeshPro Name { get; }

            public TextMeshPro Fact { get; }

            public TextMeshPro Chip { get; }

            public Surface ChipShape { get; }

            public TextMeshPro Chevron { get; }

            public GlazeButton? Button { get; private set; }

            private Surface? answerMark;
            private Surface? answerCentre;
            private TextMeshPro? answerCheck;
            private Surface? caret;

            public void ShowAnswerMark(AnswerMark kind, bool chosen, bool available, float x, float y)
            {
                var em = U(Glaze.Menu.BodyDegrees);
                var colour = available ? GlazeTokens.Text : GlazeTokens.TextSecondary;
                answerMark ??= Surface.Create(parent, name + " selection mark", Order + 3);
                answerMark.gameObject.SetActive(true);
                answerMark.Draw(Vector2.one * em, kind == AnswerMark.Single ? em / 2f : U(Glaze.Menu.RadiusDegrees) / 3f, Color.clear, colour, U(Glaze.Menu.HairlineDegrees));
                answerMark.transform.localPosition = new Vector3(x, y, -U(0.06f));
                if (!chosen) return;
                if (kind == AnswerMark.Single)
                {
                    answerCentre ??= Surface.Create(parent, name + " selected centre", Order + 4);
                    answerCentre.gameObject.SetActive(true);
                    answerCentre.Draw(Vector2.one * em * 0.45f, em / 2f, colour);
                    answerCentre.transform.localPosition = new Vector3(x, y, -U(0.07f));
                }
                else
                {
                    answerCheck ??= GlazeIcons.Create(parent, name + " selected check", Glaze.Menu.BodyDegrees, colour, Order + 4);
                    answerCheck.gameObject.SetActive(true);
                    answerCheck.color = colour;
                    GlazeIcons.Show(answerCheck, GlazeIcon.Approve);
                    answerCheck.transform.localPosition = new Vector3(x, y, -U(0.07f));
                }
            }

            public void ShowCaret(float left, float right, float middle, float height)
            {
                caret ??= Surface.Create(parent, name + " input caret", Order + 4);
                caret.gameObject.SetActive(true);
                caret.Selection = SurfaceSelection.InputCaret;
                var x = left;
                caret.Draw(new Vector2(U(Glaze.Menu.HairlineDegrees), GlazeText.LineHeight(Words)), 0f, GlazeTokens.Text);
                caret.transform.localPosition = new Vector3(x, middle + height / 2f - GlazeText.LineHeight(Words) / 2f, -U(0.07f));
            }

            /// <summary>The page line it shows, or null while hidden.</summary>
            public PageLine? Model => model;

            /// <summary>The words are text from outside, which may end in an ellipsis.</summary>
            public bool MayCut { get; set; }

            /// <summary>The small fact is text from outside, which may end in an ellipsis past its share of the line.</summary>
            public bool FactMayCut { get; set; }

            /// <summary>The grounds under outside text in its words, and in its small fact.</summary>
            public List<Surface> WordGrounds { get; } = new List<Surface>();

            public List<Surface> FactGrounds { get; } = new List<Surface>();

            /// <summary>Its first ground, under a block of outside text, shown; made the first time it is needed.</summary>
            public Surface BlockGround()
            {
                if (WordGrounds.Count == 0) WordGrounds.Add(Surface.Create(parent, name + " words ground 0", Order + 2));
                WordGrounds[0].gameObject.SetActive(true);
                return WordGrounds[0];
            }

            /// <summary>Where the ground under its block of outside text stands, in the content's units; null where it has none.</summary>
            public Rect? BlockArea { get; set; }

            /// <summary>The grounds shown, for the renders' checks.</summary>
            public IEnumerable<Surface> Grounds => WordGrounds.Concat(FactGrounds).Where(ground => ground.gameObject.activeSelf);

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
                GlazeRuns.Of(Words).Lean(Array.Empty<(int, int)>());
            }

            public void Hide()
            {
                model = null;
                MayCut = false;
                FactMayCut = false;
                Words.gameObject.SetActive(false);
                ProjectTitle.gameObject.SetActive(false);
                Name.gameObject.SetActive(false);
                Icon.gameObject.SetActive(false);
                Fact.gameObject.SetActive(false);
                Chip.gameObject.SetActive(false);
                ChipShape.gameObject.SetActive(false);
                Chevron.gameObject.SetActive(false);
                if (answerMark != null) answerMark.gameObject.SetActive(false);
                if (answerCentre != null) answerCentre.gameObject.SetActive(false);
                if (answerCheck != null) answerCheck.gameObject.SetActive(false);
                if (caret != null) caret.gameObject.SetActive(false);
                foreach (var ground in WordGrounds.Concat(FactGrounds)) ground.gameObject.SetActive(false);
                BlockArea = null;
                Button?.Hide();
            }
        }
    }
}
