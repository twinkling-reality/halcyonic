#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>DESIGN's glyphs for the redesign's actions (BUILD-SPEC-v4 1.4's table), by what each shows.</summary>
    public enum RoomGlyph
    {
        /// <summary>Arrow up: Send Instruction, Send Answer.</summary>
        Send,

        /// <summary>Microphone: Hold to Talk, Talk Again.</summary>
        Microphone,

        /// <summary>Keyboard: Type.</summary>
        Keyboard,

        /// <summary>Filled square: Stop.</summary>
        Stop,

        /// <summary>Check: Approve, and Confirm of an approval.</summary>
        Check,

        /// <summary>Barred circle: Deny, and Confirm of a denial.</summary>
        Barred,

        /// <summary>Cross: Cancel.</summary>
        Cross,

        /// <summary>Cube: Make VR Version.</summary>
        Cube,

        /// <summary>Plus, slash, minus: Changes.</summary>
        Changes,

        /// <summary>Two ticks and lines: Checks.</summary>
        Checks,

        /// <summary>Chevron left: Back.</summary>
        Back,

        /// <summary>Chevron down: Next Part, Next Page, Next Choices, Compact (the room clears below).</summary>
        Next,

        /// <summary>Chevron up: Previous Part, Previous Page, Open Workspace (the room comes back up).</summary>
        Previous,

        /// <summary>Play: Try Build.</summary>
        Play,

        /// <summary>Plus: New Project.</summary>
        Plus,

        /// <summary>Pencil: Request Change, Change.</summary>
        Pencil,

        /// <summary>Arrow right: Continue.</summary>
        Continue,

        /// <summary>Three lines: Summarize, Choose Instruction.</summary>
        Lines,

        /// <summary>Turning arrow: Run Checks Again.</summary>
        Again,

        /// <summary>The agent's own: its cursor's arrow as a stroke. The action that names the agent and its level.</summary>
        Agent,

        /// <summary>Two sliders: Settings.</summary>
        Settings,

        /// <summary>A speech bubble: Write Instruction, words for the agent.</summary>
        Bubble,

        /// <summary>An arrow turning back: Restore.</summary>
        Undo,

        /// <summary>A clock, its hands at ten past: History (the lead's yes, 0704Z).</summary>
        Clock,

        // The marks of a card's kinds (the addendum's A17): drawn in a slot's tile or before a list row's name, never on an action.

        /// <summary>A short level line: a file removed.</summary>
        Minus,

        /// <summary>A wave: a file edited.</summary>
        Wave,

        /// <summary>Two arrows passing: a file changed in both places.</summary>
        Swap,

        /// <summary>An hourglass: a check waiting to run.</summary>
        Wait,

        /// <summary>An upright stroke over a point: a check that failed, a status that went wrong.</summary>
        Alert,

        /// <summary>A wrench: a check being fixed.</summary>
        Wrench,

        /// <summary>A page with two lines: files.</summary>
        File,

        /// <summary>A folder.</summary>
        Folder,

        /// <summary>A screen on its stand: the computer an agent runs on.</summary>
        Computer,

        /// <summary>A window with its bar: the app an agent runs in.</summary>
        App,

        /// <summary>A flag: a moment after a task worked.</summary>
        Flag,

        /// <summary>A pin: a moment before a task started.</summary>
        Pin,

        /// <summary>A gauge's arc and its needle: how much is left.</summary>
        Meter,

        /// <summary>A stopwatch: a usage window of hours.</summary>
        Timer,

        /// <summary>A calendar: a usage window of days.</summary>
        Calendar,

        /// <summary>Two chevrons pointing back: a file that reverts to an earlier moment (never Restore's own arrow).</summary>
        Rewind,

        /// <summary>A chevron pointing on, at a list row's right: the row is picked from (a moment of History, a found computer).</summary>
        Chevron,

        /// <summary>Two links of a chain: sharing by a link.</summary>
        Link,

        /// <summary>The Wi-Fi's arcs over its point: sharing on this Wi-Fi.</summary>
        Wifi,
    }

    /// <summary>
    /// Every glyph of DESIGN's set as geometry (BUILD-SPEC-v4 1.4's table; the design's own path data): a 24-unit grid, y
    /// down, drawn at 20 dp in a chip and 24 dp at the home size, strokes 2.2 units wide with round caps and joins, no fill
    /// but Stop's square. Each glyph is a few shapes, a line, an arc, a circle, a rounded rectangle's outline or a filled
    /// one, which <c>Halcyonic/Room Glyph</c> draws as distances; none is a font's or a picture's.
    /// </summary>
    public static class RoomGlyphs
    {
        /// <summary>The grid a glyph is given on, its stroke's width in grid units, and the most shapes one glyph has.</summary>
        public const float Grid = 24f;
        public const float Stroke = 2.2f;
        public const int MostShapes = 10;

        /// <summary>A glyph's size in an action's chip, and at the home size.</summary>
        public const float ChipDp = 20f;
        public const float HomeDp = 24f;

        /// <summary>The glyph's colour on a chip at rest, and on the main action's light chip: white, and #172024.</summary>
        public static readonly Color OnRest = Color.white;
        public static readonly Color OnMain = new Color(0x17 / 255f, 0x20 / 255f, 0x24 / 255f, 1f);

        /// <summary>One shape of a glyph, as the shader takes it (its two vectors), on the grid.</summary>
        public readonly struct Shape
        {
            public Shape(Vector4 a, Vector4 b)
            {
                A = a;
                B = b;
            }

            public Vector4 A { get; }
            public Vector4 B { get; }

            /// <summary>Whether the shape is filled, not stroked.</summary>
            public bool Filled => B.x > 3.5f;

            /// <summary>How far a place on the grid is from the shape's line, or from a filled shape's edge (negative inside).</summary>
            public float Distance(Vector2 p)
            {
                if (B.x < 1.5f)
                {
                    var a = new Vector2(A.x, A.y);
                    var e = new Vector2(A.z, A.w) - a;
                    var w = p - a;
                    return (w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Mathf.Max(e.sqrMagnitude, 1e-6f))).magnitude;
                }
                if (B.x < 2.5f)
                {
                    var q = p - new Vector2(A.x, A.y);
                    var along = q.x * B.y + q.y * B.z;
                    var across = Mathf.Abs(q.x * B.z - q.y * B.y);
                    if (B.w * across > A.w * along) return (new Vector2(across, along) - new Vector2(A.w, B.w) * A.z).magnitude;
                    return Mathf.Abs(q.magnitude - A.z);
                }
                var d = new Vector2(Mathf.Abs(p.x - A.x) - A.z + B.y, Mathf.Abs(p.y - A.y) - A.w + B.y);
                var box = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - B.y;
                return Filled ? box : Mathf.Abs(box);
            }

            /// <summary>Places on the shape's own line, or inside a filled one: where its ink is surest.</summary>
            public IEnumerable<Vector2> Middles()
            {
                if (B.x < 1.5f) yield return new Vector2((A.x + A.z) / 2f, (A.y + A.w) / 2f);
                else if (B.x < 2.5f)
                {
                    // The arc's middle, and half way to each end.
                    var half = Mathf.Atan2(A.w, B.w);
                    var middle = Mathf.Atan2(B.z, B.y);
                    foreach (var turn in new[] { 0f, 0.5f, -0.5f })
                        yield return new Vector2(A.x + A.z * Mathf.Cos(middle + turn * half), A.y + A.z * Mathf.Sin(middle + turn * half));
                }
                else if (Filled) yield return new Vector2(A.x, A.y);
                else
                {
                    yield return new Vector2(A.x - A.z, A.y);
                    yield return new Vector2(A.x + A.z, A.y);
                    yield return new Vector2(A.x, A.y - A.w);
                    yield return new Vector2(A.x, A.y + A.w);
                }
            }
        }

        private static Shape Line(float x1, float y1, float x2, float y2) => new Shape(new Vector4(x1, y1, x2, y2), new Vector4(1f, 0f, 0f, 0f));

        /// <summary>An arc about (<paramref name="x"/>, <paramref name="y"/>): its middle points <paramref name="middle"/> degrees round from the right toward down, and it sweeps <paramref name="half"/> degrees to each side of that.</summary>
        private static Shape Arc(float x, float y, float radius, float middle, float half) =>
            new Shape(new Vector4(x, y, radius, Mathf.Sin(half * Mathf.Deg2Rad)),
                new Vector4(2f, Mathf.Cos(middle * Mathf.Deg2Rad), Mathf.Sin(middle * Mathf.Deg2Rad), Mathf.Cos(half * Mathf.Deg2Rad)));

        /// <summary>A whole circle: an arc of half a turn to each side, its sine set to exactly nothing so no place counts as past its ends.</summary>
        private static Shape Circle(float x, float y, float radius) => new Shape(new Vector4(x, y, radius, 0f), new Vector4(2f, 1f, 0f, -1f));

        private static Shape Outline(float left, float top, float right, float bottom, float corner) =>
            new Shape(new Vector4((left + right) / 2f, (top + bottom) / 2f, (right - left) / 2f, (bottom - top) / 2f), new Vector4(3f, corner, 0f, 0f));

        private static Shape Fill(float left, float top, float right, float bottom, float corner) =>
            new Shape(new Vector4((left + right) / 2f, (top + bottom) / 2f, (right - left) / 2f, (bottom - top) / 2f), new Vector4(4f, corner, 0f, 0f));

        private static readonly Dictionary<RoomGlyph, Shape[]> shapes = new Dictionary<RoomGlyph, Shape[]>
        {
            [RoomGlyph.Send] = new[] { Line(12f, 19f, 12f, 5.5f), Line(6f, 11.5f, 12f, 5.5f), Line(12f, 5.5f, 18f, 11.5f) },
            // The capsule, the cradle under it (the lower half of a circle about (12, 11)), and the stem.
            [RoomGlyph.Microphone] = new[] { Outline(9f, 3.5f, 15f, 14f, 3f), Arc(12f, 11f, 6.5f, 90f, 90f), Line(12f, 17.5f, 12f, 21f) },
            // The body, four key dots (strokes of no length, round capped) and the space bar.
            [RoomGlyph.Keyboard] = new[]
            {
                Outline(3f, 6.5f, 21f, 17.5f, 2.5f), Line(7f, 10.25f, 7f, 10.25f), Line(10.5f, 10.25f, 10.5f, 10.25f), Line(14f, 10.25f, 14f, 10.25f),
                Line(17.5f, 10.25f, 17.5f, 10.25f), Line(8.5f, 14f, 15.5f, 14f),
            },
            [RoomGlyph.Stop] = new[] { Fill(6.5f, 6.5f, 17.5f, 17.5f, 2.5f) },
            [RoomGlyph.Check] = new[] { Line(5f, 12.5f, 9.5f, 17f), Line(9.5f, 17f, 19f, 7.5f) },
            [RoomGlyph.Barred] = new[] { Circle(12f, 12f, 8.25f), Line(6.2f, 17.8f, 17.8f, 6.2f) },
            [RoomGlyph.Cross] = new[] { Line(7f, 7f, 17f, 17f), Line(17f, 7f, 7f, 17f) },
            // The hexagon, then the three edges that meet at its middle.
            [RoomGlyph.Cube] = new[]
            {
                Line(12f, 3.5f, 19.5f, 7.75f), Line(19.5f, 7.75f, 19.5f, 16.25f), Line(19.5f, 16.25f, 12f, 20.5f), Line(12f, 20.5f, 4.5f, 16.25f),
                Line(4.5f, 16.25f, 4.5f, 7.75f), Line(4.5f, 7.75f, 12f, 3.5f), Line(4.5f, 7.75f, 12f, 12f), Line(12f, 12f, 19.5f, 7.75f), Line(12f, 12f, 12f, 20.5f),
            },
            [RoomGlyph.Changes] = new[] { Line(4.5f, 8f, 11.5f, 8f), Line(8f, 4.5f, 8f, 11.5f), Line(12.5f, 16f, 19.5f, 16f), Line(6f, 19f, 18f, 5f) },
            [RoomGlyph.Checks] = new[]
            {
                Line(4f, 7.5f, 6f, 9.5f), Line(6f, 9.5f, 9.5f, 6f), Line(12.5f, 8f, 20f, 8f), Line(4f, 16.5f, 6f, 18.5f), Line(6f, 18.5f, 9.5f, 15f), Line(12.5f, 17f, 20f, 17f),
            },
            [RoomGlyph.Back] = new[] { Line(14.5f, 5.5f, 8f, 12f), Line(8f, 12f, 14.5f, 18.5f) },
            [RoomGlyph.Next] = new[] { Line(5.5f, 9f, 12f, 15.5f), Line(12f, 15.5f, 18.5f, 9f) },
            [RoomGlyph.Previous] = new[] { Line(5.5f, 15f, 12f, 8.5f), Line(12f, 8.5f, 18.5f, 15f) },
            // A triangle with corners of 1 unit: its three sides and the three small arcs that join them.
            [RoomGlyph.Play] = new[]
            {
                Line(8f, 5.6f, 8f, 18.4f), Arc(9f, 18.4f, 1f, 119.9f, 60.1f), Line(9.5f, 19.26f, 19.5f, 12.86f), Arc(18.99f, 12f, 1f, 0f, 59.3f),
                Line(19.5f, 11.14f, 9.5f, 4.74f), Arc(9f, 5.6f, 1f, -119.9f, 60.1f),
            },
            [RoomGlyph.Plus] = new[] { Line(12f, 5f, 12f, 19f), Line(5f, 12f, 19f, 12f) },
            [RoomGlyph.Pencil] = new[]
            {
                Line(5f, 19f, 6f, 14.5f), Line(6f, 14.5f, 15.5f, 5f), Line(15.5f, 5f, 19f, 8.5f), Line(19f, 8.5f, 9.5f, 18f), Line(9.5f, 18f, 5f, 19f), Line(13.5f, 7f, 17f, 10.5f),
            },
            [RoomGlyph.Continue] = new[] { Line(5f, 12f, 18.5f, 12f), Line(13f, 6.5f, 18.5f, 12f), Line(18.5f, 12f, 13f, 17.5f) },
            [RoomGlyph.Lines] = new[] { Line(5f, 7f, 19f, 7f), Line(5f, 12f, 15f, 12f), Line(5f, 17f, 11f, 17f) },
            // Most of a circle, from just under its right round by the bottom and the left to its upper right, and the arrowhead there.
            [RoomGlyph.Again] = new[] { Arc(12.14f, 11.64f, 6.75f, 162.45f, 150.85f), Line(17.5f, 3.5f, 17.5f, 7.25f), Line(17.5f, 7.25f, 13.75f, 7.25f) },
            // The cursor's arrow, closed: its tip at the upper left, down its long side, in to the notch, out to the right corner and back.
            [RoomGlyph.Agent] = new[] { Line(6.5f, 4.5f, 7f, 19.5f), Line(7f, 19.5f, 11f, 15.5f), Line(11f, 15.5f, 18f, 15f), Line(18f, 15f, 6.5f, 4.5f) },
            // A rounded box of words, its tail at the lower left.
            [RoomGlyph.Bubble] = new[] { Outline(4.5f, 5f, 19.5f, 16f, 3f), Line(8f, 16f, 7f, 19.5f), Line(7f, 19.5f, 11.5f, 16f) },
            // An arrowhead pointing left at the upper left, its shaft running right and turning down and back round a half circle.
            [RoomGlyph.Undo] = new[] { Line(9f, 6.5f, 5f, 10.5f), Line(5f, 10.5f, 9f, 14.5f), Line(5f, 10.5f, 14f, 10.5f), Arc(14f, 14.75f, 4.25f, 0f, 90f), Line(14f, 19f, 11f, 19f) },
            [RoomGlyph.Minus] = new[] { Line(6f, 12f, 18f, 12f) },
            // Two half waves, the left one rising, the right one falling, meeting in the middle.
            [RoomGlyph.Wave] = new[] { Arc(8.5f, 13.5f, 3.5f, 270f, 70f), Arc(15.5f, 10.5f, 3.5f, 90f, 70f) },
            // An arrow to the right above, an arrow to the left below.
            [RoomGlyph.Swap] = new[] { Line(5f, 8.5f, 19f, 8.5f), Line(15.5f, 5f, 19f, 8.5f), Line(15.5f, 12f, 19f, 8.5f), Line(19f, 15.5f, 5f, 15.5f), Line(8.5f, 12f, 5f, 15.5f), Line(8.5f, 19f, 5f, 15.5f) },
            // Its top and its foot, and the two sides crossing at the waist.
            [RoomGlyph.Wait] = new[] { Line(7.5f, 4.5f, 16.5f, 4.5f), Line(7.5f, 19.5f, 16.5f, 19.5f), Line(8.5f, 5f, 15.5f, 19f), Line(15.5f, 5f, 8.5f, 19f) },
            [RoomGlyph.Alert] = new[] { Line(12f, 5.5f, 12f, 13.5f), Line(12f, 18f, 12f, 18f) },
            // The open jaw at the upper right, its handle down to the lower left.
            [RoomGlyph.Wrench] = new[] { Arc(15f, 9f, 4.25f, 135f, 130f), Line(12f, 12f, 5.5f, 18.5f) },
            [RoomGlyph.File] = new[] { Outline(6f, 3.5f, 18f, 20.5f, 2f), Line(9f, 9f, 15f, 9f), Line(9f, 13f, 15f, 13f) },
            [RoomGlyph.Folder] = new[] { Outline(3.5f, 7.5f, 20.5f, 18.5f, 2f), Line(4.5f, 5f, 9.5f, 5f), Line(9.5f, 5f, 11.5f, 7.5f) },
            [RoomGlyph.Computer] = new[] { Outline(4f, 5f, 20f, 15.5f, 2f), Line(12f, 15.5f, 12f, 19f), Line(8.5f, 19.5f, 15.5f, 19.5f) },
            [RoomGlyph.App] = new[] { Outline(5f, 5f, 19f, 19f, 3f), Line(5f, 9f, 19f, 9f) },
            [RoomGlyph.Flag] = new[] { Line(6.5f, 20.5f, 6.5f, 4f), Line(6.5f, 4.5f, 17.5f, 4.5f), Line(17.5f, 4.5f, 15f, 8.5f), Line(15f, 8.5f, 17.5f, 12.5f), Line(17.5f, 12.5f, 6.5f, 12.5f) },
            // A round head with its hole, its two sides meeting in the point below.
            // Half a dial, its needle up and to the right.
            // A dial with its hand and the stem and button at its top.
            [RoomGlyph.Timer] = new[] { Circle(12f, 13f, 6.5f), Line(12f, 13f, 12f, 9f), Line(12f, 13f, 14.5f, 14.5f), Line(12f, 6.5f, 12f, 4.5f), Line(9.5f, 3.5f, 14.5f, 3.5f) },
            // A page with its bar and its two rings.
            [RoomGlyph.Calendar] = new[] { Outline(4.5f, 6f, 19.5f, 19.5f, 2f), Line(4.5f, 10.5f, 19.5f, 10.5f), Line(8.5f, 4f, 8.5f, 7.5f), Line(15.5f, 4f, 15.5f, 7.5f) },
            [RoomGlyph.Clock] = new[] { Circle(12f, 12f, 8f), Line(12f, 7.5f, 12f, 12f), Line(12f, 12f, 15f, 14f) },
            [RoomGlyph.Chevron] = new[] { Line(9.5f, 6f, 15.5f, 12f), Line(15.5f, 12f, 9.5f, 18f) },
            // Two rounded links, one up and left, one down and right, overlapping at the middle.
            [RoomGlyph.Link] = new[] { Outline(4f, 7.5f, 14f, 14.5f, 3.5f), Outline(10f, 9.5f, 20f, 16.5f, 3.5f) },
            // Three arcs opening up, their middle at the foot, and the point under them.
            [RoomGlyph.Wifi] = new[] { Arc(12f, 18.5f, 12f, 270f, 45f), Arc(12f, 18.5f, 8f, 270f, 45f), Arc(12f, 18.5f, 4f, 270f, 45f), Line(12f, 18.5f, 12f, 18.5f) },
            [RoomGlyph.Rewind] = new[] { Line(12.5f, 7f, 7.5f, 12f), Line(7.5f, 12f, 12.5f, 17f), Line(18f, 7f, 13f, 12f), Line(13f, 12f, 18f, 17f) },
            [RoomGlyph.Meter] = new[] { Arc(12f, 16f, 7.5f, 270f, 90f), Line(12f, 16f, 15.5f, 11.5f) },
            [RoomGlyph.Pin] = new[] { Arc(12f, 10f, 6f, 270f, 125f), Line(7.1f, 13.4f, 12f, 20.5f), Line(16.9f, 13.4f, 12f, 20.5f), Circle(12f, 10f, 2f) },
            // Two tracks, each broken where its knob (a circle) rides: the upper one left of the middle, the lower one right of it.
            [RoomGlyph.Settings] = new[]
            {
                Line(5f, 8f, 8.5f, 8f), Line(12.5f, 8f, 19f, 8f), Circle(10.5f, 8f, 2f), Line(5f, 16f, 12.5f, 16f), Line(16.5f, 16f, 19f, 16f), Circle(14.5f, 16f, 2f),
            },
        };

        /// <summary>The glyph's shapes on the 24-unit grid, y down.</summary>
        public static IReadOnlyList<Shape> Of(RoomGlyph glyph) => shapes[glyph];

        private static readonly Dictionary<RoomGlyph, float> inkLeft = new Dictionary<RoomGlyph, float>();

        /// <summary>
        /// Where a glyph's ink starts from its box's left, in grid units (0 to 24): what lines a glyph's drawn edge up with
        /// words beside or under it, rather than its box.
        /// </summary>
        public static float InkLeft(RoomGlyph glyph)
        {
            if (inkLeft.TryGetValue(glyph, out var left)) return left;
            left = Grid / 2f;
            for (var x = 0f; x <= Grid; x += 0.25f)
            {
                var found = false;
                for (var y = 0f; y <= Grid && !found; y += 0.25f) found = Inked(glyph, new Vector2(x, y));
                if (found) { left = x; break; }
            }
            inkLeft[glyph] = left;
            return left;
        }

        /// <summary>Whether a place on the grid is inked: nearer than half the stroke to a line, or inside a filled shape.</summary>
        public static bool Inked(RoomGlyph glyph, Vector2 place, float slack = 0f)
        {
            foreach (var shape in shapes[glyph])
                if (shape.Distance(place) <= (shape.Filled ? 0f : Stroke / 2f) + slack) return true;
            return false;
        }

        private static readonly Dictionary<string, RoomGlyph> named = new Dictionary<string, RoomGlyph>
        {
            ["Send Instruction"] = RoomGlyph.Send, ["Send Answer"] = RoomGlyph.Send, ["Hold to Talk"] = RoomGlyph.Microphone, ["Talk Again"] = RoomGlyph.Microphone,
            ["Type"] = RoomGlyph.Keyboard, ["Stop"] = RoomGlyph.Stop, ["Approve"] = RoomGlyph.Check, ["Deny"] = RoomGlyph.Barred, ["Cancel"] = RoomGlyph.Cross, ["Close"] = RoomGlyph.Cross,
            ["Discard Draft"] = RoomGlyph.Cross,
            ["Make VR Version"] = RoomGlyph.Cube, ["Changes"] = RoomGlyph.Changes, ["Checks"] = RoomGlyph.Checks, ["Back"] = RoomGlyph.Back, ["Next Part"] = RoomGlyph.Next,
            ["Next Page"] = RoomGlyph.Next, ["Next Choices"] = RoomGlyph.Next, ["Previous Part"] = RoomGlyph.Previous, ["Try Build"] = RoomGlyph.Play,
            ["New Project"] = RoomGlyph.Plus, ["Request Change"] = RoomGlyph.Pencil, ["Write Instruction"] = RoomGlyph.Bubble, ["Continue"] = RoomGlyph.Continue,
            ["Summarize"] = RoomGlyph.Lines, ["Choose Instruction"] = RoomGlyph.Lines, ["Run Checks Again"] = RoomGlyph.Again, ["Settings"] = RoomGlyph.Settings,
            ["Restore"] = RoomGlyph.Undo, ["Let Agent Fix"] = RoomGlyph.Agent, ["History"] = RoomGlyph.Clock,
            ["Previous Page"] = RoomGlyph.Previous, ["Compact"] = RoomGlyph.Next, ["Open Workspace"] = RoomGlyph.Previous, ["Projects"] = RoomGlyph.Back,
            ["Carry Out Plan"] = RoomGlyph.Continue,
            // SCREENS's Start From takes the cube, as Make VR Version (the addendum's A16).
            ["Start From"] = RoomGlyph.Cube,
        };

        /// <summary>The card's kind marks (the addendum's A17), which stand in tiles and before list rows and never on an action.</summary>
        public static readonly RoomGlyph[] Marks =
        {
            RoomGlyph.Minus, RoomGlyph.Wave, RoomGlyph.Swap, RoomGlyph.Wait, RoomGlyph.Alert, RoomGlyph.Wrench, RoomGlyph.File, RoomGlyph.Folder,
            RoomGlyph.Computer, RoomGlyph.App, RoomGlyph.Flag, RoomGlyph.Pin, RoomGlyph.Meter, RoomGlyph.Timer, RoomGlyph.Calendar, RoomGlyph.Rewind, RoomGlyph.Chevron, RoomGlyph.Link, RoomGlyph.Wifi,
        };

        /// <summary>The two levels an agent works at, as the action that names the agent ends: "{agent} · Ask First", "{agent} · Never Ask".</summary>
        private static readonly string[] levels = { " · Ask First", " · Never Ask" };

        /// <summary>
        /// The glyph the design gives an action, by the action's own words; a paging action's count (" · 2 of 3") is left
        /// out. The action that names the agent and its level ("{agent} · Ask First") takes the agent's own glyph, whatever
        /// the agent is called; a level alone is an answer with its mark, and has none. Null for words the table does not
        /// name: Confirm takes the glyph of what it confirms, which its owner knows.
        /// </summary>
        public static RoomGlyph? Named(string action)
        {
            foreach (var level in levels)
                if (action.Length > level.Length && action.EndsWith(level, System.StringComparison.Ordinal)) return RoomGlyph.Agent;
            var cut = action.IndexOf(" · ", System.StringComparison.Ordinal);
            return named.TryGetValue(cut >= 0 ? action.Substring(0, cut) : action, out var glyph) ? glyph : (RoomGlyph?)null;
        }
    }

    /// <summary>
    /// One glyph drawn from its geometry, centred on its transform: a quad the glyph's own box, in one colour. Shown again
    /// with the same glyph, size and colour, it draws nothing new.
    /// </summary>
    public sealed class RoomGlyphView : MonoBehaviour
    {
        private const string MaterialPath = "HalcyonicUI/RoomGlyph";
        private static readonly int ColourId = Shader.PropertyToID("_Colour");
        private static readonly int StrokeId = Shader.PropertyToID("_Stroke");
        private static readonly int ShapeAId = Shader.PropertyToID("_ShapeA");
        private static readonly int ShapeBId = Shader.PropertyToID("_ShapeB");
        private static readonly Dictionary<RoomGlyph, (Vector4[] A, Vector4[] B, int Count)> arrays = new Dictionary<RoomGlyph, (Vector4[], Vector4[], int)>();
        private static Material? material;
        private static Mesh? quad;

        private MeshRenderer ink = null!;
        private MaterialPropertyBlock block = null!;
        private (RoomGlyph Glyph, float SizeDp, Color Colour)? drawn;
        private float lastStroke = RoomGlyphs.Stroke;

        /// <summary>The glyph it shows, and its size in dp; null while hidden.</summary>
        public RoomGlyph? Glyph => gameObject.activeSelf ? drawn?.Glyph : null;

        public float SizeDp => drawn?.SizeDp ?? 0f;

        /// <summary>What draws the glyph.</summary>
        public MeshRenderer Ink => ink;

        /// <summary>A glyph under <paramref name="parent"/>, hidden, centred on its transform, drawn at <paramref name="order"/> (over its chip).</summary>
        public static RoomGlyphView Create(Transform parent, string name = "Glyph", int order = KitType.Order.ControlWords)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomGlyphView>();
            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            view.ink = go.AddComponent<MeshRenderer>();
            view.ink.sharedMaterial = Material;
            view.ink.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.ink.receiveShadows = false;
            view.ink.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            view.ink.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            view.ink.sortingOrder = order;
            view.block = new MaterialPropertyBlock();
            go.SetActive(false);
            return view;
        }

        /// <summary>Shows <paramref name="glyph"/> <paramref name="sizeDp"/> across (20 in a chip, 24 at the home size) in <paramref name="colour"/>.</summary>
        /// <param name="stroke">The stroke in the glyph's 24-unit grid: <see cref="RoomGlyphs.Stroke"/>, or heavier for a mark on a solid fill.</param>
        public void Show(RoomGlyph glyph, float sizeDp, Color colour, float stroke = RoomGlyphs.Stroke)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (drawn is var (lastGlyph, lastSize, lastColour) && lastGlyph == glyph && Mathf.Approximately(lastSize, sizeDp) && lastColour == colour && Mathf.Approximately(lastStroke, stroke)) return;
            drawn = (glyph, sizeDp, colour);
            lastStroke = stroke;
            var side = RoomType.Units(1f) * sizeDp;
            transform.localScale = new Vector3(side, side, 1f);
            var (a, b, count) = Arrays(glyph);
            block.SetVector(ColourId, new Vector4(colour.r, colour.g, colour.b, colour.a));
            block.SetVector(StrokeId, new Vector4(stroke, count, 0f, 0f));
            block.SetVectorArray(ShapeAId, a);
            block.SetVectorArray(ShapeBId, b);
            ink.SetPropertyBlock(block);
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>A glyph's shapes as the shader's two arrays, made once.</summary>
        private static (Vector4[] A, Vector4[] B, int Count) Arrays(RoomGlyph glyph)
        {
            if (arrays.TryGetValue(glyph, out var made)) return made;
            var list = RoomGlyphs.Of(glyph);
            var a = new Vector4[RoomGlyphs.MostShapes];
            var b = new Vector4[RoomGlyphs.MostShapes];
            for (var index = 0; index < list.Count && index < RoomGlyphs.MostShapes; index++)
            {
                a[index] = list[index].A;
                b[index] = list[index].B;
            }
            made = (a, b, Mathf.Min(list.Count, RoomGlyphs.MostShapes));
            arrays[glyph] = made;
            return made;
        }

        private static Material Material
        {
            get
            {
                if (material != null) return material;
                var loaded = Resources.Load<Material>(MaterialPath);
                if (loaded == null)
                {
                    Debug.LogError("Halcyonic: the material Resources/" + MaterialPath + " is missing, so the redesign's glyphs cannot render.");
                    var fallback = Shader.Find("Hidden/InternalErrorShader");
                    material = new Material(fallback != null ? fallback : Shader.Find("Sprites/Default"));
                    return material;
                }
                material = loaded;
                return material;
            }
        }

        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "Room glyph" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
                quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                quad.UploadMeshData(true);
                return quad;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            material = null;
            quad = null;
        }
    }
}
