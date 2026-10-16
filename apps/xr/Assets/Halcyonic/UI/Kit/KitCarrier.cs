#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// One opaque region of the spatial kit's instrument: a face drawn whole, so nothing of the room shows
    /// through the words on it, and as it needs, the one light the instrument carries, along the forward region's
    /// top in the state tone of the work it shows. Regions are told apart by tone, depth and the room between them,
    /// never by outlines. Every part is a <see cref="Surface"/>: the kit adds no shader. Centred on its transform,
    /// as every surface is.
    /// </summary>
    public sealed class KitCarrier : MonoBehaviour
    {
        private Surface face = null!;
        private Surface edgeLight = null!;

        /// <summary>The face's size, in its parent's units.</summary>
        public Vector2 Size { get; private set; }

        public float Radius { get; private set; }

        /// <summary>The face that carries words: its fill is opaque.</summary>
        public Surface Face => face;

        public static KitCarrier Create(Transform parent, string name, int order = KitType.Order.Carrier)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var carrier = go.AddComponent<KitCarrier>();
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

        /// <summary>The face's four corners, in the world, for field and footprint checks.</summary>
        public IEnumerable<Vector3> Corners()
        {
            foreach (var x in new[] { -0.5f, 0.5f })
            foreach (var y in new[] { -0.5f, 0.5f })
                yield return transform.TransformPoint(new Vector3(Size.x * x, Size.y * y, 0f));
        }
    }
}
