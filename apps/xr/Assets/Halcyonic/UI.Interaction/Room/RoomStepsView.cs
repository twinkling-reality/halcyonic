#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The plain "what is happening" list drawn (<see cref="TaskSteps"/>): one step a row, its mark in the rows' mark column, its
    /// word in the panel's reading face, and at most one line under it in the same face and size, quieter, its runs leaning where they are the person's or a file's. A step done wears the
    /// rows' green check; the step that is now the work's wrench; a step not reached yet an open ring, its word quiet; the step
    /// that waits for the person the attention amber on its mark, its word and its line; an end its state's mark (checks failed
    /// or could not finish the alert in the failed colour, stopped the square, can't tell the quiet minus, never the failed
    /// colour). No plate, no hairline, no press: it only says, so nothing in it reads as a button. Its origin is the top left of
    /// the first step; it grows down and says how tall it stands, so its panel can size itself.
    /// </summary>
    public sealed class RoomStepsView : MonoBehaviour
    {
        /// <summary>How quiet a line under a step, and a step not reached yet, stand: the rows' own quiet notes.</summary>
        public const float QuietAlpha = RoomRowsView.TagAlpha;

        /// <summary>The most lines the person's own words take under their request, cut at a word past them; ours are never cut.</summary>
        public const int TheirLines = 3;

        private readonly List<TextMeshPro> words = new List<TextMeshPro>();
        private readonly List<TextMeshPro> lines = new List<TextMeshPro>();
        private readonly List<RoomGlyphView> marks = new List<RoomGlyphView>();
        private int order;
        private int shown;

        /// <summary>How wide and how tall the steps stand as last shown.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>The steps it shows now.</summary>
        public int Count => gameObject.activeSelf ? shown : 0;

        /// <summary>Step <paramref name="index"/>'s word.</summary>
        public TextMeshPro Word(int index) => words[index];

        /// <summary>Step <paramref name="index"/>'s line under its word, where it shows one; null otherwise.</summary>
        public TextMeshPro? Line(int index) => lines[index].gameObject.activeSelf ? lines[index] : null;

        public RoomGlyphView Mark(int index) => marks[index];

        public static Color AttentionColour => RoomSurface.Hex(RoomCardView.Attention);

        public static Color AttentionLineColour => RoomSurface.Hex(RoomCardView.AttentionNote);

        public static Color WorkingColour => RoomSurface.Hex(RoomCardView.WorkingFill);

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
        /// The mark a step wears and its colour: by where the step stands, and for an end, by the end's own mark. An end whose
        /// mark is none of the four the steps give wears no mark (null), never a guessed one.
        /// </summary>
        public static (RoomGlyph? Glyph, Color Colour) MarkOf(TaskStep step) => step.Kind switch
        {
            TaskStepKind.Done => (RoomGlyph.Check, RoomRowsView.PassedColour),
            TaskStepKind.Now => (RoomGlyph.Wrench, WorkingColour),
            TaskStepKind.NotYet => (RoomGlyph.Ring, Quiet),
            TaskStepKind.NeedsYou => (RoomGlyph.Bubble, AttentionColour),
            _ => EndMarkOf(step.Mark),
        };

        /// <summary>An end's mark: checks failed or could not finish the alert in the failed colour, stopped the square, can't tell the quiet minus.</summary>
        private static (RoomGlyph? Glyph, Color Colour) EndMarkOf(GlazeIcon mark)
        {
            var glyph = EndGlyphOf(mark);
            return (glyph, glyph == RoomGlyph.Alert ? RoomRowsView.FailedColour : glyph == RoomGlyph.Minus ? Quiet : Color.white);
        }

        private static RoomGlyph? EndGlyphOf(GlazeIcon mark) => mark switch
        {
            GlazeIcon.ChecksFailed or GlazeIcon.CouldNotFinish => RoomGlyph.Alert,
            GlazeIcon.Stopped => RoomGlyph.Stop,
            GlazeIcon.CantTellYet => RoomGlyph.Minus,
            _ => null,
        };

        private static Color Quiet => new Color(1f, 1f, 1f, QuietAlpha);

        /// <summary>The colour of a step's word: quiet where it is not reached yet, amber where it waits for the person, else white.</summary>
        public static Color WordColourOf(TaskStep step) => step.Kind switch
        {
            TaskStepKind.NotYet => Quiet,
            TaskStepKind.NeedsYou => AttentionColour,
            TaskStepKind.Done or TaskStepKind.Now => Color.white,
            _ => Color.white,
        };

        /// <summary>
        /// Shows <paramref name="steps"/> <paramref name="width"/> wide, at the person's text size, and returns how tall they
        /// stand: each step's word, then its line, the next step the rows' gap under it.
        /// </summary>
        public float Show(IReadOnlyList<TaskStep> steps, float width)
        {
            gameObject.SetActive(true);
            shown = steps.Count;
            var gap = RoomType.Scaled(RoomRows.GapDp);
            var indent = RoomType.Scaled(RoomRows.MarkDp + RoomRows.MarkGapDp);
            var lineDp = RoomType.Scaled(RoomGeometry.LineDp);
            var column = Mathf.Max(RoomType.Units(40f), width - indent);
            var down = 0f;
            for (var index = 0; index < steps.Count; index++)
            {
                while (words.Count <= index)
                {
                    words.Add(RoomType.Create(transform, "Step", Color.white, order));
                    lines.Add(RoomType.Create(transform, "Step line", Color.white, order));
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
                var (glyph, colour) = MarkOf(step);
                var mark = marks[index];
                if (glyph is RoomGlyph drawn) mark.Show(drawn, RoomRows.MarkDp * GlazeText.Scale, colour);
                else mark.Hide();
                // Before the word's first line, at its middle, so a step's mark and its word read as one.
                mark.transform.localPosition = new Vector3(RoomType.Scaled(RoomRows.MarkDp) / 2f, -down - lineDp / 2f, 0f);
                down += wordTall;
                var line = lines[index];
                if (step.Line is LineWords said && said.Text.Length > 0)
                {
                    line.gameObject.SetActive(true);
                    RoomType.Size(line);
                    var tone = step.Kind == TaskStepKind.NeedsYou ? AttentionLineColour : Color.white;
                    line.color = new Color(tone.r, tone.g, tone.b, step.Kind == TaskStepKind.NeedsYou ? 1f : QuietAlpha);
                    // Wholly the person's own words (their request): its lead cut at a word past its lines; any line with ours in it whole.
                    var lineTall = said.AllTheirs ? Excerpt(line, said, column, TheirLines) : Laid(line, said, column);
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
                marks[index].Hide();
            }
            Size = new Vector2(width, down);
            return down;
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Our own words, whole, on as many lines as they take.</summary>
        private static float Laid(TextMeshPro label, LineWords said, float width)
        {
            GlazeRuns.Of(label).Set(said);
            return KitType.Lay(label, width, RoomRowsView.RowLines).Height;
        }

        /// <summary>
        /// The person's own words on at most <paramref name="most"/> lines: whole where they fit, else their lead cut at a word
        /// with its ellipsis, as a card's name of theirs is cut.
        /// </summary>
        private static float Excerpt(TextMeshPro label, LineWords theirs, float width, int most)
        {
            var runs = GlazeRuns.Of(label);
            bool Fits(LineWords tried)
            {
                runs.Set(tried);
                KitType.Lay(label, width, most);
                return !label.isTextOverflowing;
            }
            if (Fits(theirs)) return KitType.Lay(label, width, most).Height;
            var cut = theirs.CutToFit(Fits);
            var text = cut.Text;
            if (text.EndsWith(LineWords.Ellipsis, System.StringComparison.Ordinal))
            {
                // At a word: where the cut fell inside one, back to the space before it, so no word is shown in part.
                var body = text.Substring(0, text.Length - LineWords.Ellipsis.Length).TrimEnd();
                var whole = theirs.Text;
                var space = body.LastIndexOf(' ');
                if (body.Length < whole.Length && !char.IsWhiteSpace(whole[body.Length]) && space > 0) body = body.Substring(0, space);
                cut = LineWords.Theirs(body.TrimEnd(' ', ',', ';', ':') + LineWords.Ellipsis);
            }
            runs.Set(cut);
            return KitType.Lay(label, width, most).Height;
        }
    }
}
