#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
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
        /// text sizes; a bar made wider and a title shrunk under the floor must each be caught. At desk height under a raised
        /// look, it stands on the console's direction, whole in the field (<see cref="RenderRaisedLookScreen"/>). Every name is synthetic.
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
                    found.AddRange(RenderRaisedLookScreen(folder));
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
                failures.AddRange(RoomShelf(name, eyes, plane, bearing));

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

                var directorRoot = new GameObject("Room screen director witness");
                directorRoot.transform.SetParent(root.transform, false);
                var work = Work.Running();
                var presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true);
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var look = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward;
                var director = MenuDirector.Create(directorRoot.transform, new MenuDirector.Setup
                {
                    Presentation = presentation,
                    SpatialWorkspace = true,
                    Commands = commands,
                    Overview = () => WorkOverview.Of(work.State, new StageVisibility(), _ => true),
                    Comfort = comfort,
                    File = (host, _) => new FileColumn(host, () => work.Present(), commands, () => null, _ => null, () => ""),
                    State = () => work.State,
                    StageNow = () => new MenuDirector.Stage(eyes, look, new List<CharacterTarget>(), null, false),
                    Bar = place => TasksColumn.Bar(place, work.State),
                });
                director.DrawNow();
                if (director.Plane.RoomScreen != null)
                    failures.Add(name + ": the director showed a project screen before it was summoned");
                director.ShowProjectScreen(work.Workstream.ProjectId, open);
                director.DrawNow();
                if (director.Plane.RoomScreen == null || director.Plane.RoomBar == null || director.Plane.ScreenProjectId != work.Workstream.ProjectId)
                    failures.Add(name + ": the director did not show the summoned project screen");
                else if (open == director.Plane.RoomBar.Handle.gameObject.activeSelf)
                    failures.Add(name + ": the director's project screen bar did not match the requested open state");
                director.SetProjectScreenBarOpen(!open);
                director.DrawNow();
                if (director.Plane.RoomBar == null || (!open) == director.Plane.RoomBar.Handle.gameObject.activeSelf)
                    failures.Add(name + ": the director did not toggle the project screen bar");
                director.CloseProjectScreen();
                director.DrawNow();
                if (director.Plane.RoomScreen != null || director.Plane.RoomBar != null || director.Plane.ScreenProjectId != null)
                    failures.Add(name + ": the director kept the project screen after it was closed");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>
        /// DEVICE's D-XR-8: the headset on a desk, at desk height, looking up. The director stands the screen where the console
        /// stands, on the plane's own direction (the look's elevation held within the room's pitch), so the screen and its bar
        /// sit whole in the Quest 3S field less its margin; a level screen at eye height, as stood before, has its bar out of
        /// that field, and the check must say so.
        /// </summary>
        private static IEnumerable<string> RenderRaisedLookScreen(string folder)
        {
            const string name = "screen-desk-raised-look";
            const float Desk = 0.8f, Raised = 35f;
            var failures = new List<string>();
            var root = new GameObject("Raised look witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            try
            {
                var eyes = new Vector3(0f, Desk, 0f);
                var look = Quaternion.Euler(-Raised, 0f, 0f) * Vector3.forward;
                var camera = MakeCamera(root.transform, eyes, texture);
                camera.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                var work = Work.Running();
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true),
                    SpatialWorkspace = true,
                    Commands = commands,
                    Overview = () => WorkOverview.Of(work.State, new StageVisibility(), _ => true),
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    File = (host, _) => new FileColumn(host, () => work.Present(), commands, () => null, _ => null, () => ""),
                    State = () => work.State,
                    StageNow = () => new MenuDirector.Stage(eyes, look, new List<CharacterTarget>(), null, false),
                    Bar = place => TasksColumn.Bar(place, work.State),
                });
                director.DrawNow();
                director.ShowProjectScreen(work.Workstream.ProjectId, barOpen: true);
                director.DrawNow();
                var plane = director.Plane;
                if (plane.ScreenPose is not RoomPose screen || plane.BarPose is not RoomPose bar)
                {
                    failures.Add(name + ": the director did not stand the summoned screen");
                    return failures;
                }
                if (plane.Direction.Elevation < WorkspaceFollow.MaximumPitchDegrees - 0.01f)
                    failures.Add(name + ": the console did not follow the raised look to the room's pitch, " + plane.Direction.Elevation.ToString("0.0", CultureInfo.InvariantCulture));
                if (Mathf.Abs(screen.Pitch - plane.Direction.Elevation) > 0.01f || Mathf.Abs(Mathf.DeltaAngle(screen.Bearing, plane.Direction.Yaw)) > 0.01f)
                    failures.Add(name + ": the screen does not stand on the console's direction: " + screen.Bearing.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                        + screen.Pitch.ToString("0.0", CultureInfo.InvariantCulture) + " against " + plane.Direction.Yaw.ToString("0.0", CultureInfo.InvariantCulture) + ", "
                        + plane.Direction.Elevation.ToString("0.0", CultureInfo.InvariantCulture));
                failures.AddRange(OutOfField(name, eyes, look, ("screen", screen), ("bar", bar)));
                // The witness: the screen level at eye height, as it stood before, under the same look.
                var at = new System.Numerics.Vector3(eyes.x, eyes.y, eyes.z);
                var level = RoomLayout.Screen(at, screen.Bearing, SpatialKit.ScreenDegreesAt(GlazeText.Scale), SpatialKit.ScreenHeightDegreesAt(GlazeText.Scale));
                if (OutOfField(name, eyes, look, ("screen", level), ("bar", RoomLayout.Bar(at, level, SpatialKit.BarDegrees))).Count == 0)
                    failures.Add(name + ": the field check accepted a level screen under a look raised " + Raised.ToString("0", CultureInfo.InvariantCulture) + " degrees");
                ForceMeshes(root);
                var image = Render(camera, texture);
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>Each corner of each pose that stands outside the Quest 3S field less its margin, the head turned along <paramref name="look"/>.</summary>
        private static List<string> OutOfField(string name, Vector3 eyes, Vector3 look, params (string What, RoomPose Pose)[] poses)
        {
            var failures = new List<string>();
            var head = Quaternion.Inverse(Quaternion.LookRotation(look, Vector3.up));
            var full = MenuPage.Quest3S;
            var margin = ViewField.EdgeMarginDegrees;
            var field = new ViewField(full.Left - margin, full.Right - margin, full.Up - margin, full.Down - margin);
            foreach (var (what, pose) in poses)
            {
                var bearing = pose.Bearing * Mathf.Deg2Rad;
                var across = new Vector3(Mathf.Cos(bearing), 0f, -Mathf.Sin(bearing));
                var up = new Vector3(pose.Up.X, pose.Up.Y, pose.Up.Z);
                var centre = new Vector3(pose.Centre.X, pose.Centre.Y, pose.Centre.Z);
                foreach (var x in new[] { -0.5f, 0.5f })
                foreach (var y in new[] { -0.5f, 0.5f })
                {
                    var point = head * (centre + across * (pose.Width * x) + up * (pose.Height * y) - eyes);
                    var yaw = Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;
                    var elevation = Mathf.Atan2(point.y, new Vector2(point.x, point.z).magnitude) * Mathf.Rad2Deg;
                    if (point.z <= 0f || !field.Shows(yaw, elevation))
                        failures.Add(name + ": a corner of the " + what + " stands outside the field less its margin, at " + yaw.ToString("0.0", CultureInfo.InvariantCulture)
                            + " across and " + elevation.ToString("0.0", CultureInfo.InvariantCulture) + " up");
                }
            }
            return failures;
        }

        /// <summary>
        /// The agents' shelf keeps clear of the room's screen and its bar as of the console (LARGER-SHELF, 2026-10-31): one
        /// standing in front of the screen is refused, and the shared search, judged by the field and that clearance, still
        /// finds a spot beside them at both reading sizes.
        /// </summary>
        private static List<string> RoomShelf(string name, Vector3 eyes, MenuPlane plane, float bearing)
        {
            var failures = new List<string>();
            var corners = new Vector3[8];
            var width = AgentShelfDraft.Width(1);
            var ahead = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward;
            CharacterStage.WriteShelfCorners(new Pose(eyes + ahead * AgentShelfSearch.HomeDistance - Vector3.up * 0.3f, Quaternion.Euler(0f, bearing, 0f)), width, eyes, corners);
            if (plane.ClearsShelf(corners, eyes)) failures.Add(name + ": a shelf standing in front of the screen was not refused");

            var measured = ViewField.Current ?? MenuPage.Quest3S;
            var lens = new ViewField(measured.Left - ViewField.EdgeMarginDegrees, measured.Right - ViewField.EdgeMarginDegrees,
                measured.Up - ViewField.EdgeMarginDegrees, measured.Down - ViewField.EdgeMarginDegrees);
            int outside = 0, covered = 0;
            bool Fits(AgentShelfPose candidate)
            {
                CharacterStage.WriteShelfCorners(new Pose(new Vector3(candidate.Position.X, candidate.Position.Y, candidate.Position.Z), Quaternion.Euler(0f, candidate.Yaw, 0f)), width, eyes, corners);
                var inField = corners.All(corner =>
                {
                    var offset = corner - eyes;
                    return lens.Shows(Mathf.DeltaAngle(bearing, Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg),
                        Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg);
                });
                var clear = plane.ClearsShelf(corners, eyes);
                if (!inField) outside++;
                if (!clear) covered++;
                return inField && clear;
            }
            var draft = new AgentShelfDraft { Allows = Fits };
            var at = new System.Numerics.Vector3(eyes.x, eyes.y, eyes.z);
            if (!AgentShelfSearch.Begin(draft, at, bearing, out var tried, plane.ConsoleExtentFrom(eyes), distance => Halcyonic.XR.Room.RoomPlacement.ShelfHalfDegrees(width, eyes, distance))
                || !(draft.Candidate is AgentShelfPose found))
            {
                var refusal = tried + " tried, " + outside + " outside the field, " + covered + " not clear of the screen";
                failures.Add(name + ": no shelf spot clears the screen and its bar inside the field, " + refusal);
            }
            else
                Debug.Log("Halcyonic: room screen: " + name + " shelf's first spot " + Mathf.DeltaAngle(bearing, found.Yaw).ToString("0.0", CultureInfo.InvariantCulture)
                    + " degrees from the screen's bearing at " + new Vector2(found.Position.X - eyes.x, found.Position.Z - eyes.z).magnitude.ToString("0.00", CultureInfo.InvariantCulture)
                    + " m, " + (eyes.y - found.Position.Y).ToString("0.00", CultureInfo.InvariantCulture) + " m down, after " + tried + " tried.");
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
            var expectedAcross = SpatialKit.ScreenDegreesAt(GlazeText.Scale);
            var across = Between(Point(screen.transform, -screen.Size.x / 2f, 0f) - eyes, Point(screen.transform, screen.Size.x / 2f, 0f) - eyes);
            if (Mathf.Abs(across - expectedAcross) > 0.1f)
                failures.Add(name + ": the screen spans " + Degrees(across) + " degrees, not " + Degrees(expectedAcross));
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

            failures.AddRange(SeenAtTheFloor(name, eyes, screen.GetComponentsInChildren<TMP_Text>(false).Concat(bar.GetComponentsInChildren<TMP_Text>(false)),
                bar.Targets.ToList(), bar.transform, "the bar"));
            if (open && !bar.Targets.Any()) failures.Add(name + ": the open bar shows no target");
            if (!screen.ReadsWhole) failures.Add(name + ": the screen's title chip is cut");
            if (!model.ShowsPicture && (screen.Mark.text != model.Monogram || !screen.Mark.gameObject.activeInHierarchy))
                failures.Add(name + ": the screen shows no neutral mark where no picture exists");

            return failures;
        }

        /// <summary>
        /// A task's moment where the plane stands it beside a real character (ADR 0040, rule 4), measured as Unity laid it
        /// out: on the character's side toward the look, 23 degrees across at the character's distance, its nearer edge
        /// 1.5 degrees clear of the character's body and label, facing the eyes, its growing corner toward the character,
        /// the same slot for the same character, every glyph at the floor and every target whole as the eyes get them, the
        /// carrier opaque. The character stands right of the look and left of it, at both text sizes. A card moved onto the
        /// character and one on the far side of it must each be caught. Every name is synthetic.
        /// </summary>
        public static void RoomMomentCheck()
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
                    foreach (var bearing in new[] { 25f, -25f }) found.AddRange(RenderRoomMoment(folder, bearing));
                }
                finally { KeepFontAssetsAsCommitted(); }
                foreach (var failure in found) Debug.LogError("Halcyonic: room moment: " + failure);
                if (found.Count == 0) Debug.Log("Halcyonic: room moment: every check passed; " + folder);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderRoomMoment(string folder, float bearing)
        {
            var name = "moment-" + (bearing > 0f ? "right" : "left");
            var failures = new List<string>();
            var root = new GameObject("Room moment witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                camera.transform.rotation = Quaternion.Euler(12f, bearing / 2f, 0f);
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                var work = Work.Running();
                var view = CharacterView.Create(root.transform, work.Workstream.WorkstreamId);
                view.Show(work.Present().Character);
                var (height, scale) = CharacterStage.Stance(view, CharacterStage.DefaultDistance, 0f, CharacterStage.DefaultHeightFromEyes);
                var toward = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward;
                view.transform.SetPositionAndRotation(eyes + toward * CharacterStage.DefaultDistance + Vector3.up * height, Quaternion.LookRotation(-toward));
                view.transform.localScale = Vector3.one * scale;
                var target = CharacterTarget.Attach(view, work.Workstream.WorkstreamId);
                var body = WorkspaceLayout.InView(target, eyes);
                var at = new DateTimeOffset(2026, 10, 29, 10, 38, 0, TimeSpan.Zero);
                var slot = new KitMomentSlot(new[] { KitMoment.Sent("Codex"),
                    KitMoment.Question("Codex", "npm install matter-js", KitSandbox.MayLeave, complete: true, at) }, working: true);
                var plane = MenuPlane.Create(root.transform);
                plane.ShowMoment(slot, eyes, 0f, target.RestPosition, body.HalfWidth);
                var card = plane.Moment!;
                card.Pop(1f);
                failures.AddRange(RoomMomentState(name, eyes, plane, card, body, target.RestPosition));
                var first = plane.MomentPose;
                plane.ShowMoment(slot, eyes, 0f, target.RestPosition, body.HalfWidth);
                card.Pop(1f);
                if (!Equals(plane.MomentPose, first)) failures.Add(name + ": the same character's moment moved to another slot");

                // The witnesses: each must be caught by the same checks.
                var holder = card.transform.parent;
                var stood = (holder.position, holder.rotation);
                var bodyAt = target.RestPosition;
                holder.position = new Vector3(bodyAt.x, holder.position.y, bodyAt.z);
                if (!RoomMomentState(name, eyes, plane, card, body, bodyAt).Any(failure => failure.Contains("clear of the character")))
                    failures.Add(name + ": the clearance check accepted a moment moved onto the character");
                var across = Quaternion.AngleAxis(2f * Mathf.DeltaAngle(Bearing(stood.position - eyes), body.Yaw), Vector3.up);
                holder.SetPositionAndRotation(eyes + across * (stood.position - eyes), across * stood.rotation);
                if (!RoomMomentState(name, eyes, plane, card, body, bodyAt).Any(failure => failure.Contains("grows away from") || failure.Contains("on the far side")))
                    failures.Add(name + ": the corner check accepted a moment on the far side of its character");
                holder.SetPositionAndRotation(stood.position, stood.rotation);

                var image = Render(camera, texture);
                var seen = camera.WorldToViewportPoint(card.Carrier.Face.transform.position);
                if (seen.z <= 0f || seen.x < 0f || seen.x > 1f || seen.y < 0f || seen.y > 1f)
                    failures.Add(name + ": the moment stands outside the render");
                else if (image.GetPixel(Mathf.RoundToInt(seen.x * (Size - 1)), Mathf.RoundToInt(seen.y * (Size - 1))).a < 254f / 255f)
                    failures.Add(name + ": the moment's carrier is not opaque in the pixels");
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                plane.HideMoment();
                if (plane.Moment != null || plane.MomentPose != null) failures.Add(name + ": the moment stayed after it was taken away");

                var directorRoot = new GameObject("Room moment director witness");
                directorRoot.transform.SetParent(root.transform, false);
                var waitingWork = Work.Approval("npm install matter-js", "shell");
                var presentation = new WorkspacePresentationState(keepAgentsWhenMinimized: true) { ContextualMomentsEnabled = true };
                var comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard };
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var targets = new List<CharacterTarget> { target };
                var director = MenuDirector.Create(directorRoot.transform, new MenuDirector.Setup
                {
                    Presentation = presentation,
                    SpatialWorkspace = true,
                    Commands = commands,
                    Overview = () => WorkOverview.Of(waitingWork.State, new StageVisibility(), _ => true),
                    Comfort = comfort,
                    File = (host, _) => new FileColumn(host, () => waitingWork.Present(), commands, () => null, _ => null, () => ""),
                    State = () => waitingWork.State,
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward,
                        presentation.AgentsVisible ? targets : new List<CharacterTarget>(), null, false),
                    CharacterOf = task => presentation.AgentsVisible && task == target.WorkstreamId ? target : null,
                    Bar = place => TasksColumn.Bar(place, waitingWork.State),
                });
                presentation.SetAgentsEnabled(true);
                director.DrawNow();
                director.MinimizeWorkspace();
                director.DrawNow();
                if (!presentation.AgentsVisible || !view.gameObject.activeInHierarchy)
                    failures.Add(name + ": minimizing the console hid the ambient character");
                if (director.Plane.Moment == null || director.Plane.MomentWorkstreamId != waitingWork.Workstream.WorkstreamId)
                    failures.Add(name + ": the contextual moment card did not appear beside the ambient character for a task needing attention");
                director.CloseMoment();
                director.DrawNow();
                if (director.Plane.Moment != null)
                    failures.Add(name + ": closing the contextual moment left the card in the room");
                director.InspectMoment(waitingWork.Workstream.WorkstreamId);
                director.DrawNow();
                if (director.Plane.Moment == null || director.Plane.MomentWorkstreamId != waitingWork.Workstream.WorkstreamId)
                    failures.Add(name + ": inspecting the character did not reopen its moment card");
                director.OpenFile(waitingWork.Workstream.WorkstreamId);
                director.DrawNow();
                if (director.Plane.Moment != null || director.Plane.Showing(MenuColumn.File) == null)
                    failures.Add(name + ": opening the task console from an inspected moment did not replace the moment card with the task console");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        private static float Bearing(Vector3 offset) => Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;

        private static List<string> RoomMomentState(string name, Vector3 eyes, MenuPlane plane, KitMomentCard card, BodyInView body, Vector3 character)
        {
            var failures = new List<string>();
            static string Degrees(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
            var holder = card.transform.parent;
            var face = card.Carrier.Face.transform.position;
            var half = holder.right * (card.Size.x * holder.lossyScale.x / 2f);
            Vector3 left = face - half, right = face + half;
            var across = Vector3.Angle(left - eyes, right - eyes);
            if (Mathf.Abs(across - SpatialKit.MomentDegrees) > 0.1f)
                failures.Add(name + ": the moment spans " + Degrees(across) + " degrees, not " + Degrees(SpatialKit.MomentDegrees));
            if (Vector3.Dot(holder.forward, (face - eyes).normalized) < 0.9999f) failures.Add(name + ": the moment does not face the eyes");
            // Toward the look: the slot stands between the character and where the person looks (straight ahead here).
            var characterSide = Mathf.Sign(body.Yaw);
            var nearer = Mathf.Abs(Mathf.DeltaAngle(Bearing(left - eyes), body.Yaw)) < Mathf.Abs(Mathf.DeltaAngle(Bearing(right - eyes), body.Yaw)) ? left : right;
            // Apart, not only the nearer edge: the card's span and the body's with its label must not meet.
            float a = Mathf.DeltaAngle(body.Yaw, Bearing(left - eyes)), b = Mathf.DeltaAngle(body.Yaw, Bearing(right - eyes));
            var clear = Mathf.Max(Mathf.Min(a, b) - body.HalfWidth, -body.HalfWidth - Mathf.Max(a, b));
            var between = Mathf.Sign(Mathf.DeltaAngle(Bearing(face - eyes), body.Yaw)) == characterSide;
            if (!between || clear < RoomLayout.MomentClearanceDegrees - 0.05f)
                failures.Add(name + ": the moment's nearer edge is " + Degrees(clear) + " degrees " + (between ? "" : "on the far side, not ") + "clear of the character's body and label, under "
                    + Degrees(RoomLayout.MomentClearanceDegrees));
            var flatCard = new Vector2(face.x - eyes.x, face.z - eyes.z).magnitude;
            var flatCharacter = new Vector2(character.x - eyes.x, character.z - eyes.z).magnitude;
            if (Mathf.Abs(flatCard - Mathf.Clamp(flatCharacter, RoomLayout.NearestSurface, RoomLayout.FarthestSurface)) > 0.001f)
                failures.Add(name + ": the moment stands " + flatCard.ToString("0.000", CultureInfo.InvariantCulture) + " m across the floor, not the character's distance");
            // It grows from one lower corner: that corner must be the one toward the character.
            if (nearer != (card.GrowsFromLeft ? left : right)) failures.Add(name + ": the moment grows away from its character");
            failures.AddRange(SeenAtTheFloor(name, eyes, card.GetComponentsInChildren<TMP_Text>(false), card.Targets.ToList(), holder, "the moment"));
            if (!card.ReadsWhole) failures.Add(name + ": the moment's words are cut");
            return failures;
        }

        /// <summary>
        /// Every word at the reading floor and every target whole, each as the eyes get it at its own distance, and every
        /// two targets 12 mm apart edge to edge in <paramref name="space"/>; the least target is also logged along
        /// <paramref name="what"/>'s normal, the kit's KitTargets convention.
        /// </summary>
        private static List<string> SeenAtTheFloor(string name, Vector3 eyes, IEnumerable<TMP_Text> labels, List<KitPress> targets, Transform space, string what)
        {
            var failures = new List<string>();
            static string Degrees(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
            foreach (var label in labels)
            {
                if (string.IsNullOrEmpty(label.text) || GlazeIcons.IsIcon(label)) continue;
                var em = label.fontSize * 0.1f * label.transform.lossyScale.y;
                var seen = Glaze.DegreesOf(em, Vector3.Distance(label.transform.position, eyes));
                if (seen < Glaze.MinimumTextDegrees * 0.9999f)
                    failures.Add(name + ": " + PathOf(label.transform) + " reads " + seen.ToString("0.000", CultureInfo.InvariantCulture) + " degrees, under the reading floor");
            }
            // Every target whole as the eyes see it, and 12 mm from the next.
            // As the eyes get it: at the target's own distance from them, not along the part's normal (the kit's
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
                        + Degrees(SpatialKit.TargetDegrees) + " (" + Degrees(alongNormal) + " along " + what + "'s normal)");
            }
            if (targets.Count > 0)
                Debug.Log("Halcyonic: room: " + name + ": the least target " + Degrees(leastTrue) + " degrees as the eyes get it, "
                    + Degrees(leastNormal) + " along " + what + "'s normal.");
            var rects = targets.Select(press =>
            {
                var half = press.Size / 2f;
                var a = space.InverseTransformPoint(press.transform.TransformPoint(new Vector3(-half.x, -half.y, 0f)));
                var b = space.InverseTransformPoint(press.transform.TransformPoint(new Vector3(half.x, half.y, 0f)));
                return (press, Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y)));
            }).ToList();
            for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                var (first, a) = rects[i];
                var (second, b) = rects[j];
                var dx = Mathf.Max(0f, Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax));
                var dy = Mathf.Max(0f, Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax));
                var apart = Mathf.Sqrt(dx * dx + dy * dy) * space.lossyScale.x;
                if (apart < Glaze.TargetGapMeters - 0.0003f)
                    failures.Add(name + ": " + first.name + " and " + second.name + " are " + (apart * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " mm apart, under 12");
            }
            return failures;
        }
    }
}
