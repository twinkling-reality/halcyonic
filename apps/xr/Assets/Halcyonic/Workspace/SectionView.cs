#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// Draws a section, Understanding or Evaluation, in the workspace's details area: the provenance
    /// line, which may wrap, then each claim with its epistemic class beside it, or each measurement
    /// with its part. It derives nothing; the client core's presenters write every word.
    ///
    /// Everything it shows can come from a source, which may quote an agent, so it is untrusted. Every
    /// label shows its text through <see cref="WorkspaceVisuals.SetLiteral"/>, the one rule for text
    /// Halcyonic did not write: no markup, every backslash doubled for escape parsing, since
    /// TextMeshPro turns backslash sequences into other characters even with rich text off, and what
    /// would not show as itself shown as its code point. Each label shows exactly what was written.
    /// WorkspaceRender checks it on real labels in the editor.
    /// </summary>
    public sealed class SectionView : MonoBehaviour
    {
        /// <summary>The distance between lines, so a provenance line and seven more fit the details area.</summary>
        public const float Pitch = 0.025f;

        private const int MaxRows = 8;
        private const int MaxProvenanceRows = 4;
        private const float TagWidth = 0.085f;
        private const float TagGap = 0.01f;

        /// <summary>For what passed or holds; a lighter green that keeps its distance from the attention amber.</summary>
        private static readonly Color GoodColor = new Color(0.56f, 0.86f, 0.64f);

        private readonly List<(TextMeshPro Tag, TextMeshPro Text)> rows = new List<(TextMeshPro, TextMeshPro)>();
        private TextMeshPro provenance = null!;

        public static SectionView Create(Transform panel)
        {
            var go = new GameObject("Section");
            go.transform.SetParent(panel, false);
            var view = go.AddComponent<SectionView>();
            view.Build();
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

        public void Show(SectionPresentation section)
        {
            provenance.color = ColorOf(section.ProvenanceTone);
            provenance.fontStyle = FontStyles.Normal;
            // The provenance wraps when it has to, as a reason for no answer often does.
            var y = WorkspacePanel.DetailsTop;
            y -= Place(provenance, section.Provenance, WorkspacePanel.DetailsLeft, y, MaxProvenanceRows) * Pitch;

            var left = WorkspacePanel.DetailsLeft + TagWidth + TagGap;
            for (var index = 0; index < rows.Count; index++)
            {
                var (tag, text) = rows[index];
                var room = Mathf.FloorToInt((y - WorkspacePanel.DetailsBottom) / Pitch + 0.01f);
                var shown = index < section.Lines.Count && room >= 1;
                tag.gameObject.SetActive(shown);
                text.gameObject.SetActive(shown);
                if (!shown) continue;
                var line = section.Lines[index];
                WorkspaceVisuals.SetLiteral(tag, line.Tag);
                tag.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, y, -0.001f);
                text.fontSize = line.Detail ? WorkspaceVisuals.CaptionSize : WorkspaceVisuals.DetailSize;
                text.color = line.Detail && line.Tone == SectionTone.Secondary ? WorkspaceVisuals.SecondaryColor : ColorOf(line.Tone);
                // A claim reads apart by its class and color, not by italics: an italic line cut short
                // lost its ellipsis in the editor, and a quote cut short must say so.
                text.fontStyle = FontStyles.Normal;
                // A part's availability, coverage and freshness may take two rows, so none is cut off.
                y -= Place(text, line.Text, left, y, line.Detail ? Mathf.Min(2, room) : 1) * Pitch;
            }
        }

        /// <summary>Shows text at a row, wrapping to at most <paramref name="maxRows"/> rows; returns how many it takes.</summary>
        private static int Place(TextMeshPro label, string text, float left, float top, int maxRows)
        {
            var width = WorkspacePanel.DetailsLeft + WorkspacePanel.DetailsWidth - left;
            WorkspaceVisuals.SetLiteral(label, text);
            label.textWrappingMode = maxRows > 1 ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.rectTransform.localPosition = new Vector3(left, top, -0.001f);
            label.rectTransform.sizeDelta = new Vector2(width, maxRows * Pitch);
            if (maxRows <= 1) return 1;
            label.ForceMeshUpdate();
            var rows = Mathf.Clamp(label.textInfo.lineCount, 1, maxRows);
            label.rectTransform.sizeDelta = new Vector2(width, rows * Pitch);
            return rows;
        }

        private void Build()
        {
            provenance = WorkspaceVisuals.Text(transform, "Provenance", WorkspaceVisuals.DetailSize, WorkspaceVisuals.SecondaryColor,
                new Vector2(WorkspacePanel.DetailsWidth, Pitch), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            provenance.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, WorkspacePanel.DetailsTop, -0.001f);
            for (var index = 0; index < MaxRows; index++)
            {
                var tag = WorkspaceVisuals.Text(transform, "Class " + index, WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor,
                    new Vector2(TagWidth, Pitch), TextAlignmentOptions.TopLeft, order: WorkspaceVisuals.PanelTextOrder);
                var text = WorkspaceVisuals.Text(transform, "Line " + index, WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor,
                    new Vector2(WorkspacePanel.DetailsWidth - TagWidth - TagGap, Pitch), TextAlignmentOptions.TopLeft, order: WorkspaceVisuals.PanelTextOrder);
                rows.Add((tag, text));
            }
        }

        private static Color ColorOf(SectionTone tone) => tone switch
        {
            SectionTone.Secondary => WorkspaceVisuals.SecondaryColor,
            SectionTone.Claim => WorkspaceVisuals.ClaimColor,
            SectionTone.Attention => WorkspaceVisuals.AttentionColor,
            SectionTone.Problem => WorkspaceVisuals.ProblemColor,
            SectionTone.Good => GoodColor,
            _ => WorkspaceVisuals.TextColor,
        };
    }
}
