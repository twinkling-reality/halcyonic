#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class KitRender
    {
        /// <summary>The most saturation a drawn chrome colour may carry and still read as no hue (a grey's rounding, never a tint).</summary>
        private const float ChromeSaturationMost = 0.12f;

        /// <summary>
        /// No hue in the chrome (the lead's 2026-11-17T19:15Z; the lead's 2026-11-18 MUST after AUDIT's eye on 26-receipt): every glass of
        /// the kit draws no tone and no tint, and every plate, ring, disc and word it draws is a grey or white. A character's body and a figure
        /// keep their identity hue, and a code row's syntax spans are content: they are not chrome. Read from what each part is drawn with,
        /// in every kit state.
        /// </summary>
        private static IEnumerable<string> ChromeTakesNoHue(GameObject root, string what)
        {
            bool Exempt(Component part) => part.GetComponentInParent<CharacterView>() != null || part.GetComponentInParent<KitFigure>() != null;
            string Path(Transform part) => part.parent != null && part.parent != root.transform ? Path(part.parent) + "/" + part.name : part.name;
            string? Hued(Color colour)
            {
                if (colour.a < 0.05f) return null;
                Color.RGBToHSV(colour, out _, out var saturation, out var value);
                return saturation > ChromeSaturationMost && value > 0.15f ? "#" + ColorUtility.ToHtmlStringRGB(colour) : null;
            }
            foreach (var glass in root.GetComponentsInChildren<KitGlass>(false))
            {
                if (Exempt(glass)) continue;
                if (glass.Tone is GlazeColor tone) yield return what + ": the glass " + Path(glass.transform) + " is lit in a tone, #" + ColorUtility.ToHtmlStringRGB(KitType.Colour(tone));
                if (glass.Tint is GlazeColor tint) yield return what + ": the glass " + Path(glass.transform) + " carries a tint, #" + ColorUtility.ToHtmlStringRGB(KitType.Colour(tint));
            }
            foreach (var surface in root.GetComponentsInChildren<Surface>(false))
            {
                if (Exempt(surface) || surface.GetComponent<Renderer>() is Renderer drawn && !drawn.enabled) continue;
                if (Hued(surface.Fill) is string fill) yield return what + ": " + Path(surface.transform) + " is filled with a hue, " + fill;
                if (Hued(surface.Edge) is string edge) yield return what + ": " + Path(surface.transform) + " is edged with a hue, " + edge;
            }
            foreach (var label in root.GetComponentsInChildren<TMPro.TMP_Text>(false))
            {
                if (Exempt(label) || string.IsNullOrEmpty(label.text)) continue;
                // A code row's syntax spans are content, the owner's Code Colours (the lead's 2026-11-18 ruling): exactly those spans are
                // exempt, so its own colour, the words outside every span, is what is read. Nothing else of the code view is.
                var drawnIn = CodeColouring.Colours(label) ? label.color : DrawnColour(label);
                if (Hued(drawnIn) is string ink) yield return what + ": " + Path(label.transform) + " \"" + Clip(label.text) + "\" is drawn in a hue, " + ink;
            }
        }
    }
}
