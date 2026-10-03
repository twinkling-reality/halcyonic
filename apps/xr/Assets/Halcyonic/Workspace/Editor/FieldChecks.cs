#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Whether what matters stays inside a headset's field of view (<see cref="ViewField"/>) less its
    /// margin, as the person looks at it: the head turned toward it and level, or, over a desk, pitched
    /// down to the lineup, and for a panel taller than designed tipped down a little to read it
    /// (<see cref="WorkspacePlacement.ReadingPitch"/>). The renders run a pass with a Quest 3S's field, which Meta gives as 96 by
    /// 90 degrees, split evenly about forward until a device measures the split.
    /// </summary>
    internal static class FieldChecks
    {
        /// <summary>A Quest 3S, split evenly: Meta's 96 by 90 degrees.</summary>
        internal static readonly ViewField Quest3S = new ViewField(48, 48, 45, 45);

        /// <summary>The four corners of a button, in the world.</summary>
        internal static IEnumerable<Vector3> Corners(GlazeButton button)
        {
            var half = new Vector2(button.Width, GlazeButton.HeightOf(false)) / 2f;
            return Corners(button.transform, half);
        }

        /// <summary>The four corners of a panel frame's plate, in the world.</summary>
        internal static IEnumerable<Vector3> Corners(PanelFrame frame) => Corners(frame.transform, frame.Size / 2f);

        private static IEnumerable<Vector3> Corners(Transform transform, Vector2 half)
        {
            foreach (var x in new[] { -1f, 1f })
            {
                foreach (var y in new[] { -1f, 1f }) yield return transform.TransformPoint(new Vector3(x * half.x, y * half.y, 0f));
            }
        }

        /// <summary>How far above eye level <paramref name="point"/> stands as seen from <paramref name="eyes"/>, in degrees: a panel's centre, for its reading pitch.</summary>
        internal static float ElevationOf(Vector3 eyes, Vector3 point)
        {
            var toward = point - eyes;
            return Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;
        }

        /// <summary>Every point inside <paramref name="field"/> less its margin, as the head sees it (<see cref="GlazeChecks.InsideField"/>).</summary>
        internal static IEnumerable<string> Inside(string what, IEnumerable<Vector3> points, Vector3 eyes, Vector3 toward, float pitchDown, ViewField field) =>
            GlazeChecks.InsideField(what, points, eyes, toward, pitchDown, field);
    }
}
