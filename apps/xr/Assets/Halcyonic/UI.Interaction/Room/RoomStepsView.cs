#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The plain "what is happening" list drawn (<see cref="TaskSteps"/>): one step a row, its mark a small solid tile before
    /// its word, as the Checks card's nodes are (one system: a step done is the check on the deep green, the step that is now
    /// the work's wrench on its blue, a step that waits for the person the bubble on the attention amber, an end its own mark
    /// on its tone: the alert on the deep red for checks failed or could not finish, the square for stopped, the minus for
    /// can't tell, never the failed colour; a step not reached yet the dim empty tile, its word quiet). Its word in the
    /// panel's reading face, white but where it is not reached yet, and at most one line under it in the same face and size,
    /// quieter, amber where it says what waits for the person, laid whole: nothing on it is ever cut. No plate, no hairline,
    /// no press: it only says, so nothing in it reads as a button. Its origin is the top left of the first step; it grows
    /// down and says how tall it stands, so its panel can size itself.
    /// </summary>
    public sealed class RoomStepsView : MonoBehaviour
    {
        /// <summary>How quiet a line under a step, and a step not reached yet, stand: the rows' own quiet notes.</summary>
        public const float QuietAlpha = RoomRowsView.TagAlpha;

        /// <summary>A step's mark: a tile this wide, its corner, its glyph, and the gap before the step's word.</summary>
        public const float TileDp = 24f;
        public const float TileCornerDp = 7f;
        public const float TileGlyphDp = 16f;
        public const float TileGapDp = 10f;

        /// <summary>The dim empty tile's edge, as the Checks card's node never run: it holds 3:1 over the bright room.</summary>
        public const float EmptyEdgeAlpha = 0.64f;

        private readonly List<TextMeshPro> words = new List<TextMeshPro>();
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly List<RoomShape> tiles = new List<RoomShape>();
        private readonly List<RoomGlyphView> marks = new List<RoomGlyphView>();
        private readonly List<TextMeshPro> usageNotes = new List<TextMeshPro>();
        private TextMeshPro? usageLabel;
        private TextMeshPro? usageValue;
        private int order;
        private int shown;

        /// <summary>The gap between a usage figure's label and its value, laid apart on one line, never joined on a dot.</summary>
        public const float UsageGapDp = 10f;

        /// <summary>The usage figure's label, value and notes as last shown; null where the list shows none.</summary>
        public TextMeshPro? UsageLabel => usageLabel != null && usageLabel.gameObject.activeSelf ? usageLabel : null;

        public TextMeshPro? UsageValue => usageValue != null && usageValue.gameObject.activeSelf ? usageValue : null;

        public IEnumerable<TextMeshPro> UsageNotes => usageNotes.FindAll(note => note.gameObject.activeSelf);

        /// <summary>How wide and how tall the steps stand as last shown.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>The steps it shows now.</summary>
        public int Count => gameObject.activeSelf ? shown : 0;

        /// <summary>Step <paramref name="index"/>'s word.</summary>
        public TextMeshPro Word(int index) => words[index];

        /// <summary>Step <paramref name="index"/>'s line under its word, where it shows one; null otherwise.</summary>
        public TextMeshPro? Line(int index) => lines[index].gameObject.activeSelf ? lines[index] : null;

        /// <summary>Step <paramref name="index"/>'s tile, and the glyph on it (hidden where its mark has none).</summary>
        public RoomShape Tile(int index) => tiles[index];

        public RoomGlyphView Mark(int index) => marks[index];

        public static Color AttentionLineColour => RoomSurface.Hex(RoomCardView.AttentionNote);

        /// <summary>A step's mark: the glyph on its tile (null for none), the glyph's ink, the tile's fill and its edge.</summary>
        public readonly struct StepMark
        {
            public StepMark(RoomGlyph? glyph, Color ink, Color fill, Color edge)
            {
                Glyph = glyph;
                Ink = ink;
                Fill = fill;
                Edge = edge;
            }

            public RoomGlyph? Glyph { get; }
            public Color Ink { get; }
            public Color Fill { get; }
            public Color Edge { get; }
        }

        /// <summary>Steps under <paramref name="parent"/>, hidden, drawn at <paramref name="order"/> (the panel's words).</summary>
        public static RoomStepsView Create(Transform parent, int order = KitType.Order.Words)
        {
            var go = new GameObject("Steps");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomStepsView>();
            view.order = order;
            go.SetActive(false);
            return view;
        }

        /// <summary>
        /// The mark a step wears: by where the step stands, and for an end, by the end's own mark. An end whose mark is none of
        /// the four the steps give wears its quiet tile with no glyph, never a guessed one.
        /// </summary>
        public static StepMark MarkOf(TaskStep step) => step.Kind switch
        {
            TaskStepKind.Done => new StepMark(RoomGlyph.Check, Color.white, RoomSurface.Hex(RoomCardView.GoodFill), Color.clear),
            TaskStepKind.Now => new StepMark(RoomGlyph.Wrench, Color.white, RoomSurface.Hex(RoomCardView.WorkingFill), Color.clear),
            TaskStepKind.NotYet => new StepMark(null, Color.clear, Color.clear, new Color(1f, 1f, 1f, EmptyEdgeAlpha)),
            TaskStepKind.NeedsYou => new StepMark(RoomGlyph.Bubble, RoomSurface.Hex(RoomCardView.AttentionInk), RoomSurface.Hex(RoomCardView.Attention), Color.clear),
            _ => EndMarkOf(step.Mark),
        };

        /// <summary>An end's mark: checks failed or could not finish the alert on the deep red, stopped the square, can't tell the minus, on the quiet tile.</summary>
        private static StepMark EndMarkOf(GlazeIcon mark)
        {
            var glyph = EndGlyphOf(mark);
            return new StepMark(glyph, Color.white, RoomSurface.Hex(glyph == RoomGlyph.Alert ? RoomCardView.BadFill : RoomCardView.QuietFill), Color.clear);
        }

        private static RoomGlyph? EndGlyphOf(GlazeIcon mark) => mark switch
        {
            GlazeIcon.ChecksFailed or GlazeIcon.CouldNotFinish => RoomGlyph.Alert,
            GlazeIcon.Stopped => RoomGlyph.Stop,
            GlazeIcon.CantTellYet => RoomGlyph.Minus,
            _ => null,
        };

        private static Color Quiet => new Color(1f, 1f, 1f, QuietAlpha);

        /// <summary>The colour of a step's word: quiet where it is not reached yet, else white; what waits for the person says it in its mark and its line.</summary>
        public static Color WordColourOf(TaskStep step) => step.Kind switch
        {
            TaskStepKind.NotYet => Quiet,
            TaskStepKind.Done or TaskStepKind.Now or TaskStepKind.NeedsYou => Color.white,
            _ => Color.white,
        };

        /// <summary>
        /// Shows <paramref name="steps"/> <paramref name="width"/> wide, at the person's text size, and returns how tall they
        /// stand: each step's word, then its line, the next step the rows' gap under it.
        /// </summary>
        public float Show(IReadOnlyList<TaskStep> steps, float width, TaskUsageFigure? usage = null)
        {
            gameObject.SetActive(true);
            shown = steps.Count;
            var gap = RoomType.Scaled(RoomRows.GapDp);
            var tile = RoomType.Scaled(TileDp);
            var indent = tile + RoomType.Scaled(TileGapDp);
            var lineDp = RoomType.Scaled(RoomGeometry.LineDp);
            var column = Mathf.Max(RoomType.Units(40f), width - indent);
            var down = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                while (words.Count <= index)
                {
                    words.Add(RoomType.Create(transform, "Step", Color.white, order));
                    lines.Add(RoomType.Create(transform, "Step line", Color.white, order));
                    tiles.Add(RoomShape.Create(transform, "Step tile", order - 1));
                    marks.Add(RoomGlyphView.Create(transform, "Step mark", order));
                }
                var step = steps[index];
                var word = words[index];
                word.gameObject.SetActive(true);
                RoomType.Size(word);
                word.color = WordColourOf(step);
                GlazeRuns.Of(word).Set(LineWords.Ours(step.Word));
                var (_, _, wordTall) = KitType.Lay(word, column, RoomRowsView.RowLines);
                wordTall = Mathf.Max(lineDp, wordTall);
                word.transform.localPosition = new Vector3(indent, -down, 0f);
                // The tile before the word's first line, at its middle, so a step's mark and its word read as one.
                var mark = MarkOf(step);
                var centre = new Vector3(tile / 2f, -down - lineDp / 2f, 0f);
                var tileView = tiles[index];
                tileView.gameObject.SetActive(true);
                tileView.Draw(new Vector2(tile, tile), RoomType.Scaled(TileCornerDp), RoomCardView.AsShown(mark.Fill), RoomCardView.AsShown(mark.Edge),
                    mark.Edge.a > 0f ? RoomType.Scaled(2f) : 0f);
                tileView.transform.localPosition = centre + new Vector3(0f, 0f, 0.0002f);
                var glyph = marks[index];
                if (mark.Glyph is RoomGlyph drawn)
                {
                    glyph.Show(drawn, TileGlyphDp * GlazeText.Scale, mark.Ink, RoomCardView.MarkStroke);
                    glyph.transform.localPosition = centre;
                }
                else glyph.Hide();
                down += wordTall;
                var line = lines[index];
                if (step.Line is LineWords said && said.Text.Length > 0)
                {
                    line.gameObject.SetActive(true);
                    RoomType.Size(line);
                    var tone = step.Kind == TaskStepKind.NeedsYou ? AttentionLineColour : Color.white;
                    line.color = new Color(tone.r, tone.g, tone.b, step.Kind == TaskStepKind.NeedsYou ? 1f : QuietAlpha);
                    // Whole, the person's words and ours alike: nothing on the list is cut (the owner's bar, the reader of 04:48Z).
                    GlazeRuns.Of(line).Set(said);
                    var lineTall = KitType.Lay(line, column, RoomRowsView.RowLines).Height;
                    line.transform.localPosition = new Vector3(indent, -down, 0f);
                    down += Mathf.Max(lineDp, lineTall);
                }
                else line.gameObject.SetActive(false);
                if (index < steps.Count - 1) down += gap;
            }
            for (var index = steps.Count; index < words.Count; index++)
            {
                words[index].gameObject.SetActive(false);
                lines[index].gameObject.SetActive(false);
                tiles[index].gameObject.SetActive(false);
                marks[index].Hide();
            }
            down = LayUsage(usage, width, indent, down, steps.Count > 0 ? gap : 0f);
            Size = new Vector2(width, down);
            return down;
        }

        /// <summary>
        /// Under the last step, where the caller hands one (only where the task's agent app reports usage): the figure's label
        /// quiet and its value white apart on one line, aligned with the steps' words, then each note a quiet line under it, laid
        /// whole. Returns how far down the list now ends.
        /// </summary>
        private float LayUsage(TaskUsageFigure? usage, float width, float indent, float down, float gap)
        {
            if (usage == null)
            {
                usageLabel?.gameObject.SetActive(false);
                usageValue?.gameObject.SetActive(false);
                foreach (var note in usageNotes) note.gameObject.SetActive(false);
                return down;
            }
            var lineDp = RoomType.Scaled(RoomGeometry.LineDp);
            var column = Mathf.Max(RoomType.Units(40f), width - indent);
            down += gap;
            usageLabel ??= RoomType.Create(transform, "Usage label", Color.white, order);
            usageValue ??= RoomType.Create(transform, "Usage value", Color.white, order);
            usageLabel.gameObject.SetActive(true);
            usageValue.gameObject.SetActive(true);
            // As the usage tile lays them (AUDIT 2026-11-16 12:19Z: "Tokens Used  Not Reported Yet" read as one run): the label
            // small and quiet at the tile's label size, the value in the row's own size and white.
            var labelSize = GlazeTokens.FontSize(RoomType.Scaled(RoomCardView.GaugeLabelDp));
            if (!Mathf.Approximately(usageLabel.fontSize, labelSize)) usageLabel.fontSize = labelSize;
            usageLabel.lineSpacing = KitType.LineSpacingFor(usageLabel, RoomCardView.GaugeLabelLineDp / RoomCardView.GaugeLabelDp);
            RoomType.Size(usageValue);
            usageLabel.color = Quiet;
            usageValue.color = Color.white;
            GlazeRuns.Of(usageLabel).Set(LineWords.Ours(usage.Label));
            GlazeRuns.Of(usageValue).Set(LineWords.Ours(usage.Value));
            var labelLine = RoomType.Scaled(RoomCardView.GaugeLabelLineDp);
            var labelWide = KitType.Lay(usageLabel, column, 1).Width;
            var valueLeft = labelWide + RoomType.Scaled(UsageGapDp);
            // Beside the label where the value fits whole on one line; else under it, the column's width, whole, never a word alone
            // on its last line.
            var beside = column - valueLeft >= RoomType.Units(40f) && KitType.Lay(usageValue, column - valueLeft, 1).Width > 0f && !usageValue.isTextTruncated;
            float valueTall;
            if (beside)
            {
                valueTall = KitType.Lay(usageValue, column - valueLeft, 1).Height;
                usageLabel.transform.localPosition = new Vector3(indent, -(down + Mathf.Max(0f, (lineDp - labelLine) / 2f)), 0f);
                usageValue.transform.localPosition = new Vector3(indent + valueLeft, -down, 0f);
                down += Mathf.Max(lineDp, valueTall);
            }
            else
            {
                usageLabel.transform.localPosition = new Vector3(indent, -down, 0f);
                down += labelLine;
                var laid = KitType.Lay(usageValue, column, RoomRowsView.RowLines);
                for (var step = 1; step <= 6 && laid.Lines >= 2 && LastLineIsOneWord(usageValue); step++)
                {
                    var tried = KitType.Lay(usageValue, column * (1f - 0.06f * step), RoomRowsView.RowLines);
                    if (tried.Lines != laid.Lines || usageValue.isTextTruncated) break;
                    laid = tried;
                }
                if (LastLineIsOneWord(usageValue) || usageValue.isTextTruncated) laid = KitType.Lay(usageValue, column, RoomRowsView.RowLines);
                valueTall = laid.Height;
                usageValue.transform.localPosition = new Vector3(indent, -down, 0f);
                down += Mathf.Max(lineDp, valueTall);
            }
            for (var index = 0; index < usage.Notes.Count; index++)
            {
                while (usageNotes.Count <= index) usageNotes.Add(RoomType.Create(transform, "Usage note", Color.white, order));
                var note = usageNotes[index];
                note.gameObject.SetActive(true);
                RoomType.Size(note);
                note.color = Quiet;
                GlazeRuns.Of(note).Set(LineWords.Ours(usage.Notes[index]));
                var noteTall = KitType.Lay(note, column, RoomRowsView.RowLines).Height;
                note.transform.localPosition = new Vector3(indent, -down, 0f);
                down += Mathf.Max(lineDp, noteTall);
            }
            for (var index = usage.Notes.Count; index < usageNotes.Count; index++) usageNotes[index].gameObject.SetActive(false);
            return down;
        }

        /// <summary>Whether the label's last laid line holds one word alone.</summary>
        private static bool LastLineIsOneWord(TMP_Text label)
        {
            var info = label.textInfo;
            if (info.lineCount < 2) return false;
            var last = info.lineInfo[info.lineCount - 1];
            for (var index = last.firstCharacterIndex; index <= last.lastVisibleCharacterIndex && index < info.characterCount; index++)
                if (char.IsWhiteSpace(info.characterInfo[index].character)) return false;
            return last.lastVisibleCharacterIndex >= last.firstCharacterIndex;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
