#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The information panel's rows drawn (<see cref="RoomRows"/>): each row's words in the panel's reading face, the next
    /// row under a 1 dp hairline with 9 dp above it and below, and a check or a cross before the words of a row that
    /// carries one, in the result's own colours. Its origin is the top left of the first row; it grows down, and says how
    /// tall it stands so its panel can size itself. It draws nothing of the panel and takes no press.
    /// </summary>
    public sealed class RoomRowsView : MonoBehaviour
    {
        /// <summary>The lines one row's words may take at most: a fact longer than a page is the screen's to part.</summary>
        public const int RowLines = 40;

        private readonly List<TextMeshPro> labels = new List<TextMeshPro>();
        // Each hairline is a room part, the redesign's own shape, never the older kit's Surface (JUDGE's census, 2026-11-13: a plain
        // Surface under the rows counted as an older part at launch).
        private readonly List<RoomShape> hairlines = new List<RoomShape>();
        private readonly List<RoomGlyphView> marks = new List<RoomGlyphView>();
        private readonly List<TextMeshPro> tags = new List<TextMeshPro>();
        private readonly List<TextMeshPro> notes = new List<TextMeshPro>();

        /// <summary>A row's label and note: a step smaller than its words and quiet, as a card's notes are.</summary>
        public const float TagDp = 16f;
        public const float TagLineDp = 22f;
        public const float TagAlpha = 0.78f;
        public const float NoteGapDp = 14f;
        private int order;
        private int shown;

        /// <summary>How wide and how tall the rows stand as last shown.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>The rows it shows now.</summary>
        public int Count => gameObject.activeSelf ? shown : 0;

        /// <summary>Row <paramref name="index"/>'s words, its mark (hidden where it has none) and the hairline under it (hidden under the last).</summary>
        public TextMeshPro Label(int index) => labels[index];

        /// <summary>A row's quiet label above its words, where it shows one (<see cref="RoomRow.Label"/>); null otherwise.</summary>
        public TextMeshPro? Tag(int index) => tags[index].gameObject.activeSelf ? tags[index] : null;

        /// <summary>A row's quiet note at its right, where it shows one (<see cref="RoomRow.Note"/>); null otherwise.</summary>
        public TextMeshPro? Note(int index) => notes[index].gameObject.activeSelf ? notes[index] : null;

        public RoomGlyphView Mark(int index) => marks[index];

        public RoomShape Hairline(int index) => hairlines[index];

        /// <summary>The hairline's colour: white at the page's 12 percent, as the alpha a blend in linear light needs over the panel to read as the page's.</summary>
        public static Color HairlineColour => new Color(1f, 1f, 1f, RoomRows.LightAlpha(RoomRows.HairlineAlpha, RoomRows.PanelGround));

        public static Color PassedColour => Rgb(RoomRows.PassedColour);

        public static Color FailedColour => Rgb(RoomRows.FailedColour);

        private static Color Rgb(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);

        /// <summary>Rows under <paramref name="parent"/>, hidden, drawn at <paramref name="order"/> (the panel's words).</summary>
        public static RoomRowsView Create(Transform parent, int order = KitType.Order.Words)
        {
            var go = new GameObject("Rows");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomRowsView>();
            view.order = order;
            go.SetActive(false);
            return view;
        }

        private TextMeshPro? measureWords, measureTag, measureNote;

        /// <summary>
        /// The lines <paramref name="row"/> takes laid as <see cref="Show"/> lays it <paramref name="width"/> wide on a page that
        /// <paramref name="pageMarked"/> or not: its label's lines above, then its words' lines at the width left by the page's
        /// mark indent and its own note's room, by the very lay path the rows are drawn with (XR's and AUDIT's 2026-11-13T2020Z:
        /// one source of truth, so a page of eight lines is eight as drawn). Hidden labels of its own; nothing shown changes.
        /// </summary>
        public int LinesOfRow(RoomRow row, bool pageMarked, float width)
        {
            if (measureWords == null)
            {
                measureWords = RoomType.Create(transform, "Row measure", Color.clear, order);
                measureTag = KitType.Create(transform, "Row label measure", KitRole.Body, Color.clear, order, TMPro.TextAlignmentOptions.TopLeft, room: true);
                measureNote = KitType.Create(transform, "Row note measure", KitRole.Body, Color.clear, order, TMPro.TextAlignmentOptions.TopLeft, room: true);
            }
            var was = gameObject.activeSelf;
            gameObject.SetActive(true);
            measureWords.gameObject.SetActive(true);
            measureTag!.gameObject.SetActive(true);
            measureNote!.gameObject.SetActive(true);
            var indent = pageMarked ? RoomType.Scaled(RoomRows.MarkDp + RoomRows.MarkGapDp) : 0f;
            var lines = 0;
            if (row.Label is string said)
            {
                measureTag.fontSize = GlazeTokens.FontSize(RoomType.Scaled(TagDp));
                measureTag.lineSpacing = KitType.LineSpacingFor(measureTag, TagLineDp / TagDp);
                GlazeRuns.Of(measureTag).Set(LineWords.Ours(said));
                lines += Mathf.Max(1, KitType.Lay(measureTag, width - indent, 2).Lines);
            }
            var noteRoom = 0f;
            if (row.Note is string state)
            {
                measureNote.fontSize = GlazeTokens.FontSize(RoomType.Scaled(TagDp));
                measureNote.lineSpacing = KitType.LineSpacingFor(measureNote, TagLineDp / TagDp);
                GlazeRuns.Of(measureNote).Set(LineWords.Ours(state));
                noteRoom = Mathf.Min(width / 2f, measureNote.GetPreferredValues(measureNote.text).x + RoomType.Units(0.5f)) + RoomType.Scaled(NoteGapDp);
            }
            RoomType.Size(measureWords);
            var runs = GlazeRuns.Of(measureWords);
            runs.Set(row.Words);
            runs.SetProseMode(true);
            lines += Mathf.Max(1, LayAtSeparators(measureWords, runs, width - indent - noteRoom).Lines);
            measureWords.gameObject.SetActive(false);
            measureTag.gameObject.SetActive(false);
            measureNote.gameObject.SetActive(false);
            gameObject.SetActive(was);
            return lines;
        }

        /// <summary>
        /// Shows <paramref name="rows"/> <paramref name="width"/> wide, at the person's text size, and returns how tall
        /// they stand. More than a page's rows are the caller's to turn (<see cref="RoomRows.Page"/>): every row given is drawn.
        /// </summary>
        public float Show(IReadOnlyList<RoomRow> rows, float width)
        {
            gameObject.SetActive(true);
            shown = rows.Count;
            var unit = RoomType.Units(1f);
            var gap = RoomType.Scaled(RoomRows.GapDp);
            var hair = unit * RoomRows.HairlineDp;
            var indent = RoomType.Scaled(RoomRows.IndentDp(rows));
            var line = RoomType.Scaled(RoomGeometry.LineDp);
            var down = 0f;
            for (var index = 0; index < rows.Count; index++)
            {
                while (labels.Count <= index)
                {
                    labels.Add(RoomType.Create(transform, "Row", Color.white, order));
                    hairlines.Add(RoomShape.Create(transform, "Hairline", order));
                    marks.Add(RoomGlyphView.Create(transform, "Mark", order));
                    tags.Add(KitType.Create(transform, "Row label", KitRole.Body, new Color(1f, 1f, 1f, TagAlpha), order, TMPro.TextAlignmentOptions.TopLeft, room: true));
                    notes.Add(KitType.Create(transform, "Row note", KitRole.Body, new Color(1f, 1f, 1f, TagAlpha), order, TMPro.TextAlignmentOptions.TopLeft, room: true));
                }
                var row = rows[index];
                // The row's label above its words and its note at its right, each its own words, quiet (never joined on a dot).
                var tag = tags[index];
                var tagTall = 0f;
                if (row.Label is string said)
                {
                    tag.gameObject.SetActive(true);
                    tag.fontSize = GlazeTokens.FontSize(RoomType.Scaled(TagDp));
                    tag.lineSpacing = KitType.LineSpacingFor(tag, TagLineDp / TagDp);
                    // Heard as given, as a row's words are: a label joined on a dot fails the walks (AUDIT, 2026-11-13).
                    JoinedFacts.Given(said);
                    GlazeRuns.Of(tag).Set(LineWords.Ours(said));
                    tagTall = Mathf.Max(1, KitType.Lay(tag, width - indent, 2).Lines) * RoomType.Scaled(TagLineDp);
                    tag.transform.localPosition = new Vector3(indent, -down, 0f);
                    down += tagTall;
                }
                else tag.gameObject.SetActive(false);
                var note = notes[index];
                var noteRoom = 0f;
                if (row.Note is string state)
                {
                    note.gameObject.SetActive(true);
                    note.fontSize = GlazeTokens.FontSize(RoomType.Scaled(TagDp));
                    note.lineSpacing = KitType.LineSpacingFor(note, TagLineDp / TagDp);
                    JoinedFacts.Given(state);
                    GlazeRuns.Of(note).Set(LineWords.Ours(state));
                    // Every note ends on the row's right edge, so a page's states stand in one column (the reader, 2026-11-13).
                    note.alignment = TextAlignmentOptions.TopRight;
                    var noteWidth = Mathf.Min(width / 2f, note.GetPreferredValues(note.text).x + RoomType.Units(0.5f));
                    KitType.Lay(note, noteWidth, 1);
                    noteRoom = noteWidth + RoomType.Scaled(NoteGapDp);
                }
                else note.gameObject.SetActive(false);
                var label = labels[index];
                label.gameObject.SetActive(true);
                RoomType.Size(label);
                var runs = GlazeRuns.Of(label);
                runs.Set(rows[index].Words);
                runs.SetProseMode(true);
                var laid = LayAtSeparators(label, runs, width - indent - noteRoom);
                // A row that shows a part of its words (a review read part by part): its own lines from the first of the part,
                // as many as the part holds, cut where the rows themselves break the words.
                if (rows[index].FromLine > 0 || rows[index].Lines is int part && label.textInfo.lineCount > part)
                {
                    runs.Set(LineWords.Theirs(LabelText.FromTextMeshPro(Part(label, rows[index].FromLine, rows[index].Lines ?? RowLines))));
                    runs.SetProseMode(true);
                    laid = LayAtSeparators(label, runs, width - indent - noteRoom);
                }
                // Never one word alone on a row's last line (JUDGE's live walk, 2026-11-17: New Project's "by name" at Larger): its last two
                // words held together in the lay only, the same length so every run's place holds (LabelText.KeepLastTwo), as a card's row
                // is; the plain lay kept where the pair cannot stand on one line.
                if (laid.Lines >= 2 && RoomCardView.LastLineIsOneWord(label))
                {
                    var plain = label.text;
                    var held = LabelText.KeepLastTwo(plain);
                    if (held != plain)
                    {
                        label.text = held;
                        var tried = KitType.Lay(label, width - indent - noteRoom, RowLines);
                        if (label.isTextOverflowing || label.isTextTruncated || RoomCardView.PairSplit(label))
                        {
                            label.text = plain;
                            laid = KitType.Lay(label, width - indent - noteRoom, RowLines);
                        }
                        else laid = tried;
                    }
                }
                var tall = Mathf.Max(line, laid.Height);
                label.transform.localPosition = new Vector3(indent, -down, 0f);
                // Its right edge a dp inside the row, so a glyph's ink at the end never passes it.
                if (row.Note != null) note.transform.localPosition = new Vector3(width - RoomType.Units(1f) - (noteRoom - RoomType.Scaled(NoteGapDp)), -down - (line - RoomType.Scaled(TagLineDp)) / 2f, 0f);
                var mark = marks[index];
                if (rows[index].Mark == RoomRowMark.None) mark.Hide();
                else
                {
                    var passed = rows[index].Mark == RoomRowMark.Check;
                    mark.Show(passed ? RoomGlyph.Check : RoomGlyph.Cross, RoomRows.MarkDp * GlazeText.Scale, passed ? PassedColour : FailedColour);
                    mark.transform.localPosition = new Vector3(RoomType.Scaled(RoomRows.MarkDp) / 2f, -down - tall / 2f, 0f);
                }
                down += tall;
                var under = hairlines[index];
                if (index == rows.Count - 1) under.gameObject.SetActive(false);
                else
                {
                    under.gameObject.SetActive(true);
                    under.Draw(new Vector2(width, hair), 0f, HairlineColour);
                    under.transform.localPosition = new Vector3(width / 2f, -down - gap - hair / 2f, 0f);
                    down += gap + hair + gap;
                }
            }
            for (var index = rows.Count; index < labels.Count; index++)
            {
                labels[index].gameObject.SetActive(false);
                hairlines[index].gameObject.SetActive(false);
                marks[index].Hide();
                tags[index].gameObject.SetActive(false);
                notes[index].gameObject.SetActive(false);
            }
            Size = new Vector2(width, down);
            return down;
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// Lays a row's label so that no line ends in the separator (WORDS rule 2, as the panel's body and an action do):
        /// where one would, the break stands in the separator's place, the dot and the spaces round it dropped, and the
        /// places of the row's outside words move with the text.
        /// </summary>
        private static (int Lines, float Width, float Height) LayAtSeparators(TextMeshPro label, GlazeRuns runs, float width)
        {
            JoinedFacts.Given(label.text);
            var laid = KitType.Lay(label, width, RowLines);
            for (var pass = 0; pass < 8; pass++)
            {
                var info = label.textInfo;
                var at = -1;
                for (var index = 0; index < info.lineCount - 1 && at < 0; index++)
                {
                    var last = info.characterInfo[info.lineInfo[index].lastVisibleCharacterIndex];
                    if (last.character == '·') at = last.index;
                }
                var text = label.text;
                if (at < 1 || at + 1 >= text.Length || text[at - 1] != ' ' || text[at + 1] != ' ') break;
                var after = at + 1;
                while (after < text.Length && text[after] == ' ') after++;
                var gone = after - (at - 1) - 1;
                label.text = text.Substring(0, at - 1) + "\n" + text.Substring(after);
                runs.Lean(runs.Theirs.Select(run => run.Start > at ? (run.Start - gone, run.End - gone) : run).ToList());
                laid = KitType.Lay(label, width, RowLines);
            }
            return laid;
        }

        /// <summary>The label's own text of its lines from <paramref name="from"/>, <paramref name="lines"/> of them at most, as it is laid now, never cut inside a doubled backslash.</summary>
        private static string Part(TextMeshPro label, int from, int lines)
        {
            var text = label.text;
            var info = label.textInfo;
            var count = Mathf.Max(1, info.lineCount);
            var first = Mathf.Min(from, count - 1);
            var last = (int)Mathf.Min((long)from + lines, count);
            var start = LabelText.CharacterStart(text, info.characterInfo[info.lineInfo[first].firstCharacterIndex].index);
            var end = last < count ? LabelText.CharacterStart(text, info.characterInfo[info.lineInfo[last].firstCharacterIndex].index) : text.Length;
            return text.Substring(start, end - start).TrimEnd();
        }
    }
}
