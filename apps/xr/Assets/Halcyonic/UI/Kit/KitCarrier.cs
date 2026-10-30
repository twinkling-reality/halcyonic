#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// One opaque region of the spatial kit's instrument: a face drawn whole, so nothing of the room shows
    /// through the words on it, and as it needs, the one light the instrument carries, along the forward region's
    /// top in the state tone of the work it shows, and the shade the slate in front casts along the join it shares
    /// with a recessed band. Regions are told apart by tone, depth and that shade, never by outlines. Every part is
    /// a <see cref="Surface"/>; a room's panel may instead draw in the quiet glass (<see cref="DrawGlass"/>), the kit's one
    /// shader of its own.
    /// </summary>
    /// <remarks>
    /// The shade is a short ramp of black bands over the opaque face, so the face stays whole; it reads as the
    /// slate standing in front at its <see cref="SpatialKit.RecessMeters"/>, which a mono view cannot show by
    /// parallax. The carrier is centred on its transform, as every surface is.
    /// </remarks>
    public sealed class KitCarrier : MonoBehaviour
    {
        /// <summary>The shade's bands from the shared edge outward, black at these opacities.</summary>
        private static readonly float[] ShadeSteps = { 0.50f, 0.38f, 0.28f, 0.19f, 0.12f, 0.07f, 0.035f, 0.015f };

        private Surface face = null!;
        private Surface edgeLight = null!;
        private readonly List<Surface> shade = new List<Surface>();
        private KitGlass? glass;
        private int order;

        /// <summary>The face's size, in its parent's units.</summary>
        public Vector2 Size { get; private set; }

        public float Radius { get; private set; }

        /// <summary>The face that carries words: its fill is opaque. Hidden while the carrier draws in glass.</summary>
        public Surface Face => face;

        /// <summary>The quiet glass, while the carrier draws in it (<see cref="DrawGlass"/>); otherwise none.</summary>
        public KitGlass? Glass => glass != null && glass.gameObject.activeSelf ? glass : null;

        public static KitCarrier Create(Transform parent, string name, int order = KitType.Order.Carrier)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var carrier = go.AddComponent<KitCarrier>();
            carrier.order = order;
            carrier.face = Surface.Create(go.transform, "Opaque face", order);
            carrier.edgeLight = Surface.Create(go.transform, "Lit top edge", order + 1);
            carrier.edgeLight.gameObject.SetActive(false);
            return carrier;
        }

        /// <summary>
        /// Draws the face <paramref name="size"/> big with corners of <paramref name="radius"/>, filled whole with
        /// <paramref name="fill"/> and never outlined; with <paramref name="lit"/> its top edge carries the
        /// instrument's one light, in <paramref name="light"/> where given: the state tone of the work it shows,
        /// which its words also say.
        /// </summary>
        public void Draw(Vector2 size, float radius, GlazeColor fill, bool lit = false, Color? light = null)
        {
            Size = size;
            Radius = radius;
            if (glass != null) glass.Hide();
            face.gameObject.SetActive(true);
            var hairline = KitType.Units(SpatialKit.HairlineDegrees);
            face.Draw(size, radius, KitType.Colour(fill));
            face.Selection = SurfaceSelection.None;
            edgeLight.gameObject.SetActive(lit);
            if (lit)
            {
                // Just inside the top's straight run, as light along a real edge.
                var run = Mathf.Max(0f, size.x - 2f * radius);
                var thick = hairline * (light.HasValue ? 2f : 1f);
                edgeLight.Draw(new Vector2(run, thick), thick / 2f, light ?? KitType.Colour(SpatialKit.EdgeLight));
                edgeLight.transform.localPosition = new Vector3(0f, size.y / 2f - hairline - thick / 2f, -0.0002f);
            }
        }

        /// <summary>
        /// Draws the carrier as a panel of the room in the quiet glass (<see cref="KitGlass"/>): <paramref name="size"/> and
        /// <paramref name="radius"/> are its outer edge, the rim of <paramref name="rimDp"/> inside them carries no words, and
        /// a <paramref name="tone"/>, said in the panel's words too, lights its edge and blooms its face by
        /// <paramref name="bloom"/>. The opaque face, its lit edge and its shade stand aside while it does.
        /// </summary>
        public void DrawGlass(Vector2 size, float radius, float rimDp = KitGlassMaterial.RimDp, GlazeColor? tone = null, float bloom = 0f, bool glow = true)
        {
            Size = size;
            Radius = radius;
            if (glass == null) glass = KitGlass.Create(transform, "Quiet glass", order);
            face.gameObject.SetActive(false);
            edgeLight.gameObject.SetActive(false);
            foreach (var band in shade) band.gameObject.SetActive(false);
            glass.Draw(size, radius, rimDp, tone, bloom, glow);
        }

        /// <summary>
        /// Shades the face along its left or right edge, where the slate in front of it stands, over
        /// <see cref="SpatialKit.RecessShadeDegrees"/>, starting <paramref name="inset"/> in from that edge, where the
        /// slate's own edge stands over the face; neither clears it.
        /// </summary>
        public void Shade(bool left, bool right, float inset = 0f)
        {
            var sides = (left ? 1 : 0) + (right ? 1 : 0);
            var needed = sides * ShadeSteps.Length;
            while (shade.Count < needed) shade.Add(Surface.Create(transform, "Recess shade " + shade.Count, order + 2));
            var band = KitType.Units(SpatialKit.RecessShadeDegrees) / ShadeSteps.Length;
            var height = Mathf.Max(0f, Size.y - 2f * Radius);
            var index = 0;
            foreach (var (on, sign) in new[] { (left, -1f), (right, 1f) })
            {
                if (!on) continue;
                for (var step = 0; step < ShadeSteps.Length; step++, index++)
                {
                    var surface = shade[index];
                    surface.gameObject.SetActive(true);
                    surface.Draw(new Vector2(band, height), 0f, new Color(0f, 0f, 0f, ShadeSteps[step]));
                    surface.transform.localPosition = new Vector3(sign * (Size.x / 2f - inset - band * (step + 0.5f)), 0f, -0.0001f);
                }
            }
            for (; index < shade.Count; index++) shade[index].gameObject.SetActive(false);
        }

        /// <summary>The face's four corners, in the world, for field and footprint checks.</summary>
        public IEnumerable<Vector3> Corners()
        {
            foreach (var x in new[] { -0.5f, 0.5f })
            foreach (var y in new[] { -0.5f, 0.5f })
                yield return transform.TransformPoint(new Vector3(Size.x * x, Size.y * y, 0f));
        }
    }
}
