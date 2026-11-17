#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Words read in a render's pixels on any plate (the lead's 2026-11-17T01:05Z: the words check reads words on any plate, never
    /// only light strokes, stricter, never weaker). Along the middle of each of a label's lines, its ground is the median of the
    /// samples (a line's middle crosses more gap than stroke) and its ink the sample farthest from it in luminance: light words on
    /// dark glass and dark words on a light plate are read alike, by the contrast ratio the judges' walk holds (4.5 to 1, WCAG
    /// relative luminance, as KitRender.Luminance reads it). Light words keep their old floor besides, so a light word must still be as bright as before.
    /// </summary>
    public static partial class KitRender
    {
        /// <summary>The contrast a label's words must hold against their own ground, in any polarity: the judges' walk's rule.</summary>
        public const double InkRatioFloor = 4.5;

        /// <summary>The most a line's middle is sampled across its width.</summary>
        private const int InkSteps = 80;

        /// <summary>One line of a label's words as the pixels hold them.</summary>
        public readonly struct InkRead
        {
            public InkRead(int line, Color32 ink, Color32 ground)
            {
                Line = line;
                Ink = ink;
                Ground = ground;
            }

            public int Line { get; }
            public Color32 Ink { get; }
            public Color32 Ground { get; }
            public bool Light => Luminance(Ink) >= Luminance(Ground);
            public double Ratio => ContrastRatio(Ink, Ground);
            /// <summary>The ink's luminance on the old 0 to 255 scale (Rec. 709 weights on the 8-bit values), as the light-stroke checks read it.</summary>
            public int Lightness => (Ink.r * 2126 + Ink.g * 7152 + Ink.b * 722) / 10000;

            public override string ToString() => "line " + (Line + 1) + ": " + (Light ? "light" : "dark") + " ink " + Hex(Ink) + " on " + Hex(Ground) + ", "
                + Ratio.ToString("0.0", CultureInfo.InvariantCulture) + " to 1";
        }

        /// <summary>Each line of <paramref name="label"/> as <paramref name="at"/> reads the render at a world point.</summary>
        public static IReadOnlyList<InkRead> InkOf(TMPro.TMP_Text label, Func<Vector3, Color32> at)
        {
            label.ForceMeshUpdate(true);
            var info = label.textInfo;
            var lines = Mathf.Max(1, info.lineCount);
            var read = new List<InkRead>();
            for (var line = 0; line < lines; line++)
            {
                var drawn = info.lineCount > 0 ? info.lineInfo[line] : default;
                var from = info.lineCount > 0 ? drawn.lineExtents.min.x : label.textBounds.min.x;
                var to = info.lineCount > 0 ? drawn.lineExtents.max.x : label.textBounds.max.x;
                var y = info.lineCount > 0 ? (drawn.baseline + drawn.ascender) / 2f : label.textBounds.center.y;
                var samples = new List<Color32>();
                for (var step = 0; step <= InkSteps; step++)
                    samples.Add(at(label.transform.TransformPoint(new Vector3(Mathf.Lerp(from, to, step / (float)InkSteps), y, 0f))));
                var ordered = samples.OrderBy(Luminance).ToList();
                var ground = ordered[ordered.Count / 2];
                var groundLight = Luminance(ground);
                var ink = samples.OrderByDescending(sample => Math.Abs(Luminance(sample) - groundLight)).First();
                read.Add(new InkRead(line, ink, ground));
            }
            return read;
        }

        /// <summary>
        /// What is wrong with <paramref name="label"/>'s words in the pixels, or null: every line holds <see cref="InkRatioFloor"/>
        /// against its ground, and light words besides read at least <paramref name="lightFloor"/> of 255, the floor the light-stroke
        /// check held them to.
        /// </summary>
        public static string? InkFault(TMPro.TMP_Text label, Func<Vector3, Color32> at, int lightFloor)
        {
            foreach (var line in InkOf(label, at))
            {
                if (line.Ratio < InkRatioFloor) return "no words in the pixels, " + line;
                if (line.Light && line.Lightness < lightFloor)
                    return "no words in the pixels, " + line + ", the light ink reads " + line.Lightness.ToString(CultureInfo.InvariantCulture) + " of 255 under " + lightFloor;
            }
            return null;
        }

        /// <summary>WCAG contrast ratio of two sRGB pixels, 1 to 21.</summary>
        public static double ContrastRatio(Color32 a, Color32 b) => GlazeColor.Contrast(new GlazeColor(a.r, a.g, a.b), new GlazeColor(b.r, b.g, b.b));

        private static string Hex(Color32 pixel) => "#" + pixel.r.ToString("X2", CultureInfo.InvariantCulture) + pixel.g.ToString("X2", CultureInfo.InvariantCulture)
            + pixel.b.ToString("X2", CultureInfo.InvariantCulture);
    }
}
