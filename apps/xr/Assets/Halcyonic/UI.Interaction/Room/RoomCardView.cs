#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The information panel as a card, drawn (<see cref="RoomCard"/>, the addendum's A17), every length the design page's:
    /// a status, its mark in a 56 dp disc and its title word over a quieter line, its tries as pips hung on the mark; or a
    /// head, the thing's emblem in a 104 dp tile beside its title (34 dp on a 40 dp line); a description under it; the
    /// gauges as flat wells; the facts as slots, each a flat faint well, its kind's glyph small at its top left and its value
    /// inside it under the glyph, left-aligned (one form everywhere, nothing that reads as a press); the steps
    /// as squircle marks joined by lines, their names under them, a step's tries as pips hung on its mark like a game's
    /// lives; a list, each row a 38 dp tile
    /// of its kind's mark, its name and a quiet note at its right, a hairline between rows; and a quiet line at its foot.
    /// Its origin is the top left of its first part; it grows down and says how tall it stands. It draws nothing of the
    /// panel and takes no press.
    /// </summary>
    public sealed class RoomCardView : MonoBehaviour
    {
        // The design page's numbers (design10's information language), in dp.
        public const float StatusMarkDp = 56f;
        public const float StatusGlyphDp = 40f;
        public const float StatusGapDp = 14f;
        public const float StatusTitleDp = 30f;
        public const float StatusTitleLineDp = 36f;

        /// <summary>A status under a card's own title: a step smaller, so the thing's name stays the card's one title.</summary>
        public const float UnderTitleStatusDp = 24f;
        public const float UnderTitleStatusLineDp = 30f;

        /// <summary>The most lines a status's line takes before anything is cut; the product's own words are laid whole within it.</summary>
        public const int StatusLineMost = 20;

        /// <summary>The gap between someone's words on a row and our line under them, in dp.</summary>
        public const float UnderTheirsGapDp = 6f;

        /// <summary>The gap between a byline's bubble and the speaker's name, in dp.</summary>
        public const float BylineGapDp = 6f;

        /// <summary>
        /// A description's light: where rows follow it, it is the card's lead and reads a tier above them (the quote over the
        /// quiet lines, SCREENS's critic 6, 2026-11-13: "nearly the same light grey"); elsewhere the reading tier as before.
        /// </summary>
        private static float DescriptionAlpha(RoomCard card) => card.List.Count > 0 || card.Results.Count > 0 ? 0.94f : 0.80f;

        /// <summary>The most lines a status's line takes in the column beside its mark; longer, it runs the card's width under it.</summary>
        public const int StatusColumnLines = 3;
        public const float LineTypeDp = 17f;
        public const float LineLineDp = 23f;
        public const float HeadDp = 104f;
        public const float HeadCornerDp = 28f;
        public const float HeadGlyphDp = 52f;
        public const float HeadGapDp = 20f;
        public const float TitleTypeDp = 34f;
        public const float TitleLineDp = 40f;
        public const float LongTitleTypeDp = 26f;
        public const float LongTitleLineDp = 31f;
        public const float FinalTitleTypeDp = 22f;
        public const float FinalTitleLineDp = 27f;
        public const float DescriptionTypeDp = 18f;
        public const float DescriptionLineDp = 25f;
        public const float DescriptionGapDp = 6f;
        public const float BlockGapDp = 22f;

        /// <summary>
        /// Between a status's lines kept apart (RoomCardStatus.Lines): a paragraph's gap, still clearly wider than the lines' own leading
        /// (the reader's 2026-11-14T0100Z), drawn in from 14 for the cards that lay a status apart (the lead's 01:20Z item 2: the height).
        /// </summary>
        public const float LinesApartGapDp = 10f;

        /// <summary>A status's line kept apart: the description's size and light (the reader's 01:00Z), on a pitch a dp tighter than the description's.</summary>
        public const float LinesApartLineDp = 24f;
        public const float TileGapDp = 16f;
        /// <summary>A well's corner: smaller than the panel's own 20 dp, so a well sits in the panel and never reads as a tile of its own.</summary>
        public const float TileCornerDp = 14f;
        /// <summary>A slot's well: a step stronger than 0.065, which over the firmer glass and a bright room stood at 1.10:1 and vanished (the critic, 2026-11-12).</summary>
        public const float WellAlpha = 0.10f;

        /// <summary>A neutral row's tile: a step stronger than a slot's well, so the mark reads as a tile over either room.</summary>
        public const float RowTileAlpha = 0.09f;
        public const float SlotPadDp = 14f;
        public const float SlotFootDp = 16f;
        public const float SlotGlyphDp = 24f;
        public const float SlotGlyphBoxDp = 26f;
        public const float SlotLevelDp = 21f;
        public const float SlotLevelCheckDp = 13f;

        /// <summary>A chosen answer's disc at rest, neutral: the accent stays the main action's (#E2E7EA, its check #1F1F1F).</summary>
        public const int ChosenAtRest = 0xE2E7EA;
        public const float CaptionTypeDp = 16f;
        public const float CaptionLineDp = 20f;
        public const float CaptionGapDp = 12f;
        public const float StatusCornerDp = 17f;
        public const float StepDp = 38f;
        public const float StepCornerDp = 12f;
        public const float StepGlyphDp = 24f;
        public const float StepJoinDp = 3f;
        public const float StepJoinGapDp = 8f;
        public const float StepNameDp = 17f;
        public const float StepNameLineDp = 21f;
        public const float StepNameGapDp = 36f;
        /// <summary>
        /// Between a node and its name where no step carries tries' pips: the name reads as its node's own label, never as a line
        /// standing below the row (AUDIT2's readers of 11-14 and 11-16: "labels sit below their boxes"). The 36 dp keeps the pips' room.
        /// </summary>
        public const float StepNameTightDp = 10f;
        /// <summary>Under a step the run never reached, quiet: never an empty box alone (AUDIT2's W5, 2026-11-16).</summary>
        public const string NotRun = "Not Run";
        public const float PipDp = 14f;
        public const float PipCornerDp = 4f;
        public const float PipGapDp = 4f;
        public const float PipLineDp = 2f;
        /// <summary>How far under its node a pip's foot hangs.</summary>
        public const float PipDropDp = 26f;
        public const float TrackTallDp = 8f;
        public const float ListGapDp = 16f;
        public const float RowPadDp = 11f;
        public const float RowTileDp = 38f;
        public const float RowTileCornerDp = 10f;
        public const float RowGlyphDp = 22f;

        /// <summary>A colour row's swatch, a filled disc before its name: a mark's size, so it reads as the colour, never as a control.</summary>
        public const float SwatchDp = 22f;

        /// <summary>The most lines a list row's name or quieter line lays: a page's eight (the spec), so words that are not an excerpt are never cut.</summary>
        public const int RowMostLines = 8;

        /// <summary>The most lines an agent's own description of a row takes before it stops at a word, faded.</summary>
        public const int TheirsUnderLines = 3;
        public const float RowGapDp = 14f;
        public const float RowNameDp = 20f;
        public const float RowNameLineDp = 26f;
        public const float RowNoteDp = 16f;
        public const float RowNoteLineDp = 20f;

        /// <summary>
        /// The quiet words' white, in the reading face, for every note, a status's line, a card's foot line and a gauge's words:
        /// a step quieter than a name, legible over the bright room and the dark one (0.66 measured 4.05:1 and 0.72 4.55:1 over
        /// the bright room, under the 4.5:1 floor's margin, the critic of 2026-11-12; the kind's colour is the mark's alone).
        /// </summary>
        public const float NoteAlpha = 0.78f;

        /// <summary>
        /// A green or red mark's tile and its ink: a deep tint under a light glyph, so the mark reads over the bright room
        /// as over the dark (0.18 went muddy there, and a red minus on red was the weakest mark).
        /// </summary>
        public const float ToneTileAlpha = 0.38f;
        public const float GaugePadXDp = 14f;
        public const float GaugeGapDp = 12f;
        public const float GaugePadTopDp = 14f;
        public const float GaugePadFootDp = 16f;
        public const float GaugeNumberDp = 40f;
        public const float GaugeNumberLineDp = 44f;
        public const float GaugeLabelDp = 17f;
        public const float GaugeLabelLineDp = 22f;
        public const float GaugeResetDp = 15f;
        public const float GaugeResetLineDp = 19f;
        public const float MoreDp = 16f;
        public const float MoreLineDp = 21f;

        /// <summary>
        /// A code to scan: its well as wide as the card (at most 400 dp, 25 degrees), the card's hero, on every block's left edge, standing in the card's
        /// middle; its cells dark on a near-white well, the kit's margin of light cells round them, the well's corners inside
        /// that margin. Opaque both, so a camera reads it the same over the bright room as over the dark.
        /// </summary>
        public const float CodeDp = 400f;
        public const int CodeGround = 0xF4F7F8;
        public const int CodeInk = 0x111111;
        public const float CodeCornerCells = 2f;

        /// <summary>The page of reading's well: the panel's darkest, near solid, its rows 8 dp in from its top and foot.</summary>
        public const int ReadingWell = 0x111111;

        /// <summary>A changed run's band, the hue a fifth over the well, solid; the removed rule a lighter red so it weighs as the green.</summary>
        public const int AddedBand = 0x203A2C;
        public const int RemovedBand = 0x3D2929;
        public const int RemovedRule = 0xFF8A80;
        public const float ReadingWellAlpha = 1f;
        public const float ReadingWellPadDp = 8f;

        /// <summary>How strong the accent's tint stands under rows a later read changed: a wash, never a slab.</summary>
        public const float LitTintAlpha = 0.1f;

        /// <summary>
        /// The status marks' colours: no hue in the chrome (the lead's 2026-11-17 19:15Z; visionos-label-vibrant-primary.png's white).
        /// A pass, a failure, the accent and attention all stand in white on the quiet ground, told apart by their glyphs (a check, a
        /// cross, the step's own) and their words; colour stays only where it is content (code, a diff's bands, the cast).
        /// </summary>
        public const int Passed = 0xFFFFFF;
        public const int PassedInk = 0xFFFFFF;
        public const int PassedGround = 0x434343;
        public const int PassedNote = 0xE6EAEC;
        public const int Failed = 0xFFFFFF;
        public const int FailedInk = 0xFFFFFF;
        public const int FailedGround = 0x434343;
        public const int FailedNote = 0xE6EAEC;
        public const int Accent = 0xFFFFFF;
        public const int AccentNote = 0xE6EAEC;
        public const int Dark = 0x1F1F1F;

        /// <summary>The attention colour (a task or the assistant needs the person): white, its call carried by motion and sound, never hue.</summary>
        public const int Attention = 0xFFFFFF;
        public const int AttentionNote = 0xE6EAEC;
        public const int StepGround = 0x343434;

        /// <summary>The deep grounds of the accent and of attention, as the passed and failed ones, and how solid a status mark stands.</summary>
        public const int AccentGround = 0x434343;
        public const int AttentionGround = 0x434343;
        public const int AttentionInk = 0x1F1F1F;
        public const int QuietGround = 0x434343;

        /// <summary>A status mark stands solid, so nothing of the room shows through it (the critic, 2026-11-11).</summary>
        public const float StatusGroundAlpha = 1f;

        /// <summary>
        /// One recipe for every status mark and step node (the critics, 2026-11-11): the tone's own hue, solid and saturated,
        /// its glyph bold and white over it; attention's amber is light, so its glyph is dark.
        /// </summary>
        public const int GoodFill = 0x929292;  // was 0x2FA65A: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        public const int BadFill = 0x787878;  // was 0xD4463B: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        public const int WorkingFill = 0x8D8D8D;  // was 0x1F98C2: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        public const int QuietFill = 0x525252;

        /// <summary>A mark's glyph stroke, heavier than a row's, so it reads at a glance on a solid fill.</summary>
        public const float MarkStroke = 3.4f;

        /// <summary>The tones' mid values, between hue and deep ground: a try's pip, never louder than the mark it hangs on.</summary>
        public const int PassedMid = 0x8C8C8C;  // was 0x4C9C67: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        public const int FailedMid = 0x747474;  // was 0xB25A54: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        public const int AccentMid = 0x848484;  // was 0x3B8EA6: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        public const int AttentionMid = 0x818181;  // was 0xA37A23: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        public const int AgentOrb = 0x7A7A7A;  // was 0x7A6BD6: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        /// <summary>
        /// The project emblem's tile: a deep, quiet teal, so the one accent stays the main action's (the lead's one-accent ruling; SCREENS's
        /// 2026-11-14T02:28Z reader: the brighter 0x3F8FB0 pulled the eye nearly as much as Start Task). Its white glyph reads about 8:1 on it.
        /// </summary>
        public const int ProjectTile = 0x505050;  // was 0x2E5566: the true grey of its luminance, no hue in the chrome (the lead's 2026-11-18)
        public const int TileInk = 0xF3F3F3;

        private readonly Dictionary<KitRole, List<TextMeshPro>> texts = new Dictionary<KitRole, List<TextMeshPro>>();
        private readonly Dictionary<KitRole, int> textsUsed = new Dictionary<KitRole, int>();
        private readonly List<RoomShape> plates = new List<RoomShape>();
        private readonly List<RoomGlyphView> glyphs = new List<RoomGlyphView>();
        private readonly List<(KitPress Press, GameObject Target)> rowPresses = new List<(KitPress, GameObject)>();
        private readonly List<KitControl?> rowControls = new List<KitControl?>();
        private int rowPressesUsed;

        /// <summary>The card's own press, where the card is the way in, and the accent face that shows it, felt on hover.</summary>
        private KitPress? cardPress;

        /// <summary>How far down the card the way in's chevron stands: beside the name's first line, or the lead status's mark.</summary>
        private float? chevronAt;
        private RoomShape? cardFace;
        private Vector2 cardFaceSize;
        private bool? cardFaceLit;
        private int cardFaceHue = Accent;

        /// <summary>The control a press on the card raises, while the shown card takes one.</summary>
        private KitControl? cardControl;

        /// <summary>A row picked from a list (<see cref="RoomListRow.Press"/>): its own control, for the bar to act on.</summary>
        public event System.Action<KitControl>? RowPressed;

        /// <summary>The list rows' press targets shown now, in order, with their controls: for checks.</summary>
        public IEnumerable<(KitPress Press, KitControl Control)> RowTargets
        {
            get
            {
                for (var index = 0; index < rowPressesUsed; index++)
                    if (rowControls[index] is KitControl control) yield return (rowPresses[index].Press, control);
            }
        }

        /// <summary>The chevron at a picked-from row's right, 18 dp, and its room before the note.</summary>
        public const float ChevronDp = 18f;
        public const float ChevronGapDp = 10f;
        private RoomCodeView? codeView;

        // The task's still on a picture head, where one is read (TaskStill); hidden otherwise.
        private RoomPictureView? pictureView;
        private bool pictureUsed;

        /// <summary>
        /// The still a picture head draws for a card's <see cref="RoomCard.Still"/>, or null where it is not read yet or cannot be: the
        /// director's reads (TaskPictures). Unset, every picture head draws its own glyph.
        /// </summary>
        public static System.Func<TaskStill, Texture2D?>? Pictures { get; set; }

        /// <summary>The still the picture head draws, or null where it draws its glyph.</summary>
        public RoomPictureView? PictureShown => pictureUsed ? pictureView : null;
        private bool codeUsed;
        private int platesUsed;
        private int glyphsUsed;
        private int order;

        /// <summary>How wide and how tall the card stands as last shown.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Every label the card shows now, in the order it laid them: for checks.</summary>
        public List<TextMeshPro> Shown { get; } = new List<TextMeshPro>();

        /// <summary>The label over the title (<see cref="RoomCard.Label"/>) as last shown; null where the card has none.</summary>
        public TextMeshPro? LabelShown { get; private set; }

        /// <summary>The list's heads (<see cref="RoomCard.Heads"/>) as last shown, the names' and the notes'; null where it has none.</summary>
        public (TextMeshPro Name, TextMeshPro Note)? HeadsShown { get; private set; }

        /// <summary>The gap under a list's heads before its first row (tile f1).</summary>
        public const float HeadsGapDp = 6f;

        /// <summary>The labels shown as an excerpt of outside words, their lead cut at a word (a row that opens them whole).</summary>
        public List<TextMeshPro> Excerpts { get; } = new List<TextMeshPro>();

        /// <summary>
        /// The labels whose last line stops at a word with its tail faded, never an ellipsis: a card's title in the person's own
        /// words past its lines (AUDIT 2026-11-16 13:53Z: "A page that greets each visitor by..." on the recap, drawn). For checks.
        /// </summary>
        public HashSet<TMP_Text> FadedTails { get; } = new HashSet<TMP_Text>();

        /// <summary>How many slots' values were wider than the card and wrapped whole in a well of their own.</summary>
        public int SlotsWrapped { get; private set; }

        /// <summary>Every tile, mark and plate the card shows now, with what it is: for checks.</summary>
        public List<(RoomShape Plate, string What)> Plates { get; } = new List<(RoomShape, string)>();

        private readonly List<int> listLines = new List<int>();

        /// <summary>Each list row's lines as the card last laid it (its label, name, a note under the name, its quieter line), in order.</summary>
        public IReadOnlyList<int> ListLines => listLines;

        /// <summary>
        /// The lines <paramref name="row"/> takes laid as a card's list row <paramref name="width"/> wide, by the very path the card
        /// draws it (its mark's room, its swatch's, its label, name, note and quieter line): one measure for the file that pages a
        /// question and the reader that lays its answers as rows, so the two never count apart (AUDIT's 2026-11-13T2206Z; the lesson
        /// of charN's part 2). Lays this view: call it on a view kept for measuring, never on one shown.
        /// </summary>
        public int LinesOfListRow(RoomListRow row, float width)
        {
            Show(new RoomCard(list: new[] { row }), width);
            return listLines.Count > 0 ? listLines[0] : 1;
        }

        /// <summary>
        /// Whether the card as last shown stands whole: every label it laid (the title, the line, each slot's value, each
        /// row, each line of reading, the foot line) shows all its words, none cut or ellipsed. XR's host reports a frame
        /// drawn only while this holds, so nothing counts as read that was cut (XR, 0750Z; the review before Start).
        /// </summary>
        public bool LaidWhole
        {
            get
            {
                if (!gameObject.activeInHierarchy) return false;
                foreach (var label in Shown)
                {
                    label.ForceMeshUpdate(true);
                    // A paged description runs on past its page by design; its page stands whole, broken between lines.
                    if (label == PagedDescription) continue;
                    // Overflowing, or cut to an ellipsis (a row's note in the label role's capitals at Larger: AUDIT2's 01:55Z MUST).
                    if (label.isTextOverflowing || label.isTextTruncated) return false;
                    if (!string.IsNullOrEmpty(label.text) && label.textInfo.characterCount == 0) return false;
                }
                return true;
            }
        }

        /// <summary>The description's label where it pages (<see cref="RoomCard.DescriptionPage"/>); null where it stands whole.</summary>
        /// <summary>The paged description's whole text as laid, and where the page shown starts in it: for the checks.</summary>
        public string PagedDescriptionWhole { get; private set; } = "";
        public int PagedDescriptionStart { get; private set; }

        /// <summary>The page of the paged description shown, from 0.</summary>
        public int PagedDescriptionPage { get; private set; }

        /// <summary>How many lines the paged description's page shows: four, or what is left on the last page.</summary>
        public int PagedDescriptionLines { get; private set; }

        public TextMeshPro? PagedDescription { get; private set; }

        /// <summary>The pages the description made as last shown: 1 where it does not page. The flow pages the review by it.</summary>
        public int DescriptionPages { get; private set; } = 1;

        /// <summary>The labels of the card's page of reading now, a file's own lines: for checks, which read no separator in them.</summary>
        public List<TextMeshPro> ReadingShown { get; } = new List<TextMeshPro>();

        /// <summary>The code to scan the card shows now; null where it shows none. For checks.</summary>
        public RoomCodeView? CodeShown => codeUsed ? codeView : null;

        /// <summary>Every glyph the card shows now, with what it is: for checks.</summary>
        public List<(RoomGlyphView Glyph, string What)> Glyphs { get; } = new List<(RoomGlyphView, string)>();

        /// <summary>A card under <paramref name="parent"/>, hidden, its words drawn at <paramref name="order"/> and its tiles under them.</summary>
        public static RoomCardView Create(Transform parent, int order = KitType.Order.Words)
        {
            var go = new GameObject("Card");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomCardView>();
            view.order = order;
            // A code sample repaints on a theme change, laying nothing again (the Code Colours preview); a card with none ignores it.
            CodeThemes.Changed += view.SampleThemeChanged;
            go.SetActive(false);
            return view;
        }

        /// <summary>The card shown now where it carries a code sample (<see cref="RoomCard.Sample"/>); null otherwise.</summary>
        private RoomCard? sampleCard;

        /// <summary>For each sample row drawn, in the order its words were laid: the sample's line it belongs to and where in that line it starts.</summary>
        private readonly List<(int Line, int Start)> sampleRows = new List<(int, int)>();

        /// <summary>A card view destroyed before it was ever active never ran OnDestroy (Unity skips it), so it lets go here.</summary>
        private void SampleThemeChanged(CodeTheme _)
        {
            if (this == null)
            {
                CodeThemes.Changed -= SampleThemeChanged;
                return;
            }
            ColourSample();
        }

        private void OnDestroy() => CodeThemes.Changed -= SampleThemeChanged;

        /// <summary>
        /// Colours the sample's rows by the palette in force: each LOGICAL line tokenized whole, one carry through the lines in order,
        /// its runs mapped onto the rows it wraps to, so a wrapped keyword or string keeps its colour on both halves (AUDIT3's PV1).
        /// The text is never changed; nothing is laid again.
        /// </summary>
        private void ColourSample()
        {
            if (sampleCard == null) return;
            var palette = CodeColouring.Now();
            var carry = CodeCarry.None;
            var lineRuns = new List<IReadOnlyList<CodeToken>>(sampleCard.Sample.Count);
            foreach (var line in sampleCard.Sample) lineRuns.Add(CodeTokens.Of(line, sampleCard.SampleLanguage, ref carry));
            for (var index = 0; index < ReadingShown.Count && index < sampleRows.Count; index++)
            {
                var label = ReadingShown[index];
                if (label == null) continue;
                var (line, start) = sampleRows[index];
                var length = label.text.Length;
                var ranges = new List<(int Start, int End, Color32 Colour)>();
                // Paint only a row whose words are exactly its slice of the line (AUDIT3's N-b): a spelled character or a shifted
                // offset leaves the row in its own white rather than colour the wrong letters.
                var source = sampleCard.Sample[line];
                if (start + length > source.Length || string.CompareOrdinal(source, start, label.text, 0, length) != 0)
                {
                    GlazeRuns.Of(label).Paint(ranges);
                    continue;
                }
                foreach (var run in lineRuns[line])
                {
                    var from = Mathf.Max(run.Start, start);
                    var to = Mathf.Min(run.Start + run.Length, start + length);
                    if (to > from && palette(run.Kind) is Color32 colour) ranges.Add((from - start, to - start, colour));
                }
                GlazeRuns.Of(label).Paint(ranges);
            }
        }

        /// <summary>
        /// Lays <paramref name="card"/>'s code sample as a bare reading wrapped to <paramref name="width"/> at the person's text size (each
        /// row's place in its line kept for the colours), with no well under it, as Watch It Work's code stands on the glass.
        /// </summary>
        private float LaySample(RoomCard card, float width, float down)
        {
            var widthDp = width / RoomType.Units(1f);
            var columns = RoomReading.ColumnsIn(widthDp, GlazeText.Scale, bare: true);
            var continuing = RoomReading.ColumnsIn(widthDp, GlazeText.Scale, continued: true, bare: true);
            var rows = new List<RoomReadingRow>();
            sampleRows.Clear();
            for (var line = 0; line < card.Sample.Count; line++)
            {
                var text = card.Sample[line];
                var cursor = 0;
                foreach (var row in FileChangeColumn.Wrap(RoomLineKind.Same, null, text, columns, continuing))
                {
                    var words = row.Text?.Text ?? "";
                    var at = words.Length == 0 ? cursor : text.IndexOf(words, cursor, System.StringComparison.Ordinal);
                    if (at < 0) at = cursor;
                    cursor = at + words.Length;
                    rows.Add(new RoomReadingRow(row.Kind, null, words.Length == 0 ? null : LineWords.Ours(words), row.Continued, row.Indent));
                    if (words.Length > 0) sampleRows.Add((line, at));
                }
            }
            return LayReading(rows, width, down, null, bare: true, well: false);
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Shows <paramref name="card"/> <paramref name="width"/> wide at the person's text size, and returns how tall it stands.</summary>
        public float Show(RoomCard card, float width)
        {
            gameObject.SetActive(true);
            // A card that is the way in stands its chevron beside its name's first line, at the text's own inset from the edge (the
            // critic, 2026-11-12: it sat 7 px from the ring against a 36 px inset), so only the name is laid a chevron's room narrower
            // and never meets it; the person's own words, the slots and the note under it take the card's whole width (the lead's
            // 2026-11-14T03:21Z (A): the recap at its budgets read under 14 dp at the floor with every word narrowed).
            var full = width;
            var named = card.Title != null || card.Head != RoomCardHead.None;
            var beside = card.Press != null ? Mathf.Max(S(StepDp), full - S(CardChevronDp + RowGapDp)) : full;
            chevronAt = null;
            foreach (var role in new List<KitRole>(textsUsed.Keys)) textsUsed[role] = 0;
            foreach (var cap in capLabels) cap.characterSpacing = 0f;
            capLabels.Clear();
            platesUsed = 0;
            gaugeFills.Clear();
            gaugeNumbers.Clear();
            glyphsUsed = 0;
            rowPressesUsed = 0;
            codeUsed = false;
            pictureUsed = false;
            PagedDescription = null;
            PagedDescriptionLines = 0;
            PagedDescriptionPage = 0;
            DescriptionPages = 1;
            Shown.Clear();
            Excerpts.Clear();
            FadedTails.Clear();
            SlotsWrapped = 0;
            ReadingShown.Clear();
            Plates.Clear();
            Glyphs.Clear();
            listLines.Clear();
            LabelShown = null;
            HeadsShown = null;
            var down = 0f;
            void Gap(float dp) { if (down > 0f) down += S(dp); }

            // A thing's name first where it has one, its state beneath it (the critic, 2026-11-11: a status over the name read as
            // a second title); a card with no name leads with its status, the chevron beside its mark.
            if (!named && card.Status is RoomCardStatus lead)
            {
                down = LayStatus(lead, card, beside, down);
                chevronAt = S(StatusMarkDp) / 2f;
            }
            if (named)
            {
                // The review card's state word over its name, in the one language's spaced capitals (FIELD's f6).
                if (card.Eyebrow != null) down = LayCap(card.Eyebrow, width, down) + S(EyebrowGapDp);
                else Gap(BlockGapDp);
                down = LayHead(card, beside, down);
                if (card.Status is RoomCardStatus status && !card.StatusAfter)
                {
                    Gap(BlockGapDp);
                    down = LayStatus(status, card, width, down);
                }
            }
            // The agent's own first sentence under THE AGENT SAYS, its quote mark before it: reported, theirs (AUDIT2, AUDIT3).
            if (card.Says is LineWords says)
            {
                Gap(BlockGapDp);
                down = LayCap(SaysLabel, width, down) + S(CapGapDp);
                // On the card's one left edge (visionos-window-example.png; the lead's 19:38Z fault 5): the quote mark opens the words
                // in their own line and closes them, never a mark hung in a gutter beside them (VRIFY's 19:40Z critic: "off the text's left edge").
                var (_, saidTall) = Text(KitRole.Body, SaysTypeDp, SaysLineDp, White(SaysAlpha), LineWords.Ours("\u201C") + says + LineWords.Ours("\u201D"), 0f, -down, width, SaysLines);
                down += saidTall;
                // Where the reply goes on, the rest is one press away, quiet under the quote (the lead's 15:17Z; AUDIT3's R6 SHOULD).
                if (card.SaysAll is KitControl all)
                {
                    var indent = 0f;
                    var top = down + S(ReadAllGapDp);
                    var (readLabel, readTall) = Text(KitRole.Strong, FactLineDp, FactLineLineDp, White(ReadAllAlpha), LineWords.Ours(all.Words), indent, -top,
                        width - indent - S(ReadAllChevronDp + 6f), 1);
                    var wordsWide = RoomCursor.WordsWidth(readLabel, 0f);
                    Glyph(RoomGlyph.Chevron, ReadAllChevronDp, White(ReadAllAlpha), new Vector2(indent + wordsWide + S(6f) + S(ReadAllChevronDp) / 2f, -(top + readTall / 2f)),
                        "read all chevron", CardChevronStroke);
                    var wide = wordsWide + S(6f + ReadAllChevronDp);
                    RowTarget(all, new Vector2(indent + wide / 2f, -(top + readTall / 2f)), new Vector2(wide + S(12f), readTall + S(12f)));
                    down = top + readTall;
                }
            }

            // A card never says the same words twice: a description that only repeats the title is not drawn (an idea that is
            // also its name; the critic, 2026-11-12).
            var description = card.Description != null && card.Title != null && string.Equals(card.Description.Text.Trim(), card.Title.Text.Trim(), System.StringComparison.Ordinal)
                ? null : card.Description;
            if (description != null)
            {
                // Under a head or a status a block's gap, as every other block, so another's words never read as the status's
                // line (the critic, 2026-11-11); under a title alone, its own small gap.
                down += S(card.Head != RoomCardHead.None || card.Status != null ? BlockGapDp : DescriptionGapDp);
                if (card.DescriptionPage is int page)
                {
                    // Paged: exactly four whole lines a page, by the card's own count (JUDGE's 06:08Z: TMP's page mode filled a page
                    // by its box's height and a fifth line stood cut at the card's foot). The words laid whole; the page's lines shown
                    // alone and lifted to the block's top; the page asked for, or the last there is.
                    var (label, _) = Text(KitRole.Body, DescriptionTypeDp, DescriptionLineDp, White(DescriptionAlpha(card)), card.Description, 0f, -down, width, RoomRowsView.RowLines * 4);
                    label.overflowMode = TextOverflowModes.Overflow;
                    label.ForceMeshUpdate(true);
                    var info = label.textInfo;
                    var lineCount = Mathf.Max(1, info.lineCount);
                    DescriptionPages = Mathf.Max(1, (lineCount + RoomCard.DescriptionPageLines - 1) / RoomCard.DescriptionPageLines);
                    var shownPage = Mathf.Min(page, DescriptionPages - 1);
                    var firstLine = shownPage * RoomCard.DescriptionPageLines;
                    var lastLine = Mathf.Min(firstLine + RoomCard.DescriptionPageLines, lineCount) - 1;
                    PagedDescriptionWhole = label.text;
                    PagedDescriptionStart = 0;
                    if (info.lineCount > 0)
                    {
                        // The page's own words alone, cut between the whole layout's lines (never inside a word), their outside runs
                        // moved with them; laid again at the same width they break where the whole did.
                        var whole = label.text;
                        var start = info.characterInfo[info.lineInfo[firstLine].firstCharacterIndex].index;
                        var end = lastLine + 1 < info.lineCount ? info.characterInfo[info.lineInfo[lastLine + 1].firstCharacterIndex].index : whole.Length;
                        var runs = GlazeRuns.Of(label);
                        var ranges = runs.Theirs.Select(run => (Start: Mathf.Max(run.Start, start) - start, End: Mathf.Min(run.End, end) - start)).Where(run => run.End > run.Start).ToList();
                        label.text = whole.Substring(start, end - start);
                        runs.Lean(ranges);
                        KitType.Lay(label, width, RoomCard.DescriptionPageLines);
                        PagedDescriptionStart = start;
                    }
                    PagedDescription = label;
                    PagedDescriptionLines = lastLine - firstLine + 1;
                    PagedDescriptionPage = shownPage;
                    down += PagedDescriptionLines * S(DescriptionLineDp);
                }
                else
                {
                    var (described, tall) = Text(KitRole.Body, DescriptionTypeDp, DescriptionLineDp, White(DescriptionAlpha(card)), card.Description, 0f, -down, width, card.DescriptionMostLines);
                    // Someone's own words on a card that presses to them whole (the recap's first task, the lead's 2026-11-14T02:04Z):
                    // their lead cut at a word in the card's lines, counted whole as a row's excerpt is. Our own words are never cut.
                    if (card.Press != null && card.Description.AllTheirs) tall = Excerpt(described, card.Description, width, card.DescriptionMostLines, DescriptionLineDp);
                    down += tall;
                }
            }
            // A status the card's model declares a warning about what the description says (RoomCard.StatusAfter): after it, so the
            // main fact reads first (AUDIT2's 2026-11-13T2237Z).
            if (named && card.StatusAfter && card.Status is RoomCardStatus after)
            {
                Gap(BlockGapDp);
                down = LayStatus(after, card, width, down);
            }
            if (card.Gauges.Count > 0)
            {
                Gap(BlockGapDp);
                down = LayGauges(card.Gauges, width, down);
            }
            if (card.Code is RoomCode code)
            {
                Gap(BlockGapDp);
                down = LayCode(code, width, down);
            }
            if (card.Slots.Count > 0)
            {
                Gap(BlockGapDp);
                down = card.Slots.Any(slot => slot.Label != null) ? LayFacts(card.Slots, width, down) : LaySlots(card.Slots, width, down);
            }
            if (card.Reading.Count > 0)
            {
                Gap(BlockGapDp);
                down = LayReading(card.Reading, width, down, card.ReadingLit, card.ReadingBare);
            }
            sampleCard = null;
            if (card.Sample.Count > 0 && card.Reading.Count == 0)
            {
                Gap(BlockGapDp);
                down = LaySample(card, width, down);
                sampleCard = card;
                ColourSample();
            }
            if (card.Steps.Count > 0)
            {
                Gap(BlockGapDp);
                down = LaySteps(card.Steps, width, down);
            }
            if (card.List.Count > 0)
            {
                Gap(ListGapDp);
                down = LayList(card.List, width, down, card.Heads);
            }
            if (card.Results.Count > 0)
            {
                Gap(BlockGapDp);
                down = LayResults(card.Results, width, down);
            }
            if (card.More != null)
            {
                // A block's own gap, as between every other block (the critic, 2026-11-11: the foot sat tight).
                Gap(BlockGapDp);
                var (_, tall) = Text(KitRole.Body, MoreDp, MoreLineDp, White(NoteAlpha), card.More, 0f, -down, width, 2);
                down += tall;
            }
            LayCardPress(card, full, down);
            HideUnused();
            Size = new Vector2(full, down);
            return down;
        }

        /// <summary>
        /// A card that is the way in (<see cref="RoomCard.Press"/>): its whole face a press target behind the parts that take
        /// their own, an accent face under its words that brightens while it will take the press.
        /// </summary>
        private void LayCardPress(RoomCard card, float width, float tall)
        {
            if (card.Press is KitControl next && (cardControl is not KitControl was || RoomButton.Doing(was) != RoomButton.Doing(next))) cardPress?.Unsettle();
            cardControl = card.Press;
            PanelRing = null;
            if (card.Press == null)
            {
                if (cardPress != null) cardPress.gameObject.SetActive(false);
                if (cardFace != null) cardFace.gameObject.SetActive(false);
                return;
            }
            // The face is the card: it fills the panel to its edge (the critic, 2026-11-12: a grey rim round an accent face read as
            // a card inside a card), and the chevron of a way in stands in the panel's right margin, clear of every word.
            var pad = S(RoomGeometry.PanelPadDp);
            var size = new Vector2(width + 2f * pad, tall + 2f * pad);
            if (cardPress == null)
            {
                var go = new GameObject("Card press");
                go.transform.SetParent(transform, false);
                cardPress = KitPress.Create(go, size);
                cardPress.Pressed += () => { if (cardControl is KitControl pressed) RowPressed?.Invoke(pressed); };
                cardFace = RoomShape.Create(transform, "Card face", order - 6);
            }
            cardPress.gameObject.SetActive(true);
            cardPress.Resize(size);
            cardPress.Available = true;
            // Behind the rows' and slots' own targets, so a part that takes its own press keeps it.
            cardPress.transform.localPosition = new Vector3(width / 2f, -tall / 2f, -RoomType.Units(0.4f));
            cardFace!.gameObject.SetActive(true);
            cardFace.transform.localPosition = new Vector3(width / 2f, -tall / 2f, -RoomType.Units(0.1f));
            cardFaceSize = size;
            // One hue on the card: the way in takes its status's own tone where it has one (a request that needs the person is
            // the attention amber its mark already wears), and a quiet neutral light where it has none, never the accent: the one
            // accent is the main action's (the owner's 2026-11-10 rule; AUDIT2's 2026-11-14T01:28Z on the recap's card beside Start
            // Task). Its chevron says it opens.
            cardFaceHue = card.Status is RoomCardStatus said && said.Tone == RoomTone.Attention ? Attention
                : card.Status is RoomCardStatus good && good.Tone == RoomTone.Good ? GoodFill : ChosenAtRest;
            cardFaceLit = null;
            PaintCardFace();
            // The way on at the words' right edge, the panel's pad from its ring as the text is from the left, a capital's stem in weight.
            Glyph(RoomGlyph.Chevron, CardChevronDp, White(0.90f), new Vector2(width - S(CardChevronDp) / 2f, -(chevronAt ?? tall / 2f)), "card chevron", CardChevronStroke);
        }

        private void PaintCardFace()
        {
            if (cardFace == null || cardPress == null || !cardFace.gameObject.activeSelf) return;
            var lit = cardPress.Hovered || cardPress.Pressing;
            if (cardFaceLit == lit) return;
            cardFaceLit = lit;
            // Its edge the hue itself, firm enough to read over the bright room (at 0.55 it stood at 1.9:1 there), on the panel's
            // own corner so no plain glass shows outside it; its face a quiet wash of the hue.
            // The wash alone: the ring is the panel's own edge (PanelRing), drawn on the panel's own corner, so no glass shows
            // outside it (the critic, 2026-11-12: a round edge over the squircle left grey crescents at each corner).
            cardFace.Draw(cardFaceSize, RoomType.Units(RoomGeometry.PanelCornerDp), AsShown(Rgb(cardFaceHue, lit ? 0.22f : 0.14f)), Color.clear, 0f);
            PanelRing = (Rgb(cardFaceHue), 3f);
        }

        /// <summary>The chevron of a card that is the way in: as tall as a title's capitals, so the way on reads at a glance.</summary>
        public const float CardChevronDp = 28f;

        /// <summary>The chevron's stroke: a capital's stem, so it weighs as the words beside it.</summary>
        public const float CardChevronStroke = 2.6f;

        private void Update()
        {
            PaintCardFace();
            EaseGauges();
        }

        /// <summary>The seconds every gauge's move is timed by; a render fixes it.</summary>
        public System.Func<float> Clock { get; set; } = () => Time.unscaledTime;

        // Each gauge's reading as it moves, by its window: where it started, where it goes and when (RoomMotion.Gauge).
        private readonly Dictionary<RoomKind, (float From, float To, float Since)> gaugeEases = new Dictionary<RoomKind, (float, float, float)>();
        // The fills laid this show, so a move is drawn each frame until it ends.
        private readonly List<(RoomShape Fill, RoomKind Kind, float Bar, float Middle)> gaugeFills = new List<(RoomShape, RoomKind, float, float)>();
        // Each gauge's number, which follows its bar in whole percents while it moves (AUDIT's SHOULD, 2026-11-16).
        private readonly List<(TextMeshPro Number, RoomKind Kind)> gaugeNumbers = new List<(TextMeshPro, RoomKind)>();

        /// <summary>A gauge's number as it reads now: its bar's place, in whole percents, in the product's own words ("62% Left", AUDIT2's 10:05Z).</summary>
        public static string GaugeWords(float percent) => RoomPanelCards.GaugeWords(Mathf.RoundToInt(Mathf.Clamp(percent, 0f, 100f)));

        /// <summary>The reading a gauge's number shows as it is laid: where its bar stands, the move started first where its reading changed.</summary>
        private float GaugeShownBefore(RoomGauge gauge)
        {
            var now = Clock();
            if (!gaugeEases.TryGetValue(gauge.Kind, out var ease) || !Mathf.Approximately(ease.To, gauge.PercentLeft))
            {
                var from = gaugeEases.ContainsKey(gauge.Kind) ? GaugeShown(gauge.Kind) : gauge.PercentLeft;
                gaugeEases[gauge.Kind] = (from, gauge.PercentLeft, now);
            }
            return GaugeShown(gauge.Kind);
        }

        /// <summary>A gauge's bar as it stands now, in percent left: its new reading, or on its way there from the one it showed.</summary>
        public float GaugeShown(RoomKind kind) =>
            gaugeEases.TryGetValue(kind, out var ease) ? RoomMotion.Gauge(GlazeMotion.Still ? float.PositiveInfinity : Clock() - ease.Since, ease.From, ease.To) : 0f;

        private void EaseGauges()
        {
            foreach (var (fill, kind, bar, middle) in gaugeFills)
                if (gaugeEases.TryGetValue(kind, out var ease) && !GlazeMotion.Still && Clock() - ease.Since <= RoomMotion.GaugeSeconds + 0.05f)
                    DrawGaugeFill(fill, bar, middle, GaugeShown(kind));
            foreach (var (number, kind) in gaugeNumbers)
            {
                if (!gaugeEases.TryGetValue(kind, out var ease) || GlazeMotion.Still || Clock() - ease.Since > RoomMotion.GaugeSeconds + 0.05f) continue;
                var words = GaugeWords(GaugeShown(kind));
                if (number.text == words) continue;
                GlazeRuns.Of(number).Set(LineWords.Ours(words));
                number.ForceMeshUpdate(true);
            }
        }

        private void DrawGaugeFill(RoomShape fill, float bar, float middle, float percent)
        {
            var filled = bar * Mathf.Clamp(percent, 0f, 100f) / 100f;
            fill.gameObject.SetActive(filled > 1e-4f);
            if (filled <= 1e-4f) return;
            fill.Draw(new Vector2(filled, S(TrackTallDp)), Mathf.Min(S(TrackTallDp) / 2f, filled / 2f), AsShown(White(0.62f)), AsShown(default), 0f);
            fill.transform.localPosition = new Vector3(S(GaugePadXDp) + filled / 2f, -middle, fill.transform.localPosition.z);
        }

        /// <summary>
        /// The ring the panel holding the card draws as its edge where the card is the way in (RoomSurface.Ring): the card's
        /// hue, firmer while it will take the press; null for a card that only says. Read after Show and each frame.
        /// </summary>
        public (Color Colour, float WidthDp)? PanelRing { get; private set; }

        /// <summary>Whether the shown card is itself a press target, and the control it raises.</summary>
        public KitControl? CardTarget => cardPress != null && cardPress.gameObject.activeSelf ? cardControl : null;

        private float LayStatus(RoomCardStatus status, RoomCard card, float width, float down)
        {
            // Whose words the card holds (RoomKind.Says) is a byline, not a state: the bubble small and bare beside the words,
            // no filled tile, which read as a button (SCREENS's critic, 2026-11-13).
            if (status.Mark == RoomKind.Says) return LayByline(status, width, down);
            var (ground, ink, glyph) = status.Tone switch
            {
                // Deep grounds of the tone's hue, near solid, the light ink over them: a faint tint vanished over the bright room
                // (the critic, 2026-11-11), where the recap's solid head held.
                // The one language (the lead's 2026-11-17 01:40Z ruling): a thin white line mark with no tile, its SHAPE the state;
                // amber only where it waits for you; the orange stays the main action's rim alone.
                RoomTone.Good => (Color.clear, Color.white, RoomGlyph.Check),
                RoomTone.Bad => (Color.clear, Color.white, RoomGlyph.Alert),
                RoomTone.Working => (Color.clear, Color.white, RoomGlyph.Wrench),
                RoomTone.Attention => (Color.clear, Rgb(Attention), RoomGlyph.Bubble),
                _ => (Color.clear, Color.white, RoomGlyph.Minus),
            };
            if (status.Mark != RoomKind.None) glyph = GlyphOf(status.Mark);
            var left = S(StatusMarkDp + StatusGapDp);
            var column = width - left;
            var top = down;
            var under = card.Title != null;
            // Under a name the state is a step smaller in the status's own weight; under a file's mono name, whose face is regular,
            // in the reading face, so the name stays the heavier of the two.
            var titleRole = under && card.TitleMono ? KitRole.Body : KitRole.Title;
            var (titleLabel, titleTall) = Text(titleRole, under ? UnderTitleStatusDp : StatusTitleDp, under ? UnderTitleStatusLineDp : StatusTitleLineDp, Color.white,
                LineWords.HostPlain(status.Title), left, -down, column, 2);
            // A state is never cut (JUDGE's live walk at Larger: "Turn Finished" cut beside the mark on the narrow form): a step
            // smaller on three lines beside the mark, else under the mark across the card's whole width at its own size.
            var stacked = false;
            if (titleLabel.isTextOverflowing)
            {
                void Unlay(TextMeshPro label)
                {
                    label.gameObject.SetActive(false);
                    Shown.Remove(label);
                    textsUsed[titleRole]--;
                }
                Unlay(titleLabel);
                (titleLabel, titleTall) = Text(titleRole, under ? FinalTitleTypeDp : UnderTitleStatusDp, under ? FinalTitleLineDp : UnderTitleStatusLineDp, Color.white,
                    LineWords.HostPlain(status.Title), left, -down, column, 3);
                if (titleLabel.isTextOverflowing)
                {
                    Unlay(titleLabel);
                    stacked = true;
                    left = 0f;
                    column = width;
                    (titleLabel, titleTall) = Text(titleRole, under ? UnderTitleStatusDp : StatusTitleDp, under ? UnderTitleStatusLineDp : StatusTitleLineDp, Color.white,
                        LineWords.HostPlain(status.Title), 0f, -(down + S(StatusMarkDp) + S(DescriptionGapDp)), width, 3);
                }
            }
            var tall = stacked ? S(StatusMarkDp) + S(DescriptionGapDp) + titleTall : titleTall;
            TextMeshPro? lineLabel = null;
            if (status.Line != null)
            {
                // The product's own words are never cut: a status line runs as long as it needs (the host's money words before a
                // paid model, four sentences under Check First, the lead's 0635Z).
                // Laid a dp inside its room, so a glyph's ink at the end of a wrapped line never passes the card's edge.
                var laidLine = Text(KitRole.Body, LineTypeDp, LineLineDp, White(NoteAlpha), LineWords.HostPlain(status.Line), left, -(down + tall + S(2f)), column - S(1f), StatusLineMost);
                lineLabel = laidLine.Label;
                if (!stacked && laidLine.Tall > StatusColumnLines * S(LineLineDp) + 0.5f)
                {
                    // Too long for the column beside the mark (the host's money words under Check First): the title stands centred
                    // on the mark, and the line runs the card's whole width under both, a paragraph, never a thin column.
                    var titleDrop = Mathf.Max(0f, (S(StatusMarkDp) - titleTall) / 2f);
                    titleLabel.transform.localPosition += new Vector3(0f, -titleDrop, 0f);
                    var head = Mathf.Max(S(StatusMarkDp), titleTall) + S(DescriptionGapDp);
                    var wide = KitType.Lay(lineLabel, width - S(1f), StatusLineMost);
                    lineLabel.transform.localPosition = new Vector3(0f, -(down + head), -RoomType.Units(0.5f));
                    tall = head + Mathf.Max(wide.Lines, 1) * S(LineLineDp);
                }
                else tall += S(2f) + laidLine.Tall;
            }
            else if (status.Lines.Count > 0)
            {
                // Lines kept apart (RoomCardStatus.Lines): the title centred on the mark, then each line its own short paragraph the
                // card's whole width under both, never joined into one. Each is a thing the person must read (where their requests go,
                // the price), so it stands at the description's size and light, not a quieter note's, and a paragraph's gap clearly
                // wider than a line apart separates them (a fresh reader's 2026-11-14T0100Z: gaps barely wider than the pitch, the
                // warnings smaller and dimmer than the line above).
                // A title stacked under the mark (a state too long beside it, never cut) already stands where it is read: the lines
                // start under it, never over it.
                if (!stacked)
                {
                    var titleDrop = Mathf.Max(0f, (S(StatusMarkDp) - titleTall) / 2f);
                    titleLabel.transform.localPosition += new Vector3(0f, -titleDrop, 0f);
                }
                var at = (stacked ? tall : Mathf.Max(S(StatusMarkDp), titleTall)) + S(DescriptionGapDp);
                foreach (var each in status.Lines)
                {
                    var (_, eachTall) = Text(KitRole.Body, DescriptionTypeDp, LinesApartLineDp, White(DescriptionAlpha(card)), LineWords.HostPlain(each), 0f, -(down + at), width - S(1f), StatusLineMost);
                    at += eachTall + S(LinesApartGapDp);
                }
                tall = at - S(LinesApartGapDp);
            }
            var block = Mathf.Max(S(StatusMarkDp), tall);
            // Words shorter than the mark stand centred on it, a title alone too (SCREENS's critic, 2026-11-11).
            var drop = (block - tall) / 2f;
            if (drop > 0f)
            {
                titleLabel.transform.localPosition += new Vector3(0f, -drop, 0f);
                if (lineLabel != null) lineLabel.transform.localPosition += new Vector3(0f, -drop, 0f);
            }
            // The mark beside the title: centred on words shorter than it, its top on the title's where they run taller.
            var middle = top + S(StatusMarkDp) / 2f;
            // A squircle, as the check nodes: a disc is only ever an answer.
            Plate(new Vector2(S(StatusMarkDp), S(StatusMarkDp)), S(StatusCornerDp), ground, new Vector2(S(StatusMarkDp) / 2f, -middle), "status mark");
            Glyph(glyph, StatusGlyphDp, ink, new Vector2(S(StatusMarkDp) / 2f, -middle), "status " + status.Tone, MarkStroke);
            // The tries as pips while they run; once spent, where the line says them in words ("The agent tried twice and could
            // not fix it."), the pips would say nothing the line does not (AUDIT and AUDIT2, 10:05Z): none.
            var spentAndSaid = card.TrackOf is int all && (card.TrackAt ?? 0) >= all && !string.IsNullOrWhiteSpace(status.Line);
            if (card.TrackOf is int of && !spentAndSaid)
            {
                var used = Mathf.Clamp(card.TrackAt is int at ? at > of ? of : at : 0, 0, of);
                Pips(used, of, status.Tone, new Vector2(S(StatusMarkDp) / 2f, -(middle + S(StatusMarkDp) / 2f + S(PipDropDp) - S(PipDp) / 2f)));
                block = Mathf.Max(block, S(StatusMarkDp) + S(PipDropDp));
            }
            return top + block;
        }

        /// <summary>Whose words follow: the speech bubble at a row mark's size, bare, and the speaker's name beside it at a note's size, quiet.</summary>
        private float LayByline(RoomCardStatus status, float width, float down)
        {
            // The bubble close to its speaker's name, nearer than the byline stands to the words under it, so it reads as a
            // byline and not a chip (SCREENS's critic 6, 2026-11-13).
            var left = S(RowGlyphDp + BylineGapDp);
            // At a note's size, so the speaker's name never outranks the words it introduces (SCREENS's critic, 2026-11-13).
            // Whose words follow stands clearly over them, so a change of speaker between parts registers (critic 8): the note's
            // size, the name's light.
            var (label, tall) = Text(KitRole.Body, RowNoteDp, RowNoteLineDp, Color.white, LineWords.HostPlain(status.Title), left, -down, width - left, 1);
            var block = Mathf.Max(S(RowGlyphDp), tall);
            var drop = (block - tall) / 2f;
            if (drop > 0f) label.transform.localPosition += new Vector3(0f, -drop, 0f);
            Glyph(RoomGlyph.Bubble, RowGlyphDp, White(0.85f), new Vector2(S(RowGlyphDp) / 2f, -(down + block / 2f)), "byline " + status.Mark);
            return down + block;
        }

        /// <summary>
        /// The code to scan in the card's middle: a near-white well as wide as the card allows up to <see cref="CodeDp"/>, the
        /// host's cells inside it with <see cref="RoomCode.MarginCells"/> light cells round them. A cell is a whole share of
        /// the well, so every cell stands the same size.
        /// </summary>
        private float LayCode(RoomCode code, float width, float down)
        {
            // The card's own width, so the code shares every block's left edge (the critic, 2026-11-11), up to its most.
            var well = Mathf.Min(width, RoomType.Units(CodeDp));
            var cell = well / (code.Side + 2 * RoomCode.MarginCells);
            var centre = new Vector2(width / 2f, -(down + well / 2f));
            Plate(new Vector2(well, well), Mathf.Min(S(TileCornerDp), cell * CodeCornerCells), Rgb(CodeGround), centre, "code well");
            if (codeView == null) codeView = RoomCodeView.Create(transform, "Code", order - 3);
            codeView.Show(code, cell * code.Side, Rgb(CodeInk));
            codeView.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(0.4f));
            codeUsed = true;
            return down + well;
        }

        /// <summary>
        /// A page of a file's lines (<see cref="RoomReading"/>'s measures): each row its line number at the gutter's right,
        /// quiet; an added line's plus in green and a removed line's minus in red, small and bare, over a faint band of its
        /// hue the card's width, so a run of changed lines reads as one block; the words mono, theirs, stood their indent in,
        /// a line the change left a step quieter; a continued row hung in under its start; a gap a hairline across.
        /// </summary>
        /// <summary>How far left of a bare reading's words a lit run's rule stands, its middle, in dp: inside the panel's padding.</summary>
        public const float BareRuleOutDp = 12f;

        private float LayReading(IReadOnlyList<RoomReadingRow> rows, float width, float down, IReadOnlyList<int>? litRows = null, bool bare = false, bool well = true)
        {
            var lit = new HashSet<int>(litRows ?? System.Array.Empty<int>());
            // A dark well under the whole page, near solid, so a band keeps one colour whatever the room shows behind the
            // panel (the critic, 2026-11-11), as a diff view sets its code apart.
            var blockTall = 0f;
            foreach (var row in rows) blockTall += row.Kind == RoomLineKind.Gap && row.Number == null ? S(RoomReading.GapRowDp) : S(RoomReading.LineDp);
            if (well) Plate(new Vector2(width, blockTall + 2f * S(ReadingWellPadDp)), S(TileCornerDp), Rgb(ReadingWell, ReadingWellAlpha), new Vector2(width / 2f, -(down + blockTall / 2f + S(ReadingWellPadDp))), "reading well", under: true);
            down += S(ReadingWellPadDp);
            var advance = S(RoomReading.TypeDp) * RoomReading.AdvanceEm;
            var gutter = S(RoomReading.GutterDp);
            var pitch = S(RoomReading.LineDp);
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                if (row.Kind == RoomLineKind.Gap)
                {
                    // A hairline across, from the bands' own left edge, one dp at either size; where the gap says how many lines it
                    // skips, the count quiet in its middle, the hairline parted round it.
                    var gapTall = row.Number is int ? S(RoomReading.LineDp) : S(RoomReading.GapRowDp);
                    var gapMiddle = -(down + gapTall / 2f);
                    if (row.Number is int skipped)
                    {
                        var (count, _) = Text(KitRole.Mono, RoomReading.NumberDp, RoomReading.LineDp, White(NoteAlpha), LineWords.Ours(RoomReading.Skipped(skipped)), 0f, -down, width, 1);
                        var countWidth = RoomCursor.WordsWidth(count, 0f);
                        var countLeft = (width - countWidth) / 2f;
                        count.transform.localPosition = new Vector3(countLeft, -down, -RoomType.Units(0.5f));
                        var side = countLeft - S(10f);
                        Plate(new Vector2(side, RoomType.Units(1f)), 0f, White(0.18f), new Vector2(side / 2f, gapMiddle), "reading gap");
                        Plate(new Vector2(side, RoomType.Units(1f)), 0f, White(0.18f), new Vector2(width - side / 2f, gapMiddle), "reading gap part");
                    }
                    else Plate(new Vector2(width, RoomType.Units(1f)), 0f, White(0.18f), new Vector2(width / 2f, gapMiddle), "reading gap");
                    down += gapTall;
                    continue;
                }
                var middle = down + pitch / 2f;
                var changed = row.Kind == RoomLineKind.Added || row.Kind == RoomLineKind.Removed;
                // A row a later read changed (Watch It Work): a light tint of the one accent under the run of lit rows, its ends
                // rounded inside the well, and the accent's rule at its left, with no mark (the reader of 07:15Z: an opaque band was a slab).
                if (lit.Contains(index) && (index == 0 || !lit.Contains(index - 1)))
                {
                    var run = 0;
                    for (var next = index; next < rows.Count && lit.Contains(next); next++) run++;
                    var runTall = run * pitch;
                    var centreY = -(down + runTall / 2f);
                    Plate(new Vector2(width, runTall), S(6f), Rgb(Accent, LitTintAlpha), new Vector2(width / 2f, centreY), "reading lit band");
                    // Bare rows start at the card's edge, so their rule, a hairline, stands out in the padding, clear of the first letter.
                    Plate(new Vector2(bare ? S(1.5f) : S(3f), runTall), 0f, Rgb(Accent), new Vector2(bare ? -S(BareRuleOutDp) : S(1.5f), centreY), "reading lit rule", front: true);
                }
                if (changed && (index == 0 || rows[index - 1].Kind != row.Kind))
                {
                    // One band for the whole run of rows of this change, so no seam parts them; solid, the hue mixed into the
                    // well, and a rule of the hue at its left as a diff marks a run (the critic, 2026-11-11).
                    var run = 0;
                    for (var next = index; next < rows.Count && rows[next].Kind == row.Kind; next++) run++;
                    var runTall = run * pitch;
                    var added = row.Kind == RoomLineKind.Added;
                    var centreY = -(down + runTall / 2f);
                    Plate(new Vector2(width, runTall), 0f, added ? Rgb(AddedBand) : Rgb(RemovedBand), new Vector2(width / 2f, centreY), "reading band");
                    Plate(new Vector2(S(3f), runTall), 0f, added ? Rgb(Passed) : Rgb(RemovedRule), new Vector2(S(1.5f), centreY), "reading rule", front: true);
                }
                if (changed)
                {
                    if (!row.Continued)
                        Glyph(row.Kind == RoomLineKind.Added ? RoomGlyph.Plus : RoomGlyph.Minus, RoomReading.MarkDp, Rgb(row.Kind == RoomLineKind.Added ? Passed : Failed),
                            new Vector2(S(RoomReading.GutterDp + RoomReading.MarkGapDp + RoomReading.MarkDp / 2f), -middle), "reading mark");
                }
                if (row.Number is int number)
                {
                    var (label, _) = Text(KitRole.Mono, RoomReading.NumberDp, RoomReading.LineDp, White(NoteAlpha), LineWords.Ours(number.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        0f, -down, gutter, 1);
                    label.transform.localPosition = new Vector3(gutter - RoomCursor.WordsWidth(label, 0f), -(down + S(RoomReading.TypeDp - RoomReading.NumberDp) * 0.6f), -RoomType.Units(0.5f));
                }
                if (row.Text != null && !row.Text.IsEmpty)
                {
                    var left = S(RoomReading.TextLeftDp(1f, row.Continued, bare)) + row.Indent * advance;
                    var (label, _) = Text(KitRole.Mono, RoomReading.TypeDp, RoomReading.LineDp, White(changed || lit.Contains(index) ? 0.96f : 0.80f), row.Text, left, -down,
                        Mathf.Max(width - left - S(RoomReading.RightPadDp), advance), 1);
                    ReadingShown.Add(label);
                }
                down += pitch;
            }
            return down + S(ReadingWellPadDp);
        }

        /// <summary>
        /// Whether the card stands under the agent's words in the question's dialog (RoomBar.AsDialog): its title then takes the
        /// caption's size (16 dp), so the question over it stays the dialog's largest text (AUDIT's MUST 4).
        /// </summary>
        public bool TitleUnderWords { get; set; }

        private float LayHead(RoomCard card, float width, float down)
        {
            // Where the place is, over its title in the one language's label role (tile f1: HALORA over Projects).
            if (card.Label != null)
            {
                var (label, labelTall) = Text(KitRole.Body, RoomType.LabelDp, RowNoteLineDp, White(RoomType.LabelAlpha), LineWords.HostPlain(card.Label), 0f, -down, width, 1, labelRole: true);
                LabelShown = label;
                down += labelTall + S(4f);
            }
            // The review card (TASKS, FIELD's f6) draws no picture box of its own: its name stands alone unless the person gave a photo.
            var headed = card.Head != RoomCardHead.None && !(card.Eyebrow != null && card.Still == null);
            var left = headed ? S(HeadDp + HeadGapDp) : 0f;
            var column = width - left;
            var tall = 0f;
            var words = new List<(KitRole Role, float Dp, float LineDp, Color Colour, LineWords Words, int Lines, float Before)>();
            if (card.Title != null)
            {
                // A card's title takes at most THREE lines (the lead's 2026-11-14T01:19Z, one rule for every card): two at the title's
                // size, else three a step smaller; past that the person's own words show their lead cut at a word. The four-at-22 form
                // is gone: at its budgets it stood the recap so tall its far words read under 14 dp as the eye sees them (F115).
                (float Dp, float Line, int Lines) chosen = (LongTitleTypeDp, LongTitleLineDp, 3);
                // A file's name in the mono face, as the list of changes sets it.
                var titleRole = card.TitleMono ? KitRole.Mono : KitRole.Title;
                if (TitleUnderWords) chosen = (CaptionTypeDp, CaptionLineDp, 2);
                else foreach (var (dp, line, lines) in new[] { (TitleTypeDp, TitleLineDp, 2) })
                {
                    var (probe, _) = Text(titleRole, dp, line, Color.white, card.Title, left, 0f, column, lines);
                    var fits = !probe.isTextOverflowing;
                    probe.gameObject.SetActive(false);
                    Shown.Remove(probe);
                    textsUsed[titleRole]--;
                    if (fits) { chosen = (dp, line, lines); break; }
                }
                words.Add((titleRole, chosen.Dp, chosen.Line, Color.white, card.Title, chosen.Lines, 0f));
            }
            // Measure first, then stand the words in the middle of the head's height where it has one.
            var laid = new List<(TextMeshPro Label, float Tall, float Before)>();
            foreach (var each in words)
            {
                var (label, height) = Text(each.Role, each.Dp, each.LineDp, each.Colour, each.Words, left, 0f, column, each.Lines);
                // A name of the person's own words past three lines (an idea taken as its name) stops at its last whole word with
                // its tail faded, never an ellipsis (the owner's rule; AUDIT 2026-11-16 13:53Z); the whole is one press away where
                // the card's model gives one, and the recap's body says it whole.
                if (each.Words.AllTheirs) height = Excerpt(label, each.Words, column, each.Lines, each.LineDp, fade: true);
                laid.Add((label, height, S(each.Before)));
                tall += S(each.Before) + height;
            }
            var block = headed ? Mathf.Max(S(HeadDp), tall) : tall;
            var y = down + (block - tall) / 2f;
            // The way in's chevron beside the name's first line, or the head's middle where there is no name.
            chevronAt = laid.Count > 0 ? y + laid[0].Before + S(words[0].LineDp) / 2f : down + block / 2f;
            foreach (var (label, height, before) in laid)
            {
                y += before;
                label.transform.localPosition = new Vector3(left, -y, -RoomType.Units(0.5f));
                y += height;
            }
            if (headed)
            {
                var centre = new Vector2(S(HeadDp) / 2f, -(down + block / 2f));
                var side = new Vector2(S(HeadDp), S(HeadDp));
                switch (card.Head)
                {
                    case RoomCardHead.Agent:
                        Plate(side, side.x / 2f, Rgb(AgentOrb), centre, "agent orb");
                        Glyph(RoomGlyph.Agent, HeadGlyphDp, Color.white, centre, "agent");
                        break;
                    case RoomCardHead.Picture:
                        // The photo or video the person gave the task, where its still is read (ADR 0066); else the tile and its glyph.
                        if (card.Still != null && Pictures?.Invoke(card.Still) is Texture2D still)
                        {
                            if (pictureView == null) pictureView = RoomPictureView.Create(transform, "Picture", order - 3);
                            pictureView.Show(still, side.x, S(14f));
                            pictureView.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(0.4f));
                            pictureUsed = true;
                            Plate(side, S(14f), new Color(1f, 1f, 1f, 0f), centre, "picture edge", White(0.25f), S(1f), front: true);
                            break;
                        }
                        Plate(side, S(14f), White(0.10f), centre, "picture", White(0.25f), S(1f));
                        Glyph(RoomGlyph.Cube, HeadGlyphDp, Rgb(TileInk), centre, "picture");
                        break;
                    default:
                        Plate(side, S(HeadCornerDp), Rgb(ProjectTile), centre, "project emblem");
                        Glyph(RoomGlyph.Cube, HeadGlyphDp, Color.white, centre, "project");
                        break;
                }
            }
            return down + block;
        }

        private float LayGauges(IReadOnlyList<RoomGauge> gauges, float width, float down)
        {
            // Each window a well of its own the card's width, one above the next: its first line the window's glyph, the
            // number large and the window's word quiet on its baseline; its second the bar, every bar ending at one point,
            // with when it resets right-aligned after it.
            var count = gauges.Count;
            var inner = width - 2f * S(GaugePadXDp);
            var noteWidth = 0f;
            var notes = new List<TextMeshPro?>();
            foreach (var gauge in gauges)
            {
                if (gauge.Note == null) { notes.Add(null); continue; }
                var (note, _) = Text(KitRole.Body, GaugeResetDp, GaugeResetLineDp, White(NoteAlpha), LineWords.HostPlain(gauge.Note), 0f, 0f, RoomType.Units(2000f), 1);
                notes.Add(note);
                noteWidth = Mathf.Max(noteWidth, RoomCursor.WordsWidth(note, 0f));
            }
            var bar = inner - (noteWidth > 0f ? noteWidth + S(10f) : 0f);
            // The window's word on a line of its own over the number: "62% Left" in the number's size leaves no room for it after
            // the number at the larger text size ("Weekly Limit" ran past the column, 2026-11-16 F185).
            var labelLine = S(GaugeLabelLineDp) + S(4f);
            var tall = S(GaugePadTopDp) + labelLine + S(GaugeNumberLineDp) + S(14f) + Mathf.Max(S(TrackTallDp), S(GaugeResetLineDp)) + S(GaugePadFootDp);
            for (var index = 0; index < count; index++)
            {
                var gauge = gauges[index];
                var top = down + index * (tall + S(GaugeGapDp));
                Plate(new Vector2(width, tall), S(TileCornerDp), White(WellAlpha), new Vector2(width / 2f, -(top + tall / 2f)), "gauge tile", under: true);
                var x = S(GaugePadXDp);
                var labelTop = top + S(GaugePadTopDp);
                Glyph(GlyphOf(gauge.Kind), SlotGlyphDp, Rgb(TileInk, 0.86f), new Vector2(x + S(SlotGlyphBoxDp) / 2f, -(labelTop + S(GaugeLabelLineDp) / 2f)), "gauge " + gauge.Kind);
                x += S(SlotGlyphBoxDp) + S(10f);
                // The window's word beside its glyph, the number under it at the same left.
                Text(KitRole.Label, GaugeLabelDp, GaugeLabelLineDp, White(NoteAlpha), gauge.Label, x, -labelTop, inner - x, 1);
                var line = labelTop + labelLine;
                // The number reads where its bar stands, following it in whole percents while it moves; nothing stands after it, so
                // nothing moves as its digits change.
                var shownNow = GaugeShownBefore(gauge);
                var (number, _) = Text(KitRole.Title, GaugeNumberDp, GaugeNumberLineDp, Color.white, LineWords.Ours(GaugeWords(shownNow)), x, -line, inner - x, 1);
                gaugeNumbers.Add((number, gauge.Kind));
                var middle = line + S(GaugeNumberLineDp) + S(14f) + Mathf.Max(S(TrackTallDp), S(GaugeResetLineDp)) / 2f;
                Plate(new Vector2(bar, S(TrackTallDp)), S(TrackTallDp) / 2f, White(0.10f), new Vector2(S(GaugePadXDp) + bar / 2f, -middle), "gauge track");
                // A new reading moves the bar from where it stood, on the standard curve, its number with it (started above).
                var shownPercent = GaugeShownBefore(gauge);
                var filled = bar * Mathf.Clamp(shownPercent, 0f, 100f) / 100f;
                if (filled > 0f || gauge.PercentLeft > 0f)
                {
                    Plate(new Vector2(Mathf.Max(filled, 1e-4f), S(TrackTallDp)), Mathf.Min(S(TrackTallDp) / 2f, Mathf.Max(filled, 1e-4f) / 2f), White(0.62f), new Vector2(S(GaugePadXDp) + filled / 2f, -middle), "gauge fill");
                    var fill = Plates[Plates.Count - 1].Plate;
                    fill.gameObject.SetActive(filled > 1e-4f);
                    gaugeFills.Add((fill, gauge.Kind, bar, middle));
                }
                if (notes[index] is TextMeshPro note)
                    note.transform.localPosition = new Vector3(width - S(GaugePadXDp) - RoomCursor.WordsWidth(note, 0f), -(middle - S(GaugeResetLineDp) / 2f), -RoomType.Units(0.5f));
            }
            return down + count * tall + (count - 1) * S(GaugeGapDp);
        }

        /// <summary>The most lines a slot's value wraps to where it is wider than the card: a long web address, whole.</summary>
        public const int SlotValueLines = 6;

        /// <summary>The one language's label (FIELD's f6): 15 dp spaced capitals at a quiet white; the gap under a label and under the eyebrow.</summary>
        public const float CapTypeDp = RoomType.LabelDp;
        public const float CapLineDp = 20f;
        public const float CapTracking = RoomType.LabelTracking;
        public const float CapAlpha = RoomType.LabelAlpha;
        public const float CapGapDp = 8f;
        public const float EyebrowGapDp = 10f;

        /// <summary>The agent's sentence on the review card: its size, line, alpha and lines, and its quote mark.</summary>
        public const string SaysLabel = "The Agent Says";
        public const float SaysTypeDp = 22f;
        public const float SaysLineDp = 30f;
        public const float SaysAlpha = 0.74f;
        /// <summary>The agent's sentence folds to as many lines as it takes, never cut (AUDIT3's R6): this many at most, a long sentence's own.</summary>
        public const int SaysLines = 12;
        /// <summary>The quiet press to the agent's whole reply under its quote: the fact line's size, a touch brighter than a label, a small chevron.</summary>
        public const float ReadAllGapDp = 8f;
        public const float ReadAllAlpha = 0.72f;
        public const float ReadAllChevronDp = 14f;

        /// <summary>A labelled fact's value (24 dp) and its quiet line (17 dp), and the hairline between two facts.</summary>
        public const float FactValueDp = 24f;
        public const float FactValueLineDp = 30f;
        /// <summary>A fact's line under its value, read in a headset (VRIFY's 19:40Z critic: 17 dp "too small to read in a headset").</summary>
        public const float FactLineDp = 20f;
        public const float FactLineLineDp = 26f;
        public const float FactLineAlpha = RoomType.LabelAlpha;
        public const float FactGapDp = 28f;

        /// <summary>A label in the one language's spaced capitals, our own words; returns the depth under it.</summary>
        private float LayCap(string words, float width, float down)
        {
            var (label, tall) = Text(KitRole.Strong, CapTypeDp, CapLineDp, White(CapAlpha), LineWords.Ours(words), 0f, -down, width, 1);
            return down + Plain(label, words, width, tall);
        }

        /// <summary>
        /// A label in the one language's label role as plain words (FIELD's 01:56Z: a run's colour tag drew the label at the words' full
        /// white): no runs, spaced capitals, its quiet alpha; laid again at <paramref name="width"/>, its height returned.
        /// </summary>
        private float Plain(TextMeshPro label, string words, float width, float tall)
        {
            GlazeRuns.Of(label).Set(LineWords.None);
            GlazeText.SetLiteral(label, words.ToUpperInvariant());
            label.characterSpacing = CapTracking;
            label.alpha = CapAlpha;
            capLabels.Add(label);
            return Mathf.Max(tall, KitType.Lay(label, width, 1).Lines * S(CapLineDp));
        }

        /// <summary>Labels set in spaced capitals this frame, their tracking undone when the pool hands them to other words.</summary>
        private readonly List<TextMeshPro> capLabels = new List<TextMeshPro>();

        /// <summary>
        /// The review card's facts (TASKS, FIELD's f6): side by side, a hairline between, each its label in spaced capitals, its
        /// value with its kind's mark, and its quiet line; a fact that opens its page takes a press across its column.
        /// </summary>
        private float LayFacts(IReadOnlyList<RoomSlot> slots, float width, float down)
        {
            var count = Mathf.Min(slots.Count, 2);
            var column = (width - (count - 1) * S(FactGapDp)) / count;
            var tallest = 0f;
            for (var index = 0; index < count; index++)
            {
                var slot = slots[index];
                var x = index * (column + S(FactGapDp));
                var y = down;
                if (slot.Label != null)
                {
                    var (cap, capTall) = Text(KitRole.Strong, CapTypeDp, CapLineDp, White(CapAlpha), LineWords.Ours(slot.Label), x, -y, column, 1);
                    y += Plain(cap, slot.Label, column, capTall) + S(CapGapDp);
                }
                var glyphOf = slot.Kind == RoomKind.Passed ? RoomGlyph.Check : slot.Kind == RoomKind.Failed ? RoomGlyph.AlertRing : (RoomGlyph?)null;
                var markRoom = glyphOf != null ? S(SlotGlyphDp + 8f) : 0f;
                // A thin white line mark: its shape carries the state, never its colour (the lead's 01:40Z ruling).
                if (glyphOf is RoomGlyph mark)
                    Glyph(mark, SlotGlyphDp, Color.white, new Vector2(x + S(SlotGlyphDp) / 2f, -(y + S(FactValueLineDp) / 2f)), "fact " + slot.Kind);
                // Whole, never cut: a value or its line takes a second line where the column cannot hold it on one.
                var (_, valueTall) = Text(KitRole.Strong, FactValueDp, FactValueLineDp, Color.white, slot.Value, x + markRoom, -y, column - markRoom, 3);
                y += valueTall;
                if (slot.Line is LineWords line)
                {
                    var (_, lineTall) = Text(KitRole.Body, FactLineDp, FactLineLineDp, White(FactLineAlpha), line, x, -(y + S(2f)), column, 2);
                    y += S(2f) + lineTall;
                }
                if (slot.Press is KitControl press) RowTarget(press, new Vector2(x + column / 2f, -(down + (y - down) / 2f)), new Vector2(column, y - down));
                tallest = Mathf.Max(tallest, y - down);
            }
            // The hairline between the facts, the glass's own edge white (FIELD's f6).
            if (count == 2) Plate(new Vector2(S(1f), tallest), 0f, White(0.10f), new Vector2(column + S(FactGapDp) / 2f, -(down + tallest / 2f)), "facts hairline");
            return down + tallest;
        }

        private float LaySlots(IReadOnlyList<RoomSlot> slots, float width, float down)
        {
            var count = Mathf.Min(slots.Count, RoomCard.MostSlots);
            // Each value on one line, never broken or cut: a well is at least as wide as its value and its room. The wells
            // fill a row edge to edge, sharing what is left; where a row cannot hold them (larger text), the next well starts
            // a row of its own, and every row still meets both edges.
            var values = new List<(TextMeshPro Label, float Tall)>();
            var least = new float[count];
            var extra = new int[count];
            for (var index = 0; index < count; index++)
            {
                var laid = slots[index].Mono
                    ? Text(KitRole.Mono, CaptionTypeDp, CaptionLineDp, Color.white, slots[index].Value, 0f, 0f, RoomType.Units(2000f), 1)
                    : Text(KitRole.Body, CaptionTypeDp, CaptionLineDp, Color.white, slots[index].Value, 0f, 0f, RoomType.Units(2000f), 1);
                values.Add(laid);
                // A fact that opens its page keeps room for its chevron beside its value.
                least[index] = Mathf.Min(width, Mathf.Max(RoomCursor.WordsWidth(laid.Label, 0f), S(SlotGlyphBoxDp)) + 2f * S(SlotPadDp)
                    + (slots[index].Press != null ? S(SlotGlyphDp) + S(SlotPadDp) : 0f));
                if (RoomCursor.WordsWidth(laid.Label, 0f) + 2f * S(SlotPadDp) > width + 1e-4f)
                {
                    // A value wider than the card (a long web address) is shown whole, wrapped on the card's width in a well of
                    // its own, never cut (the critic, 2026-11-12; every value that goes out is shown whole, ADR 0060).
                    laid.Label.textWrappingMode = TextWrappingModes.Normal;
                    var wrapped = KitType.Lay(laid.Label, width - 2f * S(SlotPadDp), SlotValueLines);
                    extra[index] = Mathf.Max(wrapped.Lines, 1) - 1;
                    least[index] = width;
                    SlotsWrapped++;
                }
            }
            var rows = new List<List<int>> { new List<int>() };
            var used = 0f;
            // Four stand as two rows of two (a new folder from a starter) where each holds half the card; otherwise, and for
            // fewer, they fill a row, wrapping where it cannot hold them, so no well is ever narrower than its value (the parts
            // check, 2026-11-12: a long folder forced into half a row squeezed the level to "Neve...").
            var half = (width - S(TileGapDp)) / 2f;
            var pairs = count == 4;
            for (var index = 0; index < count; index++) pairs &= least[index] <= half + 1e-4f;
            if (pairs) rows = new List<List<int>> { new List<int> { 0, 1 }, new List<int> { 2, 3 } };
            for (var index = 0; index < (pairs ? 0 : count); index++)
            {
                var row = rows[rows.Count - 1];
                var need = least[index] + (row.Count > 0 ? S(TileGapDp) : 0f);
                if (row.Count > 0 && used + need > width + 1e-4f)
                {
                    rows.Add(row = new List<int>());
                    used = 0f;
                    need = least[index];
                }
                row.Add(index);
                used += need;
            }
            var well = S(SlotPadDp) + S(SlotGlyphBoxDp) + S(CaptionGapDp) + S(CaptionLineDp) + S(SlotFootDp);
            var top = down;
            // The first row's wells, by place: a row wrapped under it stands on the same columns, never stretched past them.
            var columns = new List<float>();
            var rowIndex = 0;
            foreach (var row in rows)
            {
                // A row under the first keeps the first row's columns only where they hold every value in it; else it lays its own.
                var aligned = rowIndex > 0 && row.Count > 1 && columns.Count >= row.Count;
                if (aligned)
                {
                    var need = 0f;
                    for (var at = 0; at < row.Count; at++) need += Mathf.Max(least[row[at]], columns[at]);
                    aligned = need + S(TileGapDp) * (row.Count - 1) <= width + 1e-4f;
                }
                var rowWell = well;
                foreach (var index in row) rowWell = Mathf.Max(rowWell, well + extra[index] * S(CaptionLineDp));
                var sum = 0f;
                foreach (var index in row) sum += least[index];
                var share = (width - S(TileGapDp) * (row.Count - 1) - sum) / row.Count;
                // Equal wells wherever every value fits one (SCREENS's critic, 2026-11-11); else each its value's room and a share.
                var even = (width - S(TileGapDp) * (row.Count - 1)) / row.Count;
                var evenFits = true;
                foreach (var index in row) evenFits &= least[index] <= even + 1e-4f;
                var x = 0f;
                var place = 0;
                foreach (var index in row)
                {
                    var slot = slots[index];
                    // The first row meets both edges. A card of one slot stands it at its own width: a fact, not a bar across
                    // the card (the critic, 2026-11-11). A wrapped row keeps the first row's columns, so its wells line up; a
                    // wrapped slot alone spans the card, never leaving a hole beside it.
                    var tile = count == 1 ? least[index]
                        : rowIndex == 0 ? (evenFits ? even : least[index] + Mathf.Max(0f, share))
                        : row.Count == 1 ? width
                        : aligned ? Mathf.Max(least[index], columns[row.IndexOf(index)])
                        : evenFits ? even : least[index] + Mathf.Max(0f, share);
                    if (columns.Count < rows[0].Count) columns.Add(tile);
                    place++;
                    values[index].Label.rectTransform.sizeDelta = new Vector2(tile - 2f * S(SlotPadDp), values[index].Label.rectTransform.sizeDelta.y);
                    Plate(new Vector2(tile, rowWell), S(TileCornerDp), White(WellAlpha), new Vector2(x + tile / 2f, -(top + rowWell / 2f)), "slot " + slot.Kind, under: true);
                    // A slot that changes its fact in place takes a press across its whole well, as a row picked from does.
                    if (slot.Press is KitControl change)
                    {
                        RowTarget(change, new Vector2(x + tile / 2f, -(top + rowWell / 2f)), new Vector2(tile, rowWell));
                        // The review card's facts (TASKS, take 3): a fact that opens its page says so with the card's own chevron at its right.
                        Glyph(RoomGlyph.Chevron, SlotGlyphDp, White(0.72f), new Vector2(x + tile - S(SlotPadDp) - S(SlotGlyphDp) / 2f, -(top + rowWell / 2f)), "slot chevron", CardChevronStroke);
                    }
                    // The glyph's drawn edge on the value's left edge: its box moved left by where its ink starts.
                    var glyphOf = slot.Kind == RoomKind.Level ? (RoomGlyph?)null : slot.Kind == RoomKind.Passed ? RoomGlyph.Check : slot.Kind == RoomKind.Failed ? RoomGlyph.AlertRing : GlyphOf(slot.Kind);
                    var ink = glyphOf is RoomGlyph drawn ? RoomGlyphs.InkLeft(drawn) / RoomGlyphs.Grid * S(SlotGlyphDp) : 0f;
                    var mark = new Vector2(x + S(SlotPadDp) + S(SlotGlyphDp) / 2f - ink, -(top + S(SlotPadDp) + S(SlotGlyphBoxDp) / 2f));
                    if (slot.Kind == RoomKind.Level) mark.x = x + S(SlotPadDp) + S(SlotLevelDp) / 2f;
                    switch (slot.Kind)
                    {
                        case RoomKind.Level:
                            // The chosen answer's own mark at a glyph's size, neutral at rest: its light disc, its dark check.
                            Plate(new Vector2(S(SlotLevelDp), S(SlotLevelDp)), S(SlotLevelDp) / 2f, Rgb(ChosenAtRest), mark, "level mark");
                            Glyph(RoomGlyph.Check, SlotLevelCheckDp, Rgb(Dark), mark, "level");
                            break;
                        case RoomKind.Passed:
                            Glyph(RoomGlyph.Check, SlotGlyphDp, Rgb(Passed), mark, "passed");
                            break;
                        case RoomKind.Failed:
                            Glyph(RoomGlyph.AlertRing, SlotGlyphDp, Rgb(Failed), mark, "failed");
                            break;
                        case RoomKind.Added:
                        case RoomKind.Removed:
                            // A count of lines added or removed carries the change's own hue, as its rows' marks do.
                            Glyph(GlyphOf(slot.Kind), SlotGlyphDp, Rgb(slot.Kind == RoomKind.Added ? Passed : Failed), mark, "slot " + slot.Kind);
                            break;
                        case RoomKind.None:
                            // A slot with no kind says its value alone: no mark (the critic, 2026-11-12).
                            break;
                        default:
                            Glyph(GlyphOf(slot.Kind), SlotGlyphDp, Rgb(TileInk, 0.86f), mark, "slot " + slot.Kind);
                            break;
                    }
                    // A slot with no mark stands its value in the middle of its well, never at the foot of a mark's empty place
                    // (the critic, 2026-11-13: a model's name low in a grey box read as a stray label).
                    var valueTop = slot.Kind == RoomKind.None ? top + (rowWell - S(CaptionLineDp) * (1 + extra[index])) / 2f
                        : top + S(SlotPadDp) + S(SlotGlyphBoxDp) + S(CaptionGapDp);
                    values[index].Label.transform.localPosition = new Vector3(x + S(SlotPadDp), -valueTop, -RoomType.Units(0.5f));
                    x += tile + S(TileGapDp);
                }
                rowIndex++;
                top += rowWell + S(TileGapDp);
            }
            return top - S(TileGapDp);
        }

        /// <summary>The steps as rows, each its mark at the left and its name beside it on one line, where a row of columns cannot hold them.</summary>
        private float LayStepRows(IReadOnlyList<RoomCardStep> steps, TextMeshPro[] labels, float width, float down)
        {
            var left = S(StepDp) + S(RowGapDp);
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var top = down + index * (S(StepDp) + S(StepRowGapDp));
                var centre = new Vector2(S(StepDp) / 2f, -(top + S(StepDp) / 2f));
                StepMark(step, centre);
                var label = labels[index];
                label.textWrappingMode = TextWrappingModes.Normal;
                // Top left: TMP's Left is the middle of the box, and the box is laid two lines tall, so a one-line name stood half a
                // line low beside its mark (AUDIT's 10:51Z, measured in the pixels: 0.35 of the tick low on the Checks page).
                label.alignment = TextAlignmentOptions.TopLeft;
                var noteRoom = 0f;
                if (step.Tone == RoomTone.Skipped)
                {
                    var (notRun, _) = Text(KitRole.Label, StepNameDp, StepNameLineDp, White(NoteAlpha), LineWords.Ours(NotRun), 0f, 0f, width / 2f, 1, TextAlignmentOptions.TopRight);
                    var notRunWidth = RoomCursor.WordsWidth(notRun, 0f);
                    notRun.rectTransform.sizeDelta = new Vector2(notRunWidth + S(1f), notRun.rectTransform.sizeDelta.y);
                    notRun.rectTransform.pivot = new Vector2(1f, 1f);
                    notRun.transform.localPosition = new Vector3(width - S(1f), -(top + (S(StepDp) - S(StepNameLineDp)) / 2f), -RoomType.Units(0.5f));
                    noteRoom = notRunWidth + S(RowGapDp);
                }
                var laid = KitType.Lay(label, width - left - noteRoom, 2);
                var tall = Mathf.Max(laid.Lines, 1) * S(StepNameLineDp);
                label.rectTransform.sizeDelta = new Vector2(width - left - noteRoom, label.rectTransform.sizeDelta.y);
                label.transform.localPosition = new Vector3(left, -(top + (S(StepDp) - tall) / 2f), -RoomType.Units(0.5f));
            }
            return down + steps.Count * S(StepDp) + (steps.Count - 1) * S(StepRowGapDp);
        }

        /// <summary>The gap between two steps standing as rows.</summary>
        public const float StepRowGapDp = 10f;

        private void StepMark(RoomCardStep step, Vector2 centre)
        {
            var (ground, ink, glyph, edge) = step.Tone switch
            {
                RoomTone.Good => (Rgb(GoodFill), Color.white, (RoomGlyph?)RoomGlyph.Check, Color.clear),
                RoomTone.Bad => (Rgb(BadFill), Color.white, RoomGlyph.Alert, Color.clear),
                RoomTone.Working => (Rgb(WorkingFill), Color.white, RoomGlyph.Wrench, Color.clear),
                // Not run: an outline that holds over the bright room, nothing in it.
                // At 0.46 the outline stood at 2.56:1 over the bright room (the critic, 2026-11-12); a mark needs 3:1.
                RoomTone.Skipped => (Color.clear, Color.clear, (RoomGlyph?)null, White(0.64f)),
                _ => (Rgb(QuietFill), Color.white, RoomGlyph.Wait, Color.clear),
            };
            Plate(new Vector2(S(StepDp), S(StepDp)), S(StepCornerDp), ground, centre, "step " + step.Name, edge, edge.a > 0f ? S(2f) : 0f);
            if (glyph is RoomGlyph shown) Glyph(shown, StepGlyphDp, ink, centre, "step " + step.Name, MarkStroke);
            if (step.TriesOf > 0) Pips(step.TriesUsed, step.TriesOf, step.Tone, new Vector2(centre.x, centre.y - S(StepDp) / 2f - S(PipDropDp) + S(PipDp) / 2f));
        }

        private float LaySteps(IReadOnlyList<RoomCardStep> steps, float width, float down)
        {
            // Each step as wide as its name or its mark, whichever is wider; the joins share what is left.
            var widths = new float[steps.Count];
            var labels = new TextMeshPro[steps.Count];
            var tallest = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                var (label, tall) = Text(KitRole.Label, StepNameDp, StepNameLineDp, steps[index].Tone == RoomTone.Skipped ? White(NoteAlpha) : White(0.90f),
                    steps[index].Words ?? LineWords.HostPlain(steps[index].Name ?? ""), 0f, 0f, width, 1, TextAlignmentOptions.Top);
                labels[index] = label;
                widths[index] = Mathf.Max(S(StepDp), RoomCursor.WordsWidth(label, 0f));
                // A step never run says Not Run under its name on one line, so its column is as wide as those words too (the
                // reader of 04:48Z: "Page / Not / Run" stacked one word a line under a narrow node).
                if (steps[index].Tone == RoomTone.Skipped)
                {
                    var (notRunMeasure, _) = Text(KitRole.Label, StepNameDp, StepNameLineDp, White(NoteAlpha), LineWords.Ours(NotRun), 0f, 0f, width, 1, TextAlignmentOptions.Top);
                    widths[index] = Mathf.Max(widths[index], RoomCursor.WordsWidth(notRunMeasure, 0f) + S(2f));
                    notRunMeasure.gameObject.SetActive(false);
                    Shown.Remove(notRunMeasure);
                    textsUsed[KitRole.Label]--;
                }
                tallest = Mathf.Max(tallest, tall);
            }
            // Equal columns, each as wide as the widest name or mark, so every node stands the same pitch from the next and
            // each join runs the same inset (the critic, 2026-11-11).
            var widest = 0f;
            foreach (var each in widths) widest = Mathf.Max(widest, each);
            // Where the columns would run past the card (four steps with a long name at larger text: "VR Offered" stood behind
            // Back, XR's critic of 06:24Z), each column takes its share of the card and its name wraps to two lines, centred.
            var least = (steps.Count - 1) * (S(10f) + 2f * S(StepJoinGapDp));
            if (widest * steps.Count + least > width + 1e-4f)
            {
                widest = Mathf.Max(S(StepDp), (width - least) / steps.Count);
                tallest = 0f;
                var whole = true;
                foreach (var label in labels)
                {
                    label.textWrappingMode = TextWrappingModes.Normal;
                    var wrapped = KitType.Lay(label, widest, 2);
                    label.ForceMeshUpdate(true);
                    whole &= !label.isTextOverflowing;
                    tallest = Mathf.Max(tallest, Mathf.Max(wrapped.Lines, 1) * S(StepNameLineDp));
                }
                // Not even two lines a column hold a name: the steps stand as rows, each its mark and its name beside it.
                if (!whole) return LayStepRows(steps, labels, width, down);
            }
            for (var index = 0; index < widths.Length; index++) widths[index] = widest;
            var sum = widest * widths.Length;
            var joins = steps.Count - 1;
            var join = joins > 0 ? Mathf.Max(S(10f), (width - sum - 2f * S(StepJoinGapDp) * joins) / joins) : 0f;
            var x = 0f;
            var middle = -(down + S(StepDp) / 2f);
            // The name close under its node, unless a step's pips hang there.
            var nameGap = steps.Any(each => each.TriesOf > 0) ? S(StepNameGapDp) : S(StepNameTightDp);
            var notRunTall = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var centre = new Vector2(x + widths[index] / 2f, middle);
                StepMark(step, centre);
                // The name centred under its mark.
                var label = labels[index];
                label.rectTransform.sizeDelta = new Vector2(widths[index], label.rectTransform.sizeDelta.y);
                label.transform.localPosition = new Vector3(x, -(down + S(StepDp) + nameGap), -RoomType.Units(0.5f));
                if (step.Tone == RoomTone.Skipped)
                {
                    // Not Run, quiet, centred under the name of a step the run never reached.
                    label.ForceMeshUpdate(true);
                    var nameTall = Mathf.Max(label.textInfo.lineCount, 1) * S(StepNameLineDp);
                    var (notRun, notTall) = Text(KitRole.Label, StepNameDp, StepNameLineDp, White(NoteAlpha), LineWords.Ours(NotRun), x, -(down + S(StepDp) + nameGap + nameTall), widths[index], 2, TextAlignmentOptions.Top);
                    notRunTall = Mathf.Max(notRunTall, nameTall + notTall - tallest);
                }
                x += widths[index];
                if (index < joins)
                {
                    var joined = step.Tone == RoomTone.Good;
                    // A join not passed reads as a line, never as nothing (the reader of 04:48Z: "nearly invisible" at 0.28).
                    Plate(new Vector2(join, S(StepJoinDp)), S(StepJoinDp) / 2f, joined ? Rgb(GoodFill, 0.70f) : White(0.46f),
                        new Vector2(x + S(StepJoinGapDp) + join / 2f, middle), "join");
                    x += join + 2f * S(StepJoinGapDp);
                }
            }
            return down + S(StepDp) + nameGap + tallest + Mathf.Max(0f, notRunTall);
        }

        /// <summary>The gap between one section of results and the next head, and the rate's track: thinner than a gauge's.</summary>
        public const float ResultsSectionGapDp = 18f, ResultsTrackDp = 4f, ResultsTrackGapDp = 8f;

        /// <summary>
        /// A card's results (<see cref="RoomCard.Results"/>; AUDIT's 21:13Z after result-death-stranding-results.png and
        /// list-death-stranding-cargo-management.png): each section's head once in the label role, then its rows with a hairline
        /// between them. Every row's name stands on the card's one left edge; its value is plain body type on the right edge, its
        /// mark (by form, white) just before the value, so no mark pushes a name off the title's edge; each fact is its own quiet
        /// line under the name; a rate is a white fill on a quiet track under them, the row's whole width. A command in mono breaks only between its
        /// words, never inside one ("SUITE=sign-in" stays whole), shrinking to the caption's size at most where a word is too wide.
        /// </summary>
        private float LayResults(IReadOnlyList<RoomResultsSection> sections, float width, float down)
        {
            for (var at = 0; at < sections.Count; at++)
            {
                var section = sections[at];
                if (at > 0) down += S(ResultsSectionGapDp);
                var (_, headTall) = Text(KitRole.Body, RoomType.LabelDp, RowNoteLineDp, White(RoomType.LabelAlpha), LineWords.HostPlain(section.Head), 0f, -down, width, 1, labelRole: true);
                down += headTall + S(HeadsGapDp);
                for (var index = 0; index < section.Rows.Count; index++)
                {
                    var row = section.Rows[index];
                    if (index > 0) Plate(new Vector2(width, S(1f)), 0f, White(0.10f), new Vector2(width / 2f, -(down + S(0.5f))), "hairline");
                    var top = down + S(RowPadDp);
                    var valueWidth = 0f;
                    TextMeshPro? value = null;
                    if (row.Value != null)
                    {
                        (value, _) = Text(KitRole.Body, RowNameDp, RowNameLineDp, White(0.90f), LineWords.HostPlain(row.Value), 0f, 0f, width / 2f, 1);
                        valueWidth = RoomCursor.WordsWidth(value, 0f);
                    }
                    var markRoom = row.Mark != RoomKind.None ? S(RowGlyphDp + RowGapDp) : 0f;
                    var nameWidth = width - (valueWidth > 0f ? valueWidth + S(RowGapDp) : 0f) - markRoom;
                    var (name, nameTall) = row.Mono
                        ? Text(KitRole.Mono, RowNameDp - 3f, RowNameLineDp, Color.white, row.Name, 0f, -top, nameWidth, RowMostLines)
                        : Text(KitRole.Body, RowNameDp, RowNameLineDp, Color.white, row.Name, 0f, -top, nameWidth, RowMostLines, holdLastTwo: true);
                    if (row.Mono) nameTall = LayCommand(name, row.Name, nameWidth, nameTall);
                    var below = top + nameTall;
                    foreach (var fact in row.Lines)
                    {
                        var (_, factTall) = Text(KitRole.Body, RowNoteDp, RowNoteLineDp, White(NoteAlpha), LineWords.HostPlain(fact), 0f, -(below + S(2f)), nameWidth, 2);
                        below += S(2f) + factTall;
                    }
                    if (row.Rate is double rate)
                    {
                        var middle = below + S(ResultsTrackGapDp) + S(ResultsTrackDp) / 2f;
                        // The row's whole width, its name's edge to its value's (AUDIT's 21:13Z appended point 6, as DS Results lays a rate).
                        var track = width;
                        Plate(new Vector2(track, S(ResultsTrackDp)), S(ResultsTrackDp) / 2f, White(0.10f), new Vector2(track / 2f, -middle), "rate track");
                        var filled = track * (float)rate;
                        if (filled > 1e-4f)
                            Plate(new Vector2(filled, S(ResultsTrackDp)), Mathf.Min(S(ResultsTrackDp) / 2f, filled / 2f), White(0.62f), new Vector2(filled / 2f, -middle), "rate fill");
                        below = middle + S(ResultsTrackDp) / 2f;
                    }
                    var firstLine = top + S(RowNameLineDp) / 2f;
                    if (value != null) value.transform.localPosition = new Vector3(width - valueWidth, -(firstLine - S(RowNameLineDp) / 2f), -RoomType.Units(0.5f));
                    if (row.Mark != RoomKind.None)
                        Glyph(GlyphOf(row.Mark), RowGlyphDp, Color.white, new Vector2(width - valueWidth - (valueWidth > 0f ? S(RowGapDp) : 0f) - S(RowGlyphDp) / 2f, -firstLine), "result " + row.Mark);
                    down = below + S(RowPadDp);
                }
            }
            return down;
        }

        /// <summary>
        /// A command's words laid so a line breaks only between them, never inside one (AUDIT's 21:13Z point 3: "SUITE=sign- / in"):
        /// TMP would break at a hyphen, so the lines are laid here, each word whole, a word wider than the row shrinking the face to the
        /// caption's size at most, and past that standing alone on its line, cut at its end. Returns the height drawn.
        /// </summary>
        private float LayCommand(TextMeshPro label, LineWords name, float width, float tall)
        {
            var text = name.Text;
            if (text.IndexOf('\n') >= 0) return tall;
            label.ForceMeshUpdate(true);
            bool Fits(string line) => label.GetPreferredValues(line).x <= width + 1e-4f;
            if (Fits(text)) return tall;
            var words = text.Split(' ');
            var floor = GlazeTokens.FontSize(GlazeTokens.Units(Glaze.CaptionDegrees));
            while (words.Any(word => !Fits(word)) && label.fontSize > floor + 1e-4f) label.fontSize = Mathf.Max(floor, label.fontSize * 0.95f);
            var lines = new List<string>();
            var current = "";
            foreach (var word in words)
            {
                var tried = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && !Fits(tried)) { lines.Add(current); current = word; }
                else current = tried;
            }
            if (current.Length > 0) lines.Add(current);
            var joined = string.Join("\n", lines);
            var runs = GlazeRuns.Of(label);
            label.text = joined;
            // Every character stays as the name gave it: the breaks are line ends between its words.
            if (name.AllTheirs) runs.Lean(new List<(int Start, int End)> { (0, joined.Length) });
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.ForceMeshUpdate(true);
            return Mathf.Max(lines.Count, 1) * S(RowNameLineDp);
        }

        private float LayList(IReadOnlyList<RoomListRow> rows, float width, float down, RoomListHeads? heads = null)
        {
            // A table's heads over its rows (tile f1): the names' over the names, the notes' at the right over the notes, in the
            // label role; under heads no rule divides the rows and no chevron stands, the whole row the press.
            if (heads != null)
            {
                var (nameHead, headTall) = Text(KitRole.Body, RoomType.LabelDp, RowNoteLineDp, White(RoomType.LabelAlpha), LineWords.HostPlain(heads.Name), 0f, -down, width / 2f, 1, labelRole: true);
                var (noteHead, _) = Text(KitRole.Body, RoomType.LabelDp, RowNoteLineDp, White(RoomType.LabelAlpha), LineWords.HostPlain(heads.Note), 0f, -down, width / 2f, 1, labelRole: true);
                noteHead.transform.localPosition = new Vector3(width - RoomCursor.WordsWidth(noteHead, 0f), -down, -RoomType.Units(0.5f));
                HeadsShown = (nameHead, noteHead);
                down += headTall + S(HeadsGapDp);
            }
            var table = heads != null;
            // A list where no row has a kind keeps no room for a mark: its words start at the card's edge with the title's
            // (SCREENS's critic, 2026-11-13: the empty room read as an icon that failed to draw). Where any row has a mark, all keep it.
            var marked = false;
            for (var index = 0; index < rows.Count && index < RoomCard.ListAPage; index++) marked |= rows[index].Kind != RoomKind.None || rows[index].Mark != AnswerMark.None;
            // Where any row shows a colour, every name stands after the swatch's room, so the names line up.
            var swatchRoom = rows.Take(RoomCard.ListAPage).Any(each => each.Swatch != null) ? S(SwatchDp + RowGapDp) : 0f;
            for (var index = 0; index < rows.Count && index < RoomCard.ListAPage; index++)
            {
                var row = rows[index];
                // A plain row (no kind, no press) is a quiet line under what the card says, never louder than the card's own
                // description (SCREENS's critic, 2026-11-13: our "And 2 more reasons it gave" outranked the agent's words).
                // Someone's own words on a plain row (a reason the agent gave) are the card's matter, not our note: they keep the row
                // name's size and white, and only our plain rows go quiet (SCREENS's critic 8, 2026-11-13: "all one size and grey").
                var answer = row.Mark != AnswerMark.None;
                var plain = row.Kind == RoomKind.None && !answer && row.Press == null && !row.Name.AllTheirs;
                // A rule divides rows a person picks from or reads as marked items; plain lines of words stand as a passage, with no
                // rule (critic 8: a hairline under every quote read as "a tappable settings list").
                var ruled = !table && rows.Any(each => each.Press != null || each.Kind != RoomKind.None || each.Mark != AnswerMark.None);
                if (index > 0 && ruled) Plate(new Vector2(width, S(1f)), 0f, White(0.10f), new Vector2(width / 2f, -(down + S(0.5f))), "hairline");
                var top = down + S(RowPadDp);
                var (tone, glyph) = ToneOf(row);
                // The note reads over any room: a quiet near-white whatever the kind, the kind's colour on its mark alone
                // (the lead, 2026-11-11: "Edited in Both" in the failure's tint read 2.89 to 1 over the bright room).
                var (ground, ink, note) = tone switch
                {
                    // The one language (the lead's 2026-11-17 01:40Z ruling): every row's mark a thin white line glyph with no tile, its
                    // shape the state; amber only where it waits for you; never green, red or orange.
                    RoomTone.Good => (Color.clear, Color.white, White(RoomType.LabelAlpha)),
                    RoomTone.Bad => (Color.clear, Color.white, White(RoomType.LabelAlpha)),
                    RoomTone.Working => (Color.clear, Color.white, White(RoomType.LabelAlpha)),
                    RoomTone.Attention => (Color.clear, Rgb(Attention), White(RoomType.LabelAlpha)),
                    _ => (Color.clear, White(0.85f), White(RoomType.LabelAlpha)),
                };
                if (row.Chosen) ink = Color.white;
                // A row picked from carries a chevron at its right, the note before it, and takes a press across its whole width.
                // An answer is pressed to choose it, in place: no chevron, as nothing opens.
                var picked = row.Press != null;
                var chevronRoom = picked && !answer && !table ? S(ChevronDp + ChevronGapDp) : 0f;
                var markRoom = marked ? S(RowTileDp + RowGapDp) : 0f;
                var nameLeft = markRoom + swatchRoom;
                // Where the card is laid narrower than the panel's own words (the plane's narrower widths, the lead's 2026-11-13T1807Z
                // ruling), the note stands on a line of its own under the name, the row's whole width, so neither is ever cut: at fifteen
                // degrees a one-word name beside its note ("Appeara…") and a note held to half the width ("Up to 60…") were (F100).
                var stacked = row.Note != null && width < RoomType.Units(RoomGeometry.PanelDp) - 2f * RoomType.Scaled(RoomGeometry.PanelPadDp) - RoomType.Units(1f);
                // The note at the right first, so the name knows how far it may run.
                var noteWidth = 0f;
                var noteTall = 0f;
                TextMeshPro? noteLabel = null;
                if (row.Note != null)
                {
                    // A row's state or value words in the label role (AUDIT 2026-11-17 01:32Z, MUST 2): spaced capitals at white 0.52.
                    // A name someone owns keeps its own case and the body's size (AUDIT2 01:39Z; RoomListRow.NoteInLabelRole).
                    var labelled = RoomListRow.NoteInLabelRole(row.Note);
                    var noteDp = labelled ? RoomType.LabelDp : RowNoteDp;
                    var noteInk = labelled ? note : White(NoteAlpha);
                    if (stacked) (noteLabel, noteTall) = Text(KitRole.Body, noteDp, RowNoteLineDp, noteInk, LineWords.HostPlain(row.Note), nameLeft, 0f, width - nameLeft - chevronRoom, 3, labelRole: labelled);
                    else
                    {
                        (noteLabel, _) = Text(KitRole.Body, noteDp, RowNoteLineDp, noteInk, LineWords.HostPlain(row.Note), 0f, 0f, width / 2f, 1, labelRole: labelled);
                        noteWidth = RoomCursor.WordsWidth(noteLabel, 0f);
                        // A note that does not fit beside its name (the label role's capitals at Larger: JUDGE's "NOT LOADE…") takes its own
                        // line under the name, the row's width, wrapped, never cut.
                        if (noteLabel.isTextTruncated)
                        {
                            stacked = true;
                            noteLabel.textWrappingMode = TextWrappingModes.Normal;
                            noteTall = KitType.Lay(noteLabel, width - nameLeft - chevronRoom, 3).Height;
                            noteWidth = 0f;
                        }
                    }
                }
                // A plain row's note keeps a wider gap: its words wrap to the note's side, where one gap read as a single run
                // ("All checked after the last change Inferred", SCREENS's critic, 2026-11-13).
                var noteGap = plain ? 3f * S(RowGapDp) : S(RowGapDp);
                var nameWidth = width - nameLeft - chevronRoom - (noteWidth > 0f ? noteWidth + noteGap : 0f);
                // A name is never cut: it takes as many lines as it needs, up to a page's eight (the lead's 2026-11-13T20:15Z; a frame's
                // stating row on the card path, F111: a value sentence cut at two lines). Only the person's own words on a row that opens
                // them whole (row.Excerpt) show their lead, cut at a word, in three lines (XR's critic, 06:33Z).
                var nameLines = row.Excerpt ? 3 : RowMostLines;
                // One word of theirs (a model's id) never breaks inside itself, at a hyphen or anywhere: it stands on one line,
                // its end cut with its ellipsis where it does not fit, on a row that presses to where it is named whole (the
                // reader, 2026-11-13: "muse-wonder-1.0-" over "preview").
                if (row.Excerpt && row.Name.Text.Trim().IndexOf(' ') < 0) nameLines = 1;
                var (nameLabel, nameTall) = row.Mono
                    ? Text(KitRole.Mono, RowNameDp - 3f, RowNameLineDp, Color.white, row.Name, nameLeft, 0f, nameWidth, nameLines)
                    : plain ? Text(KitRole.Body, RowNoteDp, RowNoteLineDp, White(NoteAlpha), row.Name, nameLeft, 0f, nameWidth, nameLines, holdLastTwo: true)
                    : Text(KitRole.Body, RowNameDp, RowNameLineDp, Color.white, row.Name, nameLeft, 0f, nameWidth, nameLines, holdLastTwo: true);
                // The person's own words of any length, on a row that opens them whole: their lead, cut at a word.
                if (row.Excerpt) nameTall = Excerpt(nameLabel, row.Name, nameWidth, nameLines, RowNameLineDp);
                // A path of theirs that wraps breaks only after a slash or a dot, never inside a name (SCREENS's PathWrap; the lead's
                // 06:09Z finish item: "welcome.uikitm" / "l"), a piece no line holds shrunk to the caption's size at most, then
                // broken at its finer places (AUDIT's 06:15Z floor).
                else if (row.Name.AllTheirs) nameTall = LayPath(nameLabel, row.Name, nameWidth, nameLines, row.Mono || !plain ? RowNameLineDp : RowNoteLineDp, nameTall);
                // What the row belongs to, a quieter line under its name.
                TextMeshPro? underLabel = null;
                var underTall = 0f;
                if (row.Under != null)
                {
                    // Outside words lead cut on one line; the product's own (a host's reason) are never cut, as many lines as a page holds.
                    // The agent's own description of an answer reads whole up to three lines (AUDIT2's point 3, the judge room's cut
                    // descriptions, a release MUST), past that stopping at a word with its tail faded, never an ellipsis.
                    (underLabel, underTall) = Text(KitRole.Body, RowNoteDp, RowNoteLineDp, White(NoteAlpha), row.Under, nameLeft, 0f, nameWidth, row.Under.AllTheirs ? TheirsUnderLines : RowMostLines);
                    if (row.Under.AllTheirs) underTall = Excerpt(underLabel, row.Under, nameWidth, TheirsUnderLines, RowNoteLineDp, fade: true);
                }
                // What the name is, ours, a quiet line of its own above it, whole: never joined to it on a dot.
                TextMeshPro? labelLabel = null;
                var labelTall = 0f;
                if (row.Label != null)
                {
                    JoinedFacts.Given(row.Label);
                    (labelLabel, labelTall) = Text(KitRole.Body, RowNoteDp, RowNoteLineDp, White(NoteAlpha), LineWords.HostPlain(row.Label), nameLeft, 0f, nameWidth, 2);
                }
                var labelGap = labelLabel != null ? S(2f) : 0f;
                // Under someone's words, our line stands a small gap apart, so it never reads as them still talking (critic 8).
                var underGap = row.Name.AllTheirs && row.Under != null && !row.Under.AllTheirs ? S(UnderTheirsGapDp) : S(2f);
                var noteUnder = stacked && noteLabel != null ? S(2f) + noteTall : 0f;
                var block = labelTall + labelGap + nameTall + noteUnder + (underLabel != null ? underGap + underTall : 0f);
                // The row's lines as laid, each part by its own pitch: its label's, its name's, its note's where it stands under the
                // name (beside it, it shares the name's line), its quieter line's. What a page of eight lines counts (AUDIT's 22:06Z).
                int LinesOf(float tall, float lineDp) => tall <= 0f ? 0 : Mathf.Max(1, Mathf.RoundToInt(tall / S(lineDp)));
                listLines.Add(LinesOf(labelTall, RowNoteLineDp) + LinesOf(nameTall, plain && !row.Mono ? RowNoteLineDp : RowNameLineDp)
                    + (stacked && noteLabel != null ? LinesOf(noteTall, RowNoteLineDp) : 0) + (underLabel != null ? LinesOf(underTall, RowNoteLineDp) : 0));
                var rowTall = marked ? Mathf.Max(S(RowTileDp), block) : block;
                var middle = top + rowTall / 2f;
                if (labelLabel != null) labelLabel.transform.localPosition = new Vector3(nameLeft, -(middle - block / 2f), -RoomType.Units(0.5f));
                var nameTop = middle - block / 2f + labelTall + labelGap;
                nameLabel.transform.localPosition = new Vector3(nameLeft, -nameTop, -RoomType.Units(0.5f));
                if (underLabel != null) underLabel.transform.localPosition = new Vector3(nameLeft, -(nameTop + nameTall + noteUnder + underGap), -RoomType.Units(0.5f));
                if (noteLabel != null)
                    noteLabel.transform.localPosition = stacked ? new Vector3(nameLeft, -(nameTop + nameTall + S(2f)), -RoomType.Units(0.5f))
                        : new Vector3(width - chevronRoom - noteWidth, -(middle - S(RowNoteLineDp) / 2f), -RoomType.Units(0.5f));
                if (picked)
                {
                    if (!answer && !table) Glyph(RoomGlyph.Chevron, ChevronDp, White(0.55f), new Vector2(width - S(ChevronDp) / 2f, -middle), "row chevron");
                    RowTarget(row.Press!, new Vector2(width / 2f, -middle), new Vector2(width, rowTall + 2f * S(RowPadDp)));
                }
                // The colour the row names, a filled disc between its mark and its name, its edge a light hairline so a dark colour
                // still shows over the dark glass.
                if (row.Swatch is int colour)
                    Plate(new Vector2(S(SwatchDp), S(SwatchDp)), S(SwatchDp) / 2f, Rgb(colour), new Vector2(markRoom + S(SwatchDp) / 2f, -middle), "row swatch", White(0.35f), S(1f));
                if (answer)
                {
                    // The answer's own mark in the tile's place, as the column draws one: a disc for one answer, a squircle for several;
                    // at rest a hollow light ring, chosen the neutral light fill under the dark check (the accent stays the main action's).
                    var markCentre = new Vector2(S(RowTileDp) / 2f, -middle);
                    var markSize = S(RoomButton.MarkDp);
                    var corner = row.Mark == AnswerMark.Multiple ? S(RoomButton.MarkBoxCornerDp) : markSize / 2f;
                    if (row.Chosen)
                    {
                        Plate(new Vector2(markSize, markSize), corner, Color.clear, markCentre, "answer mark chosen", Color.white, S(1.5f));
                        Glyph(RoomGlyph.Check, RoomButton.MarkCheckDp, Color.white, markCentre, "answer chosen");
                    }
                    else Plate(new Vector2(markSize, markSize), corner, Color.clear, markCentre, "answer mark", White(RoomButton.MarkLineAlpha), S(1.5f));
                }
                // A row with no kind draws no mark: a minus in a tile read as a control to collapse (the critic, 2026-11-12).
                else if (row.Kind != RoomKind.None)
                {
                    var tileCentre = new Vector2(S(RowTileDp) / 2f, -middle);
                    Plate(new Vector2(S(RowTileDp), S(RowTileDp)), S(RowTileCornerDp), ground, tileCentre, "row tile " + row.Kind,
                        row.Chosen ? White(0.74f) : Color.clear, row.Chosen ? S(1.5f) : 0f);
                    Glyph(glyph, RowGlyphDp, ink, tileCentre, "row " + row.Kind);
                }
                down = top + rowTall + S(RowPadDp);
            }
            return down;
        }

        /// <summary>
        /// Lays outside words that may run long as their lead cut at a word (<see cref="LineWords.CutToFit"/>) where they do not
        /// fit <paramref name="lines"/>, and returns the height they take. Halcyonic's own runs are never cut.
        /// </summary>
        private float Excerpt(TextMeshPro label, LineWords words, float width, int lines, float lineDp, bool fade = false)
        {
            label.ForceMeshUpdate(true);
            // On one line TMP does not wrap, so words wider than the row never overflow by its count: their width is measured too.
            bool Fits() => !label.isTextOverflowing && (lines > 1 || label.GetPreferredValues(label.text).x <= width + 1e-4f);
            if (Fits()) return Mathf.Max(label.textInfo.lineCount, 1) * S(lineDp);
            var runs = GlazeRuns.Of(label);
            var cut = words.CutToFit(tried =>
            {
                runs.Set(tried);
                label.ForceMeshUpdate(true);
                return Fits();
            });
            // At a word: where the cut fell inside one, back to the space before it, so no word is shown in part.
            var text = cut.Text;
            if (text.EndsWith("\u2026", System.StringComparison.Ordinal))
            {
                var body = text.Substring(0, text.Length - 1).TrimEnd();
                var whole = words.Text;
                var space = body.LastIndexOf(' ');
                if (body.Length < whole.Length && !char.IsWhiteSpace(whole[body.Length]) && space > 0) body = body.Substring(0, space);
                // Faded, the words stop at their last whole word and carry no mark of ours: the fade says they go on.
                cut = LineWords.TheirName(fade ? body.TrimEnd() : body.TrimEnd(' ', ',', ';', ':') + "\u2026");
            }
            runs.Set(cut);
            if (fade) FadedTails.Add(label);
            var laid = KitType.Lay(label, width, lines);
            // Faded, never one word alone on the last line shown (JUDGE's judge room, 19:23Z: "sign-in" alone under "limiting to the"): the
            // cut steps back a word at a time while the line above can lend one, the fade still saying the words go on.
            // Cut to its ellipsis, the same (AUDIT 2026-11-17 03:21Z: "Add rate limiting to the…" left "the…" alone): a word back at a
            // time, the ellipsis kept on the last word shown.
            var ellipsed = !fade && cut.Text.EndsWith("\u2026", System.StringComparison.Ordinal);
            for (var back = 0; (fade || ellipsed) && back < 3 && laid.Lines >= 2 && LastLineIsOneWord(label); back++)
            {
                var shown = cut.Text.TrimEnd();
                if (ellipsed) shown = shown.Substring(0, shown.Length - 1).TrimEnd();
                var space = shown.LastIndexOf(' ');
                if (space <= 0) break;
                var kept = shown.Substring(0, space).TrimEnd();
                cut = LineWords.TheirName(ellipsed ? kept.TrimEnd(' ', ',', ';', ':') + "\u2026" : kept);
                runs.Set(cut);
                laid = KitType.Lay(label, width, lines);
            }
            if (fade) label.ForceMeshUpdate(true);
            Excerpts.Add(label);
            return Mathf.Max(laid.Lines, 1) * S(lineDp);
        }

        /// <summary>
        /// The tone and the mark a list row takes from its kind, where its own tone says nothing more: green for new or
        /// passed, red for removed, failed or in conflict, and neutral for the rest (an edit, a file that reverts), so the
        /// accent stays the main action's and the one thing under way's.
        /// </summary>
        public static (RoomTone Tone, RoomGlyph Glyph) ToneOf(RoomListRow row)
        {
            var glyph = GlyphOf(row.Kind);
            if (row.Tone != RoomTone.Quiet) return (row.Tone, glyph);
            return row.Kind switch
            {
                RoomKind.Added or RoomKind.Passed => (RoomTone.Good, glyph),
                RoomKind.Removed or RoomKind.Conflict or RoomKind.Failed => (RoomTone.Bad, glyph),
                _ => (RoomTone.Quiet, glyph),
            };
        }

        /// <summary>The glyph of a kind, in a slot's tile or a list row's (the addendum's A17: one glyph, one meaning).</summary>
        public static RoomGlyph GlyphOf(RoomKind kind) => kind switch
        {
            RoomKind.Starter => RoomGlyph.Pin,
            RoomKind.Project => RoomGlyph.Cube,
            RoomKind.File => RoomGlyph.File,
            RoomKind.Agent => RoomGlyph.Agent,
            RoomKind.Level => RoomGlyph.Check,
            RoomKind.Folder => RoomGlyph.Folder,
            RoomKind.Passed => RoomGlyph.Check,
            RoomKind.Failed => RoomGlyph.Alert,
            RoomKind.NotRun => RoomGlyph.Wait,
            RoomKind.Waiting => RoomGlyph.Wait,
            RoomKind.Task => RoomGlyph.Flag,
            RoomKind.Says => RoomGlyph.Bubble,
            RoomKind.ToAdd => RoomGlyph.File,
            RoomKind.Changed => RoomGlyph.Wave,
            RoomKind.Added => RoomGlyph.Plus,
            RoomKind.Removed => RoomGlyph.Minus,
            RoomKind.Conflict => RoomGlyph.Swap,
            RoomKind.Started => RoomGlyph.Pin,
            RoomKind.Worked => RoomGlyph.Flag,
            RoomKind.Reverts => RoomGlyph.Rewind,
            RoomKind.Computer => RoomGlyph.Computer,
            RoomKind.Tools => RoomGlyph.Wrench,
            RoomKind.App => RoomGlyph.App,
            RoomKind.Session => RoomGlyph.Timer,
            RoomKind.Week => RoomGlyph.Calendar,
            RoomKind.Link => RoomGlyph.Link,
            RoomKind.Wifi => RoomGlyph.Wifi,
            _ => RoomGlyph.Minus,
        };

        /// <summary>
        /// Tries as pips centred at <paramref name="centre"/>: <paramref name="of"/> of them, the first <paramref name="used"/>
        /// lit in the tone's own hue, each ringed in the panel's dark so it reads hung on the mark.
        /// </summary>
        private void Pips(int used, int of, RoomTone tone, Vector2 centre)
        {
            // A try used is its tone's full fill inside a light ring: the red alone stood at 1.6:1 on the glass over the bright
            // room even at full fill, under 3:1 for a mark (the critics, 2026-11-12), so the ring carries it.
            var lit = tone switch { RoomTone.Good => Rgb(GoodFill), RoomTone.Bad => Rgb(BadFill), RoomTone.Attention => Rgb(Attention), _ => Rgb(WorkingFill) };
            var pitch = S(PipDp + PipGapDp);
            var from = centre.x - pitch * (of - 1) / 2f;
            for (var index = 0; index < of; index++)
            {
                var at = new Vector2(from + index * pitch, centre.y);
                var spent = index < used;
                // Filled where used, hollow where left: like a game's lives, in the node's own colour; a squircle, as the node.
                // A try left is a ring firm enough to read over the bright room, a try spent the fill.
                Plate(new Vector2(S(PipDp), S(PipDp)), S(PipCornerDp), spent ? lit : Color.clear, at, spent ? "pip used" : "pip",
                    // A try left is a light ring too: in its tone it stood at 2.0:1 over the bright room.
                    spent ? White(0.92f) : White(0.64f), S(PipLineDp), front: true);
            }
        }

        /// <summary>Lays words of <paramref name="role"/> at a size and line pitch of their own, their box's top left at (x, y); returns the label and how tall its lines stand.</summary>
        /// <summary>Each pooled label's own tracking, so one laid in the label role takes its own back when the pool hands it on.</summary>
        private readonly Dictionary<TextMeshPro, float> baseTracking = new Dictionary<TextMeshPro, float>();

        private (TextMeshPro Label, float Tall) Text(KitRole role, float dp, float lineDp, Color colour, LineWords words, float x, float y, float width, int lines,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft, bool labelRole = false, bool holdLastTwo = false)
        {
            if (!texts.TryGetValue(role, out var pool)) texts[role] = pool = new List<TextMeshPro>();
            textsUsed.TryGetValue(role, out var used);
            if (used >= pool.Count)
            {
                var made = KitType.Create(transform, role + " words", role, Color.white, order, TextAlignmentOptions.TopLeft, room: true);
                // A faded tail is laid into the mesh each time it is made.
                made.OnPreRenderText += info => { if (FadedTails.Contains(made)) GlazeText.FadeTail(info); };
                pool.Add(made);
            }
            var label = pool[used];
            textsUsed[role] = used + 1;
            label.gameObject.SetActive(true);
            // The label role draws its words in spaced capitals by the face's own style, so the words themselves stay as given.
            if (!baseTracking.ContainsKey(label)) baseTracking[label] = label.characterSpacing;
            label.fontStyle = labelRole ? label.fontStyle | FontStyles.UpperCase : label.fontStyle & ~FontStyles.UpperCase;
            label.characterSpacing = labelRole ? RoomType.LabelTracking : baseTracking[label];
            label.alignment = alignment;
            label.color = colour;
            label.fontSize = GlazeTokens.FontSize(S(dp) / RoomType.Units(1f) * RoomType.Units(1f));
            label.lineSpacing = KitType.LineSpacingFor(label, lineDp / dp);
            label.textWrappingMode = lines > 1 ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var runs = GlazeRuns.Of(label);
            if (labelRole)
            {
                // Our own words as plain text, so the label's own colour, its quiet alpha, is the one drawn: a run's colour tag drew
                // the label role at the words' full white (AUDIT's room-08: 225 against 233).
                runs.Set(LineWords.None);
                GlazeText.SetLiteral(label, words.Text);
            }
            else
            {
                runs.Set(words);
                runs.SetProseMode(true);
            }
            // The body's last two words held together where a paragraph can wrap (AUDIT 2026-11-17 03:21Z: a task card's sentence ended on
            // one word): in the words handed to the lay, the same length, so every run's place holds; never in the stored text.
            // A row's words only (holdLastTwo: a task's sentence on its row, which Excerpt lays again at its own measure, undoing the
            // narrowing below): a title or any other body is held by WithoutWidow below, its words exactly its own.
            var heldText = holdLastTwo && lines > 1 && !labelRole ? LabelText.KeepLastTwo(label.text) : label.text;
            var plainText = label.text;
            if (!ReferenceEquals(heldText, plainText) && heldText != plainText) label.text = heldText;
            var laid = KitType.Lay(label, width, lines);
            // Where the pair cannot stand on one line, the plain lay: overflowing, cut, or broken inside a word because the held pair is
            // wider than the line ("Storefront A" / "PI" at Larger).
            if (label.text != plainText && (label.isTextOverflowing || label.isTextTruncated || PairSplit(label)))
            {
                label.text = plainText;
                laid = KitType.Lay(label, width, lines);
            }
            // No word alone on a paragraph's last line (a widow: SCREENS's reader 3 on 38-checks-failed, "stopped on a / step"), the
            // person's words included, a title among them (AUDIT 2026-11-16T15:23Z: "points" alone): only the measure narrows, so
            // the words and any line end of theirs stand as written.
            if (lines > 1 && laid.Lines >= 2 && label.text.IndexOf('\n') < 0) laid = WithoutWidow(label, width, lines, laid);
            label.transform.localPosition = new Vector3(x, y, -RoomType.Units(0.5f));
            Shown.Add(label);
            return (label, Mathf.Max(laid.Lines, 1) * S(lineDp));
        }

        /// <summary>
        /// Lays <paramref name="label"/> at <paramref name="width"/> on at most <paramref name="maxLines"/> lines, with no word alone on its last
        /// line where it runs four words or more (AUDIT 2026-11-16T15:23Z; JUDGE's judge room at 19:20Z): the measure narrows a twentieth at a
        /// time, down to six tenths, never onto another line nor cut; where none does, as first laid. The words and any line end in them
        /// stand as written.
        /// </summary>
        public static (int Lines, float Width, float Height) Balanced(TMP_Text label, float width, int maxLines)
        {
            var laid = KitType.Lay(label, width, maxLines);
            if (laid.Lines < 2 || !LastLineIsOneWord(label)
                || label.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Length < 4) return laid;
            for (var step = 1; step <= 8; step++)
            {
                var tried = KitType.Lay(label, width * (1f - 0.05f * step), maxLines);
                if (tried.Lines != laid.Lines || label.isTextTruncated) break;
                if (!LastLineIsOneWord(label)) return tried;
            }
            return KitType.Lay(label, width, maxLines);
        }

        /// <summary>
        /// Lays our own words a little narrower, a twentieth at a time down to six tenths of <paramref name="width"/>, until their
        /// last line holds two words, never on more lines than they took and never cut; where no narrower lay does that, the words
        /// stand as first laid. Only the measure narrows: the words, theirs or ours, and any line end in them stand as written.
        /// </summary>
        private static (int Lines, float Width, float Height) WithoutWidow(TextMeshPro label, float width, int lines, (int Lines, float Width, float Height) laid)
        {
            if (!LastLineIsOneWord(label) || WordCount(label) < 4) return laid;
            for (var step = 1; step <= 8; step++)
            {
                var tried = KitType.Lay(label, width * (1f - 0.05f * step), lines);
                if (tried.Lines != laid.Lines || label.isTextOverflowing) break;
                if (!LastLineIsOneWord(label)) return tried;
            }
            return KitType.Lay(label, width, lines);
        }

        /// <summary>
        /// Lays a path of theirs that does not fit one line by SCREENS's <see cref="PathWrap"/>: its lines break only after a slash
        /// or a dot, never inside a name. A piece no line holds whole shrinks the words, a twentieth at a time, never below the
        /// product's smallest reading size (a caption's em); where it still does not fit, it breaks at its finer places
        /// (<see cref="PathWrap.FinerPieces"/>: after a hyphen or an underscore, or before a capital), never inside letters.
        /// Where the shown text is not the path as written (an escaped character), the words stand as laid. Returns their height.
        /// </summary>
        private float LayPath(TextMeshPro label, LineWords name, float width, int lines, float lineDp, float tall)
        {
            label.ForceMeshUpdate(true);
            var text = name.Text;
            bool Fits(string line) => label.GetPreferredValues(line).x <= width + 1e-4f;
            // Only a path: no space in it, a slash or a dot to break after; a name in words (a task's title) wraps as words do.
            if (text.IndexOf(' ') >= 0 || (text.IndexOf('/') < 0 && text.IndexOf('.') < 0)) return tall;
            if (Fits(text) || label.text != text || text.IndexOf('\n') >= 0) return tall;
            var laid = PathWrap.Lines(text, Fits);
            var floor = GlazeTokens.FontSize(GlazeTokens.Units(Glaze.CaptionDegrees));
            while (laid.Any(line => !Fits(line)) && label.fontSize > floor + 1e-4f)
            {
                label.fontSize = Mathf.Max(floor, label.fontSize * 0.95f);
                laid = PathWrap.Lines(text, Fits);
            }
            if (laid.Any(line => !Fits(line)))
            {
                // At the floor and still too wide: the overflowing pieces at their finer breaks, laid greedily as PathWrap lays.
                var pieces = PathWrap.Pieces(text).SelectMany(piece => Fits(piece) ? new[] { piece } : PathWrap.FinerPieces(piece)).ToList();
                var finer = new List<string>();
                var current = "";
                foreach (var piece in pieces)
                {
                    if (current.Length > 0 && !Fits(current + piece)) { finer.Add(current); current = piece; }
                    else current += piece;
                }
                if (current.Length > 0) finer.Add(current);
                laid = finer;
            }
            var joined = string.Join("\n", laid);
            var runs = GlazeRuns.Of(label);
            label.text = joined;
            // Every character stays theirs: the breaks are line ends between their pieces.
            runs.Lean(new List<(int Start, int End)> { (0, joined.Length) });
            var drawn = KitType.Lay(label, width, Mathf.Max(lines, laid.Count));
            return Mathf.Max(drawn.Lines, 1) * S(lineDp);
        }

        /// <summary>Whether a body's held last pair (<see cref="LabelText.KeepLastTwo"/>) was laid across two lines, so a word broke inside itself.</summary>
        internal static bool PairSplit(TMP_Text label)
        {
            label.ForceMeshUpdate(true);
            var text = label.text;
            var held = text.LastIndexOf('\u00A0');
            if (held <= 0) return false;
            var start = text.LastIndexOfAny(new[] { ' ', '\n' }, held - 1) + 1;
            var end = text.TrimEnd().Length - 1;
            // A break inside the pair counts only where it falls inside a word: after a hyphen is a word's own place to break.
            var info = label.textInfo;
            for (var at = 1; at < info.characterCount; at++)
            {
                var here = info.characterInfo[at];
                var before = info.characterInfo[at - 1];
                if (here.index <= start || here.index > end || here.lineNumber == before.lineNumber) continue;
                if (before.character != '-') return true;
            }
            return false;
        }

        /// <summary>Whether the label's last laid line holds a single word.</summary>
        public static bool LastLineIsOneWord(TMP_Text label)
        {
            var info = label.textInfo;
            if (info.lineCount < 2) return false;
            var last = info.lineInfo[info.lineCount - 1];
            for (var index = last.firstCharacterIndex; index <= last.lastVisibleCharacterIndex && index < info.characterCount; index++)
                if (char.IsWhiteSpace(info.characterInfo[index].character)) return false;
            return last.lastVisibleCharacterIndex >= last.firstCharacterIndex;
        }

        private static int WordCount(TMP_Text label) =>
            label.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Length;

        private void Plate(Vector2 size, float radius, Color fill, Vector2 centre, string what, Color edge = default, float edgeWidth = 0f, bool under = false, bool front = false)
        {
            if (platesUsed >= plates.Count) plates.Add(RoomShape.Create(transform, "Tile", order - 4));
            var plate = plates[platesUsed++];
            plate.gameObject.SetActive(true);
            // A tile under the marks; a pip over the mark it hangs on, each laid after the one under it.
            plate.GetComponent<Renderer>().sortingOrder = under ? order - 5 : front ? order + 1 : order - 4;
            // The page's alphas are mixed in sRGB; the headset blends in linear light, where the same alpha shows far lighter.
            // Every faint fill and edge over the panel takes the alpha that shows as the page's (RoomRows.LightAlpha).
            plate.Draw(size, radius, AsShown(fill), AsShown(edge), edgeWidth);
            plate.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(under ? 0.2f : front ? 0.6f : 0.3f));
            Plates.Add((plate, what));
        }

        private void Glyph(RoomGlyph glyph, float dp, Color colour, Vector2 centre, string what, float stroke = RoomGlyphs.Stroke)
        {
            if (glyphsUsed >= glyphs.Count) glyphs.Add(RoomGlyphView.Create(transform, "Mark", order));
            var view = glyphs[glyphsUsed++];
            view.Show(glyph, dp * GlazeText.Scale, colour, stroke);
            view.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(0.5f));
            Glyphs.Add((view, what));
        }

        /// <summary>A row's press target, centred at <paramref name="centre"/>, as large as the row: it raises <see cref="RowPressed"/> with the row's control.</summary>
        private void RowTarget(KitControl control, Vector2 centre, Vector2 size)
        {
            if (rowPressesUsed >= rowPresses.Count)
            {
                var go = new GameObject("Row press");
                go.transform.SetParent(transform, false);
                var index = rowPresses.Count;
                var press = KitPress.Create(go, size);
                press.Pressed += () => { if (rowControls[index] is KitControl pressed) RowPressed?.Invoke(pressed); };
                rowPresses.Add((press, go));
                rowControls.Add(null);
            }
            var (target, host) = rowPresses[rowPressesUsed];
            // A row, slot or card press that now does something else arrives anew and settles again (AUDIT, 2026-11-12).
            if (rowControls[rowPressesUsed] is not KitControl was || RoomButton.Doing(was) != RoomButton.Doing(control)) target.Unsettle();
            rowControls[rowPressesUsed] = control;
            rowPressesUsed++;
            host.SetActive(true);
            target.Resize(size);
            target.Available = true;
            host.transform.localPosition = new Vector3(centre.x, centre.y, -RoomType.Units(0.7f));
        }

        private void HideUnused()
        {
            for (var index = rowPressesUsed; index < rowPresses.Count; index++)
            {
                rowPresses[index].Target.SetActive(false);
                rowControls[index] = null;
            }
            foreach (var pair in texts)
            {
                textsUsed.TryGetValue(pair.Key, out var used);
                for (var index = used; index < pair.Value.Count; index++) pair.Value[index].gameObject.SetActive(false);
            }
            for (var index = platesUsed; index < plates.Count; index++) plates[index].gameObject.SetActive(false);
            for (var index = glyphsUsed; index < glyphs.Count; index++) glyphs[index].Hide();
            if (!codeUsed && codeView != null) codeView.Hide();
            if (!pictureUsed && pictureView != null) pictureView.Hide();
        }

        /// <summary>A colour as the page shows it over the panel, its alpha the one a blend in linear light needs to look the same.</summary>
        public static Color AsShown(Color colour) =>
            colour.a <= 0f || colour.a >= 1f ? colour : new Color(colour.r, colour.g, colour.b, RoomRows.LightAlpha(colour.a, RoomRows.PanelGround));

        /// <summary>A length in dp at the person's text size, in the plane's units.</summary>
        private static float S(float dp) => RoomType.Scaled(dp);

        private static Color White(float alpha) => new Color(1f, 1f, 1f, alpha);

        private static Color Rgb(int rgb, float alpha = 1f) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);
    }
}
