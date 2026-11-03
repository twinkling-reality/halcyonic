#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Halcyonic's " · " cadence as the eyes read it (WORDS rule 2; the XR baseline of 2026-11-03, defects 7, 8, 10, 12 and 24),
    /// in every state at rest: every label shown, its glyphs' ink read from TextMeshPro's own character data in its parent's
    /// frame, and the labels that share a parent and a baseline read as one line. No line starts or ends with the separator's
    /// dot; two labels side by side on a line never touch (half a space at least between their ink); and a dot stands at least
    /// half a space clear of its neighbours, no further from what precedes it than from what follows it, nor the other way
    /// round, beyond half a space, except where outside text follows it, whose ground takes one more space by design
    /// (<see cref="LineWords.Pad"/>).
    /// </summary>
    public static partial class KitRender
    {
        /// <summary>The separator's dot.</summary>
        private const char SeparatorDot = '·';

        /// <summary>One glyph as drawn: its ink's left and right and its baseline in its parent's frame, its label's em there, and whose words.</summary>
        private sealed class Inked
        {
            public TMP_Text Label = null!;
            public char Character;
            public float Left;
            public float Right;
            public float Baseline;
            public float Em;
            public bool Theirs;
        }

        /// <summary>The cadence held in every label shown under <paramref name="root"/> (see the class summary); one failure a fault.</summary>
        private static IEnumerable<string> SeparatorsHold(Transform root, string name)
        {
            var failures = new List<string>();
            foreach (var row in Rows(root))
            {
                for (var at = 0; at < row.Count; at++)
                {
                    var glyph = row[at];
                    var halfSpace = 0.125f * glyph.Em;
                    if (at > 0 && row[at - 1].Label != glyph.Label && glyph.Left - row[at - 1].Right < 0.125f * Mathf.Min(glyph.Em, row[at - 1].Em))
                        failures.Add(name + ": \"" + Clip(row[at - 1].Label.text) + "\" and \"" + Clip(glyph.Label.text) + "\" touch on one line ("
                            + Spaces(glyph.Left - row[at - 1].Right, glyph.Em) + " of a space between them)");
                    if (glyph.Character != SeparatorDot) continue;
                    if (at == 0) { failures.Add(name + ": a line starts with the separator: \"" + Clip(LineText(row)) + "\""); continue; }
                    if (at == row.Count - 1) { failures.Add(name + ": a line ends with the separator: \"" + Clip(LineText(row)) + "\""); continue; }
                    var before = glyph.Left - row[at - 1].Right;
                    var after = row[at + 1].Left - glyph.Right;
                    var line = "\"" + Clip(LineText(row)) + "\" (" + Spaces(before, glyph.Em) + " of a space before it, " + Spaces(after, glyph.Em) + " after)";
                    if (before < halfSpace || after < halfSpace) failures.Add(name + ": a separator touches its words in " + line);
                    else if (before > after + halfSpace) failures.Add(name + ": a separator stands apart from what it follows in " + line);
                    else if (after > before + halfSpace && !(row[at + 1].Theirs && row[at + 1].Label == glyph.Label && !glyph.Theirs))
                        failures.Add(name + ": a separator stands apart from what follows it in " + line);
                }
            }
            return failures;
        }

        /// <summary>A gap in spaces of an em, for a failure's words.</summary>
        private static string Spaces(float gap, float em) => (gap / (0.25f * em)).ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>A line's glyphs as one text, a space where a gap of a third of an em or more stands, for a failure's words.</summary>
        private static string LineText(IReadOnlyList<Inked> row)
        {
            var text = new StringBuilder();
            for (var at = 0; at < row.Count; at++)
            {
                if (at > 0 && row[at].Left - row[at - 1].Right > row[at].Em / 3f) text.Append(' ');
                text.Append(row[at].Character);
            }
            return text.ToString();
        }

        /// <summary>
        /// The lines the eyes read under <paramref name="root"/>: every visible glyph of every label shown (icons aside), grouped by
        /// its label's parent and by baseline within a third of an em, each line in order from left to right.
        /// </summary>
        private static List<List<Inked>> Rows(Transform root)
        {
            var byParent = new Dictionary<Transform, List<Inked>>();
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                var parent = label.transform.parent;
                if (parent == null || GlazeIcons.IsIcon(label) || !label.enabled || label.color.a < 0.05f || GlazeText.ShownOf(label) < 0.05f) continue;
                if (label.TryGetComponent<Renderer>(out var drawn) && !drawn.enabled) continue;
                label.ForceMeshUpdate(true);
                var info = label.textInfo;
                Vector3 InParent(float x, float y) => parent.InverseTransformPoint(label.transform.TransformPoint(new Vector3(x, y, 0f)));
                var em = (InParent(label.fontSize * 0.1f, 0f) - InParent(0f, 0f)).magnitude;
                var theirs = label.TryGetComponent<GlazeRuns>(out var runs) ? runs.Theirs : new List<(int Start, int End)>();
                if (!byParent.TryGetValue(parent, out var glyphs)) byParent[parent] = glyphs = new List<Inked>();
                for (var index = 0; index < info.characterCount; index++)
                {
                    var character = info.characterInfo[index];
                    if (!character.isVisible || character.textElement == null) continue;
                    var metrics = character.textElement.glyph.metrics;
                    var left = InParent(character.origin + metrics.horizontalBearingX * character.scale, character.baseLine);
                    var right = InParent(character.origin + (metrics.horizontalBearingX + metrics.width) * character.scale, character.baseLine);
                    glyphs.Add(new Inked
                    {
                        Label = label, Character = character.character, Left = Mathf.Min(left.x, right.x),
                        Right = Mathf.Max(left.x, right.x), Baseline = left.y, Em = em, Theirs = GlazeText.InRun(info, index, theirs),
                    });
                }
            }
            var rows = new List<List<Inked>>();
            foreach (var glyphs in byParent.Values)
            {
                var row = new List<Inked>();
                foreach (var glyph in glyphs.OrderByDescending(glyph => glyph.Baseline))
                {
                    if (row.Count > 0 && row[0].Baseline - glyph.Baseline > Mathf.Max(row[0].Em, glyph.Em) / 3f)
                    {
                        rows.Add(row.OrderBy(each => each.Left).ToList());
                        row = new List<Inked>();
                    }
                    row.Add(glyph);
                }
                if (row.Count > 0) rows.Add(row.OrderBy(each => each.Left).ToList());
            }
            return rows;
        }

        /// <summary>
        /// The words under <paramref name="part"/> as the eyes read them: every label shown (icons aside), line by line from the
        /// top and from the left, its own text whole (a line it wraps is read as the space it broke at), the labels on one line
        /// joined as laid, and a line break between two labels read as the separator, which the cadence drops there (KitFlow).
        /// </summary>
        private static string ReadAsShown(Transform part)
        {
            var labels = part.GetComponentsInChildren<TMP_Text>(false).Where(label => !GlazeIcons.IsIcon(label) && !string.IsNullOrEmpty(label.text)).ToList();
            float Top(TMP_Text label) => part.InverseTransformPoint(label.transform.position).y;
            float Left(TMP_Text label) => part.InverseTransformPoint(label.transform.position).x;
            var read = new StringBuilder();
            TMP_Text? previous = null;
            foreach (var label in labels.OrderByDescending(Top).ThenBy(Left))
            {
                if (previous != null && Mathf.Abs(Top(previous) - Top(label)) > 1e-4f) read.Append(KitFlow.Separator);
                read.Append(label.text);
                previous = label;
            }
            return read.ToString();
        }
    }
}
