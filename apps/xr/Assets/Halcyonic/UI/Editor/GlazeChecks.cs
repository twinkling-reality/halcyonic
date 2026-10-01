#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI.Editor
{
    /// <summary>
    /// The interface's rules (ADR 0023), checked on what a render built: nothing overlaps as seen
    /// from the eyes, every word is large enough at its own distance, plates are opaque enough to
    /// read over a bright room, every state badge shows its whole word, and colours reach the
    /// render as authored, with text that contrasts with its plate. Each check returns its failures
    /// as sentences; a render fails when any does. The token tests in the client core hold the
    /// colours to their contrast; these hold what is drawn to the tokens.
    /// </summary>
    public static class GlazeChecks
    {
        /// <summary>The angle kept between things that must not touch.</summary>
        public const float GapDegrees = 1f;

        /// <summary>Levels of 255 a pixel may differ from its token, for rounding and filtering.</summary>
        private const float ColourTolerance = 3f / 255f;

        /// <summary>
        /// What something covers as seen from the eyes: yaw from left to right and elevation from
        /// bottom to top, in degrees.
        /// </summary>
        public readonly struct Extent
        {
            public Extent(string name, float left, float right, float bottom, float top)
            {
                Name = name;
                Left = left;
                Right = right;
                Bottom = bottom;
                Top = top;
            }

            public string Name { get; }

            public float Left { get; }

            public float Right { get; }

            public float Bottom { get; }

            public float Top { get; }

            public bool IsEmpty => Right < Left || Top < Bottom;

            /// <summary>How far apart two extents are: the larger of their horizontal and vertical gaps, negative when they overlap.</summary>
            public float Apart(Extent other) => Mathf.Max(Mathf.Max(other.Left - Right, Left - other.Right), Mathf.Max(other.Bottom - Top, Bottom - other.Top));

            public override string ToString() =>
                Name + " (" + Degrees(Left) + " to " + Degrees(Right) + " across, " + Degrees(Bottom) + " to " + Degrees(Top) + " up)";
        }

        /// <summary>
        /// What the meshes that draw now cover, seen from <paramref name="eyes"/>: from each mesh's own
        /// corners, so a plate turned toward the eyes measures as it looks, not as its bounds in the
        /// world, which grow as it turns.
        /// </summary>
        public static Extent Of(string name, Vector3 eyes, IEnumerable<Renderer> renderers)
        {
            float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!renderer.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null) continue;
                var bounds = filter.sharedMesh.bounds;
                if (bounds.size == Vector3.zero) continue;
                for (var corner = 0; corner < 8; corner++)
                {
                    var local = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    var toward = renderer.transform.TransformPoint(local) - eyes;
                    var level = Mathf.Max(new Vector2(toward.x, toward.z).magnitude, 1e-4f);
                    var yaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                    var elevation = Mathf.Atan2(toward.y, level) * Mathf.Rad2Deg;
                    left = Mathf.Min(left, yaw);
                    right = Mathf.Max(right, yaw);
                    bottom = Mathf.Min(bottom, elevation);
                    top = Mathf.Max(top, elevation);
                }
            }
            return new Extent(name, left, right, bottom, top);
        }

        public static Extent Of(string name, Vector3 eyes, GameObject root) => Of(name, eyes, root.GetComponentsInChildren<Renderer>(false));

        /// <summary>
        /// What something round covers, seen from <paramref name="eyes"/>: as far as its half width
        /// to each side and its half height up and down, across the line of sight, as a body is seen.
        /// </summary>
        public static Extent Facing(string name, Vector3 eyes, Vector3 center, float halfWidth, float halfHeight)
        {
            var toward = center - eyes;
            var right = Vector3.Cross(Vector3.up, toward).normalized;
            var up = Vector3.Cross(toward, right).normalized;
            float left = float.MaxValue, rightmost = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            foreach (var point in new[] { center - right * halfWidth, center + right * halfWidth, center - up * halfHeight, center + up * halfHeight })
            {
                var to = point - eyes;
                var level = Mathf.Max(new Vector2(to.x, to.z).magnitude, 1e-4f);
                var yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                var elevation = Mathf.Atan2(to.y, level) * Mathf.Rad2Deg;
                left = Mathf.Min(left, yaw);
                rightmost = Mathf.Max(rightmost, yaw);
                bottom = Mathf.Min(bottom, elevation);
                top = Mathf.Max(top, elevation);
            }
            return new Extent(name, left, rightmost, bottom, top);
        }

        /// <summary>Every pair of extents is at least <paramref name="gap"/> degrees apart.</summary>
        public static IEnumerable<string> Apart(IReadOnlyList<Extent> extents, float gap = GapDegrees)
        {
            for (var a = 0; a < extents.Count; a++)
            {
                if (extents[a].IsEmpty) continue;
                for (var b = a + 1; b < extents.Count; b++)
                {
                    if (extents[b].IsEmpty) continue;
                    var apart = extents[a].Apart(extents[b]);
                    if (apart < gap - 1e-3f) yield return extents[a] + " and " + extents[b] + " are " + Degrees(apart) + " degrees apart, under " + Degrees(gap) + ".";
                }
            }
        }

        /// <summary>
        /// Every label that shows text has an em of at least <paramref name="minimumDegrees"/> as seen
        /// from <paramref name="eyes"/>: the caption's size unless a role asks for more.
        /// </summary>
        public static IEnumerable<string> TextLargeEnough(GameObject root, Vector3 eyes, string what, float minimumDegrees = Glaze.CaptionDegrees)
        {
            var smallest = float.MaxValue;
            var smallestName = "";
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var em = label.fontSize * 0.1f * label.transform.lossyScale.y;
                var degrees = Glaze.DegreesOf(em, Vector3.Distance(eyes, label.transform.position));
                if (degrees < smallest)
                {
                    smallest = degrees;
                    smallestName = PathOf(label.transform);
                }
                if (degrees < minimumDegrees - 0.005f)
                {
                    yield return what + ": " + PathOf(label.transform) + "'s em is " + degrees.ToString("0.000", CultureInfo.InvariantCulture)
                        + " degrees, under " + minimumDegrees.ToString("0.000", CultureInfo.InvariantCulture) + ".";
                }
            }
            if (smallest < float.MaxValue)
            {
                Debug.Log("Halcyonic: " + what + ": the smallest text is " + smallestName + ", " + smallest.ToString("0.000", CultureInfo.InvariantCulture) + " degrees.");
            }
        }

        /// <summary>
        /// Every button is at least as tall and as wide as its size asks, 60 dp, or 48 dp compact, as
        /// seen from <paramref name="eyes"/>, and so is the target a hand points at.
        /// </summary>
        public static IEnumerable<string> TargetsLargeEnough(IEnumerable<GlazeButton> buttons, Vector3 eyes, string what)
        {
            foreach (var button in buttons)
            {
                if (!button.gameObject.activeInHierarchy) continue;
                var distance = Vector3.Distance(eyes, button.transform.position);
                var scale = button.transform.lossyScale;
                var tall = Glaze.DegreesOf(button.Size.y * scale.y, distance);
                var wide = Glaze.DegreesOf(button.Size.x * scale.x, distance);
                var least = Mathf.Min(tall, wide);
                var asked = button.Size.y < GlazeButton.HeightOf(false) - 1e-5f ? Glaze.MinimumTargetDegrees : Glaze.TargetDegrees;
                // The press shrinks a button for a moment, and a target a little more than its plate counts as its plate.
                if (least < asked * 0.96f - 0.01f)
                {
                    yield return what + ": " + PathOf(button.transform) + " is " + Degrees(wide) + " by " + Degrees(tall) + " degrees, under " + Degrees(asked) + ".";
                }
            }
        }

        /// <summary>No label shows its text cut short: Halcyonic's own words, on buttons, badges and tags, show whole.</summary>
        public static IEnumerable<string> NothingCut(IEnumerable<TMP_Text> labels, string what)
        {
            foreach (var label in labels)
            {
                if (!label.gameObject.activeInHierarchy || string.IsNullOrEmpty(label.text)) continue;
                label.ForceMeshUpdate();
                if (label.isTextTruncated) yield return what + ": " + PathOf(label.transform) + " cuts \"" + label.text + "\" short.";
            }
        }

        /// <summary>Every plate is at least as opaque as the plate token, so a bright room cannot wash its text out.</summary>
        public static IEnumerable<string> PlatesOpaque(IEnumerable<Surface> plates, string what)
        {
            foreach (var plate in plates)
            {
                if (plate.Fill.a < Glaze.PlateOpacity - 1e-3f) yield return what + ": " + PathOf(plate.transform) + " is " + Degrees(plate.Fill.a * 100f) + " percent opaque.";
            }
        }

        /// <summary>Every badge shows its state's whole word, with any count: never a colour alone, never a word cut short.</summary>
        public static IEnumerable<string> BadgesSayTheirState(IEnumerable<StateBadgeView> badges, string what)
        {
            foreach (var badge in badges)
            {
                if (badge.Shown == null) continue;
                var word = badge.Word;
                word.ForceMeshUpdate();
                var shown = new string(Enumerable.Range(0, word.textInfo.characterCount).Select(index => word.textInfo.characterInfo[index].character).ToArray());
                if (word.isTextTruncated || shown != badge.Shown.Text) yield return what + ": a badge shows \"" + shown + "\" for \"" + badge.Shown.Text + "\".";
            }
        }

        /// <summary>
        /// A pixel of a render shows a token as authored, drawn at <paramref name="alpha"/> over black:
        /// colours reach the render unchanged by the colour space or the shader.
        /// </summary>
        public static IEnumerable<string> AsAuthored(Texture2D render, Vector2Int pixel, GlazeColor token, float alpha, string what)
        {
            var expected = Over(GlazeTokens.ColorOf(token), alpha, Color.black);
            var actual = render.GetPixel(pixel.x, pixel.y);
            var difference = Mathf.Max(Mathf.Abs(actual.r - expected.r), Mathf.Max(Mathf.Abs(actual.g - expected.g), Mathf.Abs(actual.b - expected.b)));
            if (difference > ColourTolerance)
            {
                yield return what + ": the pixel at " + pixel + " is #" + ColorUtility.ToHtmlStringRGB(actual) + ", not #" + ColorUtility.ToHtmlStringRGB(expected)
                    + " (" + token + " at " + Degrees(alpha * 100f) + " percent).";
            }
        }

        /// <summary>
        /// The strongest contrast a pixel inside <paramref name="rect"/> reaches against
        /// <paramref name="surface"/>: what a label's strokes reach on what they sit on, as the render
        /// drew them, light on dark or dark on light.
        /// </summary>
        public static float Contrast(Texture2D render, RectInt rect, Color surface)
        {
            var background = Luminance(surface);
            var strongest = 1.0;
            for (var y = Mathf.Max(0, rect.yMin); y < Mathf.Min(render.height, rect.yMax); y++)
            {
                for (var x = Mathf.Max(0, rect.xMin); x < Mathf.Min(render.width, rect.xMax); x++)
                {
                    var pixel = Luminance(render.GetPixel(x, y));
                    strongest = System.Math.Max(strongest, (System.Math.Max(pixel, background) + 0.05) / (System.Math.Min(pixel, background) + 0.05));
                }
            }
            return (float)strongest;
        }

        /// <summary>A colour drawn at an opacity over another, blended linearly as the project blends.</summary>
        public static Color Over(Color colour, float alpha, Color under)
        {
            var mixed = Color.Lerp(under.linear, colour.linear, alpha);
            return mixed.gamma;
        }

        /// <summary>The WCAG 2 relative luminance of an sRGB colour.</summary>
        private static double Luminance(Color colour) =>
            new GlazeColor((byte)Mathf.RoundToInt(colour.r * 255f), (byte)Mathf.RoundToInt(colour.g * 255f), (byte)Mathf.RoundToInt(colour.b * 255f)).Luminance;

        public static string Degrees(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);

        private static string PathOf(Transform transform)
        {
            var path = transform.name;
            for (var parent = transform.parent; parent != null && parent.parent != null; parent = parent.parent) path = parent.name + "/" + path;
            return path;
        }
    }
}
