#nullable enable
using System.Collections.Generic;
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
        public const float StatusGlyphDp = 28f;
        public const float StatusGapDp = 14f;
        public const float StatusTitleDp = 30f;
        public const float StatusTitleLineDp = 36f;
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
        public const float TileGapDp = 16f;
        /// <summary>A well's corner: smaller than the panel's own 20 dp, so a well sits in the panel and never reads as a tile of its own.</summary>
        public const float TileCornerDp = 14f;
        public const float WellAlpha = 0.045f;
        public const float SlotPadDp = 14f;
        public const float SlotFootDp = 16f;
        public const float SlotGlyphDp = 24f;
        public const float SlotGlyphBoxDp = 26f;
        public const float SlotLevelDp = 21f;
        public const float SlotLevelCheckDp = 13f;

        /// <summary>A chosen answer's disc at rest, neutral: the accent stays the main action's (#E2E7EA, its check #172024).</summary>
        public const int ChosenAtRest = 0xE2E7EA;
        public const float CaptionTypeDp = 16f;
        public const float CaptionLineDp = 20f;
        public const float CaptionGapDp = 12f;
        public const float StatusCornerDp = 17f;
        public const float StepDp = 38f;
        public const float StepCornerDp = 12f;
        public const float StepGlyphDp = 20f;
        public const float StepJoinDp = 3f;
        public const float StepJoinGapDp = 8f;
        public const float StepNameDp = 17f;
        public const float StepNameLineDp = 21f;
        public const float StepNameGapDp = 36f;
        public const float PipDp = 18f;
        public const float PipCornerDp = 5f;
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
        public const float RowGapDp = 14f;
        public const float RowNameDp = 20f;
        public const float RowNameLineDp = 26f;
        public const float RowNoteDp = 16f;
        public const float RowNoteLineDp = 20f;

        /// <summary>
        /// A note's white, in the reading face: a step quieter than a name, legible over the bright room and the dark one
        /// (0.72 passed the contrast check over the bright room; the kind's colour is the mark's alone).
        /// </summary>
        public const float NoteAlpha = 0.72f;

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
        public const float MoreDp = 15f;
        public const float MoreLineDp = 20f;

        /// <summary>
        /// A code to scan: its well at most 264 dp across (16.5 degrees), never wider than the card, standing in the card's
        /// middle; its cells dark on a near-white well, the kit's margin of light cells round them, the well's corners inside
        /// that margin. Opaque both, so a camera reads it the same over the bright room as over the dark.
        /// </summary>
        public const float CodeDp = 264f;
        public const int CodeGround = 0xF4F7F8;
        public const int CodeInk = 0x0E1214;
        public const float CodeCornerCells = 2f;

        /// <summary>The colours of the language: the pass, failure and accent hues, and their deep grounds.</summary>
        public const int Passed = 0x69D98D;
        public const int PassedInk = 0x9CF0B6;
        public const int PassedGround = 0x2E5E40;
        public const int PassedNote = 0x7EE0A0;
        public const int Failed = 0xF7857D;
        public const int FailedInk = 0xFFB4AD;
        public const int FailedGround = 0x6E302C;
        public const int FailedNote = 0xFF9C94;
        public const int Accent = 0x59CFF1;
        public const int AccentNote = 0x7FD9F5;
        public const int Dark = 0x172024;

        /// <summary>The attention colour (a task or the assistant needs the person), as the name tag's Needs You chip: amber.</summary>
        public const int Attention = 0xF2B134;
        public const int AttentionNote = 0xFFD27A;
        public const int StepGround = 0x2F3539;
        public const int AgentOrb = 0x7A6BD6;
        public const int ProjectTile = 0x3F8FB0;
        public const int TileInk = 0xE8F6FB;

        private readonly Dictionary<KitRole, List<TextMeshPro>> texts = new Dictionary<KitRole, List<TextMeshPro>>();
        private readonly Dictionary<KitRole, int> textsUsed = new Dictionary<KitRole, int>();
        private readonly List<RoomShape> plates = new List<RoomShape>();
        private readonly List<RoomGlyphView> glyphs = new List<RoomGlyphView>();
        private readonly List<(KitPress Press, GameObject Target)> rowPresses = new List<(KitPress, GameObject)>();
        private readonly List<KitControl?> rowControls = new List<KitControl?>();
        private int rowPressesUsed;

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
        private bool codeUsed;
        private int platesUsed;
        private int glyphsUsed;
        private int order;

        /// <summary>How wide and how tall the card stands as last shown.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Every label the card shows now, in the order it laid them: for checks.</summary>
        public List<TextMeshPro> Shown { get; } = new List<TextMeshPro>();

        /// <summary>Every tile, mark and plate the card shows now, with what it is: for checks.</summary>
        public List<(RoomShape Plate, string What)> Plates { get; } = new List<(RoomShape, string)>();

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
                    if (label.isTextOverflowing) return false;
                    if (!string.IsNullOrEmpty(label.text) && label.textInfo.characterCount == 0) return false;
                }
                return true;
            }
        }

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
            go.SetActive(false);
            return view;
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Shows <paramref name="card"/> <paramref name="width"/> wide at the person's text size, and returns how tall it stands.</summary>
        public float Show(RoomCard card, float width)
        {
            gameObject.SetActive(true);
            foreach (var role in new List<KitRole>(textsUsed.Keys)) textsUsed[role] = 0;
            platesUsed = 0;
            glyphsUsed = 0;
            rowPressesUsed = 0;
            codeUsed = false;
            Shown.Clear();
            ReadingShown.Clear();
            Plates.Clear();
            Glyphs.Clear();
            var down = 0f;
            void Gap(float dp) { if (down > 0f) down += S(dp); }

            if (card.Status is RoomCardStatus status) down = LayStatus(status, card, width, down);
            if (card.Title != null || card.Head != RoomCardHead.None)
            {
                Gap(BlockGapDp);
                down = LayHead(card, width, down);
            }
            if (card.Description != null)
            {
                // Under a head a block's gap, as every other block; under a title alone, its own small gap.
                down += S(card.Head != RoomCardHead.None ? BlockGapDp : DescriptionGapDp);
                var (_, tall) = Text(KitRole.Body, DescriptionTypeDp, DescriptionLineDp, White(0.80f), card.Description, 0f, -down, width, 4);
                down += tall;
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
                down = LaySlots(card.Slots, width, down);
            }
            if (card.Reading.Count > 0)
            {
                Gap(BlockGapDp);
                down = LayReading(card.Reading, width, down);
            }
            if (card.Steps.Count > 0)
            {
                Gap(BlockGapDp);
                down = LaySteps(card.Steps, width, down);
            }
            if (card.List.Count > 0)
            {
                Gap(ListGapDp);
                down = LayList(card.List, width, down);
            }
            if (card.More != null)
            {
                Gap(8f);
                var (_, tall) = Text(KitRole.Body, MoreDp, MoreLineDp, White(0.66f), card.More, 0f, -down, width, 2);
                down += tall;
            }
            HideUnused();
            Size = new Vector2(width, down);
            return down;
        }

        private float LayStatus(RoomCardStatus status, RoomCard card, float width, float down)
        {
            var (ground, ink, glyph) = status.Tone switch
            {
                RoomTone.Good => (Rgb(Passed, 0.16f), Rgb(Passed), RoomGlyph.Check),
                RoomTone.Bad => (Rgb(Failed, 0.16f), Rgb(Failed), RoomGlyph.Alert),
                RoomTone.Working => (Rgb(Accent, 0.14f), Rgb(Accent), RoomGlyph.Wrench),
                RoomTone.Attention => (Rgb(Attention, 0.16f), Rgb(Attention), RoomGlyph.Bubble),
                _ => (White(0.08f), White(0.85f), RoomGlyph.Wait),
            };
            var left = S(StatusMarkDp + StatusGapDp);
            var column = width - left;
            var top = down;
            var (_, titleTall) = Text(KitRole.Title, StatusTitleDp, StatusTitleLineDp, Color.white, LineWords.Ours(status.Title), left, -down, column, 2);
            var tall = titleTall;
            if (status.Line != null)
            {
                var (_, lineTall) = Text(KitRole.Body, LineTypeDp, LineLineDp, White(0.66f), LineWords.Ours(status.Line), left, -(down + tall + S(2f)), column, 3);
                tall += S(2f) + lineTall;
            }
            var block = Mathf.Max(S(StatusMarkDp), tall);
            var middle = top + Mathf.Max(S(StatusMarkDp), tall) / 2f;
            // A squircle, as the check nodes: a disc is only ever an answer.
            Plate(new Vector2(S(StatusMarkDp), S(StatusMarkDp)), S(StatusCornerDp), ground, new Vector2(S(StatusMarkDp) / 2f, -middle), "status mark");
            Glyph(glyph, StatusGlyphDp, ink, new Vector2(S(StatusMarkDp) / 2f, -middle), "status " + status.Tone);
            if (card.TrackOf is int of)
            {
                var used = Mathf.Clamp(card.TrackAt is int at ? at > of ? of : at : 0, 0, of);
                Pips(used, of, status.Tone, new Vector2(S(StatusMarkDp) / 2f, -(middle + S(StatusMarkDp) / 2f + S(PipDropDp) - S(PipDp) / 2f)));
                block = Mathf.Max(block, S(StatusMarkDp) + S(PipDropDp));
            }
            return top + block;
        }

        /// <summary>
        /// The code to scan in the card's middle: a near-white well as wide as the card allows up to <see cref="CodeDp"/>, the
        /// host's cells inside it with <see cref="RoomCode.MarginCells"/> light cells round them. A cell is a whole share of
        /// the well, so every cell stands the same size.
        /// </summary>
        private float LayCode(RoomCode code, float width, float down)
        {
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
        private float LayReading(IReadOnlyList<RoomReadingRow> rows, float width, float down)
        {
            var advance = S(RoomReading.TypeDp) * RoomReading.AdvanceEm;
            var gutter = S(RoomReading.GutterDp);
            var pitch = S(RoomReading.LineDp);
            foreach (var row in rows)
            {
                if (row.Kind == RoomLineKind.Gap)
                {
                    var from = S(RoomReading.GutterDp + RoomReading.MarkGapDp);
                    Plate(new Vector2(width - from, Mathf.Max(S(1f), RoomType.Units(1f))), 0f, White(0.16f), new Vector2(from + (width - from) / 2f, -(down + S(RoomReading.GapRowDp) / 2f)), "reading gap");
                    down += S(RoomReading.GapRowDp);
                    continue;
                }
                var middle = down + pitch / 2f;
                var changed = row.Kind == RoomLineKind.Added || row.Kind == RoomLineKind.Removed;
                if (changed)
                {
                    var hue = row.Kind == RoomLineKind.Added ? Passed : Failed;
                    Plate(new Vector2(width, pitch), S(4f), Rgb(hue, 0.10f), new Vector2(width / 2f, -middle), "reading band", under: true);
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
                    var left = S(RoomReading.TextLeftDp(1f, row.Continued)) + row.Indent * advance;
                    var (label, _) = Text(KitRole.Mono, RoomReading.TypeDp, RoomReading.LineDp, White(changed ? 0.96f : 0.80f), row.Text, left, -down, Mathf.Max(width - left, advance), 1);
                    ReadingShown.Add(label);
                }
                down += pitch;
            }
            return down;
        }

        private float LayHead(RoomCard card, float width, float down)
        {
            var headed = card.Head != RoomCardHead.None;
            var left = headed ? S(HeadDp + HeadGapDp) : 0f;
            var column = width - left;
            var tall = 0f;
            var words = new List<(KitRole Role, float Dp, float LineDp, Color Colour, LineWords Words, int Lines, float Before)>();
            if (card.Title != null)
            {
                // A name is never cut: two lines at the title's size, else three a step smaller, else four smaller still (a long
                // task's name beside its picture at larger text, the gate's 38-result).
                (float Dp, float Line, int Lines) chosen = (FinalTitleTypeDp, FinalTitleLineDp, 4);
                foreach (var (dp, line, lines) in new[] { (TitleTypeDp, TitleLineDp, 2), (LongTitleTypeDp, LongTitleLineDp, 3) })
                {
                    var (probe, _) = Text(KitRole.Title, dp, line, Color.white, card.Title, left, 0f, column, lines);
                    var fits = !probe.isTextOverflowing;
                    probe.gameObject.SetActive(false);
                    Shown.Remove(probe);
                    textsUsed[KitRole.Title]--;
                    if (fits) { chosen = (dp, line, lines); break; }
                }
                words.Add((KitRole.Title, chosen.Dp, chosen.Line, Color.white, card.Title, chosen.Lines, 0f));
            }
            // Measure first, then stand the words in the middle of the head's height where it has one.
            var laid = new List<(TextMeshPro Label, float Tall, float Before)>();
            foreach (var each in words)
            {
                var (label, height) = Text(each.Role, each.Dp, each.LineDp, each.Colour, each.Words, left, 0f, column, each.Lines);
                laid.Add((label, height, S(each.Before)));
                tall += S(each.Before) + height;
            }
            var block = headed ? Mathf.Max(S(HeadDp), tall) : tall;
            var y = down + (block - tall) / 2f;
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
                var (note, _) = Text(KitRole.Body, GaugeResetDp, GaugeResetLineDp, White(0.62f), LineWords.Ours(gauge.Note), 0f, 0f, RoomType.Units(2000f), 1);
                notes.Add(note);
                noteWidth = Mathf.Max(noteWidth, RoomCursor.WordsWidth(note, 0f));
            }
            var bar = inner - (noteWidth > 0f ? noteWidth + S(10f) : 0f);
            var tall = S(GaugePadTopDp) + S(GaugeNumberLineDp) + S(14f) + Mathf.Max(S(TrackTallDp), S(GaugeResetLineDp)) + S(GaugePadFootDp);
            for (var index = 0; index < count; index++)
            {
                var gauge = gauges[index];
                var top = down + index * (tall + S(GaugeGapDp));
                Plate(new Vector2(width, tall), S(TileCornerDp), White(WellAlpha), new Vector2(width / 2f, -(top + tall / 2f)), "gauge tile", under: true);
                var x = S(GaugePadXDp);
                var line = top + S(GaugePadTopDp);
                Glyph(GlyphOf(gauge.Kind), SlotGlyphDp, Rgb(TileInk, 0.86f), new Vector2(x + S(SlotGlyphBoxDp) / 2f, -(line + S(GaugeNumberLineDp) / 2f)), "gauge " + gauge.Kind);
                x += S(SlotGlyphBoxDp) + S(10f);
                var (number, _) = Text(KitRole.Title, GaugeNumberDp, GaugeNumberLineDp, Color.white, LineWords.Ours(gauge.PercentLeft + "%"), x, -line, inner, 1);
                x += RoomCursor.WordsWidth(number, 0f) + S(10f);
                // The window's word on the number's baseline: its line's foot against the number's.
                Text(KitRole.Label, GaugeLabelDp, GaugeLabelLineDp, White(0.66f), gauge.Label, x, -(line + S(GaugeNumberLineDp) - S(GaugeLabelLineDp) - S(4f)), inner, 1);
                var middle = line + S(GaugeNumberLineDp) + S(14f) + Mathf.Max(S(TrackTallDp), S(GaugeResetLineDp)) / 2f;
                Plate(new Vector2(bar, S(TrackTallDp)), S(TrackTallDp) / 2f, White(0.10f), new Vector2(S(GaugePadXDp) + bar / 2f, -middle), "gauge track");
                var filled = bar * gauge.PercentLeft / 100f;
                if (filled > 0f)
                    Plate(new Vector2(filled, S(TrackTallDp)), Mathf.Min(S(TrackTallDp) / 2f, filled / 2f), White(0.62f), new Vector2(S(GaugePadXDp) + filled / 2f, -middle), "gauge fill");
                if (notes[index] is TextMeshPro note)
                    note.transform.localPosition = new Vector3(width - S(GaugePadXDp) - RoomCursor.WordsWidth(note, 0f), -(middle - S(GaugeResetLineDp) / 2f), -RoomType.Units(0.5f));
            }
            return down + count * tall + (count - 1) * S(GaugeGapDp);
        }

        private float LaySlots(IReadOnlyList<RoomSlot> slots, float width, float down)
        {
            var count = Mathf.Min(slots.Count, RoomCard.MostSlots);
            // Each value on one line, never broken or cut: a well is at least as wide as its value and its room. The wells
            // fill a row edge to edge, sharing what is left; where a row cannot hold them (larger text), the next well starts
            // a row of its own, and every row still meets both edges.
            var values = new List<(TextMeshPro Label, float Tall)>();
            var least = new float[count];
            for (var index = 0; index < count; index++)
            {
                var laid = Text(KitRole.Body, CaptionTypeDp, CaptionLineDp, Color.white, slots[index].Value, 0f, 0f, RoomType.Units(2000f), 1);
                values.Add(laid);
                least[index] = Mathf.Min(width, Mathf.Max(RoomCursor.WordsWidth(laid.Label, 0f), S(SlotGlyphBoxDp)) + 2f * S(SlotPadDp));
            }
            var rows = new List<List<int>> { new List<int>() };
            var used = 0f;
            for (var index = 0; index < count; index++)
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
            foreach (var row in rows)
            {
                var sum = 0f;
                foreach (var index in row) sum += least[index];
                var share = (width - S(TileGapDp) * (row.Count - 1) - sum) / row.Count;
                var x = 0f;
                foreach (var index in row)
                {
                    var slot = slots[index];
                    // Every row meets both edges, a row of one too (the built frame's critic, 2026-11-11: a lone well at its own width reads as an orphan).
                    var tile = least[index] + Mathf.Max(0f, share);
                    values[index].Label.rectTransform.sizeDelta = new Vector2(tile - 2f * S(SlotPadDp), values[index].Label.rectTransform.sizeDelta.y);
                    Plate(new Vector2(tile, well), S(TileCornerDp), White(WellAlpha), new Vector2(x + tile / 2f, -(top + well / 2f)), "slot " + slot.Kind, under: true);
                    // The glyph's drawn edge on the value's left edge: its box moved left by where its ink starts.
                    var glyphOf = slot.Kind == RoomKind.Level ? (RoomGlyph?)null : slot.Kind == RoomKind.Passed ? RoomGlyph.Check : slot.Kind == RoomKind.Failed ? RoomGlyph.Alert : GlyphOf(slot.Kind);
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
                            Glyph(RoomGlyph.Alert, SlotGlyphDp, Rgb(Failed), mark, "failed");
                            break;
                        default:
                            Glyph(GlyphOf(slot.Kind), SlotGlyphDp, Rgb(TileInk, 0.86f), mark, "slot " + slot.Kind);
                            break;
                    }
                    values[index].Label.transform.localPosition = new Vector3(x + S(SlotPadDp), -(top + S(SlotPadDp) + S(SlotGlyphBoxDp) + S(CaptionGapDp)), -RoomType.Units(0.5f));
                    x += tile + S(TileGapDp);
                }
                top += well + S(TileGapDp);
            }
            return top - S(TileGapDp);
        }

        private float LaySteps(IReadOnlyList<RoomCardStep> steps, float width, float down)
        {
            // Each step as wide as its name or its mark, whichever is wider; the joins share what is left.
            var widths = new float[steps.Count];
            var labels = new TextMeshPro[steps.Count];
            var tallest = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                var (label, tall) = Text(KitRole.Label, StepNameDp, StepNameLineDp, steps[index].Tone == RoomTone.Skipped ? White(0.50f) : White(0.86f),
                    steps[index].Words ?? LineWords.Ours(steps[index].Name ?? ""), 0f, 0f, width, 1, TextAlignmentOptions.Top);
                labels[index] = label;
                widths[index] = Mathf.Max(S(StepDp), RoomCursor.WordsWidth(label, 0f));
                tallest = Mathf.Max(tallest, tall);
            }
            var sum = 0f;
            foreach (var each in widths) sum += each;
            var joins = steps.Count - 1;
            var join = joins > 0 ? Mathf.Max(S(10f), (width - sum - 2f * S(StepJoinGapDp) * joins) / joins) : 0f;
            var x = 0f;
            var middle = -(down + S(StepDp) / 2f);
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var centre = new Vector2(x + widths[index] / 2f, middle);
                var (ground, ink, glyph, edge) = step.Tone switch
                {
                    RoomTone.Good => (Rgb(PassedGround), Rgb(PassedInk), (RoomGlyph?)RoomGlyph.Check, Color.clear),
                    RoomTone.Bad => (Rgb(FailedGround), Rgb(FailedInk), RoomGlyph.Alert, Color.clear),
                    RoomTone.Working => (Rgb(Accent, 0.22f), Rgb(AccentNote), RoomGlyph.Wrench, Rgb(Accent, 0.70f)),
                    RoomTone.Skipped => (Color.clear, Color.clear, (RoomGlyph?)null, White(0.14f)),
                    _ => (Rgb(StepGround), White(0.55f), RoomGlyph.Wait, White(0.18f)),
                };
                Plate(new Vector2(S(StepDp), S(StepDp)), S(StepCornerDp), ground, centre, "step " + step.Name, edge, edge.a > 0f ? S(2f) : 0f);
                if (glyph is RoomGlyph shown) Glyph(shown, StepGlyphDp, ink, centre, "step " + step.Name);
                if (step.TriesOf > 0) Pips(step.TriesUsed, step.TriesOf, step.Tone, new Vector2(centre.x, centre.y - S(StepDp) / 2f - S(PipDropDp) + S(PipDp) / 2f));
                // The name centred under its mark.
                var label = labels[index];
                label.rectTransform.sizeDelta = new Vector2(widths[index], label.rectTransform.sizeDelta.y);
                label.transform.localPosition = new Vector3(x, -(down + S(StepDp) + S(StepNameGapDp)), -RoomType.Units(0.5f));
                x += widths[index];
                if (index < joins)
                {
                    var joined = step.Tone == RoomTone.Good;
                    Plate(new Vector2(join, S(StepJoinDp)), S(StepJoinDp) / 2f, joined ? Rgb(Passed, 0.55f) : White(0.16f),
                        new Vector2(x + S(StepJoinGapDp) + join / 2f, middle), "join");
                    x += join + 2f * S(StepJoinGapDp);
                }
            }
            return down + S(StepDp) + S(StepNameGapDp) + tallest;
        }

        private float LayList(IReadOnlyList<RoomListRow> rows, float width, float down)
        {
            for (var index = 0; index < rows.Count && index < RoomCard.ListAPage; index++)
            {
                var row = rows[index];
                if (index > 0) Plate(new Vector2(width, S(1f)), 0f, White(0.10f), new Vector2(width / 2f, -(down + S(0.5f))), "hairline");
                var top = down + S(RowPadDp);
                var (tone, glyph) = ToneOf(row);
                // The note reads over any room: a quiet near-white whatever the kind, the kind's colour on its mark alone
                // (the lead, 2026-11-11: "Edited in Both" in the failure's tint read 2.89 to 1 over the bright room).
                var (ground, ink, note) = tone switch
                {
                    RoomTone.Good => (Rgb(PassedGround, ToneTileAlpha * 2f), Rgb(PassedInk), White(NoteAlpha)),
                    RoomTone.Bad => (Rgb(FailedGround, ToneTileAlpha * 2f), Rgb(FailedInk), White(NoteAlpha)),
                    RoomTone.Working => (Rgb(Accent, 0.16f), Rgb(Accent), White(NoteAlpha)),
                    RoomTone.Attention => (Rgb(Attention, 0.16f), Rgb(Attention), White(NoteAlpha)),
                    // A neutral row's mark is a bare glyph, no well, so it never reads as an action's chip.
                    _ => (Color.clear, White(0.85f), White(NoteAlpha)),
                };
                if (row.Chosen) (ground, ink) = (Rgb(Accent, 0.24f), Rgb(AccentNote));
                // The note at the right first, so the name knows how far it may run.
                var noteWidth = 0f;
                TextMeshPro? noteLabel = null;
                if (row.Note != null)
                {
                    (noteLabel, _) = Text(KitRole.Body, RowNoteDp, RowNoteLineDp, note, LineWords.Ours(row.Note), 0f, 0f, width / 2f, 1);
                    noteWidth = RoomCursor.WordsWidth(noteLabel, 0f);
                }
                // A row picked from carries a chevron at its right, the note before it, and takes a press across its whole width.
                var picked = row.Press != null;
                var chevronRoom = picked ? S(ChevronDp + ChevronGapDp) : 0f;
                var nameLeft = S(RowTileDp + RowGapDp);
                var nameWidth = width - nameLeft - chevronRoom - (noteWidth > 0f ? noteWidth + S(RowGapDp) : 0f);
                // A name is never cut: a path or a moment's name takes a second line where one is not enough (a long path at larger text).
                var (nameLabel, nameTall) = row.Mono
                    ? Text(KitRole.Mono, RowNameDp - 3f, RowNameLineDp, Color.white, row.Name, nameLeft, 0f, nameWidth, 2)
                    : Text(KitRole.Body, RowNameDp, RowNameLineDp, Color.white, row.Name, nameLeft, 0f, nameWidth, 2);
                var rowTall = Mathf.Max(S(RowTileDp), nameTall);
                var middle = top + rowTall / 2f;
                nameLabel.transform.localPosition = new Vector3(nameLeft, -(middle - nameTall / 2f), -RoomType.Units(0.5f));
                if (noteLabel != null) noteLabel.transform.localPosition = new Vector3(width - chevronRoom - noteWidth, -(middle - S(RowNoteLineDp) / 2f), -RoomType.Units(0.5f));
                if (picked)
                {
                    Glyph(RoomGlyph.Chevron, ChevronDp, White(0.55f), new Vector2(width - S(ChevronDp) / 2f, -middle), "row chevron");
                    RowTarget(row.Press!, new Vector2(width / 2f, -middle), new Vector2(width, rowTall + 2f * S(RowPadDp)));
                }
                var tileCentre = new Vector2(S(RowTileDp) / 2f, -middle);
                Plate(new Vector2(S(RowTileDp), S(RowTileDp)), S(RowTileCornerDp), ground, tileCentre, "row tile " + row.Kind,
                    row.Chosen ? Rgb(Accent, 0.70f) : Color.clear, row.Chosen ? S(1.5f) : 0f);
                Glyph(glyph, RowGlyphDp, ink, tileCentre, "row " + row.Kind);
                down = top + rowTall + S(RowPadDp);
            }
            return down;
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
            RoomKind.Starter => RoomGlyph.Cube,
            RoomKind.Agent => RoomGlyph.Agent,
            RoomKind.Level => RoomGlyph.Check,
            RoomKind.Folder => RoomGlyph.Folder,
            RoomKind.Passed => RoomGlyph.Check,
            RoomKind.Failed => RoomGlyph.Alert,
            RoomKind.NotRun => RoomGlyph.Wait,
            RoomKind.Waiting => RoomGlyph.Wait,
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
            var lit = tone switch { RoomTone.Good => Rgb(Passed), RoomTone.Bad => Rgb(Failed), RoomTone.Attention => Rgb(Attention), _ => Rgb(Accent) };
            var pitch = S(PipDp + PipGapDp);
            var from = centre.x - pitch * (of - 1) / 2f;
            for (var index = 0; index < of; index++)
            {
                var at = new Vector2(from + index * pitch, centre.y);
                var spent = index < used;
                // Filled where used, hollow where left: like a game's lives, in the node's own colour; a squircle, as the node.
                Plate(new Vector2(S(PipDp), S(PipDp)), S(PipCornerDp), spent ? lit : Color.clear, at, spent ? "pip used" : "pip", lit, spent ? 0f : S(PipLineDp), front: true);
            }
        }

        /// <summary>Lays words of <paramref name="role"/> at a size and line pitch of their own, their box's top left at (x, y); returns the label and how tall its lines stand.</summary>
        private (TextMeshPro Label, float Tall) Text(KitRole role, float dp, float lineDp, Color colour, LineWords words, float x, float y, float width, int lines,
            TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft)
        {
            if (!texts.TryGetValue(role, out var pool)) texts[role] = pool = new List<TextMeshPro>();
            textsUsed.TryGetValue(role, out var used);
            if (used >= pool.Count) pool.Add(KitType.Create(transform, role + " words", role, Color.white, order, TextAlignmentOptions.TopLeft, room: true));
            var label = pool[used];
            textsUsed[role] = used + 1;
            label.gameObject.SetActive(true);
            label.alignment = alignment;
            label.color = colour;
            label.fontSize = GlazeTokens.FontSize(S(dp) / RoomType.Units(1f) * RoomType.Units(1f));
            label.lineSpacing = KitType.LineSpacingFor(label, lineDp / dp);
            label.textWrappingMode = lines > 1 ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            var runs = GlazeRuns.Of(label);
            runs.Set(words);
            runs.SetProseMode(true);
            var laid = KitType.Lay(label, width, lines);
            label.transform.localPosition = new Vector3(x, y, -RoomType.Units(0.5f));
            Shown.Add(label);
            return (label, Mathf.Max(laid.Lines, 1) * S(lineDp));
        }

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

        private void Glyph(RoomGlyph glyph, float dp, Color colour, Vector2 centre, string what)
        {
            if (glyphsUsed >= glyphs.Count) glyphs.Add(RoomGlyphView.Create(transform, "Mark", order));
            var view = glyphs[glyphsUsed++];
            view.Show(glyph, dp * GlazeText.Scale, colour);
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
