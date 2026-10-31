#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// The room's one panel material, ART8's window glass (2026-10-31, at the floor-safe value the lead kept): one opaque face
    /// over the whole panel, a grey gradient lightest at its top with a soft radial light there, one edge on its outer rim (a
    /// top line of white at 30 percent and a ring of white at 6), and one soft shadow (ART8B); on a moment its state edge and the bloom of its arrival. No part is
    /// see-through and nothing glows round it. The rim of the quiet glass is gone: its width stays as the words' inset.
    /// </summary>
    /// <remarks>
    /// The prototype composites in sRGB, as a browser does; Unity blends alpha in linear light, where the same alpha comes
    /// out lighter. Everything on the opaque face is mixed in sRGB inside the shader, so it matches exactly. Only the shadow
    /// lies over the room: it is fitted for the lit room (<see cref="ShadowLinear"/>), and the render check reports how far
    /// the others drift. Every value here is in dp at the eye (16 to the degree) or sRGB, and the shader repeats these formulas.
    /// </remarks>
    public static class KitGlassMaterial
    {
        /// <summary>The art direction's three grounds: a dark room, a lit room and a white wall.</summary>
        public static GlazeColor DarkRoom => GlazeColor.Hex(0x1E1C1A);

        public static GlazeColor LitRoom => GlazeColor.Hex(0xB9AE9E);

        public static GlazeColor WhiteWall => GlazeColor.Hex(0xEEEEEE);

        /// <summary>How far a panel's words keep from its edge (the screen, a moment, the reader, the bar), in dp: the quiet glass's rim, now face.</summary>
        public const float RimDp = 10f;

        /// <summary>The rim round a tile and the character's tag.</summary>
        public const float SmallRimDp = 6f;

        /// <summary>The rim round the lost plate and the earlier moments' edge.</summary>
        public const float PlateRimDp = 8f;

        /// <summary>The glow round the edge of the quiet glass: none in ART8's window. Kept at 0 so nothing lights the room round a panel.</summary>
        public const float GlowBlurDp = 18f;

        public const double GlowAlpha = 0;

        /// <summary>The top line just inside the face's top edge: white at <see cref="HighlightAlpha"/>.</summary>
        public const float HighlightDp = 1f;

        public const double HighlightAlpha = 0.30;

        /// <summary>The ring on the face's outer edge, all round, and only there (ART8B: no second frame inside it): 1 dp of white at this alpha.</summary>
        public const float InnerEdgeDp = 1f;

        public const double InnerEdgeAlpha = 0.06;

        /// <summary>The soft light at the face's top: white at this alpha at the top's middle, gone by <see cref="TopLightReach"/> of the height.</summary>
        public const double TopLightAlpha = 0.10, TopLightReach = 0.60;

        /// <summary>The shadow: this far down, this blur, black at this alpha (ART8B's 0 18 40, 24 percent).</summary>
        public const float ShadowDropDp = 18f, ShadowBlurDp = 40f;

        public const double ShadowAlpha = 0.24;

        /// <summary>The near shadow of the quiet glass: none in ART8's window.</summary>
        public const float ContactDropDp = 0f, ContactBlurDp = 1f;

        public const double ContactAlpha = 0;

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

        /// <summary>The face: ART8's grey from its top to its foot, n1 at the middle. Opaque over the whole panel.</summary>
        public static Stop[] Face { get; } = { new Stop(0, SpatialKit.FaceTop, 1), new Stop(0.5, SpatialKit.Carrier, 1), new Stop(1, SpatialKit.FaceBottom, 1) };

        /// <summary>
        /// The face as the prototype draws it at <paramref name="t"/> of its height from the top, <paramref name="across"/> of
        /// its half width from its middle (0 to 1): the gradient, then the soft top light, both mixed in sRGB.
        /// </summary>
        public static double[] LitFace(double t, double across = 0)
        {
            var (face, _) = At(Face, t);
            var reach = Math.Sqrt(across * across * 0.25 + Math.Pow(t / TopLightReach, 2));
            return Prototype(new double[] { 1, 1, 1 }, TopLightAlpha * Math.Max(0, 1 - reach), ToColour(face));
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

        /// <summary>A layer already in linear light as Unity draws it over <paramref name="ground"/>, back in sRGB.</summary>
        public static double[] Drawn((double[] Linear, double Alpha) layer, GlazeColor ground)
        {
            var g = Channels(ground);
            var blended = new double[3];
            for (var c = 0; c < 3; c++) blended[c] = Encode(layer.Alpha * layer.Linear[c] + (1 - layer.Alpha) * Decode(g[c]));
            return blended;
        }

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
    }
}
