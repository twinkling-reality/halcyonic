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

        private readonly List<TextMeshPro> words = new List<TextMeshPro>();
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly List<RoomShape> tiles = new List<RoomShape>();
        private readonly List<RoomGlyphView> marks = new List<RoomGlyphView>();
        private RoomShape? rail;
        private readonly List<TextMeshPro> usageNotes = new List<TextMeshPro>();
        private TextMeshPro? usageLabel;
        private TextMeshPro? usageValue;
        private int order;
        private int shown;

        /// <summary>
        /// The one language's steps (FIELD's sheet, tile f3, the lead's 2026-11-17T01:05Z): on the panel's one glass, a thin rail down
        /// the left with each step's mark on it (a thin check where finished, a white dot where it is now, a hollow diamond
        /// where it is still to come), each step's word in spaced capitals; the step that is now the largest and white, its act
        /// under it in its own case; the next a little brighter than the later ones; the finished ones calm. Never a box per step.
        /// </summary>
        public const float RailXDp = 8f;
        public const float IndentDp = 36f;
        public const float StepGapDp = 26f;
        public const float NowWordScale = 1.5f;
        /// <summary>The steps' words in the one language's label role (FIELD's tokens, RoomType.Label): its size and its spacing.</summary>
        public static float WordScale => RoomType.LabelDp / RoomGeometry.TypeDp;
        public const float Tracking = RoomType.LabelTracking;
        public const float DotDp = 9f;
        public const float DiamondDp = 9f;
        public const float RailAlpha = 0.22f;

        /// <summary>A step's word by where it stands: the next brighter than the later ones, the finished ones calm.</summary>
        public const float NextAlpha = 0.86f;
        public const float LaterAlpha = 0.52f;
        public const float DoneAlpha = 0.56f;

        /// <summary>
        /// The dot on the step that is now: white, its shape alone saying now (the lead's 01:40Z ruling: every mark a white line,
        /// amber only on what waits for the person, the orange only on the main action's rim).
        /// </summary>
        public static readonly Color NowInk = Color.white;

        /// <summary>The thin rail down the left, from the first mark to the last, as last shown; null where fewer than two steps stand.</summary>
        public RoomShape? Rail => rail != null && rail.gameObject.activeSelf ? rail : null;

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

        /// <summary>
        /// Step <paramref name="index"/>'s line as words, as last laid: the step's own, or where its file's path would take more
        /// than three lines, the same act with the path's leading folders left out (<see cref="PathWrap.Fold"/>), its name whole.
        /// </summary>
        public string? LineWordsOf(int index) => index < lineWords.Count ? lineWords[index] : null;

        private readonly List<string?> lineWords = new List<string?>();

        /// <summary>The most lines a step's line takes: a path past them leaves out its leading folders first.</summary>
        public const int LineMostLines = 3;

        /// <summary>Step <paramref name="index"/>'s tile, and the glyph on it (hidden where its mark has none).</summary>
        public RoomShape Tile(int index) => tiles[index];

        public RoomGlyphView Mark(int index) => marks[index];

        /// <summary>
        /// A step's mark: its glyph on the rail (null for none) and the ink of its thin line, white but what waits for the person, on
        /// the attention amber (the lead's 2026-11-17T01:40Z and 02:02Z); never a fill.
        /// </summary>
        public readonly struct StepMark
        {
            public StepMark(RoomGlyph? glyph, Color ink)
            {
                Glyph = glyph;
                Ink = ink;
            }

            public RoomGlyph? Glyph { get; }
            public Color Ink { get; }
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
        /// the four the steps give wears no glyph, never a guessed one. The step that is now is drawn as the white dot, the step
        /// still to come as the hollow diamond (<see cref="LayMark"/>); the wrench names the now step's kind in the logs alone.
        /// </summary>
        public static StepMark MarkOf(TaskStep step) => step.Kind switch
        {
            TaskStepKind.Done => new StepMark(RoomGlyph.Check, new Color(1f, 1f, 1f, DoneAlpha + 0.1f)),
            TaskStepKind.Now => new StepMark(RoomGlyph.Wrench, NowInk),
            TaskStepKind.NotYet => new StepMark(null, Color.white),
            TaskStepKind.NeedsYou => new StepMark(RoomGlyph.Bubble, RoomSurface.Hex(RoomCardView.Attention)),
            TaskStepKind.End => new StepMark(EndGlyphOf(step.Mark), Color.white),
            _ => throw new System.ArgumentOutOfRangeException(nameof(step), step.Kind, "a step of no known kind"),
        };

        private static RoomGlyph? EndGlyphOf(GlazeIcon mark) => mark switch
        {
            GlazeIcon.ChecksFailed or GlazeIcon.CouldNotFinish => RoomGlyph.Alert,
            GlazeIcon.Stopped => RoomGlyph.Stop,
            GlazeIcon.CantTellYet => RoomGlyph.Minus,
            _ => null,
        };

        private static Color Quiet => new Color(1f, 1f, 1f, QuietAlpha);

        /// <summary>
        /// The colour of a step's word: white where it is now; the next step brighter than the later ones; the finished ones calm;
        /// what waits for the person, and an end, white.
        /// </summary>
        public static Color WordColourOf(TaskStep step, bool next) => step.Kind switch
        {
            TaskStepKind.Now or TaskStepKind.NeedsYou or TaskStepKind.End => Color.white,
            TaskStepKind.NotYet => new Color(1f, 1f, 1f, next ? NextAlpha : LaterAlpha),
            TaskStepKind.Done => new Color(1f, 1f, 1f, DoneAlpha),
            _ => throw new System.ArgumentOutOfRangeException(nameof(step), step.Kind, "a step of no known kind"),
        };

        /// <summary>Whether step <paramref name="index"/> is the next to come: the first not reached yet after the one that is now.</summary>
        public static bool IsNext(IReadOnlyList<TaskStep> steps, int index) =>
            steps[index].Kind == TaskStepKind.NotYet && (index == 0 || steps[index - 1].Kind != TaskStepKind.NotYet);

        /// <summary>
        /// Shows <paramref name="steps"/> <paramref name="width"/> wide, at the person's text size, and returns how tall they
        /// stand: each step's word, then its line, the next step the rows' gap under it.
        /// </summary>
        public float Show(IReadOnlyList<TaskStep> steps, float width, TaskUsageFigure? usage = null)
        {
            gameObject.SetActive(true);
            shown = steps.Count;
            var gap = RoomType.Scaled(StepGapDp);
            var railX = RoomType.Scaled(RailXDp);
            var indent = RoomType.Scaled(IndentDp);
            var lineDp = RoomType.Scaled(RoomGeometry.LineDp);
            var column = Mathf.Max(RoomType.Units(40f), width - indent);
            var down = 0f;
            var firstMark = 0f;
            var lastMark = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                while (words.Count <= index)
                {
                    words.Add(RoomType.Create(transform, "Step", Color.white, order));
                    lines.Add(RoomType.Create(transform, "Step line", Color.white, order));
                    tiles.Add(RoomShape.Create(transform, "Step mark shape", order));
                    marks.Add(RoomGlyphView.Create(transform, "Step mark", order));
                }
                var step = steps[index];
                var now = step.Kind == TaskStepKind.Now;
                var word = words[index];
                word.gameObject.SetActive(true);
                RoomType.Size(word);
                word.fontSize *= now ? NowWordScale : WordScale;
                word.fontStyle = FontStyles.UpperCase;
                word.characterSpacing = Tracking;
                word.color = WordColourOf(step, IsNext(steps, index));
                GlazeRuns.Of(word).Set(LineWords.Ours(step.Word));
                var wordTall = Mathf.Max(lineDp, Balanced(word, column));
                word.transform.localPosition = new Vector3(indent, -down, 0f);
                // The mark on the rail at the word's first line's middle.
                var firstLine = Mathf.Min(wordTall, KitType.LinePitch(word));
                var markAt = new Vector3(railX, -down - firstLine / 2f, 0f);
                if (index == 0) firstMark = -markAt.y;
                lastMark = -markAt.y;
                LayMark(index, step, markAt);
                down += wordTall;
                var line = lines[index];
                while (lineWords.Count <= index) lineWords.Add(null);
                lineWords[index] = null;
                if (step.Line is LineWords said && said.Text.Length > 0)
                {
                    lineWords[index] = said.Text;
                    line.gameObject.SetActive(true);
                    RoomType.Size(line);
                    // The act under the step that is now, and what waits for the person, in their own case, white; any other line quiet.
                    // Words are never amber: only the waiting step's mark is (the lead's 2026-11-17T02:02Z).
                    line.color = new Color(1f, 1f, 1f, now || step.Kind == TaskStepKind.NeedsYou ? 1f : QuietAlpha);
                    // Whole: nothing on the list is cut (the owner's bar, the reader of 04:48Z); a file's path breaks only at its slashes
                    // and leaves out its leading folders before it takes a fourth line (the lead's 2026-11-17T02:03Z).
                    GlazeRuns.Of(line).Set(said);
                    var lineTall = PathFolded(index, line, said, column) is float folded ? folded : Balanced(line, column);
                    line.transform.localPosition = new Vector3(indent, -down, 0f);
                    down += Mathf.Max(lineDp, lineTall);
                }
                else line.gameObject.SetActive(false);
                if (index < steps.Count - 1) down += gap;
            }
            // The thin rail joining the marks, behind them.
            if (steps.Count >= 2)
            {
                rail ??= RoomShape.Create(transform, "Step rail", order - 1);
                rail.gameObject.SetActive(true);
                var stroke = RoomType.Scaled(1f);
                rail.Draw(new Vector2(stroke, lastMark - firstMark), 0f, new Color(1f, 1f, 1f, RailAlpha));
                rail.transform.localPosition = new Vector3(railX, -(firstMark + lastMark) / 2f, 0.0003f);
            }
            else rail?.gameObject.SetActive(false);
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
            // The label and the value apart on one line (the lead's 2026-11-17T02:02Z): the label in the one language's label role,
            // spaced capitals, quiet; the value in the words' role, the row's own size, white.
            RoomType.Label(usageLabel, usage.Label);
            RoomType.Size(usageValue);
            usageValue.color = Color.white;
            GlazeRuns.Of(usageValue).Set(LineWords.Ours(usage.Value));
            var labelLine = KitType.LinePitch(usageLabel);
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
                var noteTall = Balanced(note, column);
                note.transform.localPosition = new Vector3(indent, -down, 0f);
                down += Mathf.Max(lineDp, noteTall);
            }
            for (var index = usage.Notes.Count; index < usageNotes.Count; index++) usageNotes[index].gameObject.SetActive(false);
            return down;
        }

        /// <summary>
        /// Lays <paramref name="label"/> at <paramref name="column"/> on the list's lines and returns its height, with no word alone on its last
        /// line where it runs four words or more (AUDIT 2026-11-16T15:23Z; JUDGE's live walk at 19:08Z: "visits!" and "first." alone at
        /// Larger): the measure narrows a twentieth at a time, down to six tenths, never onto another line nor cut; where none does, as
        /// first laid. The words, theirs or ours, stand as written.
        /// </summary>
        private static float Balanced(TMP_Text label, float column)
        {
            var laid = KitType.Lay(label, column, RoomRowsView.RowLines);
            if (laid.Lines < 2 || !LastLineIsOneWord(label) || label.text.IndexOf('\n') >= 0
                || label.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries).Length < 4) return laid.Height;
            for (var step = 1; step <= 8; step++)
            {
                var tried = KitType.Lay(label, column * (1f - 0.05f * step), RoomRowsView.RowLines);
                if (tried.Lines != laid.Lines || label.isTextTruncated) break;
                if (!LastLineIsOneWord(label)) return tried.Height;
            }
            return KitType.Lay(label, column, RoomRowsView.RowLines).Height;
        }

        /// <summary>
        /// Step <paramref name="index"/>'s line, where <paramref name="said"/> is our act and then a file's path of theirs ("Editing " and the path) that does not fit one
        /// line: the path in lines breaking only after its slashes, its leading folders left out before a fourth line, its name
        /// whole (<see cref="PathWrap.Fold"/>), the act ours and the path theirs as given. Returns the height, or null for any
        /// other line, which is laid as words.
        /// </summary>
        private float? PathFolded(int index, TextMeshPro label, LineWords said, float column)
        {
            if (said.Runs.Count != 2 || said.Runs[0].Theirs || !said.Runs[1].Theirs) return null;
            var lead = said.Runs[0].Text;
            var path = said.Runs[1].Text;
            if (path.IndexOf(' ') >= 0 || path.IndexOf('/') < 0 || label.text != said.Text) return null;
            bool Fits(string text) => label.GetPreferredValues(text).x <= column + 1e-4f;
            if (Fits(said.Text)) return null;
            var (folded, shown) = PathWrap.Fold(lead, path, Fits, LineMostLines);
            lineWords[index] = lead + shown;
            var joined = string.Join("\n", folded);
            GlazeText.SetLiteral(label, joined);
            // The act stays ours; every character of the path, the elision with it, leans as theirs.
            GlazeRuns.Of(label).Lean(new List<(int Start, int End)> { (lead.Length, label.text.Length) });
            var drawn = KitType.Lay(label, column, Mathf.Max(LineMostLines, folded.Count));
            return Mathf.Max(drawn.Lines, 1) * KitType.LinePitch(label);
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

        /// <summary>A glyph's ink on the rail: a finished step's check calm, what waits for the person on the attention amber, an end white.</summary>
        public static Color InkOf(TaskStep step) => MarkOf(step).Ink;

        /// <summary>
        /// Step <paramref name="index"/>'s mark on the rail at <paramref name="at"/>: the thin check where finished, a white dot
        /// where it is now, a hollow diamond where it is still to come; what waits for the person and an end keep
        /// their own glyphs (the bubble on the attention amber, the alert, the square, the minus).
        /// </summary>
        private void LayMark(int index, TaskStep step, Vector3 at)
        {
            var shape = tiles[index];
            var glyph = marks[index];
            shape.transform.localPosition = at + new Vector3(0f, 0f, 0.0002f);
            switch (step.Kind)
            {
                case TaskStepKind.Now:
                    shape.gameObject.SetActive(true);
                    var dot = RoomType.Scaled(DotDp);
                    shape.Draw(new Vector2(dot, dot), dot / 2f, NowInk);
                    glyph.Hide();
                    break;
                case TaskStepKind.NotYet:
                    shape.gameObject.SetActive(true);
                    var side = RoomType.Scaled(DiamondDp) / Mathf.Sqrt(2f);
                    shape.Draw(new Vector2(side, side), 0f, Color.clear, new Color(1f, 1f, 1f, 0.6f), RoomType.Scaled(1.2f));
                    shape.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    glyph.Hide();
                    break;
                default:
                    shape.gameObject.SetActive(false);
                    var mark = MarkOf(step);
                    if (mark.Glyph is RoomGlyph drawn)
                    {
                        glyph.Show(drawn, TileGlyphDp * GlazeText.Scale, InkOf(step), RoomCardView.MarkStroke);
                        glyph.transform.localPosition = at;
                    }
                    else glyph.Hide();
                    break;
            }
            if (step.Kind != TaskStepKind.NotYet) shape.transform.localRotation = Quaternion.identity;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
