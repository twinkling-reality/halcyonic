#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>
        /// A picked project's screen and its bar where the plane stands them in the room (ADR 0040, rule 2), measured as
        /// Unity laid them out: the screen 56 degrees across at 2 m, the bar its width, beneath it by the room's gap,
        /// 2 cm nearer and tilted to face the eyes, every glyph at the reading floor, every target whole and 12 mm from the next, the carriers opaque,
        /// the neutral mark where no picture exists. Straight ahead and 40 degrees round, the bar open and folded, at both
        /// text sizes; a bar made wider and a title shrunk under the floor must each be caught. Every name is synthetic.
        /// </summary>
        public static void RoomScreenCheck()
        {
            InstrumentFontAtlas.ValidateSaved();
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "RoomRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var found = new List<string>();
                try
                {
                    foreach (var bearing in new[] { 0f, 40f })
                    foreach (var open in new[] { true, false })
                        found.AddRange(RenderRoomScreen(folder, bearing, open));
                }
                finally { KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: room screen: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: room screen: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderRoomScreen(string folder, float bearing, bool open)
        {
            var name = "screen-" + bearing.ToString("0", CultureInfo.InvariantCulture) + (open ? "-open" : "-folded");
            var failures = new List<string>();
            var root = new GameObject("Room screen witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                camera.transform.rotation = Quaternion.Euler(6f, bearing, 0f);
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                var plane = MenuPlane.Create(root.transform);
                var passed = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, new DateTimeOffset(2026, 10, 29, 10, 38, 0, TimeSpan.Zero));
                var model = new KitScreenModel("Tower Toss", "Codex", "Ready for a change", true, passed, true, null, barOpen: open, working: false, newestChangeAt: null);
                plane.ShowScreen(model, eyes, bearing);
                var screen = plane.RoomScreen!;
                var bar = plane.RoomBar!;
                failures.AddRange(RoomScreenState(name, eyes, screen, bar, model, open));

                // The witnesses: each must be caught by the same checks.
                var scale = bar.transform.localScale;
                bar.transform.localScale = new Vector3(scale.x * 1.05f, scale.y, scale.z);
                if (!RoomScreenState(name, eyes, screen, bar, model, open).Any(failure => failure.Contains("not as wide as the screen")))
                    failures.Add(name + ": the width check accepted a bar 5% wider than the screen");
                bar.transform.localScale = scale;
                var size = screen.Title.fontSize;
                screen.Title.fontSize = GlazeTokens.FontSize(GlazeTokens.Units(Glaze.MinimumTextDegrees * 0.8f));
                screen.Title.ForceMeshUpdate();
                if (!RoomScreenState(name, eyes, screen, bar, model, open).Any(failure => failure.Contains("under the reading floor")))
                    failures.Add(name + ": the glyph check accepted a title shrunk under the reading floor");
                screen.Title.fontSize = size;
                screen.Title.ForceMeshUpdate();

                var image = Render(camera, texture);
                foreach (var carrier in screen.Carriers.Concat(bar.Carriers))
                {
                    var seen = camera.WorldToViewportPoint(carrier.Face.transform.position);
                    var pixel = image.GetPixel(Mathf.RoundToInt(seen.x * (Size - 1)), Mathf.RoundToInt(seen.y * (Size - 1)));
                    if (seen.z <= 0f || seen.x < 0f || seen.x > 1f || seen.y < 0f || seen.y > 1f)
                        failures.Add(name + ": " + PathOf(carrier.transform) + " stands outside the render");
                    else if (pixel.a < 254f / 255f)
                        failures.Add(name + ": " + PathOf(carrier.transform) + " is not opaque in the pixels, alpha " + pixel.a.ToString("0.000", CultureInfo.InvariantCulture));
                }
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                plane.HideScreen();
                if (plane.RoomScreen != null || plane.RoomBar != null || plane.ScreenPose != null)
                    failures.Add(name + ": the screen stayed after it was taken away");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        private static List<string> RoomScreenState(string name, Vector3 eyes, KitScreen screen, KitScreenBar bar, KitScreenModel model, bool open)
        {
            var failures = new List<string>();
            static string Degrees(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
            static float Between(Vector3 a, Vector3 b) => Vector3.Angle(a, b);
            static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0f, value.z);
            Vector3 Point(Transform view, float x, float y) => view.TransformPoint(new Vector3(x, y, 0f));

            // The screen spans the kit's angle where it stands, and faces the eyes.
            var across = Between(Point(screen.transform, -screen.Size.x / 2f, 0f) - eyes, Point(screen.transform, screen.Size.x / 2f, 0f) - eyes);
            if (Mathf.Abs(across - SpatialKit.ScreenDegrees) > 0.1f)
                failures.Add(name + ": the screen spans " + Degrees(across) + " degrees, not " + Degrees(SpatialKit.ScreenDegrees));
            var away = (screen.transform.position - eyes).normalized;
            if (Vector3.Dot(screen.transform.forward, away) < 0.9999f) failures.Add(name + ": the screen does not face the eyes");
            var distance = Flat(screen.transform.position - eyes).magnitude;
            if (Mathf.Abs(distance - RoomLayout.ScreenDistance) > 0.001f) failures.Add(name + ": the screen stands " + distance.ToString("0.000", CultureInfo.InvariantCulture) + " m away, not 2");

            // The bar: the screen's width, edges aligned, beneath by the room's gap, a little nearer, parallel.
            var screenWide = screen.Size.x * screen.transform.lossyScale.x;
            var barWide = bar.Size.x * bar.transform.lossyScale.x;
            if (Mathf.Abs(barWide - screenWide) > 0.0005f)
                failures.Add(name + ": the bar is " + barWide.ToString("0.0000", CultureInfo.InvariantCulture) + " m, not as wide as the screen's " + screenWide.ToString("0.0000", CultureInfo.InvariantCulture));
            foreach (var side in new[] { -1f, 1f })
            {
                var screenEdge = Flat(Point(screen.transform, side * screen.Size.x / 2f, 0f));
                var barEdge = Flat(Point(bar.transform, side * bar.Size.x / 2f, 0f));
                if (Vector3.Distance(screenEdge, barEdge) > RoomLayout.BarForward + 0.001f)
                    failures.Add(name + ": the bar's " + (side < 0 ? "left" : "right") + " edge is not the screen's");
            }
            // Parallel across; low in the view it tilts up to face the eyes, so its targets keep their angle.
            if (Vector3.Dot(Flat(bar.transform.forward).normalized, Flat(screen.transform.forward).normalized) < 0.9999f) failures.Add(name + ": the bar is not parallel to the screen");
            if (Vector3.Dot(bar.transform.forward, (bar.transform.position - eyes).normalized) < 0.9999f) failures.Add(name + ": the bar does not face the eyes");
            var nearer = distance - Flat(bar.transform.position - eyes).magnitude;
            if (Mathf.Abs(nearer - RoomLayout.BarForward) > 0.001f) failures.Add(name + ": the bar stands " + nearer.ToString("0.000", CultureInfo.InvariantCulture) + " m nearer, not 0.02");
            var gap = Between(Point(screen.transform, 0f, -screen.Size.y / 2f) - eyes, Point(bar.transform, 0f, bar.Size.y / 2f) - eyes);
            var below = Point(bar.transform, 0f, bar.Size.y / 2f).y < Point(screen.transform, 0f, -screen.Size.y / 2f).y;
            if (!below || Mathf.Abs(gap - RoomLayout.BarGapDegrees) > 0.05f)
                failures.Add(name + ": the bar's top is " + Degrees(gap) + " degrees " + (below ? "under" : "over") + " the screen's bottom, not " + Degrees(RoomLayout.BarGapDegrees) + " under");
            if (open == bar.Handle.gameObject.activeSelf) failures.Add(name + ": the bar is " + (open ? "folded" : "open") + " against the model");

            // Every word at the reading floor as the eyes see it where it stands.
            foreach (var label in screen.GetComponentsInChildren<TMP_Text>(false).Concat(bar.GetComponentsInChildren<TMP_Text>(false)))
            {
                if (string.IsNullOrEmpty(label.text) || GlazeIcons.IsIcon(label)) continue;
                var em = label.fontSize * 0.1f * label.transform.lossyScale.y;
                var seen = Glaze.DegreesOf(em, Vector3.Distance(label.transform.position, eyes));
                if (seen < Glaze.MinimumTextDegrees * 0.9999f)
                    failures.Add(name + ": " + PathOf(label.transform) + " reads " + seen.ToString("0.000", CultureInfo.InvariantCulture) + " degrees, under the reading floor");
            }
            if (!screen.ReadsWhole) failures.Add(name + ": the screen's title chip is cut");
            if (!model.ShowsPicture && (screen.Mark.text != model.Monogram || !screen.Mark.gameObject.activeInHierarchy))
                failures.Add(name + ": the screen shows no neutral mark where no picture exists");

            // Every target whole as the eyes see it, and 12 mm from the next.
            var targets = bar.Targets.ToList();
            if (open && targets.Count == 0) failures.Add(name + ": the open bar shows no target");
            // As the eyes get it: at the target's own distance from them, not along the bar's normal (the kit's
            // KitTargets convention), which reads a 56-degree bar's end targets about a tenth larger. Both are logged.
            float leastTrue = float.PositiveInfinity, leastNormal = float.PositiveInfinity;
            foreach (var press in targets)
            {
                var at = Vector3.Distance(press.transform.position, eyes);
                var scale = press.transform.lossyScale;
                float tall = Glaze.DegreesOf(press.Size.y * scale.y, at), wide = Glaze.DegreesOf(press.Size.x * scale.x, at);
                var normal = GlazeChecks.PlaneDistance(eyes, press.transform);
                var alongNormal = Mathf.Min(Glaze.DegreesOf(press.Size.y * scale.y, normal), Glaze.DegreesOf(press.Size.x * scale.x, normal));
                leastTrue = Mathf.Min(leastTrue, Mathf.Min(tall, wide));
                leastNormal = Mathf.Min(leastNormal, alongNormal);
                if (Mathf.Min(tall, wide) < SpatialKit.TargetDegrees * 0.96f - 0.01f)
                    failures.Add(name + ": " + PathOf(press.transform) + " is " + Degrees(wide) + " by " + Degrees(tall) + " degrees as the eyes get it, under "
                        + Degrees(SpatialKit.TargetDegrees) + " (" + Degrees(alongNormal) + " along the bar's normal)");
            }
            if (targets.Count > 0)
                Debug.Log("Halcyonic: room screen: " + name + ": the least target " + Degrees(leastTrue) + " degrees as the eyes get it, "
                    + Degrees(leastNormal) + " along the bar's normal.");
            var rects = targets.Select(press =>
            {
                var half = press.Size / 2f;
                var a = bar.transform.InverseTransformPoint(press.transform.TransformPoint(new Vector3(-half.x, -half.y, 0f)));
                var b = bar.transform.InverseTransformPoint(press.transform.TransformPoint(new Vector3(half.x, half.y, 0f)));
                return (press, Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }).ToList();
            for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                var (first, a) = rects[i];
                var (second, b) = rects[j];
                var dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
                var dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
                var apart = Mathf.Sqrt(dx * dx + dy * dy) * bar.transform.lossyScale.x;
                if (apart < Glaze.TargetGapMeters - 0.0003f)
                    failures.Add(name + ": " + first.name + " and " + second.name + " are " + (apart * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart, under 12");
            }
            return failures;
        }
    }
}
