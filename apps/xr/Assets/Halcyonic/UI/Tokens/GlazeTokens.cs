#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The Glaze tokens (<see cref="Glaze"/>, in the client core) in Unity's terms: colours as Unity
    /// colours, and angles at the eye as sizes. Components are built in units of the distance from
    /// the eyes, one unit being that distance, and scaled by it, so every size here is an angle and
    /// keeps its angular size wherever the stage or a panel puts it.
    /// </summary>
    public static class GlazeTokens
    {
        /// <summary>A token's colour as authored, in sRGB, with an opacity.</summary>
        public static Color ColorOf(GlazeColor color, float alpha = 1f) => new Color(color.R / 255f, color.G / 255f, color.B / 255f, alpha);

        /// <summary>How long an angle at the eye is, in units of the distance from the eyes.</summary>
        public static float Units(float degrees) => Glaze.MetersAt(degrees, 1f);

        /// <summary>A TextMeshPro font size for an em of <paramref name="em"/> units: TextMeshPro draws a size of one as a tenth of a unit.</summary>
        public static float FontSize(float em) => em * 10f;

        /// <summary>The colour of text on a panel.</summary>
        public static Color Text => ColorOf(Glaze.Text);

        public static Color TextSecondary => ColorOf(Glaze.TextSecondary);
    }
}
