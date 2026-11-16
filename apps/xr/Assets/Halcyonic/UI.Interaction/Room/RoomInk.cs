#nullable enable
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The words' ink on the panels' glass, as <see cref="RoomSurface.Theme"/> stands: white on Dark (the owner's reference), and
    /// on Light (ours) the reference's structure inverted, rgb(12,14,22) at 0.92 for the words and their quiet alphas scaled the
    /// same (the lead's 2026-11-16 13:33Z). The views lay their words white as ever; <see cref="Follow"/> turns a view's white
    /// words to the light ink after it lays them, and back, so Dark is untouched by Light and costs nothing until Light is ever
    /// chosen. The actions keep their own dark halos and white words on either glass, and the agent's words band its own dark
    /// ground: Light's are the next part of this sweep.
    /// </summary>
    public static class RoomInk
    {
        /// <summary>The light glass's ink, rgb(12,14,22).</summary>
        public static readonly Color Ink = new Color(12f / 255f, 14f / 255f, 22f / 255f, 1f);

        /// <summary>The light ink's alpha on a word that is white at full on Dark.</summary>
        public const float LightAlpha = 0.92f;

        private static bool everLight;

        /// <summary>The words' colour on the glass as it stands: white, or the light ink.</summary>
        public static Color Text => RoomSurface.Theme == PanelTheme.Light ? new Color(Ink.r, Ink.g, Ink.b, LightAlpha) : Color.white;

        /// <summary>
        /// Turns every white word under <paramref name="root"/> to the light ink on Light, its alpha scaled, and the light ink back to
        /// white on Dark; an action's words (on its own halo), a coloured word and a word on the band's ground stay as they are.
        /// Nothing at all on Dark until Light was once chosen.
        /// </summary>
        public static void Follow(Transform root)
        {
            var light = RoomSurface.Theme == PanelTheme.Light;
            if (light) everLight = true;
            if (!everLight || root == null) return;
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.GetComponentInParent<RoomButton>() != null || label.GetComponentInParent<RoomBand>() != null) continue;
                var colour = label.color;
                if (light && colour.r > 0.97f && colour.g > 0.97f && colour.b > 0.97f)
                    label.color = new Color(Ink.r, Ink.g, Ink.b, colour.a * LightAlpha);
                else if (!light && Near(colour.r, Ink.r) && Near(colour.g, Ink.g) && Near(colour.b, Ink.b))
                    label.color = new Color(1f, 1f, 1f, Mathf.Min(1f, colour.a / LightAlpha));
            }
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.002f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => everLight = false;
    }
}
