#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Draws a page of a section's answer, Help me understand's or What was checked?'s, in the space
    /// the workspace's frame leaves under the section's heading: the provenance line, which may wrap,
    /// then each claim with its epistemic class beside it, or each measurement with its part, each
    /// line on as many rows as it may take; a second source's provenance line runs the full width, as
    /// the first does. It derives nothing; the client core's presenters write every word and split
    /// the pages. A claim, the agent's words, a model's explanation or a source quoting them, leans,
    /// as every claim in the workspace does.
    ///
    /// Everything it shows can come from a source, which may quote an agent, so it is untrusted. Every
    /// label shows its text by the one rule for text Halcyonic did not write
    /// (<see cref="GlazeText.SetLiteral"/>): no markup, every backslash doubled for escape parsing,
    /// and what would not show as itself shown as its code point. WorkspaceRender checks it on real
    /// labels in the editor.
    /// </summary>
    public sealed class SectionView : MonoBehaviour
    {
        private const int MaxRows = 10;
        private const int MaxProvenanceRows = 2;
        private const float TagDegrees = 5f;
        private const float TagGapDegrees = 0.5f;
        private const float ProvenanceGapDegrees = 0.4f;
        private const int Order = 12;

        private readonly List<(TextMeshPro Tag, TextMeshPro Text)> rows = new List<(TextMeshPro, TextMeshPro)>();
        private readonly HashSet<TMP_Text> leaning = new HashSet<TMP_Text>();
        private readonly Dictionary<(string Text, float Width, int Rows), int> measured = new Dictionary<(string, float, int), int>();
        private TextMeshPro provenance = null!;
        private TextMeshPro measure = null!;
        private Rect area;

        public static SectionView Create(Transform parent)
        {
            var go = new GameObject("Section");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<SectionView>();
            view.Build();
            go.SetActive(false);
            return view;
        }

        /// <summary>Every label it draws, so the editor can check that none interprets what it shows.</summary>
        public IEnumerable<TextMeshPro> Labels
        {
            get
            {
                yield return provenance;
                foreach (var (tag, text) in rows)
                {
                    yield return tag;
                    yield return text;
                }
            }
        }

        /// <summary>Where it draws, in the frame's units.</summary>
        public Rect Area => area;

        /// <summary>Whether a line leans, as a claim does.</summary>
        public bool Leans(TMP_Text label) => leaning.Contains(label);

        /// <summary>
        /// Draws <paramref name="section"/> in <paramref name="space"/>, the frame's units, its lines
        /// kept left of <paramref name="notch"/> where it reaches down into the space, as Refresh does;
        /// lines with no room left are not shown.
        /// </summary>
        public void Show(SectionPresentation section, Rect space, Rect? notch = null)
        {
            area = space;
            gameObject.SetActive(true);
            provenance.color = ColorOf(section.ProvenanceTone);
            float Right(float top, float height) =>
                notch is Rect beside && top - height < beside.yMax && top > beside.yMin ? beside.xMin - GlazeTokens.Units(ProvenanceGapDegrees) : space.xMax;
            var y = space.yMax;
            var provenanceHeight = GlazeText.LineHeight(provenance) * MaxProvenanceRows;
            y -= Place(provenance, section.Provenance, space.xMin, Right(y, provenanceHeight), y, MaxProvenanceRows) + GlazeTokens.Units(ProvenanceGapDegrees);

            var tagWidth = GlazeTokens.Units(TagDegrees);
            var left = space.xMin + tagWidth + GlazeTokens.Units(TagGapDegrees);
            for (var index = 0; index < rows.Count; index++)
            {
                var (tag, text) = rows[index];
                var lineHeight = GlazeText.LineHeight(text);
                var room = Mathf.FloorToInt((y - space.yMin) / lineHeight + 0.01f);
                var shown = index < section.Lines.Count && room >= 1;
                tag.gameObject.SetActive(shown);
                text.gameObject.SetActive(shown);
                if (!shown) continue;
                var line = section.Lines[index];
                GlazeText.SetLiteral(tag, line.Source ? "" : line.Tag);
                GlazeText.Lay(tag, tagWidth, 1);
                tag.transform.localPosition = new Vector3(space.xMin, y, -0.0005f);
                text.color = line.Detail && line.Tone == SectionTone.Secondary ? GlazeTokens.TextSecondary : ColorOf(line.Tone);
                Lean(text, line.Tone == SectionTone.Claim);
                // A part's availability, coverage and freshness, or an agent's reason, may take more
                // than one row, so none is cut off where it has room.
                var lines = Mathf.Min(line.Rows, room);
                var start = line.Source ? space.xMin : left;
                y -= Place(text, line.Text, start, Right(y, lines * lineHeight), y, lines);
            }
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// The rows a page holds in <paramref name="space"/> under a provenance line of
        /// <paramref name="sample"/>'s length, kept left of <paramref name="notch"/>, and how many rows
        /// each line takes there: as many as its words wrap to, never more than it may.
        /// </summary>
        public AnswerRoom Room(Rect space, Rect? notch, string sample)
        {
            var lineHeight = GlazeText.LineHeight(measure);
            var beside = notch is Rect rect && rect.yMin < space.yMax ? rect.xMin - GlazeTokens.Units(ProvenanceGapDegrees) : space.xMax;
            var provenanceRows = Rows(sample, beside - space.xMin, MaxProvenanceRows);
            var rows = Mathf.FloorToInt((space.height - provenanceRows * lineHeight - GlazeTokens.Units(ProvenanceGapDegrees)) / lineHeight + 0.01f);
            var lines = space.width - GlazeTokens.Units(TagDegrees) - GlazeTokens.Units(TagGapDegrees);
            return new AnswerRoom(Mathf.Clamp(rows, 1, MaxRows), line => Rows(line.Text, line.Source ? space.width : lines, line.Rows));
        }

        /// <summary>How many rows text wraps to at a width, at most <paramref name="maxRows"/>, measured once for each.</summary>
        private int Rows(string text, float width, int maxRows)
        {
            var key = (text, Mathf.Round(width * 10000f) / 10000f, maxRows);
            if (measured.TryGetValue(key, out var rows)) return rows;
            GlazeText.SetLiteral(measure, text);
            rows = Mathf.Max(1, GlazeText.Lay(measure, width, maxRows).Lines);
            // Answers change while a workspace is open; the words measured stay few.
            if (measured.Count > 512) measured.Clear();
            measured[key] = rows;
            return rows;
        }

        /// <summary>Shows text at a row, wrapping to at most <paramref name="maxRows"/> rows; returns how tall it is.</summary>
        private static float Place(TextMeshPro label, string text, float left, float right, float top, int maxRows)
        {
            GlazeText.SetLiteral(label, text);
            var (count, _) = GlazeText.Lay(label, right - left, Mathf.Max(1, maxRows));
            label.transform.localPosition = new Vector3(left, top, -0.0005f);
            return Mathf.Max(1, count) * GlazeText.LineHeight(label);
        }

        /// <summary>A claim leans; a label that changes from one to the other is drawn again.</summary>
        private void Lean(TextMeshPro label, bool claim)
        {
            if (claim == leaning.Contains(label)) return;
            if (claim) leaning.Add(label);
            else leaning.Remove(label);
            label.havePropertiesChanged = true;
        }

        private void Build()
        {
            provenance = Label("Provenance", GlazeTokens.TextSecondary);
            // Never shown: it lays a line out at a width to count its rows.
            measure = Label("Measure", GlazeTokens.Text);
            measure.gameObject.SetActive(false);
            for (var index = 0; index < MaxRows; index++)
            {
                var tag = Label("Class " + index, GlazeTokens.TextSecondary);
                var text = Label("Line " + index, GlazeTokens.Text);
                text.OnPreRenderText += info =>
                {
                    if (leaning.Contains(text)) GlazeText.Lean(info);
                };
                rows.Add((tag, text));
            }
        }

        private TextMeshPro Label(string name, Color color)
        {
            var label = GlazeText.Create(transform, name, GlazeType.Caption, color, TextAlignmentOptions.TopLeft, Order);
            label.rectTransform.pivot = new Vector2(0f, 1f);
            return label;
        }

        private static Color ColorOf(SectionTone tone) => tone switch
        {
            SectionTone.Secondary => GlazeTokens.TextSecondary,
            SectionTone.Attention => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground),
            SectionTone.Problem => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Failure).Foreground),
            SectionTone.Good => GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Success).Foreground),
            _ => GlazeTokens.Text,
        };
    }
}
