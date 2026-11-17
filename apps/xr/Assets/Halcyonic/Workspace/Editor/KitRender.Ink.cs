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
    /// only light strokes, stricter, never weaker). Along the middle of each of a label's lines, the words' strokes and their soft
    /// edges may cover more than half the samples (an e's bar, a heavy weight at a large size), so the ground is read from the far
    /// side of the words: the 15th percentile of the samples' luminance under light words, the 85th under dark ones, whichever the
    /// label's own colour is farther from. Whatever stands under the words there is their ground: the glass, a dark underlay laid under
    /// them (a band's subtitle ground), or the room through a translucent plate, never the plate the eye might expect. Each line is held to the polarity its label is drawn in (AUDIT3's
    /// 01:17Z I1): words drawn lighter than their ground are read by their lightest sample, words drawn darker by their darkest, so
    /// a glyph's dark edge, a rim or a dark spot of the room behind the glass never stands in for light words. The ink must hold the
    /// contrast the judges' walk holds (4.5 to 1, WCAG relative luminance, as KitRender.Luminance reads it), on the side it is drawn,
    /// and light words besides keep their old floor, so a light word must still be as bright as before.
    /// </summary>
    public static partial class KitRender
    {
        /// <summary>The contrast a label's words must hold against their own ground, in either polarity: the judges' walk's rule.</summary>
        public const double InkRatioFloor = 4.5;

        /// <summary>The share of a line's samples taken as surely ground, from the far side of the words: a line's strokes never cover more.</summary>
        private const double GroundShare = 0.15;

        /// <summary>The most a line's middle is sampled across its width.</summary>
        private const int InkSteps = 80;

        /// <summary>One line of a label's words as the pixels hold them.</summary>
        public readonly struct InkRead
        {
            public InkRead(int line, bool drawnLight, Color32 ink, Color32 ground)
            {
                Line = line;
                DrawnLight = drawnLight;
                Ink = ink;
                Ground = ground;
            }

            public int Line { get; }
            /// <summary>The label's own colour is lighter than the ground read under it: light words, read by their lightest sample.</summary>
            public bool DrawnLight { get; }
            public Color32 Ink { get; }
            public Color32 Ground { get; }
            /// <summary>The ink read on the side it is drawn: lighter than the ground for light words, darker for dark ones.</summary>
            public bool OnItsSide => DrawnLight ? Luminance(Ink) > Luminance(Ground) : Luminance(Ink) < Luminance(Ground);
            public double Ratio => ContrastRatio(Ink, Ground);
            /// <summary>The ink's luminance on the old 0 to 255 scale (Rec. 709 weights on the 8-bit values), as the light-stroke checks read it.</summary>
            public int Lightness => (Ink.r * 2126 + Ink.g * 7152 + Ink.b * 722) / 10000;

            public override string ToString() => "line " + (Line + 1) + ": drawn " + (DrawnLight ? "light" : "dark") + ", ink " + Hex(Ink) + " on " + Hex(Ground) + ", "
                + Ratio.ToString("0.0", CultureInfo.InvariantCulture) + " to 1";
        }

        /// <summary>
        /// One line read from <paramref name="samples"/> along its middle, its words drawn in <paramref name="drawn"/>: the ground the
        /// samples' 15th or 85th percentile, whichever <paramref name="drawn"/> is farther from, the ink the sample farthest from it
        /// on the side the words are drawn.
        /// </summary>
        public static InkRead ReadInkLine(int line, IReadOnlyList<Color32> samples, Color32 drawn)
        {
            if (samples.Count == 0) throw new ArgumentException("A line is read from at least one sample.", nameof(samples));
            var ordered = samples.OrderBy(Luminance).ToList();
            var low = ordered[(int)(GroundShare * (ordered.Count - 1))];
            var high = ordered[(int)((1.0 - GroundShare) * (ordered.Count - 1))];
            var drawnLight = Math.Abs(Luminance(drawn) - Luminance(high)) < Math.Abs(Luminance(drawn) - Luminance(low));
            var ground = drawnLight ? low : high;
            var ink = drawnLight ? ordered[ordered.Count - 1] : ordered[0];
            return new InkRead(line, drawnLight, ink, ground);
        }

        /// <summary>Each line of <paramref name="label"/> as <paramref name="at"/> reads the render at a world point.</summary>
        public static IReadOnlyList<InkRead> InkOf(TMPro.TMP_Text label, Func<Vector3, Color32> at)
        {
            label.ForceMeshUpdate(true, true);
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
                read.Add(ReadInkLine(line, samples, DrawnColour(label)));
            }
            return read;
        }

        /// <summary>
        /// What is wrong with one line read, or null: the ink on the side its words are drawn, holding <see cref="InkRatioFloor"/>
        /// against its ground, and light words besides at least <paramref name="lightFloor"/> of 255, the floor the light-stroke
        /// check held them to.
        /// </summary>
        public static string? InkFault(InkRead line, int lightFloor)
        {
            if (!line.OnItsSide) return "no words in the pixels, " + line + ", read " + (line.DrawnLight ? "dark" : "light") + " where drawn " + (line.DrawnLight ? "light" : "dark");
            if (line.Ratio < InkRatioFloor) return "no words in the pixels, " + line;
            if (line.DrawnLight && line.Lightness < lightFloor)
                return "no words in the pixels, " + line + ", the light ink reads " + line.Lightness.ToString(CultureInfo.InvariantCulture) + " of 255 under " + lightFloor;
            return null;
        }

        /// <summary>What is wrong with <paramref name="label"/>'s words in the pixels, line by line, or null.</summary>
        public static string? InkFault(TMPro.TMP_Text label, Func<Vector3, Color32> at, int lightFloor) =>
            InkOf(label, at).Select(line => InkFault(line, lightFloor)).FirstOrDefault(fault => fault != null);

        /// <summary>
        /// The reader held to cases whose answer is known, before any render is read by it (AUDIT3's 01:17Z I1 fail-first): light words
        /// no brighter than 130 on a grey ground of 120 with one black sample fail (4.8 to 1 against the black), which a reader taking the farthest sample either way
        /// passed; white on dark glass passes, as do words whose strokes cover most of their line's middle; near-black on the lifted orange passes; white on the orange fails; dark words read light fail.
        /// </summary>
        private static IEnumerable<string> InkReaderHolds()
        {
            Color32 Grey(byte value) => new Color32(value, value, value, 255);
            List<Color32> Line(Color32 ground, Color32 ink, int inked, Color32? odd = null)
            {
                var samples = Enumerable.Repeat(ground, InkSteps + 1 - inked).Concat(Enumerable.Repeat(ink, inked)).ToList();
                if (odd is Color32 one) samples[0] = one;
                return samples;
            }
            var white = Grey(255);
            var orange = new Color32(0xF3, 0x95, 0x52, 255);
            var ink = new Color32(0x07, 0x09, 0x0B, 255);
            var cases = new (string What, List<Color32> Samples, Color32 Drawn, int Floor, bool Passes)[]
            {
                ("white words reading 130 on a 120 ground, one black sample", Line(Grey(120), Grey(130), 20, Grey(0)), white, 150, false),
                ("white words reading 230 on a 40 ground", Line(Grey(40), Grey(230), 20), white, 150, true),
                ("white words covering 60 percent of their line's middle on a 20 ground", Line(Grey(20), white, 49), white, 170, true),
                ("near-black words covering 60 percent of their line's middle on the lifted orange", Line(orange, ink, 49), ink, 150, true),
                ("near-black words on the lifted orange", Line(orange, ink, 20), ink, 150, true),
                ("white words on the lifted orange", Line(orange, white, 20), white, 150, false),
                ("dark words that read only lighter than their ground", Line(Grey(200), white, 20), ink, 150, false),
                ("white words reading 160 on a 20 ground, under the action's 170", Line(Grey(20), Grey(160), 20), white, 170, false),
            };
            foreach (var (what, samples, drawn, floor, passes) in cases)
            {
                var fault = InkFault(ReadInkLine(0, samples, drawn), floor);
                if ((fault == null) != passes) yield return "the words reader: " + what + (passes ? " should pass, and fails: " + fault : " should fail, and passes");
            }
        }

        /// <summary>
        /// A word's contrast in a rendered frame by its own box, as the eye and AUDIT read it (the lead's 2026-11-17T02:53Z):
        /// <paramref name="rect"/> is the word's box in the frame's pixels. Held to the polarity its label is drawn in: light words
        /// are read by their glyph cores (the pixels within 3 of the box's brightest max(R, G, B), averaged), dark words by the
        /// cores within 3 of its darkest min(R, G, B), each against the box's most common colour: the glass the
        /// words stand on (a word's box hugs its letters, so a percentile of it lands in the strokes' soft edges, never the ground).
        /// Neither a dark spot of the room seen through the glass, nor a glyph's dark edge, nor a single bright pixel can lift the
        /// ratio: the ink is a mean of cores on the words' own side, so the ratio is never above the per-pixel read it replaces.
        /// Returns the ratio, or 1 where the cores do not stand on their drawn side of the ground, or where no pixel of the box lies
        /// inside the words' own glyph quads (<paramref name="glyphs"/>, in the frame's pixels).
        /// </summary>
        public static (double Ratio, bool DrawnLight, double Ink, double Ground) InkBox(Color32[] pixels, RectInt rect, Color drawn, IReadOnlyList<RectInt>? glyphs = null)
        {
            // The ink is read only inside the words' own glyph quads, where given (the lead's 2026-11-17T15:11Z): a bright pixel of the
            // room seen through the glass beside the letters is never taken for the words' ink. The ground stays the whole box's most
            // common colour, the glass they stand on.
            bool InGlyph(int x, int y) => glyphs == null || glyphs.Any(quad => quad.Contains(new Vector2Int(x, y)));
            var box = new List<Color32>(rect.width * rect.height);
            for (var y = rect.yMin; y < rect.yMax; y++)
            for (var x = rect.xMin; x < rect.xMax; x++)
                if (InGlyph(x, y)) box.Add(pixels[y * Width + x]);
            if (box.Count == 0) return (1.0, true, 0.0, 0.0);
            var mode = Luminance(MostCommon(pixels, rect));
            var drawnLight = Luminance((Color32)drawn) > mode;
            double ink;
            double ground;
            if (drawnLight)
            {
                var byLight = box.OrderBy(p => Math.Max(p.r, Math.Max(p.g, p.b))).ToList();
                var top = MaxChannel(byLight[byLight.Count - 1]);
                ink = byLight.Where(p => MaxChannel(p) >= top - 3).Average(Luminance);
                ground = mode;
                return (ink > ground ? (ink + 0.05) / (ground + 0.05) : 1.0, true, ink, ground);
            }
            var byDark = box.OrderBy(p => Math.Min(p.r, Math.Min(p.g, p.b))).ToList();
            var bottom = MinChannel(byDark[0]);
            ink = byDark.Where(p => MinChannel(p) <= bottom + 3).Average(Luminance);
            ground = mode;
            return (ink < ground ? (ground + 0.05) / (ink + 0.05) : 1.0, false, ink, ground);
        }

        private static int MaxChannel(Color32 p) => Math.Max(p.r, Math.Max(p.g, p.b));

        private static int MinChannel(Color32 p) => Math.Min(p.r, Math.Min(p.g, p.b));

        /// <summary>
        /// The colour <paramref name="label"/>'s words are drawn in, for their polarity: the mean of its visible characters' own vertex
        /// colours (the label's colour with any run's colour on it, as TMP lays them), times its renderer's face colour where a block
        /// sets one. The label's colour property alone misses a run coloured by its words, so it could judge words the wrong way round.
        /// </summary>
        public static Color DrawnColour(TMPro.TMP_Text label)
        {
            var info = label.textInfo;
            double r = 0, g = 0, b = 0;
            var count = 0;
            if (info != null)
                for (var index = 0; index < info.characterCount; index++)
                {
                    var character = info.characterInfo[index];
                    if (!character.isVisible) continue;
                    r += character.color.r; g += character.color.g; b += character.color.b;
                    count++;
                }
            var drawn = count > 0 ? new Color((float)(r / count / 255.0), (float)(g / count / 255.0), (float)(b / count / 255.0), 1f) : label.color;
            if (label.TryGetComponent<Renderer>(out var renderer) && renderer.HasPropertyBlock())
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                if (block.HasColor("_FaceColor"))
                {
                    var face = block.GetColor("_FaceColor");
                    drawn = new Color(drawn.r * face.r, drawn.g * face.g, drawn.b * face.b, 1f);
                }
            }
            return drawn;
        }

        /// <summary>WCAG contrast ratio of two sRGB pixels, 1 to 21.</summary>
        public static double ContrastRatio(Color32 a, Color32 b) => GlazeColor.Contrast(new GlazeColor(a.r, a.g, a.b), new GlazeColor(b.r, b.g, b.b));

        private static string Hex(Color32 pixel) => "#" + pixel.r.ToString("X2", CultureInfo.InvariantCulture) + pixel.g.ToString("X2", CultureInfo.InvariantCulture)
            + pixel.b.ToString("X2", CultureInfo.InvariantCulture);
    }
}
