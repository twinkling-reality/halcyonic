#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A meter (ADR 0023): a share drawn as a slim bar on its track, a picture of words beside it and
    /// never in their place. The share it draws is an upper bound, as usage left's "at most" is, so
    /// the bar's end is open, its last stretch in dots, and never reads as exact. While what it
    /// measures is read again it shows only its track. As wide wherever it stands, so meters compare
    /// at a glance. Built in units of its distance from the eyes, centred on its transform.
    /// </summary>
    public sealed class MeterView : MonoBehaviour
    {
        public const float WidthDegrees = 14f;
        public const float HeightDegrees = 0.5f;
        private const float RadiusDegrees = 0.15f;
        private const float EdgeDegrees = 0.05f;

        /// <summary>The open end: this much of the bar's end, or all of a shorter bar, in dots at this pitch.</summary>
        private const float OpenDegrees = 1.5f;
        private const float DotDegrees = 0.2f;

        private Surface track = null!;
        private Surface fill = null!;
        private Surface end = null!;

        public static float Width => GlazeTokens.Units(WidthDegrees);

        public static float Height => GlazeTokens.Units(HeightDegrees);

        /// <summary>How much of the track is filled, the open end included, in its units, for checks.</summary>
        public float Filled { get; private set; }

        public static MeterView Create(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var meter = go.AddComponent<MeterView>();
            meter.track = Surface.Create(go.transform, "Track", order);
            meter.fill = Surface.Create(go.transform, "Fill", order + 1);
            meter.end = Surface.Create(go.transform, "Open end", order + 1);
            return meter;
        }

        /// <summary>Draws <paramref name="share"/>, from 0 to 1, or only the track while <paramref name="waiting"/>.</summary>
        public void Show(float share, bool waiting)
        {
            var width = Width;
            var height = Height;
            var radius = GlazeTokens.Units(RadiusDegrees);
            track.Draw(new Vector2(width, height), radius, GlazeTokens.ColorOf(Glaze.Well), GlazeTokens.ColorOf(Glaze.Outline, 0.6f), GlazeTokens.Units(EdgeDegrees));
            track.transform.localPosition = new Vector3(0f, 0f, 0.0002f);
            Filled = waiting ? 0f : Mathf.Clamp01(share) * width;
            var open = Mathf.Min(Filled, GlazeTokens.Units(OpenDegrees));
            var solid = Filled - open;
            var left = -width / 2f;
            fill.gameObject.SetActive(solid > 1e-5f);
            if (solid > 1e-5f)
            {
                fill.Draw(new Vector2(solid, height), Mathf.Min(radius, solid / 2f), GlazeTokens.TextSecondary);
                fill.transform.localPosition = new Vector3(left + solid / 2f, 0f, 0f);
            }
            end.gameObject.SetActive(open > 1e-5f);
            if (open > 1e-5f)
            {
                end.Draw(new Vector2(open, height), Mathf.Min(radius, open / 2f), GlazeTokens.TextSecondary, halftone: GlazeTokens.Units(DotDegrees));
                end.transform.localPosition = new Vector3(left + solid + open / 2f, 0f, 0f);
            }
        }
    }
}
