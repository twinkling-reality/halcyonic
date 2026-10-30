#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// The room's one panel material, the art direction's quiet glass (handoff 2026-10-30, section 1): an opaque face lit
    /// from above that carries every word, a see-through rim round it that carries none, a light along the outer edge, a
    /// faint glow, two soft shadows, and on a moment its state edge and the bloom of its arrival.
    /// </summary>
    /// <remarks>
    /// The prototype composites in sRGB, as a browser does; Unity blends alpha in linear light, where the same alpha comes
    /// out lighter. Layers over the opaque face are mixed in sRGB inside the shader, so they match exactly. Layers over the
    /// room cannot match every room: each is solved for one reference room (<see cref="Solve"/>), the rim for the lit room
    /// and the edge light for the dark room (handoff 7.3), and the render check reports how far the others drift. Every
    /// value here is in dp at the eye (16 to the degree) or sRGB, and the shader repeats these formulas.
    /// </remarks>
    public static class KitGlassMaterial
    {
        /// <summary>The art direction's three grounds: a dark room, a lit room and a white wall.</summary>
        public static GlazeColor DarkRoom => GlazeColor.Hex(0x1E1C1A);

        public static GlazeColor LitRoom => GlazeColor.Hex(0xB9AE9E);

        public static GlazeColor WhiteWall => GlazeColor.Hex(0xEEEEEE);

        /// <summary>The see-through rim round a panel (the screen, a moment, the reader, the bar): no words stand on it.</summary>
        public const float RimDp = 10f;

        /// <summary>The rim round a tile and the character's tag.</summary>
        public const float SmallRimDp = 6f;

        /// <summary>The rim round the lost plate and the earlier moments' edge.</summary>
        public const float PlateRimDp = 8f;

        /// <summary>The light along the outer edge, over the rim.</summary>
        public const float EdgeDp = 2f;

        /// <summary>The glow round the edge: a Gaussian of this blur, n7 at <see cref="GlowAlpha"/>. The first thing dropped for frame time.</summary>
        public const float GlowBlurDp = 18f;

        public const double GlowAlpha = 0.10;

        /// <summary>The light from above just inside the face's top edge: white at <see cref="HighlightAlpha"/>.</summary>
        public const float HighlightDp = 1f;

        public const double HighlightAlpha = 0.06;

        /// <summary>The far shadow: this far down, this blur, black at this alpha.</summary>
        public const float ShadowDropDp = 12f, ShadowBlurDp = 34f;

        public const double ShadowAlpha = 0.34;

        /// <summary>The near shadow, tight under the panel.</summary>
        public const float ContactDropDp = 2f, ContactBlurDp = 6f;

        public const double ContactAlpha = 0.22;

        /// <summary>
        /// A moment's bloom at its peak: its tone over the face's top, fading out by <see cref="BloomReach"/> of the face's height.
        /// The art direction's 14 percent takes tertiary words under 4.5:1 on the face's lit top (needs 4.26, passed 4.31), so
        /// the kit holds it at 11 percent, where every word keeps its floor (DESIGN's measurement, 2026-10-30).
        /// </summary>
        public const double BloomPeak = 0.11;

        public const double BloomReach = 0.70;

        /// <summary>The bloom's alpha at <paramref name="t"/> of the face's height from its top, at its peak (<paramref name="strength"/> 1).</summary>
        public static double BloomAt(double t, double strength = 1) => BloomPeak * strength * Math.Max(0, 1 - t / BloomReach);

        /// <summary>How far past the panel's edge the quad reaches: three blurs of the far shadow, so less than 0.2 percent of it is cut.</summary>
        public static float ReachDp => 3f * ShadowBlurDp / 2f;

        /// <summary>How far past a panel's edges, in dp, its quad reaches at each side, at the top and at the bottom: the far shadow reaches further down than up.</summary>
        public static (float Side, float Top, float Bottom) Margins => (ReachDp, ReachDp - ShadowDropDp, ReachDp + ShadowDropDp);

        /// <summary>The quad's area over the panel's: what the shadow and the glow cost in fill beyond the panel itself.</summary>
        public static double Overdraw(float width, float height)
        {
            var (side, top, bottom) = Margins;
            return (width + 2.0 * side) * (height + top + bottom) / (width * (double)height);
        }

        /// <summary>One stop of a vertical gradient: where it stands from the top (0) to the bottom (1), its sRGB colour and alpha.</summary>
        public readonly struct Stop
        {
            public Stop(double at, GlazeColor colour, double alpha)
            {
                At = at;
                Colour = new[] { colour.R / 255.0, colour.G / 255.0, colour.B / 255.0 };
                Alpha = alpha;
            }

            private Stop(double at, double[] colour, double alpha)
            {
                At = at;
                Colour = colour;
                Alpha = alpha;
            }

            public double At { get; }

            /// <summary>sRGB, 0 to 1 a channel.</summary>
            public double[] Colour { get; }

            public double Alpha { get; }

            internal static Stop Of(double at, double[] colour, double alpha) => new Stop(at, colour, alpha);
        }

        /// <summary>The face: lit from above, n1 at 36 percent of its height. Opaque.</summary>
        public static Stop[] Face { get; } = { new Stop(0, SpatialKit.FaceTop, 1), new Stop(0.36, SpatialKit.Carrier, 1), new Stop(1, SpatialKit.FaceBottom, 1) };

        /// <summary>The rim, over the panel's whole height: n7 at 20 percent at the top, 7 at 28 percent, n1 at 42 at the bottom.</summary>
        public static Stop[] Rim { get; } = { new Stop(0, SpatialKit.Text, 0.20), new Stop(0.28, SpatialKit.Text, 0.07), new Stop(1, SpatialKit.Carrier, 0.42) };

        /// <summary>The edge light at rest: n7 at 78 percent at the top, 46 at 26 percent, 40 below.</summary>
        public static Stop[] Edge { get; } = { new Stop(0, SpatialKit.Text, 0.78), new Stop(0.26, SpatialKit.Text, 0.46), new Stop(1, SpatialKit.Text, 0.40) };

        /// <summary>
        /// A moment's state edge: the edge's top in its tone, through the tone mixed 60 percent in OKLab with n7 at 40, to
        /// the neutral edge by 62 percent. The words of the moment say the same state; the light never stands alone.
        /// </summary>
        public static Stop[] StateEdge(GlazeColor tone)
        {
            var toneStop = new Stop(0, tone, 1);
            var neutral = new Stop(0, SpatialKit.Text, 0.40);
            var (mixed, alpha) = MixOklab(toneStop.Colour, 1, neutral.Colour, 0.40, 0.60);
            return new[] { toneStop, Stop.Of(0.30, mixed, alpha), Stop.Of(0.62, neutral.Colour, 0.40), Stop.Of(1, neutral.Colour, 0.40) };
        }

        /// <summary>
        /// A gradient's colour and alpha at <paramref name="t"/>, interpolated as a browser does: between the stops either
        /// side, in sRGB with premultiplied alpha.
        /// </summary>
        public static (double[] Colour, double Alpha) At(Stop[] stops, double t)
        {
            if (t <= stops[0].At) return (stops[0].Colour, stops[0].Alpha);
            for (var i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].At) continue;
                var (a, b) = (stops[i - 1], stops[i]);
                var f = b.At > a.At ? (t - a.At) / (b.At - a.At) : 1;
                var alpha = a.Alpha + (b.Alpha - a.Alpha) * f;
                var colour = new double[3];
                for (var c = 0; c < 3; c++)
                {
                    var premultiplied = a.Colour[c] * a.Alpha + (b.Colour[c] * b.Alpha - a.Colour[c] * a.Alpha) * f;
                    colour[c] = alpha > 0 ? premultiplied / alpha : b.Colour[c];
                }
                return (colour, alpha);
            }
            var last = stops[stops.Length - 1];
            return (last.Colour, last.Alpha);
        }

        /// <summary>One layer over another, as one layer, in the space they are given in (premultiplied over).</summary>
        public static (double[] Colour, double Alpha) Over((double[] Colour, double Alpha) top, (double[] Colour, double Alpha) under)
        {
            var alpha = top.Alpha + under.Alpha * (1 - top.Alpha);
            var colour = new double[3];
            for (var c = 0; c < 3; c++)
                colour[c] = alpha > 0 ? (top.Colour[c] * top.Alpha + under.Colour[c] * under.Alpha * (1 - top.Alpha)) / alpha : 0;
            return (colour, alpha);
        }

        /// <summary>A layer of sRGB <paramref name="colour"/> at <paramref name="alpha"/> over <paramref name="ground"/>, blended in sRGB as the prototype does.</summary>
        public static double[] Prototype(double[] colour, double alpha, GlazeColor ground)
        {
            var g = Channels(ground);
            var blended = new double[3];
            for (var c = 0; c < 3; c++) blended[c] = alpha * colour[c] + (1 - alpha) * g[c];
            return blended;
        }

        /// <summary>The same layer as Unity draws it, at <paramref name="linearAlpha"/>, blended in linear light, back in sRGB.</summary>
        public static double[] Drawn(double[] colour, double linearAlpha, GlazeColor ground)
        {
            var linear = new double[3];
            for (var c = 0; c < 3; c++) linear[c] = Decode(colour[c]);
            return Drawn((linear, linearAlpha), ground);
        }

        /// <summary>A solved layer (<see cref="Solve"/>) as Unity draws it over <paramref name="ground"/>, back in sRGB.</summary>
        public static double[] Drawn((double[] Linear, double Alpha) layer, GlazeColor ground)
        {
            var g = Channels(ground);
            var blended = new double[3];
            for (var c = 0; c < 3; c++) blended[c] = Encode(layer.Alpha * layer.Linear[c] + (1 - layer.Alpha) * Decode(g[c]));
            return blended;
        }

        /// <summary>
        /// The layer Unity must draw, in linear light, so that its blend over <paramref name="ground"/> lands where the
        /// prototype's sRGB blend of <paramref name="colour"/> at <paramref name="alpha"/> does: the alpha is the least-squares
        /// fit across the three channels for the colour as authored; with <paramref name="nudge"/> the colour is then nudged
        /// per channel so the blend lands exactly. A nudge toward a warm room tints the layer cool over every other room, so
        /// the rim, which the room shows through, takes none (<see cref="RimNudge"/>). Over any other ground the layer drifts;
        /// the render check reports how far.
        /// </summary>
        public static (double[] Linear, double Alpha) Solve(double[] colour, double alpha, GlazeColor ground, bool nudge = true)
        {
            var g = Channels(ground);
            var target = Prototype(colour, alpha, ground);
            double along = 0, length = 0;
            for (var c = 0; c < 3; c++)
            {
                var span = Decode(colour[c]) - Decode(g[c]);
                along += (Decode(target[c]) - Decode(g[c])) * span;
                length += span * span;
            }
            var fitted = length < 1e-12 ? alpha : Math.Max(0, Math.Min(1, along / length));
            var linear = new double[3];
            for (var c = 0; c < 3; c++)
                linear[c] = !nudge || fitted <= 1e-6 ? Decode(colour[c]) : Math.Max(0, Math.Min(1, (Decode(target[c]) - (1 - fitted) * Decode(g[c])) / fitted));
            return (linear, fitted);
        }

        /// <summary>Whether the rim's colour is nudged toward its reference room: no, so it keeps no cast over any other (ART, 2026-10-30).</summary>
        public const bool RimNudge = false;

        /// <summary>
        /// A shadow's alpha in linear light for its prototype <paramref name="alpha"/>: black over the lit room, fitted as
        /// 1 - (1 - a)^<see cref="ShadowExponent"/>; cheap enough for every pixel of a shadow.
        /// </summary>
        public static double ShadowLinear(double alpha) => 1 - Math.Pow(1 - alpha, ShadowExponent);

        public const double ShadowExponent = 2.2;

        /// <summary>The glow's alpha in linear light: n7 over the dark room, fitted as k a^p.</summary>
        public static double GlowLinear(double alpha) => GlowScale * Math.Pow(alpha, GlowPower);

        public const double GlowScale = 0.390, GlowPower = 1.21;

        /// <summary>A rounded box's signed distance, in dp, from a point <paramref name="x"/>, <paramref name="y"/> from its centre: negative inside.</summary>
        public static double Box(double x, double y, double halfWidth, double halfHeight, double radius)
        {
            var qx = Math.Abs(x) - halfWidth + radius;
            var qy = Math.Abs(y) - halfHeight + radius;
            var outside = Math.Sqrt(Math.Pow(Math.Max(qx, 0), 2) + Math.Pow(Math.Max(qy, 0), 2));
            return outside + Math.Min(Math.Max(qx, qy), 0) - radius;
        }

        /// <summary>The error function, as the shader computes it (Winitzki's approximation, to about 1e-4).</summary>
        public static double Erf(double x)
        {
            var x2 = x * x;
            var t = Math.Exp(-x2 * (1.2732395 + 0.147 * x2) / (1 + 0.147 * x2));
            return Math.Sign(x) * Math.Sqrt(1 - t);
        }

        /// <summary>How much of a shape a CSS blur of <paramref name="blur"/> spreads to a point <paramref name="distance"/> dp outside its edge.</summary>
        public static double Blurred(double distance, double blur)
        {
            var sigma = Math.Max(blur / 2, 1e-3);
            return 0.5 * (1 - Erf(distance / (sigma * Math.Sqrt(2))));
        }

        /// <summary>
        /// The prototype's colour at a point outside a panel <paramref name="width"/> by <paramref name="height"/> dp with
        /// corners of <paramref name="radius"/>, the point given in dp from the panel's centre: its two shadows and, where
        /// on, its glow over <paramref name="ground"/>, blended as the browser blends them.
        /// </summary>
        public static double[] PrototypeOutside(double x, double y, double width, double height, double radius, GlazeColor ground, bool glow = true)
        {
            var (hw, hh) = (width / 2, height / 2);
            var black = new double[] { 0, 0, 0 };
            var colour = Channels(ground);
            colour = Blend(black, ShadowAlpha * Blurred(Box(x, y + ShadowDropDp, hw, hh, radius), ShadowBlurDp), colour);
            colour = Blend(black, ContactAlpha * Blurred(Box(x, y + ContactDropDp, hw, hh, radius), ContactBlurDp), colour);
            if (glow) colour = Blend(Channels(SpatialKit.Text), GlowAlpha * Blurred(Box(x, y, hw, hh, radius), GlowBlurDp), colour);
            return colour;
        }

        private static double[] Blend(double[] colour, double alpha, double[] under)
        {
            var blended = new double[3];
            for (var c = 0; c < 3; c++) blended[c] = alpha * colour[c] + (1 - alpha) * under[c];
            return blended;
        }

        /// <summary>The worst channel of two sRGB colours apart, in 255ths.</summary>
        public static double Deviation(double[] a, double[] b)
        {
            double worst = 0;
            for (var c = 0; c < 3; c++) worst = Math.Max(worst, Math.Abs(a[c] - b[c]) * 255);
            return worst;
        }

        public static GlazeColor ToColour(double[] srgb) =>
            new GlazeColor(ToByte(srgb[0]), ToByte(srgb[1]), ToByte(srgb[2]));

        public static double[] Channels(GlazeColor colour) => new[] { colour.R / 255.0, colour.G / 255.0, colour.B / 255.0 };

        public static double Decode(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

        public static double Encode(double l)
        {
            l = Math.Max(0, Math.Min(1, l));
            return l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
        }

        private static byte ToByte(double c) => (byte)Math.Round(Math.Max(0, Math.Min(1, c)) * 255);

        /// <summary>CSS color-mix in OKLab: <paramref name="share"/> of the first, premultiplied, as a browser mixes two colours with alpha.</summary>
        private static (double[] Colour, double Alpha) MixOklab(double[] first, double firstAlpha, double[] second, double secondAlpha, double share)
        {
            var a = ToOklab(first);
            var b = ToOklab(second);
            var alpha = firstAlpha * share + secondAlpha * (1 - share);
            var mixed = new double[3];
            for (var c = 0; c < 3; c++) mixed[c] = (a[c] * firstAlpha * share + b[c] * secondAlpha * (1 - share)) / alpha;
            return (FromOklab(mixed), alpha);
        }

        private static double[] ToOklab(double[] srgb)
        {
            var (r, g, b) = (Decode(srgb[0]), Decode(srgb[1]), Decode(srgb[2]));
            var l = Math.Pow(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b, 1 / 3.0);
            var m = Math.Pow(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b, 1 / 3.0);
            var s = Math.Pow(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b, 1 / 3.0);
            return new[]
            {
                0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
            };
        }

        private static double[] FromOklab(double[] lab)
        {
            var l = Math.Pow(lab[0] + 0.3963377774 * lab[1] + 0.2158037573 * lab[2], 3);
            var m = Math.Pow(lab[0] - 0.1055613458 * lab[1] - 0.0638541728 * lab[2], 3);
            var s = Math.Pow(lab[0] - 0.0894841775 * lab[1] - 1.2914855480 * lab[2], 3);
            return new[]
            {
                Encode(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
                Encode(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
                Encode(-0.0041960863 * l - 0.5034933500 * m + 1.1301676265 * s),
            };
        }
    }
}
