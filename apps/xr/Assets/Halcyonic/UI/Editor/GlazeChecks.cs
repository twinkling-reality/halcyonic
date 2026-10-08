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
        /// A part of a composition on one plane (ADR 0026), as the plane checks take it: its name, the
        /// transform whose XY plane it lies on, facing along its forward with its centre on its origin,
        /// and its size there in meters.
        /// </summary>
        public readonly struct PlaneShape
        {
            public PlaneShape(string name, Transform root, Vector2 size)
            {
                Name = name;
                Root = root;
                Size = size;
            }

            public string Name { get; }

            public Transform Root { get; }

            public Vector2 Size { get; }
        }

        /// <summary>The centre of a composition: the middle of its parts' corners, in the world.</summary>
        public static Vector3 CompositionCenter(IEnumerable<PlaneShape> parts)
        {
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var part in parts)
            {
                foreach (var x in new[] { -0.5f, 0.5f })
                {
                    foreach (var y in new[] { -0.5f, 0.5f })
                    {
                        var corner = part.Root.position + part.Root.right * (x * part.Size.x) + part.Root.up * (y * part.Size.y);
                        min = Vector3.Min(min, corner);
                        max = Vector3.Max(max, corner);
                    }
                }
            }
            return (min + max) / 2f;
        }

        /// <summary>
        /// A composition's parts lie on one plane facing the eyes (ADR 0026): every part turned as the
        /// first within 0.05 degrees and within half a millimetre of its plane; none rolled, its right
        /// level; the plane square to the line from <paramref name="eyes"/> to the composition's centre
        /// within half a degree; no two parts closer on the plane than a degree at its distance, 8 mm at
        /// 0.46 m; and the columns starting on one line and ending on one line, within half a millimetre.
        /// </summary>
        public static IEnumerable<string> OnePlane(IReadOnlyList<IReadOnlyList<PlaneShape>> columns, Vector3 eyes, string what)
        {
            var parts = columns.SelectMany(column => column).ToList();
            if (parts.Count == 0) yield break;
            var plane = parts[0].Root;
            foreach (var part in parts)
            {
                var turned = Vector3.Angle(part.Root.forward, plane.forward);
                if (turned > 0.05f) yield return what + ": " + part.Name + " is turned " + Degrees(turned) + " degrees against " + parts[0].Name + "; every part shares one plane.";
                var off = Vector3.Dot(part.Root.position - plane.position, plane.forward);
                if (Mathf.Abs(off) > 0.0005f) yield return what + ": " + part.Name + " stands " + Millimetres(off) + " mm off the plane.";
                if (Mathf.Abs(part.Root.right.y) > 0.001f) yield return what + ": " + part.Name + " is rolled; its rows run level.";
            }
            var center = CompositionCenter(parts);
            var square = Vector3.Angle(plane.forward, center - eyes);
            if (square > 0.5f) yield return what + ": the plane is " + Degrees(square) + " degrees off square to the eyes at its centre; it faces them there.";

            // Apart on the plane: the distance between two rectangles in the plane's own right and up.
            var gap = Glaze.MetersAt(GapDegrees, PlaneDistance(eyes, plane));
            for (var a = 0; a < parts.Count; a++)
            {
                for (var b = a + 1; b < parts.Count; b++)
                {
                    var offset = parts[b].Root.position - parts[a].Root.position;
                    var across = Mathf.Abs(Vector3.Dot(offset, plane.right)) - (parts[a].Size.x + parts[b].Size.x) / 2f;
                    var down = Mathf.Abs(Vector3.Dot(offset, plane.up)) - (parts[a].Size.y + parts[b].Size.y) / 2f;
                    var apart = across > 0f && down > 0f ? Mathf.Sqrt(across * across + down * down) : Mathf.Max(across, down);
                    if (apart < gap - 0.0001f)
                    {
                        yield return what + ": " + parts[a].Name + " and " + parts[b].Name + " are " + Millimetres(apart) + " mm apart on the plane; parts keep "
                            + Millimetres(gap) + ", a degree.";
                    }
                }
            }

            // Edges aligned: every column's first part starts on one line and its last ends on one line.
            float Along(PlaneShape part, float side) => Vector3.Dot(part.Root.position - plane.position, plane.up) + side * part.Size.y / 2f;
            var tops = columns.Where(column => column.Count > 0).Select(column => Along(column[0], 1f)).ToList();
            var bottoms = columns.Where(column => column.Count > 0).Select(column => Along(column[column.Count - 1], -1f)).ToList();
            if (tops.Max() - tops.Min() > 0.0005f) yield return what + ": the columns' tops are " + Millimetres(tops.Max() - tops.Min()) + " mm apart; they start on one line.";
            if (bottoms.Max() - bottoms.Min() > 0.0005f) yield return what + ": the columns' bottoms are " + Millimetres(bottoms.Max() - bottoms.Min()) + " mm apart; they end on one line.";
        }

        /// <summary>
        /// Type only steps down in each column of a composition (ADR 0026): its labels of words, icons
        /// left out, grouped into rows where they overlap along the plane's up, a row's size its
        /// largest em in degrees at its plane's distance; from the top, no row more than a hundredth
        /// of a degree larger than the row above. The one exception: a state pill on the column's
        /// subject, the split header's, reads with that subject.
        /// </summary>
        public static IEnumerable<string> TypeStepsDown(IReadOnlyList<IReadOnlyList<PlaneShape>> columns, Vector3 eyes, string what)
        {
            foreach (var column in columns)
            {
                if (column.Count == 0) continue;
                var plane = column[0].Root;
                var labels = new List<(string Name, float Top, float Bottom, float Size)>();
                foreach (var label in column.SelectMany(part => part.Root.GetComponentsInChildren<TMP_Text>(false)))
                {
                    if (string.IsNullOrEmpty(label.text) || GlazeIcons.IsIcon(label)) continue;
                    if (label.GetComponentInParent<StateBadgeView>() != null && label.transform.IsChildOf(column[0].Root)) continue;
                    if (label.textInfo == null || label.textInfo.characterCount == 0) label.ForceMeshUpdate();
                    if (label.textInfo == null || label.textInfo.characterCount == 0) continue;
                    var bounds = label.textBounds;
                    float Up(float y) => Vector3.Dot(label.transform.TransformPoint(new Vector3(bounds.center.x, y, 0f)) - plane.position, plane.up);
                    var size = Glaze.DegreesOf(label.fontSize * 0.1f * label.transform.lossyScale.y, PlaneDistance(eyes, label.transform));
                    labels.Add((PathOf(label.transform), Up(bounds.max.y), Up(bounds.min.y), size));
                }
                var rows = new List<(string Name, float Top, float Bottom, float Size)>();
                foreach (var label in labels.OrderByDescending(label => label.Top))
                {
                    var row = rows.FindIndex(existing => label.Top > existing.Bottom && label.Bottom < existing.Top);
                    if (row < 0)
                    {
                        rows.Add(label);
                        continue;
                    }
                    var was = rows[row];
                    rows[row] = (was.Size >= label.Size ? was.Name : label.Name, Mathf.Max(was.Top, label.Top), Mathf.Min(was.Bottom, label.Bottom), Mathf.Max(was.Size, label.Size));
                }
                rows.Sort((a, b) => b.Top.CompareTo(a.Top));
                for (var index = 1; index < rows.Count; index++)
                {
                    if (rows[index].Size > rows[index - 1].Size + 0.01f)
                    {
                        yield return what + ": " + rows[index].Name + " (" + rows[index].Size.ToString("0.000", CultureInfo.InvariantCulture) + " degrees) stands under the smaller "
                            + rows[index - 1].Name + " (" + rows[index - 1].Size.ToString("0.000", CultureInfo.InvariantCulture) + "); type only steps down.";
                    }
                }
            }
        }

        /// <summary>
        /// One selection treatment (ADR 0026), by the role each shape's component gives it
        /// (<see cref="Surface.Selection"/>): what is chosen is the lit fill, white at
        /// <see cref="Glaze.Menu.LitFillOpacity"/>, with the lit frame; what is pointed at is the fainter
        /// frame alone; no shape is filled with the accent but the main action's cap; and no shape
        /// thinner than 0.2 degrees is drawn over 20 percent opaque, which rules out bars and underlines
        /// marking anything.
        /// </summary>
        public static IEnumerable<string> OneSelectionTreatment(IEnumerable<Surface> shapes, Vector3 eyes, string what)
        {
            var accent = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Accent).Strong);
            var lit = new Color(1f, 1f, 1f, Glaze.Menu.LitFillOpacity);
            var litEdge = new Color(1f, 1f, 1f, Glaze.Menu.LitFrameOpacity);
            var pointedEdge = new Color(1f, 1f, 1f, Glaze.Menu.PointedFrameOpacity);
            bool Same(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 0.01f;
            foreach (var shape in shapes)
            {
                if (!shape.isActiveAndEnabled) continue;
                var name = PathOf(shape.transform);
                var framed = shape.EdgeWidth > 0f && shape.Edge.a > 0f;
                switch (shape.Selection)
                {
                    case SurfaceSelection.Lit:
                        if (!framed || shape.Fill.a <= 0f) yield return what + ": " + name + " is chosen without its lit fill and frame; there is one selection treatment.";
                        else if (!Same(shape.Fill, lit) || !Same(shape.Edge, litEdge))
                        {
                            yield return what + ": " + name + " is lit otherwise than the lit fill and frame; there is one selection treatment.";
                        }
                        break;
                    case SurfaceSelection.Pointed:
                        if (!framed || shape.Fill.a > 0f) yield return what + ": " + name + " is pointed at with more than a frame; pointed at is the frame alone.";
                        else if (!Same(shape.Edge, pointedEdge)) yield return what + ": " + name + " is framed otherwise than the pointed frame.";
                        break;
                }
                var fill = shape.Fill;
                var accented = Mathf.Abs(fill.r - accent.r) + Mathf.Abs(fill.g - accent.g) + Mathf.Abs(fill.b - accent.b) < 0.05f && fill.a > 0.3f;
                if (accented && shape.Selection != SurfaceSelection.MainCap) yield return what + ": " + name + " is filled with the accent, which marks only the main action's cap.";
                var scale = shape.transform.lossyScale;
                var thinnest = Glaze.DegreesOf(Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y)), PlaneDistance(eyes, shape.transform));
                if (thinnest < 0.2f && fill.a > 0.2f) yield return what + ": " + name + " is a bar, " + thinnest.ToString("0.00", CultureInfo.InvariantCulture) + " degrees thin; nothing is marked by a bar or an underline.";
            }
        }

        /// <summary>
        /// Every point inside <paramref name="field"/> less <see cref="ViewField.EdgeMarginDegrees"/>,
        /// seen from <paramref name="eyes"/> with the head turned toward <paramref name="toward"/> and
        /// pitched <paramref name="pitchDown"/> degrees down, as <c>FieldChecks</c> takes a stage and the
        /// gallery a composition. Logs the lowest point, in the head's view.
        /// </summary>
        public static IEnumerable<string> InsideField(string what, IEnumerable<Vector3> points, Vector3 eyes, Vector3 toward, float pitchDown, ViewField field)
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

        /// <summary>
        /// How far a flat part's outline stands from <paramref name="other"/>, in degrees as the eyes see
        /// them: its edges sampled where they lie, so a plane far below eye level, whose low corners spread
        /// wider than its top, is measured as it looks, not by the box round it. Negative where they
        /// overlap, as <see cref="Extent.Apart"/>.
        /// </summary>
        public static float OutlineApart(PlaneShape part, Vector3 eyes, Extent other)
        {
            const int Steps = 40;
            var corners = new[] { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };
            var least = float.MaxValue;
            var seen = new List<Vector2>();
            for (var edge = 0; edge < 4; edge++)
            {
                for (var step = 0; step < Steps; step++)
                {
                    var at = Vector2.Lerp(corners[edge], corners[(edge + 1) % 4], step / (float)Steps);
                    var toward = part.Root.position + part.Root.right * (at.x * part.Size.x) + part.Root.up * (at.y * part.Size.y) - eyes;
                    var yaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
                    var elevation = Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;
                    seen.Add(new Vector2(yaw, elevation));
                    least = Mathf.Min(least, Mathf.Max(Mathf.Max(other.Left - yaw, yaw - other.Right), Mathf.Max(other.Bottom - elevation, elevation - other.Top)));
                }
            }
            // The other inside the outline altogether: as far inside as its middle is.
            var middle = new Vector2((other.Left + other.Right) / 2f, (other.Bottom + other.Top) / 2f);
            var inside = false;
            for (int a = 0, b = seen.Count - 1; a < seen.Count; b = a++)
            {
                if ((seen[a].y > middle.y) != (seen[b].y > middle.y) && middle.x < (seen[b].x - seen[a].x) * (middle.y - seen[a].y) / (seen[b].y - seen[a].y) + seen[a].x) inside = !inside;
            }
            return inside ? -Mathf.Abs(least) : least;
        }

        /// <summary>
        /// A content surface's light ends above its first target (ADR 0026): no target whose top stands
        /// higher than <paramref name="glass"/>'s top less its light's reach, so no row looks lit but the
        /// chosen one.
        /// </summary>
        public static IEnumerable<string> GlowEndsAboveTargets(Surface glass, IEnumerable<GlazeButton> targets, string what)
        {
            var space = glass.transform.parent;
            var line = glass.transform.localPosition.y + glass.Size.y / 2f - glass.GlowReach;
            foreach (var target in targets)
            {
                if (!target.gameObject.activeInHierarchy || !target.transform.IsChildOf(space)) continue;
                var top = space.InverseTransformPoint(target.transform.TransformPoint(new Vector3(0f, target.Size.y / 2f, 0f))).y;
                if (top > line + 1e-5f)
                {
                    yield return what + ": " + PathOf(target.transform) + " reaches " + Millimetres((top - line) * space.lossyScale.y) + " mm into "
                        + PathOf(glass.transform) + "'s light; the light ends in the top padding, above the first row.";
                }
            }
        }

        private static string Millimetres(float meters) => (meters * 1000f).ToString("0.0", CultureInfo.InvariantCulture);

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
        /// What <paramref name="step"/> allocates, called for each of <paramref name="steps"/> steps in three
        /// tries, each step counted on its own by Unity's count of allocations. That count takes in the
        /// editor's other threads, which once counted bytes for a motion that allocates none, and this
        /// editor's Mono counts nothing for one thread alone (both seen 2026-10-07), so only a step that
        /// allocated in every try is the step's own: another thread's allocation does not come again at
        /// the same step. A thread that allocates without pause, as the editor importing after files change
        /// (seen 2026-10-07, 38 of 60 frames at a load over 100), can still land in every try, so a round that
        /// finds any step allocating is measured again after a pause, up to three rounds: what the step
        /// allocates comes back in every round. Returns the quietest round's steps that allocated in every try
        /// and in some, and its quietest try's bytes on every thread; null where this editor counts no
        /// allocation at all.
        /// </summary>
        public static (int Every, int Some, long Bytes)? Allocations(Action<int> step, int steps)
        {
            const int Tries = 3;
            using var calls = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocation In Frame Count");
            using var allocated = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            var allocating = new int[steps];
            for (var index = 0; index < 4; index++) step(index % steps);
            var probe = calls.CurrentValue;
            var kept = new byte[256];
            GC.KeepAlive(kept);
            if (calls.CurrentValue - probe < 1) return null;
            (int Every, int Some, long Bytes)? quietest = null;
            for (var round = 0; round < 3; round++)
            {
                if (round > 0) System.Threading.Thread.Sleep(250);
                Array.Clear(allocating, 0, steps);
                var bytes = long.MaxValue;
                for (var attempt = 0; attempt < Tries; attempt++)
                {
                    var before = allocated.CurrentValue;
                    for (var index = 0; index < steps; index++)
                    {
                        var count = calls.CurrentValue;
                        step(index);
                        if (calls.CurrentValue > count) allocating[index]++;
                    }
                    bytes = Math.Min(bytes, allocated.CurrentValue - before);
                }
                var found = (allocating.Count(tries => tries == Tries), allocating.Count(tries => tries > 0), bytes);
                if (quietest == null || found.Item1 < quietest.Value.Every) quietest = found;
                if (quietest.Value.Every == 0) break;
            }
            return quietest;
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
            // A word beside it: a label of its own parent that shows words, level with it and no more than
            // an em away as the eyes see the gap (IconGapDegrees); seen as yaw, a gap far below eye level
            // would look wider than it is.
            var plane = icon.transform.parent;
            var beside = false;
            var nearest = "";
            foreach (Transform sibling in plane)
            {
                if (sibling == icon.transform || !sibling.gameObject.activeInHierarchy) continue;
                if (!sibling.TryGetComponent<TMP_Text>(out var word) || GlazeIcons.IsIcon(word) || string.IsNullOrWhiteSpace(word.text)) continue;
                if (!(IconGapDegrees(icon, word, eyes) is float gap)) continue;
                var near = gap <= degrees;
                beside |= near;
                nearest += " " + sibling.name + " (" + Degrees(gap) + " degrees away)";
            }
            if (!beside) yield return name + " " + Of("the icon", eyes, new[] { icon.GetComponent<Renderer>() }) + " has no words beside it:" + (nearest.Length > 0 ? nearest : " none") + ".";
        }

        /// <summary>
        /// The gap between an icon and a word beside it as the eyes see it: the angle between the rays to
        /// the icon's and the word's nearest edges, level with both, measured where their heights overlap;
        /// 0 or less where they overlap across; null where they are not level, sharing no height.
        /// </summary>
        public static float? IconGapDegrees(TMP_Text icon, TMP_Text word, Vector3 eyes)
        {
            var plane = icon.transform.parent;
            var seen = OnPlane(icon, plane);
            var words = OnPlane(word, plane);
            if (words.width <= 0f || seen.width <= 0f) return null;
            var bottom = Mathf.Max(words.yMin, seen.yMin);
            var top = Mathf.Min(words.yMax, seen.yMax);
            if (bottom >= top) return null;
            var middle = (bottom + top) / 2f;
            var (near, far) = words.xMin >= seen.xMax ? (seen.xMax, words.xMin) : words.xMax <= seen.xMin ? (seen.xMin, words.xMax) : (0f, 0f);
            if (near == far) return 0f;
            var a = plane.TransformPoint(new Vector3(near, middle, 0f)) - eyes;
            var b = plane.TransformPoint(new Vector3(far, middle, 0f)) - eyes;
            return Vector3.Angle(a, b);
        }

        /// <summary>The rectangle a label's mesh covers on the plane of <paramref name="plane"/>, in that plane's units.</summary>
        private static Rect OnPlane(TMP_Text label, Transform plane)
        {
            if (!label.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null || filter.sharedMesh.bounds.size == Vector3.zero) return Rect.zero;
            var bounds = filter.sharedMesh.bounds;
            float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            for (var corner = 0; corner < 4; corner++)
            {
                var local = bounds.center + Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, 0f));
                var point = plane.InverseTransformPoint(label.transform.TransformPoint(local));
                left = Mathf.Min(left, point.x);
                right = Mathf.Max(right, point.x);
                bottom = Mathf.Min(bottom, point.y);
                top = Mathf.Max(top, point.y);
            }
            return Rect.MinMaxRect(left, bottom, right, top);
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

        /// <summary>The WCAG 2 contrast between two sRGB colours, from 1 to 21.</summary>
        public static float Contrast(Color a, Color b)
        {
            var (x, y) = (Luminance(a), Luminance(b));
            return (float)((System.Math.Max(x, y) + 0.05) / (System.Math.Min(x, y) + 0.05));
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
