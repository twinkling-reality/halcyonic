#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The working field alone (DESIGN's BUILD-SPEC-v4 2.3), at the size the design gives it round the first character and
    /// the information panel's place, at fixed clocks: standing, standing seven seconds later, and 300 ms into its fade in.
    /// Each render is read against what the page mixes at the same place: the bare ground, the middle of every point well
    /// inside the field, the hairlines, and nothing at all at the quad's edge, within 6 of 255 over white and over the
    /// design's own dark room, the two grounds the field's blend is fit to. The points' numbers come from <see cref="RoomField"/>'s own arithmetic, which the shader shares.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] FieldStates = { "41-field", "41-field-later", "41-field-arriving" };

        /// <summary>What the design's field surrounds, in dp: from the first character's left to the information panel's right, 12 degrees up from the feet line.</summary>
        private static readonly Vector2 FieldInnerDp = new Vector2(610.55f, 193.1f);

        /// <summary>The design's dark room where the field stands, as its page paints it: about #1E1C1A.</summary>
        private static readonly Color FieldDarkRoom = new Color(30f / 255f, 28f / 255f, 26f / 255f, 1f);

        private static PartShot FieldShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = -12f, Dark = FieldDarkRoom };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var unit = RoomType.Units(1f);
            var arriving = state == "41-field-arriving";
            const float ArrivingSeconds = 0.3f;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var field = RoomField.Create(plane);
            var clock = 10f;
            field.Clock = () => clock;
            var size = RoomField.Around(FieldInnerDp * unit);
            var sizeDp = size / unit;
            Require(Mathf.Abs(sizeDp.x - 793.7f) < 0.2f && Mathf.Abs(sizeDp.y - 374.5f) < 0.2f,
                "grown as the design grows it, the field is " + sizeDp.x.ToString("0.0") + " by " + sizeDp.y.ToString("0.0") + " dp, not 793.7 by 374.5");
            field.Show(size);
            Require(field.gameObject.activeSelf && field.Shown == 0f && field.Fading, "it does not start from nothing");
            var passed = arriving ? ArrivingSeconds : state == "41-field-later" ? 9.7f : 2.5f;
            clock += passed;
            field.Draw();

            var shownExpected = arriving ? RoomMotion.FieldArrives(ArrivingSeconds) : 1f;
            Require(Mathf.Abs(field.Shown - shownExpected) < 1e-3f, "it is " + field.Shown + " into view, not " + shownExpected);
            Require(field.Fading == arriving, arriving ? "it has stopped mid-fade" : "it still fades " + passed + " s on");
            Require(Mathf.Abs(field.Seconds - passed) < 1e-4f, "its clock reads " + field.Seconds + " s, not " + passed);
            Require(field.Size == size, "it did not take its size");
            Require(field.Ground.sharedMaterial != null && field.Ground.sharedMaterial.shader.name == "Halcyonic/Room Field", "it is not drawn as the working field");
            Require(field.GetComponentsInChildren<Renderer>(false).Length == 1 && field.GetComponentsInChildren<TMP_Text>(true).Length == 0, "it is more than one quad, or carries words");
            var scale = field.Ground.transform.localScale;
            Require(Mathf.Abs(scale.x - size.x) < 1e-5f && Mathf.Abs(scale.y - size.y) < 1e-5f, "its quad is not its size");

            // The points: about one in twelve lit, and their light moves with the clock.
            var points = 0;
            var lit = 0;
            var moved = 0;
            var columns = Mathf.CeilToInt(sizeDp.x / 2f / RoomField.PitchDp);
            var rows = Mathf.CeilToInt(sizeDp.y / 2f / RoomField.PitchDp);
            for (var x = -columns; x <= columns; x++)
            for (var y = -rows; y <= rows; y++)
            {
                if (RoomField.Edge(new Vector2(x, y) * RoomField.PitchDp, sizeDp) <= 0.01f) continue;
                points++;
                if (RoomField.PointAt(x, y).Lit) lit++;
                if (Mathf.Abs(RoomField.PointNow(x, y, sizeDp, field.Seconds).Alpha - RoomField.PointNow(x, y, sizeDp, field.Seconds + 0.5f).Alpha) > 0.01f) moved++;
            }
            Require(points > 1000 && lit >= points * 0.06f && lit <= points * 0.10f, lit + " of " + points + " points are lit, not about one in twelve");
            Require(moved > points / 2, "only " + moved + " of " + points + " points change their light in half a second");

            // Stood for the render: square to the eyes, its middle on the line of sight.
            Place(plane, eyes, 0f, shot.Elevation);

            // Where the design stands it on the level plane (its middle at yaw -12.8, pitch -11.6), it is inside the field.
            var level = new GameObject("Level plane").transform;
            level.SetParent(root, false);
            Place(level, eyes, 0f, 0f);
            var middle = new Vector2(-12.84f, -11.63f) * RoomGeometry.DpPerDegree * unit;
            var corners = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) }
                .Select(corner => level.TransformPoint(new Vector3(middle.x + corner.x * size.x / 2f, middle.y + corner.y * size.y / 2f, 0f))).ToList();
            shot.Failures.AddRange(GlazeChecks.InsideField(name, corners, eyes, level.position, 0f, Quest3S));

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector2 dp, int right, int up, out bool seen)
                {
                    var screen = camera.WorldToScreenPoint(field.transform.TransformPoint(new Vector3(dp.x * unit, dp.y * unit, 0f)));
                    // The pixel the place falls in: a point is two or three pixels across.
                    var x = Mathf.FloorToInt(screen.x) + right;
                    var y = Mathf.FloorToInt(screen.y) + up;
                    seen = screen.z > 0f && x >= 0 && y >= 0 && x < Width && y < Height;
                    return seen ? pixels[y * Width + x] : default;
                }
                var night = (Color32)FieldDarkRoom;
                var grounds = new (string Over, Color32[] Pixels, Color32 Under)[] { ("white", white, new Color32(255, 255, 255, 255)), ("the dark room", dark, night) };
                var worst = 0;

                // How far a drawn pixel is from what the page mixes at a place, laid over a ground as the page lays it.
                int Off(Color32 drawn, Vector2 dp, Color32 under)
                {
                    var mixed = RoomField.Mixed(dp, sizeDp, field.Seconds, field.Shown);
                    var r = Mathf.RoundToInt(255f * mixed.x + (1f - mixed.w) * under.r);
                    var g = Mathf.RoundToInt(255f * mixed.y + (1f - mixed.w) * under.g);
                    var b = Mathf.RoundToInt(255f * mixed.z + (1f - mixed.w) * under.b);
                    return Mathf.Max(Mathf.Abs(drawn.r - r), Mathf.Max(Mathf.Abs(drawn.g - g), Mathf.Abs(drawn.b - b)));
                }
                void Hold(string what, Vector2 dp)
                {
                    foreach (var (over, pixels, under) in grounds)
                    {
                        var drawn = At(pixels, dp, 0, 0, out var seen);
                        if (!seen) { failures.Add(what + " is out of the frame"); continue; }
                        var off = Off(drawn, dp, under);
                        worst = Mathf.Max(worst, off);
                        if (off > 6) failures.Add(what + " at (" + dp.x.ToString("0.0") + ", " + dp.y.ToString("0.0") + ") dp over " + over + " is (" + drawn.r + ", " + drawn.g + ", " + drawn.b + "), " + off + " of 255 from the page");
                    }
                }

                // Bare ground, between the points and off the hairlines: at the middle, part way out, and near the edge.
                foreach (var cell in new[] { new Vector2(1f, 1f), new Vector2(10f, 3f), new Vector2(-20f, -8f), new Vector2(30f, 0f), new Vector2(0f, 15f), new Vector2(-33f, 9f) })
                    Hold("the ground", (cell + new Vector2(0.5f, 0.5f)) * RoomField.PitchDp);

                // Every point well inside the field, at its middle, clear of the hairlines.
                var held = 0;
                var brightest = 0;
                for (var x = -columns; x <= columns; x++)
                for (var y = -rows; y <= rows; y++)
                {
                    if (x % 4 == 0 || y % 4 == 0) continue;
                    var at = new Vector2(x, y) * RoomField.PitchDp;
                    if (RoomField.Edge(at, sizeDp) <= 0.5f) continue;
                    Hold(RoomField.PointAt(x, y).Lit ? "a lit point" : "a point", at);
                    brightest = Mathf.Max(brightest, At(dark, at, 0, 0, out _).g);
                    held++;
                }
                if (held < 300) failures.Add("only " + held + " points were read");

                // The hairlines: a 1 dp line is under two pixels wide, so its light is read across it. Over the dark room,
                // the pixels across a line's middle carry together what the page's line carries over 1 dp.
                var pixelsADp = Vector3.Distance(camera.WorldToScreenPoint(field.transform.TransformPoint(Vector3.zero)), camera.WorldToScreenPoint(field.transform.TransformPoint(new Vector3(unit, 0f, 0f))));
                var lines = "";
                foreach (var (dp, across) in new[] { (new Vector2(36f, 4.5f), true), (new Vector2(-72f, 13.5f), true), (new Vector2(4.5f, 36f), false), (new Vector2(-13.5f, -72f), false) })
                {
                    var beside = dp + (across ? new Vector2(3f, 0f) : new Vector2(0f, 3f));
                    var onLine = RoomField.Mixed(dp, sizeDp, field.Seconds, field.Shown);
                    var offLine = RoomField.Mixed(beside, sizeDp, field.Seconds, field.Shown);
                    var lineValue = 255f * onLine.y + (1f - onLine.w) * night.g;
                    var groundValue = 255f * offLine.y + (1f - offLine.w) * night.g;
                    var carried = 0f;
                    for (var step = -3; step <= 3; step++)
                        carried += (At(dark, dp, across ? step : 0, across ? 0 : step, out _).g - groundValue) / Mathf.Max(lineValue - groundValue, 1f);
                    carried /= pixelsADp;
                    lines += (lines.Length > 0 ? ", " : "") + carried.ToString("0.00", CultureInfo.InvariantCulture);
                    if (carried < 0.7f || carried > 1.3f) failures.Add("the hairline at (" + dp.x + ", " + dp.y + ") dp carries " + carried.ToString("0.00", CultureInfo.InvariantCulture) + " of the page's 1 dp line's light");
                }

                // Nothing at the quad's edge: the field is gone before its box ends.
                var most = 0;
                foreach (var edge in new[] { new Vector2(-1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, 0f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(0f, 1f) })
                    most = Mathf.Max(most, At(eye, new Vector2(edge.x * (sizeDp.x / 2f - 1f), edge.y * (sizeDp.y / 2f - 1f)), 0, 0, out _).a);
                if (most > 2) failures.Add("the field still draws at its box's edge (alpha " + most + " of 255)");

                Debug.Log("Halcyonic: parts: " + name + ": " + sizeDp.x.ToString("0.0", CultureInfo.InvariantCulture) + " by " + sizeDp.y.ToString("0.0", CultureInfo.InvariantCulture) + " dp, "
                    + points + " points, " + lit + " lit; " + field.Shown.ToString("0.000", CultureInfo.InvariantCulture) + " into view at clock " + field.Seconds.ToString("0.00", CultureInfo.InvariantCulture)
                    + " s; " + held + " points and 6 grounds read, at most " + worst + " of 255 from the page over white and the design's dark room; 4 hairlines carry " + lines
                    + " of the page's line; the brightest point's middle " + brightest + " over the dark room; alpha at the box's edge " + most + ".");
            };
            return shot;
        }

        /// <summary>
        /// The field's fade and clock, checked without a render at fixed clock times: it fades in on the standard curve, a new
        /// size is taken with no fade, it fades out and is gone, and kept still it is whole at once with its points' light held.
        /// </summary>
        private static List<string> FieldMotion()
        {
            var failures = new List<string>();
            void Require(bool condition, string why) { if (!condition) failures.Add("the field's motion: " + why); }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Field motion");
            var stillWas = GlazeMotion.Still;
            try
            {
                GlazeMotion.Still = false;
                var field = RoomField.Create(root.transform);
                var clock = 1f;
                field.Clock = () => clock;
                var unit = RoomType.Units(1f);
                var size = new Vector2(600f, 300f) * unit;
                void After(float seconds)
                {
                    clock += seconds;
                    field.Draw();
                }

                Require(!field.gameObject.activeSelf, "a new field is not hidden");
                field.Show(size);
                Require(field.gameObject.activeSelf && field.Fading && field.Shown == 0f, "it does not start from nothing");
                After(0.3f);
                Require(Mathf.Abs(field.Shown - RoomMotion.FieldArrives(0.3f)) < 1e-3f, "300 ms in, it is " + field.Shown + " into view");
                After(0.4f);
                Require(field.Shown == 1f && !field.Fading, "700 ms in, it has not arrived");
                field.Show(size * 1.2f);
                Require(field.Size == size * 1.2f && field.Shown == 1f && !field.Fading, "a new size made it fade again");
                var before = field.Seconds;
                After(2f);
                Require(Mathf.Abs(field.Seconds - before - 2f) < 1e-4f, "its clock does not run while it stands");
                field.Leave();
                After(0.3f);
                Require(field.Fading && Mathf.Abs(field.Shown - RoomMotion.FieldLeaves(0.3f)) < 1e-3f, "300 ms into leaving, it is " + field.Shown + " into view");
                After(0.4f);
                Require(!field.gameObject.activeSelf && !field.Fading, "700 ms on, it has not left");

                GlazeMotion.Still = true;
                field.Show(size);
                Require(field.gameObject.activeSelf && field.Shown == 1f && !field.Fading, "kept still, it did not arrive at once");
                var held = field.Seconds;
                After(3f);
                Require(field.Seconds == held, "kept still, its points' clock ran on");
                field.Leave();
                Require(!field.gameObject.activeSelf, "kept still, it did not leave at once");
            }
            catch (Exception error) { failures.Add("the field's motion threw " + error); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                GlazeMotion.Still = stillWas;
            }
            if (failures.Count == 0) Debug.Log("Halcyonic: parts: the field's fade and clock hold at every fixed time.");
            return failures;
        }
    }
}
