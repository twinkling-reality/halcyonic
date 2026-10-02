#nullable enable
using System;
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
    /// read over a bright room, every state badge shows its whole word, every icon is one glyph of
    /// the icon atlas with words beside it and no label of words draws from that atlas, and colours
    /// reach the render as authored, with text that contrasts with its plate. Each check returns its failures
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
        /// from <paramref name="eyes"/>, the caption's size unless a role asks for more. Sizes are
        /// measured at the distance of the plane the label lies on, as Meta's dp are: on a flat panel
        /// facing the eyes, a word at the edge is the same size as one in the middle.
        /// </summary>
        public static IEnumerable<string> TextLargeEnough(GameObject root, Vector3 eyes, string what, float minimumDegrees = Glaze.CaptionDegrees)
        {
            var smallest = float.MaxValue;
            var smallestName = "";
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text)) continue;
                var em = label.fontSize * 0.1f * label.transform.lossyScale.y;
                var degrees = Glaze.DegreesOf(em, PlaneDistance(eyes, label.transform));
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

        /// <summary>A label's em as the eyes see it, in degrees, and the share of its designed size that is.</summary>
        public readonly struct SeenText
        {
            public SeenText(string name, float degrees, float share)
            {
                Name = name;
                Degrees = degrees;
                Share = share;
            }

            public string Name { get; }

            public float Degrees { get; }

            public float Share { get; }
        }

        /// <summary>
        /// Every label of words under <paramref name="root"/> as the eyes see it (ADR 0026): the angle
        /// from <paramref name="eyes"/> between the top and the bottom of its em, at its text's middle,
        /// slant included. Where a surface faces the eyes it is the size <see cref="TextLargeEnough"/>
        /// measures; where the eyes meet a surface at a slant, as an upright one below them, it is less,
        /// down to its size times the square of the slant's cosine. Icons are left to
        /// <see cref="IconBesideWord"/>. Smallest first.
        /// </summary>
        public static List<SeenText> TextSeen(GameObject root, Vector3 eyes)
        {
            var seen = new List<SeenText>();
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrEmpty(label.text) || GlazeIcons.IsIcon(label)) continue;
                if (label.textInfo == null || label.textInfo.characterCount == 0) label.ForceMeshUpdate();
                if (label.textInfo == null || label.textInfo.characterCount == 0) continue;
                var em = label.fontSize * 0.1f * label.transform.lossyScale.y;
                var middle = label.transform.TransformPoint(label.textBounds.center);
                var half = label.transform.up * (em / 2f);
                var degrees = Vector3.Angle(middle + half - eyes, middle - half - eyes);
                seen.Add(new SeenText(PathOf(label.transform), degrees, degrees / Glaze.DegreesOf(em, PlaneDistance(eyes, label.transform))));
            }
            seen.Sort((a, b) => a.Degrees.CompareTo(b.Degrees));
            return seen;
        }

        /// <summary>
        /// No text reads under Meta's 14 dp (<see cref="Glaze.MinimumTextDegrees"/>) as the eyes see it
        /// (<see cref="TextSeen"/>), each label failing with how large it looks and what share of its
        /// size that is. <see cref="TextLargeEnough"/> stays beside it: it holds each role to its
        /// size, this holds what the eyes get.
        /// </summary>
        public static IEnumerable<string> TextAsSeen(GameObject root, Vector3 eyes, string what)
        {
            var seen = TextSeen(root, eyes);
            if (seen.Count > 0)
            {
                var shrunk = seen.OrderBy(label => label.Share).First();
                Debug.Log("Halcyonic: " + what + ": as the eyes see it, the smallest text is " + seen[0].Name + " at " + seen[0].Degrees.ToString("0.000", CultureInfo.InvariantCulture)
                    + " degrees; the most shrunk is " + shrunk.Name + " at " + Mathf.RoundToInt(shrunk.Share * 100f) + " percent of its size.");
            }
            foreach (var label in seen)
            {
                if (label.Degrees >= Glaze.MinimumTextDegrees - 0.0005f) break;
                yield return what + ": " + label.Name + " is " + label.Degrees.ToString("0.000", CultureInfo.InvariantCulture) + " degrees as the eyes see it ("
                    + Mathf.RoundToInt(label.Share * 100f) + " percent of its size), under 14 dp, " + Glaze.MinimumTextDegrees.ToString("0.000", CultureInfo.InvariantCulture) + ".";
            }
        }

        /// <summary>
        /// For a surface ADR 0026 replaces, which may read under 14 dp as the eyes see it until it is
        /// rebuilt: logs <see cref="TextAsSeen"/>'s failures as a list (<c>Halcyonic: text as seen ...</c>)
        /// and fails nothing, as the owner chose on 2026-10-02. New surfaces call
        /// <see cref="TextAsSeen"/> itself.
        /// </summary>
        public static void ListTextAsSeen(GameObject root, Vector3 eyes, string what)
        {
            var under = TextAsSeen(root, eyes, what).ToList();
            if (under.Count == 0) return;
            Debug.Log("Halcyonic: text as seen, listed, not failed: " + what + ": " + under.Count + " labels under 14 dp: " + string.Join(" | ", under));
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
                var distance = PlaneDistance(eyes, button.transform);
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

        /// <summary>From the eyes to the plane something lies on, along that plane's normal: the distance its sizes are designed for.</summary>
        public static float PlaneDistance(Vector3 eyes, Transform surface) => Mathf.Abs(Vector3.Dot(surface.position - eyes, surface.forward));

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
        /// Runs a render at the standard text size and again with reading text a step larger
        /// (<see cref="Comfort.LargerTextScale"/>), as the person's comfort settings may ask, the second
        /// pass's renders in a folder of their own ("Larger"): every rule holds at both sizes.
        /// </summary>
        public static List<string> AtEachTextSize(Func<string, List<string>> run)
        {
            var failures = run("");
            Debug.Log("Halcyonic: the same again with reading text a step larger.");
            GlazeText.SetScale(Comfort.LargerTextScale);
            try
            {
                failures.AddRange(run("Larger").Select(failure => "at the larger text size, " + failure));
            }
            finally
            {
                GlazeText.SetScale(1f);
            }
            return failures;
        }

        /// <summary>
        /// Every icon that shows stands on its own label (<see cref="GlazeIcons"/>): exactly one glyph
        /// of the icon set, drawn from the icon atlas, an em of at least
        /// <see cref="GlazeIcons.MinimumDegrees"/> as seen from <paramref name="eyes"/>, and a word
        /// beside it, so an icon never says anything alone.
        /// </summary>
        public static IEnumerable<string> IconsBesideWords(GameObject root, Vector3 eyes, string what)
        {
            var failures = new List<string>();
            var count = 0;
            foreach (var icon in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!GlazeIcons.IsIcon(icon)) continue;
                count++;
                failures.AddRange(IconBesideWord(icon, eyes, what));
            }
            Debug.Log("Halcyonic: " + what + ": checked " + count + " icons, " + failures.Count + " failing.");
            return failures;
        }

        /// <summary>One icon label: one glyph of the set from the atlas, large enough, with a word beside it.</summary>
        public static IEnumerable<string> IconBesideWord(TMP_Text icon, Vector3 eyes, string what)
        {
            var name = what + ": the icon " + PathOf(icon.transform);
            if (icon.text.Length != 1 || GlazeIconGlyphs.All.IndexOf(icon.text[0]) < 0)
            {
                yield return name + " shows " + Codes(icon.text) + ", not one glyph of the icon set.";
                yield break;
            }
            icon.ForceMeshUpdate();
            var info = icon.textInfo;
            if (info.characterCount != 1 || !info.characterInfo[0].isVisible || info.characterInfo[0].fontAsset != GlazeIcons.Font)
            {
                yield return name + " does not draw its glyph from the icon atlas.";
            }
            var em = icon.fontSize * 0.1f * icon.transform.lossyScale.y;
            var degrees = Glaze.DegreesOf(em, PlaneDistance(eyes, icon.transform));
            if (degrees < GlazeIcons.MinimumDegrees - 0.005f)
            {
                yield return name + "'s em is " + degrees.ToString("0.000", CultureInfo.InvariantCulture) + " degrees, under "
                    + GlazeIcons.MinimumDegrees.ToString("0.000", CultureInfo.InvariantCulture) + ".";
            }
            // A word beside it: a label of its own parent that shows words, level with it and no more than an em away.
            var seen = Of("the icon", eyes, new[] { icon.GetComponent<Renderer>() });
            var beside = false;
            var nearest = "";
            foreach (Transform sibling in icon.transform.parent)
            {
                if (sibling == icon.transform || !sibling.gameObject.activeInHierarchy) continue;
                if (!sibling.TryGetComponent<TMP_Text>(out var word) || GlazeIcons.IsIcon(word) || string.IsNullOrWhiteSpace(word.text)) continue;
                var words = Of(sibling.name, eyes, new[] { word.GetComponent<Renderer>() });
                if (words.IsEmpty) continue;
                var level = words.Bottom < seen.Top && seen.Bottom < words.Top;
                var near = Mathf.Max(words.Left - seen.Right, seen.Left - words.Right) <= degrees;
                if (level && near) beside = true;
                nearest += " " + words;
            }
            if (!beside) yield return name + " " + seen + " has no words beside it:" + (nearest.Length > 0 ? nearest : " none") + ".";
        }

        /// <summary>
        /// The microphone shows only on a button that is held, as hold to talk is: never on one that
        /// approves, denies, stops, confirms or does anything else with a single press.
        /// </summary>
        public static IEnumerable<string> MicrophoneOnlyWhereHeld(IEnumerable<GlazeButton> buttons, string what)
        {
            var microphone = GlazeIconGlyphs.Of(GlazeIcon.HoldToTalk);
            foreach (var button in buttons)
            {
                if (!button.gameObject.activeInHierarchy || button.Holds || button.Icon == null || button.Icon.text != microphone) continue;
                yield return what + ": " + PathOf(button.transform) + ", \"" + button.Label.text + "\", shows the microphone but takes a single press.";
            }
        }

        /// <summary>
        /// No label of words draws a character from the icon atlas or shows an icon's glyph, so only an
        /// icon's own label ever draws an icon.
        /// </summary>
        public static IEnumerable<string> NotFromIcons(TMP_Text label, string what)
        {
            if (GlazeIcons.IsIcon(label)) yield break;
            if (GlazeIcons.Font != null && label.font == GlazeIcons.Font)
            {
                yield return what + ": " + PathOf(label.transform) + " is set in the icon font.";
                yield break;
            }
            var info = label.textInfo;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (character.fontAsset == GlazeIcons.Font || GlazeIconGlyphs.All.IndexOf(character.character) >= 0)
                {
                    yield return what + ": " + PathOf(label.transform) + " draws " + Codes(character.character.ToString()) + " from the icon atlas.";
                    yield break;
                }
            }
        }

        /// <summary>
        /// The icon atlas holds every icon the client core names (<see cref="GlazeIcon"/>), stays static,
        /// keeps no font file and falls back to nothing, and no font of words falls back to it.
        /// </summary>
        public static IEnumerable<string> IconAtlasHoldsEveryIcon(string what)
        {
            var atlas = GlazeIcons.Font;
            if (atlas == null)
            {
                yield return what + ": there is no icon atlas at Resources/" + GlazeIcons.ResourcePath + ".";
                yield break;
            }
            foreach (GlazeIcon icon in Enum.GetValues(typeof(GlazeIcon)))
            {
                var glyph = GlazeIconGlyphs.Of(icon);
                if (glyph.Length != 1 || !atlas.characterLookupTable.TryGetValue(glyph[0], out var character) || character.glyph == null
                    || character.glyph.glyphRect.width == 0 || character.glyph.glyphRect.height == 0)
                {
                    yield return what + ": the icon atlas has no glyph for " + icon + ".";
                }
            }
            if (atlas.atlasPopulationMode != AtlasPopulationMode.Static) yield return what + ": the icon atlas is not static.";
            if (atlas.sourceFontFile != null) yield return what + ": the icon atlas refers to its font file.";
            if (atlas.fallbackFontAssetTable != null && atlas.fallbackFontAssetTable.Count > 0) yield return what + ": the icon atlas falls back to another font.";
            if (TMP_Settings.fallbackFontAssets != null && TMP_Settings.fallbackFontAssets.Contains(atlas)) yield return what + ": every font falls back to the icon atlas.";
            if (TMP_Settings.defaultFontAsset == atlas) yield return what + ": the icon atlas is the default font.";
            var fallbacks = new Stack<TMP_FontAsset>();
            var seen = new HashSet<TMP_FontAsset>();
            fallbacks.Push(TMP_Settings.defaultFontAsset);
            while (fallbacks.Count > 0)
            {
                var font = fallbacks.Pop();
                if (font == null || !seen.Add(font) || font.fallbackFontAssetTable == null) continue;
                foreach (var fallback in font.fallbackFontAssetTable)
                {
                    if (fallback == atlas) yield return what + ": " + font.name + " falls back to the icon atlas.";
                    fallbacks.Push(fallback);
                }
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

        /// <summary>Text for a failure's message, every character that is not printable ASCII as its code.</summary>
        private static string Codes(string text)
        {
            var result = new System.Text.StringBuilder();
            foreach (var character in text.Take(100))
            {
                result.Append(character >= 0x20 && character < 0x7F ? character.ToString() : "[U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture) + "]");
            }
            return result.ToString();
        }
    }
}
