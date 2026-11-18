#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class KitRender
    {
        /// <summary>How many parts of the redesign's chrome the no-hue check read in this run; none fails the run.</summary>
        private static int chromePartsRead;

        /// <summary>How many workspace hosts shown under the redesign the old-kit guard read in this run; none fails a whole run.</summary>
        private static int redesignHostsRead;

        /// <summary>The name a glass sheet's room ground begins with (KitRender.Glass): the room behind the panels, never chrome.</summary>
        internal const string RoomGroundPrefix = "Ground: ";

        /// <summary>The old kit's control sheet (KitRender.Scenes), drawn with the redesign off.</summary>
        internal const string ControlSheetName = "Control kit";

        /// <summary>The most saturation a drawn chrome colour may carry and still read as no hue (a grey's rounding, never a tint).</summary>
        private const float ChromeSaturationMost = 0.12f;

        /// <summary>The least value a chrome colour is read at for hue: under it, a near black, whose channels round apart.</summary>
        private const float ChromeValueLeast = 0.04f;

        /// <summary>
        /// No hue in the chrome (the lead's 2026-11-17T19:15Z; the lead's 2026-11-18 MUST after AUDIT's eye on 26-receipt): every glass of
        /// the kit draws no tone and no tint, and every plate, ring, disc and word it draws is a grey or white. A character's body and a figure
        /// keep their identity hue, and a code row's syntax spans are content: they are not chrome. Read from what each part is drawn with,
        /// in every kit state.
        /// </summary>
        internal static IEnumerable<string> ChromeTakesNoHue(GameObject root, string what)
        {
            // The old kit's views never ship (KitRender draws them with the redesign off until ON-2 removes them; the lead's 2026-11-18 ruling
            // 1): Focused work, Overview, Compact presence and the control sheet are not read.
            bool OldKit(Component part) => part.GetComponentInParent<KitWorkView>() != null || part.GetComponentInParent<KitOverviewView>() != null
                || part.GetComponentInParent<KitPresenceView>() != null || InControlSheet(part.transform);
            static bool InControlSheet(Transform part) { for (var at = part; at != null; at = at.parent) if (at.name == ControlSheetName) return true; return false; }
            bool Exempt(Component part)
            {
                if (part.GetComponentInParent<CharacterView>() != null || part.GetComponentInParent<KitFigure>() != null || OldKit(part)) return true;
                chromePartsRead++;
                return false;
            }
            string Path(Transform part) => part.parent != null && part.parent != root.transform ? Path(part.parent) + "/" + part.name : part.name;
            string? Hued(Color colour)
            {
                if (colour.a < 0.05f) return null;
                Color.RGBToHSV(colour, out _, out var saturation, out var value);
                // Down to the darkest chrome (#172024 read navy at a value of 0.14, F305): only a near black, whose rounding alone
                // gives it a saturation, is let through.
                return saturation > ChromeSaturationMost && value > ChromeValueLeast ? "#" + ColorUtility.ToHtmlStringRGB(colour) : null;
            }
            // The old kit's views are exempt only because the redesign never draws them (the lead's 2026-11-18 ruling 1; AUDIT3's NH3): a host
            // shown under the redesign that stands either of them is a fallback that ships, and fails here.
            foreach (var host in root.GetComponentsInChildren<KitWorkspaceHost>(false))
                if (host.UnderRedesign && ++redesignHostsRead > 0 && (host.View.gameObject.activeInHierarchy || host.Presence.gameObject.activeInHierarchy))
                    yield return what + ": " + Path(host.transform) + " shown under the redesign stands the old kit's " + (host.View.gameObject.activeInHierarchy ? "work view" : "presence");
            foreach (var glass in root.GetComponentsInChildren<KitGlass>(false))
            {
                if (Exempt(glass)) continue;
                // Lit white is light, not hue (an answer's pulse); lit in a hue is a tone on the chrome.
                if (glass.Tone is GlazeColor tone && Hued(KitType.Colour(tone)) is string toned) yield return what + ": the glass " + Path(glass.transform) + " is lit in a tone, " + toned;
                if (glass.Tint is GlazeColor tint) yield return what + ": the glass " + Path(glass.transform) + " carries a tint, #" + ColorUtility.ToHtmlStringRGB(KitType.Colour(tint));
            }
            foreach (var surface in root.GetComponentsInChildren<Surface>(false))
            {
                // The room a glass sheet stands its panels on (KitRender.Glass's "Ground: " plates) is the room, not chrome.
                if (surface.name.StartsWith(RoomGroundPrefix, System.StringComparison.Ordinal)) continue;
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
