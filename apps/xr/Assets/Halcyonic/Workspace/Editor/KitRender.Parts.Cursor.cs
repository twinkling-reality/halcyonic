#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The agent's cursor and its label alone (DESIGN's BUILD-SPEC-v4 2.3), fed the way production feeds it, from an
    /// execution's own view of its tools under way: editing a file, running tests, a file name too long for the label, and
    /// 300 ms into the glide to a next step. Each says only Halcyonic's verb and at most the file's own name, on one line in
    /// the one type size, inside its ground; the arrow's fill and edge and the label's ground are read in the pixels against
    /// the page's own mix; the cursor stands on its stop, drifting no more than the design allows.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] CursorStates = { "42-cursor-editing", "42-cursor-tests", "42-cursor-long", "42-cursor-gliding" };

        /// <summary>An execution with one tool under way, synthetic.</summary>
        private static ExecutionView CursorExecution(string id, string tool, string? title) => new ExecutionView
        {
            ExecutionId = "render",
            ActiveTools = new List<ToolActivityView> { new ToolActivityView { ToolCallId = id, ToolName = tool, Title = title, StartedAt = "2026-11-10T09:00:00.000Z" } },
            ToolActivity = ExecutionViewToolActivity.Running,
        };

        private static PartShot CursorShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = -12f, Dark = FieldDarkRoom };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var unit = RoomType.Units(1f);
            var scale = GlazeText.Scale;
            var gliding = state == "42-cursor-gliding";
            const float GlidingSeconds = 0.3f;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var cursor = RoomCursor.Create(plane);
            var clock = 10f;
            cursor.Clock = () => clock;

            ExecutionView execution;
            string expected;
            switch (state)
            {
                case "42-cursor-tests":
                    execution = CursorExecution("a", "Bash", "npm test");
                    execution.ActiveTestRun = new TestRunView { TestRunId = "tests", Label = "12 tests", StartedAt = "2026-11-10T09:00:01.000Z" };
                    expected = "Running Tests";
                    break;
                case "42-cursor-long":
                    execution = CursorExecution("a", "Edit", "src/levels/a-very-long-level-file-name-that-no-label-holds.generated.js");
                    expected = "Editing";
                    break;
                default:
                    execution = CursorExecution("a", "Edit", "src/score.js");
                    expected = "Editing score.js";
                    break;
            }
            cursor.Show(RoomWork.Of(execution));
            Require(cursor.gameObject.activeSelf && cursor.Shown == 0f && cursor.Stop == 0, "it does not start from nothing on its first stop");
            clock += 5f;
            cursor.Draw();
            var started = 5f;
            if (gliding)
            {
                // The next step: a command, whose line is never shown.
                cursor.Show(RoomWork.Of(CursorExecution("b", "Bash", "/bin/zsh -lc 'npm run build'")));
                expected = "Running Command";
                clock += GlidingSeconds;
                started += GlidingSeconds;
                cursor.Draw();
            }
            cursor.Sweep.Draw(clock);

            // The words: Halcyonic's verb and at most the file's own name, whole, on one line, in the one size.
            var label = cursor.Label;
            Require(cursor.Words == expected && label.text == LabelText.ForTextMeshPro(expected), "the label reads \"" + cursor.Words + "\", not \"" + expected + "\"");
            label.ForceMeshUpdate(true);
            cursor.Sweep.Draw(clock);
            Require(!label.isTextTruncated && label.textInfo.lineCount == 1, "the label's words are cut or run over a line");
            Require(Mathf.Approximately(label.fontSize, GlazeTokens.FontSize(RoomType.Scaled(RoomGeometry.TypeDp))), "the words are not in the one type size");
            Require(cursor.Sweep.Waits, "the words do not carry the wait's sweep");
            var labels = cursor.GetComponentsInChildren<TMP_Text>(false);
            Require(labels.Length == 1 && labels[0] == label && cursor.GetComponentsInChildren<Renderer>(false).Length == 2, "the cursor draws more than its arrow, its label's ground and its words");
            Require(cursor.Ground.sharedMaterial != null && cursor.Ground.sharedMaterial.shader.name == "Halcyonic/Room Cursor", "it is not drawn as the agent's cursor");
            Require(Mathf.Abs(cursor.Shown - 1f) < 1e-4f, "it is " + cursor.Shown + " into view five seconds on");

            // The label's ground: its words and their padding, 6 dp above and below a 28 dp line, 16 dp at the sides.
            var wordsWidth = RoomCursor.WordsWidth(label, 0f) / unit;
            var size = cursor.LabelSize;
            Require(Mathf.Abs(size.y - (RoomGeometry.LineDp + 2f * RoomCursor.LabelPadYDp) * scale) < 0.01f && Mathf.Abs(size.x - (wordsWidth + 2f * RoomCursor.LabelPadXDp * scale)) < 0.05f,
                "the label's ground is " + size.x.ToString("0.0") + " by " + size.y.ToString("0.0") + " dp for words " + wordsWidth.ToString("0.0") + " dp wide");
            Require(wordsWidth <= RoomCursor.WordsMostDp + 0.01f, "the words run " + wordsWidth.ToString("0.0") + " dp, past the label's most");
            var bounds = label.textBounds;
            var wordsLeft = label.transform.localPosition.x + bounds.min.x;
            var wordsRight = label.transform.localPosition.x + bounds.max.x;
            var wordsTop = label.transform.localPosition.y + bounds.max.y;
            var wordsFoot = label.transform.localPosition.y + bounds.min.y;
            Require(wordsLeft >= (RoomCursor.LabelRightDp + 8f) * unit && wordsRight <= (RoomCursor.LabelRightDp + size.x - 8f) * unit
                && wordsTop <= -(RoomCursor.LabelBelowDp + 2f) * unit && wordsFoot >= -(RoomCursor.LabelBelowDp + size.y - 2f) * unit, "the words stand outside their ground");

            // Where it stands: on its stop, or on its way to the next, with the drift the design allows and no more.
            var stops = RoomCursor.DesignStops;
            var (driftRight, driftUp) = RoomMotion.CursorDrift(started);
            var drift = new Vector2(driftRight, driftUp) * RoomGeometry.DpPerDegree;
            var stand = gliding ? Vector2.Lerp(stops[0], stops[1], RoomMotion.CursorGlides(GlidingSeconds)) : stops[0];
            Require(cursor.Stop == (gliding ? 1 : 0), "it is on stop " + cursor.Stop);
            Require((cursor.Corner - stand - drift).magnitude < 0.02f, "its corner is at (" + cursor.Corner.x.ToString("0.00") + ", " + cursor.Corner.y.ToString("0.00") + ") dp, not ("
                + (stand + drift).x.ToString("0.00") + ", " + (stand + drift).y.ToString("0.00") + ")");
            Require(Mathf.Abs(drift.x) <= RoomMotion.CursorDriftDegrees * RoomGeometry.DpPerDegree + 1e-3f, "it drifts more than 0.35 degrees");

            // Stood for the render: square to the eyes, the cursor's own stop on the line of sight.
            cursor.transform.localPosition = new Vector3(-stand.x * unit, -stand.y * unit, 0f);
            Place(plane, eyes, 0f, shot.Elevation);

            // Where the design stands it (its stops about the field's middle at yaw -12.84, pitch -11.63), every stop's arrow and label are inside the field.
            var level = new GameObject("Level plane").transform;
            level.SetParent(root, false);
            Place(level, eyes, 0f, 0f);
            var middle = new Vector2(-12.84f, -11.63f) * RoomGeometry.DpPerDegree;
            var reach = RoomMotion.CursorDriftDegrees * RoomGeometry.DpPerDegree;
            var corners = new List<Vector3>();
            foreach (var each in stops)
            foreach (var corner in new[] { new Vector2(-reach, reach), new Vector2(RoomCursor.LabelRightDp + size.x + reach, -(RoomCursor.LabelBelowDp + size.y) - reach) })
                corners.Add(level.TransformPoint(new Vector3((middle.x + each.x + corner.x) * unit, (middle.y + each.y + corner.y) * unit, 0f)));
            shot.Failures.AddRange(GlazeChecks.InsideField(name, corners, eyes, level.position, 0f, Quest3S));

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                var pointer = cursor.Ground.transform.parent;
                Vector2 Pixel(Vector2 dp)
                {
                    var screen = camera.WorldToScreenPoint(pointer.TransformPoint(new Vector3(dp.x * unit, dp.y * unit, 0f)));
                    return new Vector2(screen.x, screen.y);
                }
                Color32 At(Color32[] pixels, Vector2 dp, int right, int up)
                {
                    var screen = Pixel(dp);
                    var x = Mathf.Clamp(Mathf.FloorToInt(screen.x) + right, 0, Width - 1);
                    var y = Mathf.Clamp(Mathf.FloorToInt(screen.y) + up, 0, Height - 1);
                    return pixels[y * Width + x];
                }
                var night = (Color32)FieldDarkRoom;
                var worst = 0;
                void Hold(string what, Vector2 dp, Color32 colour, float alpha)
                {
                    foreach (var (over, pixels, under) in new[] { ("white", white, new Color32(255, 255, 255, 255)), ("the dark room", dark, night) })
                    {
                        var drawn = At(pixels, dp, 0, 0);
                        var r = Mathf.RoundToInt(alpha * colour.r + (1f - alpha) * under.r);
                        var g = Mathf.RoundToInt(alpha * colour.g + (1f - alpha) * under.g);
                        var b = Mathf.RoundToInt(alpha * colour.b + (1f - alpha) * under.b);
                        var off = Mathf.Max(Mathf.Abs(drawn.r - r), Mathf.Max(Mathf.Abs(drawn.g - g), Mathf.Abs(drawn.b - b)));
                        worst = Mathf.Max(worst, off);
                        if (off > 6) failures.Add(what + " over " + over + " is (" + drawn.r + ", " + drawn.g + ", " + drawn.b + ") where the page is (" + r + ", " + g + ", " + b + ")");
                    }
                }
                // The arrow: its fill well inside, and its edge on the middle of its long left side and of its top side.
                Hold("the arrow's fill", new Vector2(8f, -20f), new Color32(0x59, 0xCF, 0xF1, 255), 1f);
                Hold("the arrow's edge at its left", new Vector2(2.8f, -18.8f), new Color32(0x14, 0x14, 0x16, 255), 1f);
                Hold("the arrow's edge at its top", new Vector2(15.28f, -14f), new Color32(0x14, 0x14, 0x16, 255), 1f);
                // The label's ground, in its left padding clear of the words and the hairline: #181B1E at the page's 0.84.
                var mid = -(RoomCursor.LabelBelowDp + size.y / 2f);
                Hold("the label's ground", new Vector2(RoomCursor.LabelRightDp + 7f, mid), new Color32(0x18, 0x1B, 0x1E, 255), 0.84f);
                Hold("the label's ground at its right", new Vector2(RoomCursor.LabelRightDp + size.x - 7f, mid), new Color32(0x18, 0x1B, 0x1E, 255), 0.84f);

                // The hairline just inside the ground's top edge: its light, read across it over the dark room, against the page's 1 dp of white at 10 percent.
                var pixelsADp = (Pixel(new Vector2(1f, 0f)) - Pixel(Vector2.zero)).magnitude;
                var onLine = 0.84f * 0.9f * 0x1B + 0.1f * 255f + (1f - (0.84f * 0.9f + 0.1f)) * night.g;
                var offLine = 0.84f * 0x1B + 0.16f * night.g;
                var carried = 0f;
                var across = new Vector2(RoomCursor.LabelRightDp + size.x / 2f, -RoomCursor.LabelBelowDp - 0.5f);
                for (var step = -3; step <= 3; step++) carried += (At(dark, across, 0, step).g - offLine) / (onLine - offLine);
                carried /= pixelsADp;
                if (carried < 0.55f || carried > 1.45f) failures.Add("the label's hairline carries " + carried.ToString("0.00", CultureInfo.InvariantCulture) + " of the page's 1 dp line's light");

                // Nothing beside it: left of the arrow, and past the label.
                var beside = Mathf.Max(At(eye, new Vector2(-3f, -18f), 0, 0).a, At(eye, new Vector2(RoomCursor.LabelRightDp + size.x + 3f, mid), 0, 0).a);
                if (beside > 2) failures.Add("something is drawn beside the cursor (alpha " + beside + " of 255)");

                // The words against their ground, as rendered, over white and over the bright room.
                var a = Pixel(new Vector2(wordsLeft, wordsFoot) / unit);
                var c = Pixel(new Vector2(wordsRight, wordsTop) / unit);
                var rect = new RectInt(Mathf.FloorToInt(Mathf.Min(a.x, c.x)), Mathf.FloorToInt(Mathf.Min(a.y, c.y)), 0, 0);
                rect.xMax = Mathf.CeilToInt(Mathf.Max(a.x, c.x));
                rect.yMax = Mathf.CeilToInt(Mathf.Max(a.y, c.y));
                var weakest = double.MaxValue;
                foreach (var (over, pixels) in new[] { ("white", white), ("the bright room", bright) })
                {
                    var under = Luminance(MostCommon(pixels, rect));
                    var strongest = 1.0;
                    for (var y = rect.yMin; y < rect.yMax; y++)
                    for (var x = rect.xMin; x < rect.xMax; x++)
                    {
                        var l = Luminance(pixels[y * Width + x]);
                        strongest = Math.Max(strongest, (Math.Max(l, under) + 0.05) / (Math.Min(l, under) + 0.05));
                    }
                    weakest = Math.Min(weakest, strongest);
                    if (strongest < 4.5) failures.Add("the label's words reach " + strongest.ToString("0.00", CultureInfo.InvariantCulture) + " to 1 over " + over);
                }
                var origin = Pixel(Vector2.zero);
                Debug.Log("Halcyonic: parts: " + name + ": \"" + cursor.Words + "\" on stop " + cursor.Stop + ", its corner at (" + cursor.Corner.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                    + cursor.Corner.y.ToString("0.0", CultureInfo.InvariantCulture) + ") dp; the label's ground " + size.x.ToString("0.0", CultureInfo.InvariantCulture) + " by " + size.y.ToString("0.0", CultureInfo.InvariantCulture)
                    + " dp; arrow and ground at most " + worst + " of 255 from the page over white and the design's dark room; the hairline carries " + carried.ToString("0.00", CultureInfo.InvariantCulture)
                    + "; the weakest words " + weakest.ToString("0.00", CultureInfo.InvariantCulture) + " to 1; the corner's pixel (" + origin.x.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                    + (Height - origin.y).ToString("0.0", CultureInfo.InvariantCulture) + ") from the frame's top left.");
            };
            return shot;
        }

        /// <summary>
        /// The cursor's steps, checked without a render at fixed clock times: it fades in on its first stop, the same step
        /// again moves nothing, each new step glides to the next stop and round again, it never strays past its drift, it
        /// leaves by fading, no step makes it leave, and kept still it stands on its stop at once.
        /// </summary>
        private static List<string> CursorMotion()
        {
            var failures = new List<string>();
            void Require(bool condition, string why) { if (!condition) failures.Add("the cursor's motion: " + why); }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Cursor motion");
            var stillWas = GlazeMotion.Still;
            try
            {
                GlazeMotion.Still = false;
                var cursor = RoomCursor.Create(root.transform);
                var clock = 1f;
                cursor.Clock = () => clock;
                var stops = RoomCursor.DesignStops;
                // The drift's reach: 0.35 degrees across and 0.21 up or down, together at most.
                var most = RoomMotion.CursorDriftDegrees * RoomGeometry.DpPerDegree * Mathf.Sqrt(1f + 0.6f * 0.6f) + 0.01f;
                void After(float seconds)
                {
                    clock += seconds;
                    cursor.Draw();
                }
                RoomWorkStep Step(string id, string tool, string? title) => RoomWork.Of(CursorExecution(id, tool, title))!;

                Require(!cursor.gameObject.activeSelf && cursor.Words.Length == 0, "a new cursor is not hidden");
                cursor.Show(Step("a", "Read", "src/game.js"));
                Require(cursor.gameObject.activeSelf && cursor.Shown == 0f && cursor.Stop == 0 && cursor.Words == "Reading game.js", "it does not start from nothing on its first stop, reading \"" + cursor.Words + "\"");
                Require((cursor.Corner - stops[0]).magnitude <= most, "it does not start on its first stop");
                After(0.3f);
                Require(Mathf.Abs(cursor.Shown - RoomMotion.FieldArrives(0.3f)) < 1e-3f, "300 ms in, it is " + cursor.Shown + " into view");
                After(0.4f);
                Require(cursor.Shown == 1f, "700 ms in, it has not arrived");
                cursor.Show(Step("a", "Read", "src/game.js"));
                After(0.1f);
                Require(cursor.Stop == 0 && (cursor.Corner - stops[0]).magnitude <= most, "the same step again moved it");

                cursor.Show(Step("b", "Edit", "src/balloons.js"));
                Require(cursor.Stop == 1 && cursor.Words == "Editing balloons.js", "a new step did not turn to the next stop, reading \"" + cursor.Words + "\"");
                Require((cursor.Corner - stops[0]).magnitude <= most, "it jumped at the start of its glide");
                After(0.3f);
                var way = Vector2.Lerp(stops[0], stops[1], RoomMotion.CursorGlides(0.3f));
                Require((cursor.Corner - way).magnitude <= most, "300 ms in, it is not seven eighths of the way");
                After(0.4f);
                Require((cursor.Corner - stops[1]).magnitude <= most, "700 ms in, it is not on its next stop");
                foreach (var (id, expected) in new[] { ("c", 2), ("d", 3), ("e", 0) })
                {
                    cursor.Show(Step(id, "Bash", "npm test"));
                    After(1f);
                    Require(cursor.Stop == expected && (cursor.Corner - stops[expected]).magnitude <= most && cursor.Words == "Running Command", "step " + id + " stands on stop " + cursor.Stop + " reading \"" + cursor.Words + "\"");
                }
                var widest = 0f;
                for (var step = 0; step < 200; step++)
                {
                    After(0.13f);
                    widest = Mathf.Max(widest, (cursor.Corner - stops[0]).magnitude);
                }
                Require(widest <= most && widest > 1f, "between steps it drifts " + widest + " dp from its stop");

                cursor.Leave();
                After(0.3f);
                Require(Mathf.Abs(cursor.Shown - RoomMotion.FieldLeaves(0.3f)) < 1e-3f, "300 ms into leaving, it is " + cursor.Shown + " into view");
                After(0.4f);
                Require(!cursor.gameObject.activeSelf, "700 ms on, it has not left");

                GlazeMotion.Still = true;
                cursor.Show(Step("f", "Edit", "src/score.js"));
                Require(cursor.gameObject.activeSelf && cursor.Shown == 1f && cursor.Stop == 0 && cursor.Corner == stops[0], "kept still, it is not on its first stop at once");
                cursor.Show(Step("g", "Read", "src/menu.js"));
                After(2f);
                Require(cursor.Stop == 1 && (cursor.Corner - stops[1]).magnitude < 1e-3f, "kept still, it did not stand on its next stop at once, without drift");
                cursor.Show((RoomWorkStep?)null);
                Require(!cursor.gameObject.activeSelf, "no step did not make it leave");
            }
            catch (Exception error) { failures.Add("the cursor's motion threw " + error); }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                GlazeMotion.Still = stillWas;
            }
            if (failures.Count == 0) Debug.Log("Halcyonic: parts: the cursor's steps, glide and drift hold at every fixed time.");
            return failures;
        }
    }
}
