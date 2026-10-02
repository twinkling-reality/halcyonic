#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A composition on one plane (<see cref="PlaneComposition"/>, ADR 0026) in Unity's terms: the
    /// plane faces the eyes at the composition's centre, never rolled, and each part lies on it at its
    /// offset from that centre, built in units of the plane's distance.
    /// </summary>
    public static class PlaneLayout
    {
        /// <summary>The plane's rotation with the composition's centre at <paramref name="direction"/>: facing the eyes there, its right level.</summary>
        public static Quaternion Facing(PanelDirection direction)
        {
            var (forward, _, up) = PlaneComposition.Axes(direction);
            return Quaternion.LookRotation(new Vector3(forward.X, forward.Y, forward.Z), new Vector3(up.X, up.Y, up.Z));
        }

        /// <summary>Where a point <paramref name="right"/> and <paramref name="up"/> from the centre, along the plane in units of its distance, stands in the world.</summary>
        public static Vector3 PointOf(Vector3 eyes, PanelDirection direction, float right, float up)
        {
            var (x, y, z) = PlaneComposition.PointOf(direction, right, up);
            return eyes + new Vector3(x, y, z);
        }

        /// <summary>Stands <paramref name="part"/> where the composition lays <paramref name="placed"/>, its centre on its origin, scaled to units of the plane's distance.</summary>
        public static void Lay(Transform part, Vector3 eyes, PanelDirection direction, PlanePart placed)
        {
            part.SetPositionAndRotation(PointOf(eyes, direction, placed.Right, placed.Up), Facing(direction));
            part.localScale = Vector3.one * PlaneComposition.Distance;
        }
    }
}
