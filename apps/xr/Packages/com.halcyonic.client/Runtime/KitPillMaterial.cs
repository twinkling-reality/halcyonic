#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// The glass role of a capsule or round button in the room (2026-10-31): green for an action that runs or tries a build,
    /// red for declining, blue for the one primary where neither applies, neutral smoked-slate glass for secondary capsules and
    /// round icon buttons, and none for unframed controls.
    /// </summary>
    public enum KitPillMeaning
    {
        None = 0,
        Blue = 1,
        Green = 2,
        Red = 3,
        Neutral = 4,
    }

    /// <summary>The interaction state of a glass capsule or round button.</summary>
    public enum KitPillState
    {
        Rest = 0,
        Pointed = 1,
        Pressed = 2,
    }

    /// <summary>
    /// The room's unified glass-capsule material (2026-10-31): every capsule and round button shares the same five-stop
    /// vertical glass depth ramp, subtle bottom rim bounce, continuous curved upper specular crown inset from the top and sides,
    /// crisp 1 dp top rim catch-light, and 1.5 dp darker outer rim, in a true grey (the owner's no-hue order); the prominent ones
    /// (<see cref="Prominent"/>: <see cref="KitPillMeaning.Blue"/> and <see cref="KitPillMeaning.Green"/>) are RoomButton's flat
    /// main-action plate instead, and <see cref="KitPillMeaning.Red"/> is the neutral glass. Every layer mixes in sRGB inside the shader
    /// (<c>Halcyonic/Kit Pill</c>), and white words hold at least 4.8:1 across the middle band in every state.
    /// </summary>
    public static class KitPillMaterial
    {
        /// <summary>The darker outer rim's width in dp, and the crisp top rim catch-light's width inside it in dp.</summary>
        public const float RimDp = 1.5f, TopCatchDp = 1.2f;

        /// <summary>The continuous upper specular crown's middle inflection and bottom fade-out positions as a share of capsule height.</summary>
        public const float GlossMidShare = 0.36f, GlossEndShare = 0.50f;

        /// <summary>The upper specular crown's height as a share of the capsule's height, and its saturated top and mid white alpha (fading to 0 at <see cref="GlossEndShare"/>).</summary>
        public const double GlossHeightShare = 0.50, GlossTopAlpha = 0.34, GlossBottomAlpha = 0.10;

        /// <summary>The neutral smoked-slate glass's top and mid specular crown alpha, and the top rim catch-light alpha for neutral and saturated capsules.</summary>
        public const double NeutralGlossTopAlpha = 0.22, NeutralGlossBottomAlpha = 0.06, NeutralTopCatchAlpha = 0.28, SaturatedTopCatchAlpha = 0.42;

        /// <summary>The bottom rim bounce's side inset as a share of width, bottom inset in dp, height share, radial reach and alpha.</summary>
        public const double GlowSideInsetShare = 0.14, GlowBottomInsetDp = 1.0, GlowHeightShare = 0.20, GlowReach = 0.72, GlowAlpha = 0.28, NeutralGlowAlpha = 0.16;

        /// <summary>How far pointing lifts the gradient toward white in sRGB, and how far pressing deepens it toward black.</summary>
        public const double PointedLift = 0.05, PressedDarken = 0.12;

        /// <summary>
        /// The prominent press (the primary, and Approve's green): exactly RoomButton's main-action plate, #474747 flat with white words, and
        /// #555555 pointed (the lead's ruling 2a, the owner's visionOS-style prominent plate, 2026-11-17; visionos-button-prominent.jpg): no gloss,
        /// no catch-light, no bounce and no rim of its own, so a primary pill and the redesign's main action are one plate.
        /// </summary>
        public const int ProminentPlate = 0x474747, ProminentPointedPlate = 0x555555;

        /// <summary>Whether <paramref name="meaning"/> draws the prominent plate: the primary and Approve's green; Decline's red is the neutral glass.</summary>
        public static bool Prominent(KitPillMeaning meaning) => meaning != KitPillMeaning.Neutral && meaning != KitPillMeaning.Red;

        private static readonly KitGlassMaterial.Stop[] BlueStops =
        {
            new KitGlassMaterial.Stop(0.00, GlazeColor.Hex(ProminentPlate), 1),
            new KitGlassMaterial.Stop(0.30, GlazeColor.Hex(ProminentPlate), 1),
            new KitGlassMaterial.Stop(0.68, GlazeColor.Hex(ProminentPlate), 1),
            new KitGlassMaterial.Stop(0.88, GlazeColor.Hex(ProminentPlate), 1),
            new KitGlassMaterial.Stop(1.00, GlazeColor.Hex(ProminentPlate), 1),
        };

        private static readonly KitGlassMaterial.Stop[] NeutralStops =
        {
            new KitGlassMaterial.Stop(0.00, GlazeColor.Hex(0x555555), 1),
            new KitGlassMaterial.Stop(0.30, GlazeColor.Hex(0x404040), 1),
            new KitGlassMaterial.Stop(0.68, GlazeColor.Hex(0x3D3D3D), 1),
            new KitGlassMaterial.Stop(0.88, GlazeColor.Hex(0x303030), 1),
            new KitGlassMaterial.Stop(1.00, GlazeColor.Hex(0x474747), 1),
        };

        /// <summary>The five vertical gradient stops from top (0) to bottom (1) for <paramref name="meaning"/>.</summary>
        public static KitGlassMaterial.Stop[] Stops(KitPillMeaning meaning) => meaning switch
        {
            KitPillMeaning.Red => NeutralStops,
            KitPillMeaning.Neutral => NeutralStops,
            _ => BlueStops,
        };

        /// <summary>The upper specular crown's top and bottom white alpha for <paramref name="meaning"/>.</summary>
        public static (double Top, double Bottom) CrownAlpha(KitPillMeaning meaning) =>
            !Prominent(meaning) ? (NeutralGlossTopAlpha, NeutralGlossBottomAlpha) : (0.0, 0.0);

        /// <summary>The 1 dp top rim catch-light white alpha for <paramref name="meaning"/>.</summary>
        public static double TopCatchAlpha(KitPillMeaning meaning) =>
            !Prominent(meaning) ? NeutralTopCatchAlpha : 0.0;

        /// <summary>The bottom rim bounce alpha for <paramref name="meaning"/>.</summary>
        public static double GlowAlphaOf(KitPillMeaning meaning) =>
            !Prominent(meaning) ? NeutralGlowAlpha : 0.0;

        /// <summary>The thin darker outer rim colour for <paramref name="meaning"/>.</summary>
        public static GlazeColor Rim(KitPillMeaning meaning) => meaning switch
        {
            KitPillMeaning.Red or KitPillMeaning.Neutral => GlazeColor.Hex(0x222222),
            _ => GlazeColor.Hex(ProminentPlate),
        };

        /// <summary>The bottom inner bounce / glow colour for <paramref name="meaning"/>.</summary>
        public static GlazeColor Glow(KitPillMeaning meaning) => meaning switch
        {
            KitPillMeaning.Red or KitPillMeaning.Neutral => GlazeColor.Hex(0x797979),
            _ => GlazeColor.Hex(ProminentPlate),
        };

        /// <summary>The deep middle fill of <paramref name="meaning"/> at rest, where its white word stands.</summary>
        public static GlazeColor Middle(KitPillMeaning meaning) => meaning switch
        {
            KitPillMeaning.Red or KitPillMeaning.Neutral => GlazeColor.Hex(0x3D3D3D),
            _ => GlazeColor.Hex(ProminentPlate),
        };

        /// <summary>The middle fill of <paramref name="meaning"/> when pointed.</summary>
        public static GlazeColor PointedMiddle(KitPillMeaning meaning) => Prominent(meaning) ? GlazeColor.Hex(ProminentPointedPlate)
            : KitGlassMaterial.ToColour(Blend(new double[] { 1, 1, 1 }, PointedLift, KitGlassMaterial.Channels(Middle(meaning))));

        /// <summary>The deep middle fill of <paramref name="meaning"/> when pressed.</summary>
        public static GlazeColor PressedMiddle(KitPillMeaning meaning) =>
            KitGlassMaterial.ToColour(Blend(new double[] { 0, 0, 0 }, PressedDarken, KitGlassMaterial.Channels(Middle(meaning))));

        /// <summary>The base vertical gradient at <paramref name="t"/> from the top (0 to 1) in <paramref name="state"/>, in sRGB.</summary>
        public static double[] At(KitPillMeaning meaning, double t, KitPillState state = KitPillState.Rest)
        {
            var (colour, _) = KitGlassMaterial.At(Stops(meaning), t);
            if (Prominent(meaning) && state == KitPillState.Pointed) return KitGlassMaterial.Channels(GlazeColor.Hex(ProminentPointedPlate));
            return state switch
            {
                KitPillState.Pointed => Blend(new double[] { 1, 1, 1 }, PointedLift, colour),
                KitPillState.Pressed => Blend(new double[] { 0, 0, 0 }, PressedDarken, colour),
                _ => colour,
            };
        }

        /// <summary>
        /// The prototype's sRGB colour at a point <paramref name="xDp"/>, <paramref name="yDp"/> in dp from the centre of a
        /// capsule <paramref name="widthDp"/> by <paramref name="heightDp"/> dp (<paramref name="yDp"/> positive toward the top):
        /// the five-stop vertical glass ramp, the bottom rim bounce, the inset upper specular crown, the 1 dp top rim catch-light,
        /// and the 1.5 dp outer rim.
        /// </summary>
        public static double[] Sample(KitPillMeaning meaning, double xDp, double yDp, double widthDp, double heightDp, KitPillState state = KitPillState.Rest)
        {
            var hw = widthDp * 0.5;
            var hh = heightDp * 0.5;
            var radius = Math.Min(hw, hh);
            var outer = KitGlassMaterial.Box(xDp, yDp, hw, hh, radius);
            var t = Math.Max(0, Math.Min(1, (hh - yDp) / Math.Max(heightDp, 1e-4)));
            var colour = At(meaning, t, state);

            var gx = Math.Max((0.5 - GlowSideInsetShare) * widthDp, 1.0);
            var gy = Math.Max(GlowHeightShare * heightDp, 1.0);
            var y0 = -hh + GlowBottomInsetDp;
            var dx = xDp / gx;
            var dy = Math.Max(0.0, yDp - y0) / gy;
            var glowDist = Math.Sqrt(dx * dx + dy * dy) / GlowReach;
            var glowWeight = GlowAlphaOf(meaning) * Math.Max(0.0, 1.0 - glowDist);
            colour = Blend(KitGlassMaterial.Channels(Glow(meaning)), glowWeight, colour);

            if (t < GlossEndShare)
            {
                var (topAlpha, midAlpha) = CrownAlpha(meaning);
                var glossAlpha = t < GlossMidShare
                    ? topAlpha + (midAlpha - topAlpha) * (t / GlossMidShare)
                    : midAlpha * (1.0 - (t - GlossMidShare) / (GlossEndShare - GlossMidShare));
                colour = Blend(new double[] { 1, 1, 1 }, glossAlpha, colour);
            }

            if (yDp > 0 && outer + RimDp < 0 && KitGlassMaterial.Box(xDp, yDp + TopCatchDp, hw - RimDp, hh - RimDp, Math.Max(radius - RimDp, 0.0)) >= 0)
                colour = Blend(new double[] { 1, 1, 1 }, TopCatchAlpha(meaning), colour);

            if (outer + RimDp >= 0)
                colour = KitGlassMaterial.Channels(Rim(meaning));

            return colour;
        }

        private static double[] Blend(double[] top, double alpha, double[] under)
        {
            var blended = new double[3];
            for (var c = 0; c < 3; c++) blended[c] = alpha * top[c] + (1 - alpha) * under[c];
            return blended;
        }
    }
}
