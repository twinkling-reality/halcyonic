#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Halcyonic's " · " cadence (WORDS rule 2) laid as a flow of labels: each segment and each dot its own label, set by its
    /// runs so a space at its edge stays, and laid by its pen's advance, not by its ink (TextMeshPro's text bounds end at the
    /// last glyph, which put a dot under the next segment and "Builds· 6 min" in the XR baseline, 2026-11-03). A line breaks
    /// only between segments, and the dot at a break is dropped, so no line starts or ends with a separator.
    /// </summary>
    public static class KitFlow
    {
        /// <summary>Halcyonic's separator between two segments, its spaces included.</summary>
        public const string Separator = " · ";

        /// <summary>Wide enough to lay any one segment on one line, to measure it before it is placed.</summary>
        private const float Measuring = 1000f;

        /// <summary>
        /// How far a laid label's pen moved: from its box's left to after its last character, a space at either end included,
        /// in the label's units. Zero for a label with nothing laid.
        /// </summary>
        public static float Advance(TMP_Text label)
        {
            var info = label.textInfo;
            if (info.characterCount == 0) return 0f;
            return info.characterInfo[info.characterCount - 1].xAdvance - label.rectTransform.rect.xMin;
        }

        /// <summary>
        /// <paramref name="words"/> split at Halcyonic's own separators into the segments a flow lays, in order: a separator
        /// inside outside text is theirs and stays where it stands, and a segment keeps its runs.
        /// </summary>
        public static IReadOnlyList<LineWords> Segments(LineWords words)
        {
            var segments = new List<LineWords>();
            var current = LineWords.None;
            foreach (var run in words.Runs)
            {
                if (run.Theirs)
                {
                    current += LineWords.Theirs(run.Text);
                    continue;
                }
                var parts = run.Text.Split(new[] { Separator }, StringSplitOptions.None);
                for (var part = 0; part < parts.Length; part++)
                {
                    if (part > 0)
                    {
                        if (!current.IsEmpty) segments.Add(current);
                        current = LineWords.None;
                    }
                    if (parts[part].Length > 0) current += LineWords.Ours(parts[part]);
                }
            }
            if (!current.IsEmpty) segments.Add(current);
            return segments;
        }

        /// <summary>
        /// Lays <paramref name="segments"/> as one flow from <paramref name="left"/> and <paramref name="top"/> up to
        /// <paramref name="right"/>, in the labels <paramref name="segmentLabel"/> and <paramref name="dotLabel"/> give for each
        /// segment's index (styled by the caller; shown and placed here, <paramref name="z"/> in front of their parent): a dot
        /// between two segments that share a line, and a segment that does not fit after its dot starts the next line without
        /// it. A segment wider than a whole line wraps inside itself, to at most <paramref name="segmentLines"/> lines, and the
        /// flow goes on below it. Returns the height laid, from the first line's top to the pitch below the last, and the
        /// indices of the segments that did not fit whole.
        /// </summary>
        public static (float Height, IReadOnlyList<int> Cut) Lay(IReadOnlyList<LineWords> segments, Func<int, TMP_Text> segmentLabel,
            Func<int, TMP_Text> dotLabel, float left, float top, float right, float z, int segmentLines = 1)
        {
            var cut = new List<int>();
            var x = left;
            var lineTop = top;
            var lineHeight = 0f;
            for (var index = 0; index < segments.Count; index++)
            {
                var label = segmentLabel(index);
                label.gameObject.SetActive(true);
                GlazeRuns.Of(label).Set(segments[index]);
                KitType.Lay(label, Measuring, 1);
                var wide = Advance(label);
                if (x > left)
                {
                    var dot = dotLabel(index);
                    GlazeRuns.Of(dot).Set(LineWords.Ours(Separator));
                    KitType.Lay(dot, Measuring, 1);
                    var dotWide = Advance(dot);
                    if (x + dotWide + wide > right + 1e-5f)
                    {
                        // The next line, without the dot: a separator never ends a line, nor starts one.
                        dot.gameObject.SetActive(false);
                        x = left;
                        lineTop -= lineHeight;
                        lineHeight = 0f;
                    }
                    else
                    {
                        dot.gameObject.SetActive(true);
                        var (_, _, dotHeight) = KitType.Lay(dot, right - x, 1);
                        dot.transform.localPosition = new Vector3(x, lineTop, z);
                        x += dotWide;
                        lineHeight = Mathf.Max(lineHeight, dotHeight);
                    }
                }
                // A segment wraps inside itself only where it starts a line; after a dot it fits whole, or it would have broken.
                var (laid, _, height) = KitType.Lay(label, right - x, x > left ? 1 : segmentLines);
                if (label.isTextTruncated) cut.Add(index);
                label.transform.localPosition = new Vector3(x, lineTop, z);
                if (laid > 1)
                {
                    lineTop -= height;
                    lineHeight = 0f;
                    x = left;
                }
                else
                {
                    x += Advance(label);
                    lineHeight = Mathf.Max(lineHeight, height);
                }
            }
            return (top - lineTop + lineHeight, cut);
        }
    }
}
