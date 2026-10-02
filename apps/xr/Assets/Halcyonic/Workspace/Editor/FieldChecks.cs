#nullable enable
using System.Collections.Generic;
using System.Globalization;
using Halcyonic.Client;
using Halcyonic.XR.UI;
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

        /// <summary>
        /// Every point inside <paramref name="field"/> less <see cref="ViewField.EdgeMarginDegrees"/>,
        /// seen from <paramref name="eyes"/> with the head turned toward <paramref name="toward"/> and
        /// pitched <paramref name="pitchDown"/> degrees down. Logs the lowest point, in the head's view.
        /// </summary>
        internal static IEnumerable<string> Inside(string what, IEnumerable<Vector3> points, Vector3 eyes, Vector3 toward, float pitchDown, ViewField field)
        {
            var margin = ViewField.EdgeMarginDegrees;
            // A hundredth of a degree for rounding: a plate placed at its lowest has its corners on the edge.
            var tolerance = margin - 0.01;
            var shrunk = new ViewField(field.Left - tolerance, field.Right - tolerance, field.Up - tolerance, field.Down - tolerance);
            var flat = new Vector3(toward.x - eyes.x, 0f, toward.z - eyes.z);
            var yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
            var head = Quaternion.Euler(pitchDown, yaw, 0f);
            var outside = 0;
            var lowest = 90f;
            var widest = 0f;
            foreach (var point in points)
            {
                var local = Quaternion.Inverse(head) * (point - eyes);
                var across = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                var up = Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg;
                lowest = Mathf.Min(lowest, up);
                widest = Mathf.Max(widest, Mathf.Abs(across));
                if (shrunk.Shows(across, up)) continue;
                outside++;
                Debug.Log("Halcyonic: field check " + what + ": a corner at " + Degrees(across) + " across and " + Degrees(up) + " up is outside.");
            }
            Debug.Log("Halcyonic: field check " + what + ": lowest point " + Degrees(-lowest) + " degrees below the view's middle, widest "
                + Degrees(widest) + " to the side, looking " + Degrees(pitchDown) + " down; the field less its margin reaches "
                + Degrees((float)shrunk.Down) + " down and " + Degrees((float)shrunk.Right) + " across.");
            if (outside > 0) yield return what + ": " + outside + " corners lie outside the field less its margin.";
        }

        private static string Degrees(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
